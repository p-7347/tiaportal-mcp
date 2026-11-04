using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        private static S7Diagnostics? _s7Diagnostics;

        // Independent of Portal - a live S7CommPlus (direct, non-Openness) connection to a PLC.
        public static S7Diagnostics S7Diagnostics
        {
            get
            {
                if (_services != null)
                {
                    return _services.GetRequiredService<S7Diagnostics>();
                }
                else
                {
                    if (_s7Diagnostics == null)
                    {
                        _s7Diagnostics = new S7Diagnostics();
                    }
                    return _s7Diagnostics;
                }
            }
            set
            {
                _s7Diagnostics = value ?? throw new ArgumentNullException(nameof(value), "S7Diagnostics cannot be null");
            }
        }

        #region direct plc (S7CommPlus) - experimental, bypasses Openness entirely

        [McpServerTool(Name = "ConnectPlcDirect", Title = "Connect directly to a PLC (S7CommPlus)", Destructive = false, Idempotent = false, OpenWorld = true), Description("EXPERIMENTAL. Open a direct S7CommPlus connection (TCP 102) to a live CPU, independent of TIA Portal and of GoOnline/GoOffline. Community, reverse-engineered protocol: prefer PLCSIM or a non-critical CPU. Needs S7-1200 >= V4.3 or S7-1500 >= V2.9. One connection at a time ('DisconnectPlcDirect' first)")]
        public static ResponseS7Connect ConnectPlcDirect(
            [Description("ipAddress: IP address of the PLC's PROFINET interface (or PLCSIM virtual adapter)")] string ipAddress,
            [Description("username: optional, default: none")] string username = "",
            [Description("password: optional, default: none")] string password = "")
        {
            try
            {
                S7Diagnostics.Connect(ipAddress, username, password);

                return new ResponseS7Connect
                {
                    Message = $"Connected directly to '{ipAddress}' (S7CommPlus)",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Direct connect to '{ipAddress}' failed: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "DisconnectPlcDirect", Title = "Disconnect the direct PLC connection", Destructive = false, Idempotent = true, OpenWorld = false), Description("Closes the direct S7CommPlus connection opened by ConnectPlcDirect. Does not affect TIA Portal's own online/engineering connection.")]
        public static ResponseMessage DisconnectPlcDirect()
        {
            try
            {
                var ip = S7Diagnostics.ConnectedIp;
                S7Diagnostics.Disconnect();

                return new ResponseMessage
                {
                    Message = ip != null ? $"Disconnected from '{ip}'" : "Already disconnected",
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error disconnecting direct PLC connection: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "BrowsePlcTagsDirect", Title = "Browse PLC tags (S7CommPlus)", ReadOnly = true, OpenWorld = false), Description("EXPERIMENTAL. Lists symbolic tags the directly-connected CPU exposes (name, data type, internal access sequence) - requires ConnectPlcDirect first. Read-only; does not modify anything on the PLC. Every sub-member of every nested FB instance is its own tag - a real project can expose hundreds of thousands of them, confirmed live, so pass regexName (e.g. a DB name) rather than browsing unfiltered.")]
        public static ResponseS7TagBrowse BrowsePlcTagsDirect(
            [Description("regexName: optional case-insensitive regex to filter tag names, e.g. 'MainAssy' or '^MyDb\\\\.'")] string? regexName = null)
        {
            try
            {
                var tags = S7Diagnostics.BrowseTags(regexName);

                return new ResponseS7TagBrowse
                {
                    Message = $"{tags.Count} tag(s) found",
                    Tags = tags.Select(t => new ResponseS7TagBrowseEntry
                    {
                        Name = t.Name,
                        DataType = t.DataType,
                        AccessSequence = t.AccessSequence
                    }).ToList(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error browsing PLC tags: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ReadPlcTagValuesDirect", Title = "Read live PLC tag values (S7CommPlus)", ReadOnly = true, OpenWorld = false), Description("EXPERIMENTAL. Read live values from the CPU connected with 'ConnectPlcDirect' by symbolic name, dotted without quotes (e.g. 'MainAssy.Mode.Dry_Run', 'MyTag'). Read-only; a bad tag is reported per item instead of failing the call")]
        public static ResponseS7TagValues ReadPlcTagValuesDirect(
            [Description("tagNames: symbolic tag names to read, e.g. [\"MainAssy.Mode.Dry_Run\", \"MyGlobalDb.Counter\"]")] string[] tagNames)
        {
            try
            {
                var values = S7Diagnostics.ReadTagValues(tagNames);

                return new ResponseS7TagValues
                {
                    Message = $"Read {values.Count(v => v.Success)}/{values.Count} tag(s) successfully",
                    Values = values.Select(v => new ResponseS7TagValue
                    {
                        Name = v.Name,
                        DataType = v.DataType,
                        Value = v.Value,
                        Success = v.Success,
                        Error = v.Error
                    }).ToList(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading PLC tag values: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "WritePlcTagValueDirect", Title = "Write live PLC tag values (S7CommPlus)", Destructive = true, Idempotent = false, OpenWorld = false), Description("EXPERIMENTAL and DESTRUCTIVE. Write values to the CPU connected with 'ConnectPlcDirect'. PLCSIM ONLY: the protocol cannot tell PLCSIM from a real CPU, so you are the only safety check - never confirm against a possibly real CPU. Writes only with confirm=true AND confirmPlcSimOnly=true; otherwise returns a preview (current and new value). Types: Bool (true/false/1/0), Int, DInt, Real, Time (integer ms or e.g. '300ms', '10s'). Refuses more than 10 tags and names that look like F-DB/safety tags (best effort). Returns old and new values")]
        public static ResponseS7TagWrites WritePlcTagValueDirect(
            [Description("tagNames: symbolic tag names to write, same dotted format as ReadPlcTagValuesDirect, e.g. [\"MainAssy.Mode.Dry_Run\"]")] string[] tagNames,
            [Description("values: new value for each tag, same order/length as tagNames, e.g. [\"true\"]")] string[] values,
            [Description("confirm: must be true to actually write (default false = preview only, writes nothing)")] bool confirm = false,
            [Description("confirmPlcSimOnly: must be true to actually write - you are asserting the connected target is PLCSIM, never a real line (default false = preview only, writes nothing)")] bool confirmPlcSimOnly = false)
        {
            try
            {
                if (tagNames.Length != values.Length)
                {
                    throw new McpException($"tagNames ({tagNames.Length}) and values ({values.Length}) must be the same length.");
                }

                var writes = tagNames.Zip(values, (n, v) => (Name: n, Value: v));
                var results = S7Diagnostics.WriteTagValues(writes, confirm, confirmPlcSimOnly);
                var actuallyWritten = results.Any(r => r.Written);

                return new ResponseS7TagWrites
                {
                    Message = actuallyWritten
                        ? $"Wrote {results.Count(r => r.Success)}/{results.Count} tag(s)"
                        : $"Preview only (confirm={confirm}, confirmPlcSimOnly={confirmPlcSimOnly}) - nothing written. {results.Count(r => r.Success)}/{results.Count} tag(s) resolved OK.",
                    Written = actuallyWritten,
                    Results = results.Select(r => new ResponseS7TagWriteResult
                    {
                        Name = r.Name,
                        DataType = r.DataType,
                        OldValue = r.OldValue,
                        NewValue = r.NewValue,
                        Written = r.Written,
                        Success = r.Success,
                        Error = r.Error
                    }).ToList(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error writing PLC tag values: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetActivePlcAlarmsDirect", Title = "Get active PLC alarms (S7CommPlus)", ReadOnly = true, OpenWorld = false), Description("EXPERIMENTAL. Snapshot of currently active (coming) and not-yet-cleared (going) alarms straight off the directly-connected CPU - requires ConnectPlcDirect first. This is a one-time poll, not a live subscription - call it again to refresh. Covers ProDiag/user-program alarms, not the classic SZL-based diagnostic buffer (S7CommPlus has no SZL access). Read-only.")]
        public static ResponseS7Alarms GetActivePlcAlarmsDirect(
            [Description("languageId: Windows LCID for alarm text language, default 1033 (en-US); e.g. 1042 for ko-KR, 1031 for de-DE")] int languageId = 1033)
        {
            try
            {
                var alarms = S7Diagnostics.GetActiveAlarms(languageId);

                return new ResponseS7Alarms
                {
                    Message = $"{alarms.Count} active alarm(s)",
                    Alarms = alarms.Select(a => new ResponseS7Alarm
                    {
                        CpuAlarmId = a.CpuAlarmId,
                        AlarmDomain = a.AlarmDomain,
                        MessageType = a.MessageType,
                        SequenceCounter = a.SequenceCounter,
                        IsComing = a.IsComing,
                        Timestamp = a.Timestamp,
                        AckTimestamp = a.AckTimestamp,
                        AlarmText = a.AlarmText,
                        InfoText = a.InfoText
                    }).ToList(),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error getting active PLC alarms: {ex.Message}", ex);
            }
        }

        #endregion
    }
}
