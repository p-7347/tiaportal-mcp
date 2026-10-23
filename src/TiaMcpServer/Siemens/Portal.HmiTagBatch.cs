using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // HMI tags in bulk: CreateHmiTags makes many WinCC Unified tags in one tag table in one
        // call (e.g. a few hundred tags linked to the members of a PLC DB), and UpdateHmiTags
        // relinks existing tags to another PLC tag or connection, or changes their data type,
        // without deleting them, so screen and alarm bindings to the tag names stay intact.
        //
        // Both check every entry first and change nothing when one is wrong; the tool call runs in
        // one transaction, so a failure while writing undoes the tags written before it.
        // UpdateHmiTags saves the previous values as a .json backup.

        public sealed class HmiTagSpec
        {
            public string Name { get; set; } = string.Empty;
            public string? DataType { get; set; }
            public string? Connection { get; set; }
            public string? PlcTag { get; set; }
        }

        public sealed class HmiTagState
        {
            public string Name { get; set; } = string.Empty;
            public string? DataType { get; set; }
            public string? Connection { get; set; }
            public string? PlcTag { get; set; }
        }

        public sealed class HmiTagUpdate
        {
            public string Name { get; set; } = string.Empty;
            public HmiTagState? Before { get; set; }
            public HmiTagState? After { get; set; }
        }

        public sealed class HmiTagBatchResult
        {
            public List<HmiTagState> Created { get; set; } = new List<HmiTagState>();
            public List<string> Skipped { get; set; } = new List<string>();
            public List<HmiTagUpdate> Updated { get; set; } = new List<HmiTagUpdate>();
            public string? Backup { get; set; }
            public string? BackupWarning { get; set; }
        }

        private const int MaxListedProblems = 20;

        /// <param name="defaultConnection">Connection for entries that give a plcTag but no connection.</param>
        /// <param name="skipExisting">Report tags whose name already exists as skipped instead of refusing the batch.</param>
        public HmiTagBatchResult CreateHmiTags(string softwarePath, string tagTablePath, IReadOnlyList<HmiTagSpec> tags,
            string defaultConnection = "", bool skipExisting = false)
        {
            return Operation.Run(_logger, nameof(CreateHmiTags), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);

                    var table = GetHmiTagTable(softwarePath, tagTablePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"HMI tag table not found at '{tagTablePath}'. Use 'GetHmiTagTables' to list them.");

                    if (tags.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No tags given.");
                    }

                    var result = new HmiTagBatchResult();
                    var problems = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var toCreate = new List<HmiTagSpec>();

                    for (var i = 0; i < tags.Count; i++)
                    {
                        var spec = tags[i];
                        var name = (spec.Name ?? string.Empty).Trim();
                        var connection = string.IsNullOrWhiteSpace(spec.Connection) ? defaultConnection?.Trim() : spec.Connection!.Trim();
                        var plcTag = spec.PlcTag?.Trim();

                        if (name.Length == 0)
                        {
                            problems.Add($"entry {i + 1}: no name");
                            continue;
                        }

                        if (!seen.Add(name))
                        {
                            problems.Add($"'{name}': given twice");
                            continue;
                        }

                        try
                        {
                            EnsureValidName(name);
                        }
                        catch (PortalException ex)
                        {
                            problems.Add($"'{name}': {ex.Message}");
                            continue;
                        }

                        if (!string.IsNullOrEmpty(plcTag) && string.IsNullOrEmpty(connection))
                        {
                            problems.Add($"'{name}': plcTag without a connection (give 'connection' for the entry or the call)");
                            continue;
                        }

                        if (hmi.Tags.Find(name) != null)
                        {
                            if (skipExisting) result.Skipped.Add(name);
                            else problems.Add($"'{name}': already exists");
                            continue;
                        }

                        toCreate.Add(new HmiTagSpec
                        {
                            Name = name,
                            DataType = spec.DataType?.Trim(),
                            Connection = string.IsNullOrEmpty(plcTag) ? null : connection,
                            PlcTag = plcTag
                        });
                    }

                    ThrowIfProblems(problems, "Nothing was created");

                    foreach (var spec in toCreate)
                    {
                        var tag = table.Tags.Create(spec.Name);

                        // Connection before PlcTag: the PLC tag is resolved against the connection.
                        if (!string.IsNullOrEmpty(spec.Connection)) tag.Connection = spec.Connection;
                        if (!string.IsNullOrEmpty(spec.PlcTag)) tag.PlcTag = spec.PlcTag;
                        if (!string.IsNullOrEmpty(spec.DataType)) tag.DataType = spec.DataType;

                        result.Created.Add(DescribeHmiTagState(tag));
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("tagTablePath", tagTablePath), ("count", tags.Count.ToString()));
        }

        /// <summary>
        /// Changes the connection, PLC tag or data type of existing HMI tags; an empty field keeps
        /// its value. The tags keep their names, so everything bound to them keeps working.
        /// </summary>
        public HmiTagBatchResult UpdateHmiTags(string softwarePath, IReadOnlyList<HmiTagSpec> tags, string toolName)
        {
            return Operation.Run(_logger, nameof(UpdateHmiTags), PortalErrorCode.InvalidState,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);

                    if (tags.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No tags given.");
                    }

                    var problems = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var found = new List<(HmiTag Tag, HmiTagSpec Spec)>();

                    foreach (var spec in tags)
                    {
                        var name = (spec.Name ?? string.Empty).Trim();
                        if (name.Length == 0)
                        {
                            problems.Add("an entry has no name");
                            continue;
                        }

                        if (!seen.Add(name))
                        {
                            problems.Add($"'{name}': given twice");
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(spec.Connection) && string.IsNullOrWhiteSpace(spec.PlcTag) && string.IsNullOrWhiteSpace(spec.DataType))
                        {
                            problems.Add($"'{name}': nothing to change (give connection, plcTag or dataType)");
                            continue;
                        }

                        var tag = hmi.Tags.Find(name);
                        if (tag == null)
                        {
                            problems.Add($"'{name}': no such HMI tag");
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(spec.PlcTag) && string.IsNullOrWhiteSpace(spec.Connection) && string.IsNullOrEmpty(tag.Connection))
                        {
                            problems.Add($"'{name}': is an internal tag; give 'connection' with 'plcTag'");
                            continue;
                        }

                        found.Add((tag, spec));
                    }

                    ThrowIfProblems(problems, "Nothing was changed");

                    var result = new HmiTagBatchResult();
                    var before = found.Select(f => DescribeHmiTagState(f.Tag)).ToList();

                    var json = JsonSerializer.Serialize(new { softwarePath, tags = before }, new JsonSerializerOptions { WriteIndented = true });
                    var backup = WriteBackup(toolName, found.Count == 1 ? $"HMI tag {found[0].Tag.Name}" : $"{found.Count} HMI tags",
                        true, file => System.IO.File.WriteAllText(file.FullName, json), "json");
                    result.Backup = backup.Path;
                    result.BackupWarning = backup.Warning;

                    for (var i = 0; i < found.Count; i++)
                    {
                        var (tag, spec) = found[i];

                        if (!string.IsNullOrWhiteSpace(spec.Connection)) tag.Connection = spec.Connection!.Trim();
                        if (!string.IsNullOrWhiteSpace(spec.PlcTag)) tag.PlcTag = spec.PlcTag!.Trim();
                        if (!string.IsNullOrWhiteSpace(spec.DataType)) tag.DataType = spec.DataType!.Trim();

                        result.Updated.Add(new HmiTagUpdate { Name = tag.Name, Before = before[i], After = DescribeHmiTagState(tag) });
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("count", tags.Count.ToString()));
        }

        private static HmiTagState DescribeHmiTagState(HmiTag tag) => new HmiTagState
        {
            Name = tag.Name,
            DataType = tag.DataType,
            Connection = string.IsNullOrEmpty(tag.Connection) ? null : tag.Connection,
            PlcTag = string.IsNullOrEmpty(tag.PlcTag) ? null : tag.PlcTag
        };

        private static void ThrowIfProblems(List<string> problems, string outcome)
        {
            if (problems.Count == 0)
            {
                return;
            }

            var listed = string.Join("; ", problems.Take(MaxListedProblems));
            var more = problems.Count > MaxListedProblems ? $" (and {problems.Count - MaxListedProblems} more)" : string.Empty;
            throw new PortalException(PortalErrorCode.InvalidParams, $"{outcome}: {problems.Count} entr{(problems.Count == 1 ? "y has a problem" : "ies have problems")}: {listed}{more}.");
        }
    }
}
