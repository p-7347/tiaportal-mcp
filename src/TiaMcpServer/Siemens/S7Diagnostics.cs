using Microsoft.Extensions.Logging;
using S7CommPlusDriver;
using S7CommPlusDriver.Alarming;
using S7CommPlusDriver.ClientApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    // One tag discovered by Browse() - the symbolic name/data type/access sequence S7CommPlus
    // uses internally, independent of anything Openness knows about.
    public class S7TagBrowseEntry
    {
        public string Name { get; set; } = "";
        public string? DataType { get; set; }
        public string AccessSequence { get; set; } = "";
    }

    public class S7TagValue
    {
        public string Name { get; set; } = "";
        public string? DataType { get; set; }
        public string? Value { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    // One active (currently coming or not-yet-gone) alarm, from GetActiveAlarms - a point-in-time
    // snapshot (poll), not a live subscription.
    public class S7Alarm
    {
        public ulong CpuAlarmId { get; set; }
        public int AlarmDomain { get; set; }
        public int MessageType { get; set; }
        public uint SequenceCounter { get; set; }
        public bool IsComing { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime AckTimestamp { get; set; }
        public string? AlarmText { get; set; }
        public string? InfoText { get; set; }
    }

    // Direct S7CommPlus connection to a live CPU over TCP 102 - completely bypasses Openness and
    // TIA Portal's own engineering session. Deliberately independent of Portal's _portal/_project
    // state (different risk category: a live network connection to a real controller using a
    // reverse-engineered protocol, not an attach to a local TIA Portal process) - never share
    // state between the two. See THIRD_PARTY_LICENSES.md for the driver's license/provenance.
    public class S7Diagnostics
    {
        // S7CommPlusDriver calls Console.WriteLine internally for its own diagnostics (dozens of
        // call sites) - on an MCP stdio server, stdout IS the JSON-RPC wire, so any such write
        // corrupts the protocol stream. Every call into the driver is wrapped to suppress
        // Console.Out for its duration; the lock serializes our own suppress/restore pairs since
        // Console.Out is a process-wide static (the MCP SDK's own stdio transport writes to the
        // raw stdout handle, not through Console.Out, so this does not affect it).
        private static readonly object _consoleLock = new object();

        private readonly ILogger<S7Diagnostics>? _logger;
        private S7CommPlusConnection? _conn;
        private string? _connectedIp;

        private static T WithSuppressedConsoleOut<T>(Func<T> action)
        {
            lock (_consoleLock)
            {
                var original = Console.Out;
                try
                {
                    Console.SetOut(TextWriter.Null);
                    return action();
                }
                finally
                {
                    Console.SetOut(original);
                }
            }
        }

        public S7Diagnostics(ILogger<S7Diagnostics>? logger = null)
        {
            _logger = logger;
        }

        public bool IsConnected => _conn != null;
        public string? ConnectedIp => _connectedIp;

        public void Connect(string ipAddress, string username = "", string password = "")
        {
            if (_conn != null)
            {
                throw new InvalidOperationException($"Already connected to '{_connectedIp}' - call DisconnectPlcDirect first.");
            }

            _logger?.LogInformation("Connecting directly (S7CommPlus) to {IpAddress}", ipAddress);

            var conn = new S7CommPlusConnection();
            var res = WithSuppressedConsoleOut(() => conn.Connect(ipAddress, password, username));
            if (res != 0)
            {
                throw new InvalidOperationException($"Connect to '{ipAddress}' failed (S7CommPlus error code {res}).");
            }

            _conn = conn;
            _connectedIp = ipAddress;
        }

        public void Disconnect()
        {
            if (_conn != null)
            {
                var conn = _conn;
                WithSuppressedConsoleOut<object?>(() => { conn.Disconnect(); return null; });
            }
            _conn = null;
            _connectedIp = null;
        }

        private S7CommPlusConnection RequireConnection()
        {
            return _conn ?? throw new InvalidOperationException("Not connected - call ConnectPlcDirect first.");
        }

        // regexName filters by tag name (case-insensitive) - a CPU with many instance DBs can
        // expose hundreds of thousands of individual members (every sub-member of every nested
        // FB instance is its own browsable tag), confirmed live against a real project, so an
        // unfiltered browse is rarely usable as-is.
        public List<S7TagBrowseEntry> BrowseTags(string? regexName = null)
        {
            var conn = RequireConnection();
            List<VarInfo>? vars = null;
            var res = WithSuppressedConsoleOut(() => conn.Browse(out vars));
            if (res != 0)
            {
                throw new InvalidOperationException($"Browse failed (S7CommPlus error code {res}).");
            }

            IEnumerable<VarInfo> filtered = vars ?? [];
            if (!string.IsNullOrEmpty(regexName))
            {
                var regex = new System.Text.RegularExpressions.Regex(regexName, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                filtered = filtered.Where(v => regex.IsMatch(v.Name));
            }

            return filtered.Select(v => new S7TagBrowseEntry
            {
                Name = v.Name,
                DataType = SafeTypeName(v.Softdatatype),
                AccessSequence = v.AccessSequence
            }).ToList();
        }

        // tagNames are plain dotted symbol paths (e.g. "MainAssy.Mode.Dry_Run" for a global DB
        // member, no TIA-style quoting) - getPlcTagBySymbol resolves each one independently, so
        // an unknown name becomes a per-tag Error entry rather than failing the whole batch.
        public List<S7TagValue> ReadTagValues(IEnumerable<string> tagNames)
        {
            var conn = RequireConnection();
            var results = new List<S7TagValue>();
            var tags = new List<PlcTag>();
            var namesByTag = new Dictionary<PlcTag, string>();

            foreach (var name in tagNames)
            {
                PlcTag? tag;
                try
                {
                    tag = WithSuppressedConsoleOut(() => conn.getPlcTagBySymbol(name));
                }
                catch (Exception ex)
                {
                    results.Add(new S7TagValue { Name = name, Success = false, Error = ex.Message });
                    continue;
                }

                if (tag == null)
                {
                    results.Add(new S7TagValue { Name = name, Success = false, Error = "Tag not found" });
                    continue;
                }

                tags.Add(tag);
                namesByTag[tag] = name;
            }

            if (tags.Count > 0)
            {
                var res = WithSuppressedConsoleOut(() => conn.ReadTags(tags));
                if (res != 0)
                {
                    throw new InvalidOperationException($"ReadTags failed (S7CommPlus error code {res}).");
                }

                foreach (var tag in tags)
                {
                    var good = tag.Quality == PlcTagQC.TAG_QUALITY_GOOD;
                    results.Add(new S7TagValue
                    {
                        Name = namesByTag[tag],
                        DataType = SafeTypeName(tag.Datatype),
                        Value = good ? tag.ToString() : null,
                        Success = good,
                        Error = good ? null : $"Bad quality (code {tag.Quality}), last read error {tag.LastReadError}"
                    });
                }
            }

            return results;
        }

        // Point-in-time snapshot of currently active alarms (GetActiveAlarms), not a live
        // subscription - simpler and better-documented in the driver than the AlarmSubscription*
        // API, which the author's own comments mark as an experimental work-in-progress.
        public List<S7Alarm> GetActiveAlarms(int languageId = 1033)
        {
            var conn = RequireConnection();
            List<AlarmsDai>? alarmList = null;
            var res = WithSuppressedConsoleOut(() => conn.GetActiveAlarms(out alarmList, languageId));
            if (res != 0)
            {
                throw new InvalidOperationException($"GetActiveAlarms failed (S7CommPlus error code {res}).");
            }

            return (alarmList ?? [])
                .Where(a => a != null)
                .Select(a => new S7Alarm
                {
                    CpuAlarmId = a.CpuAlarmId,
                    AlarmDomain = a.AlarmDomain,
                    MessageType = a.MessageType,
                    SequenceCounter = a.SequenceCounter,
                    IsComing = a.AsCgs?.SubtypeId == (uint)AlarmsAsCgs.SubtypeIds.Coming,
                    Timestamp = a.AsCgs?.Timestamp ?? default,
                    AckTimestamp = a.AsCgs?.AckTimestamp ?? default,
                    AlarmText = a.AlarmTexts?.AlarmText,
                    InfoText = a.AlarmTexts?.Infotext
                })
                .ToList();
        }

        private static string? SafeTypeName(uint softdatatype)
        {
            try
            {
                return Softdatatype.Types[softdatatype];
            }
            catch
            {
                return $"Unknown(0x{softdatatype:X})";
            }
        }
    }
}
