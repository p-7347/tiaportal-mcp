using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        private const string HmiTagListFields =
            "Each entry has 'name' and optionally 'dataType', 'connection' and 'plcTag'";

        [WriteTool]
        [McpServerTool(Name = "CreateHmiTags", Title = "Create many HMI tags", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to create many WinCC Unified HMI tags in one tag table at once, e.g. a few hundred tags for the members of a PLC DB, instead of calling 'CreateHmiTag' per tag. Every entry is checked first and nothing is created when one is wrong (or, with 'skipExisting', existing names are skipped). Give the list as JSON in 'tags' or as a .json/.csv file in 'tagsFile'")]
        public static ResponseHmiTagBatch CreateHmiTags(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("tagTablePath: HMI tag table, as 'GetHmiTagTables' lists it, e.g. 'Default tag table'")] string tagTablePath,
            [Description("tags: JSON array of tags, e.g. '[{\"name\":\"PT_Count_U1\",\"plcTag\":\"\\\"ProductTime\\\".Unit[1].Count\"},{\"name\":\"PT_Reset\",\"dataType\":\"Bool\"}]'. " + HmiTagListFields + "; an internal tag without dataType becomes Int, a PLC-linked tag takes the PLC tag's type. Leave empty when using 'tagsFile'")] string tags = "",
            [Description("tagsFile: optional absolute path of a .json file with the same array, or a .csv/.txt file with a header line 'name;dataType;connection;plcTag' (separator ';', tab or ','; no quoting, so use ';' or tab when a PLC tag contains a comma)")] string tagsFile = "",
            [Description("connection: optional HMI connection for every entry with a plcTag but no connection, e.g. 'HMI_Connection_1'")] string connection = "",
            [Description("skipExisting: report tags whose name already exists as skipped instead of creating nothing (default false)")] bool skipExisting = false)
        {
            return Guarded(nameof(CreateHmiTags), () =>
            {
                var list = ReadHmiTagList(tags, tagsFile);
                var result = Portal.CreateHmiTags(softwarePath, tagTablePath, list, connection, skipExisting);

                var skipped = result.Skipped.Count == 0
                    ? string.Empty
                    : $" Skipped {result.Skipped.Count} existing: {string.Join(", ", result.Skipped.Take(20))}{(result.Skipped.Count > 20 ? ", ..." : string.Empty)}.";

                return new ResponseHmiTagBatch
                {
                    Message = $"{result.Created.Count} HMI tag(s) created in '{tagTablePath}'.{skipped} {SaveHint}",
                    Created = result.Created,
                    Skipped = result.Skipped.Count == 0 ? null : result.Skipped,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "UpdateHmiTags", Title = "Relink or change HMI tags", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to change the PLC tag, connection or data type of existing WinCC Unified HMI tags without deleting and re-creating them, so screens, scripts and alarms bound to the tag names keep working, e.g. after a PLC DB was renamed or restructured. One or many tags per call; an empty field keeps its value. Every entry is checked first and nothing is changed when one is wrong. The previous values are saved as a .json backup and returned with the new ones")]
        public static ResponseHmiTagBatch UpdateHmiTags(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("tags: JSON array of changes, e.g. '[{\"name\":\"PT_Count_U1\",\"plcTag\":\"\\\"ProductTime\\\".Unit[1].CountTotal\"}]'. " + HmiTagListFields + " (the new values). Leave empty when using 'tagsFile'")] string tags = "",
            [Description("tagsFile: optional absolute path of a .json file with the same array, or a .csv/.txt file with a header line 'name;dataType;connection;plcTag' (separator ';', tab or ','; empty cells keep the value)")] string tagsFile = "")
        {
            return Guarded(nameof(UpdateHmiTags), () =>
            {
                var list = ReadHmiTagList(tags, tagsFile);
                var result = Portal.UpdateHmiTags(softwarePath, list, nameof(UpdateHmiTags));
                var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

                return new ResponseHmiTagBatch
                {
                    Message = $"{result.Updated.Count} HMI tag(s) changed. Bindings to their names are unchanged. {SaveHint}" + BackupNote(backup),
                    Updated = result.Updated,
                    Backup = result.Backup,
                    BackupWarning = result.BackupWarning,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiTags", Title = "Delete HMI tags", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Delete WinCC Unified HMI tags by name and/or regular expression, e.g. test tags. Every name is checked first and nothing is deleted when one does not exist. The deleted tags are saved as a .json backup that 'CreateHmiTags' can take back ('tagsFile'). Screens and scripts bound to a deleted tag keep the name and show a missing tag; check with 'GetHmiTagUsage' first")]
        public static ResponseHmiTagBatch DeleteHmiTags(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("tagNames: tag names separated by commas or line breaks, e.g. 'MCP_TEST_B1, MCP_TEST_B2'. May be empty when 'regexName' is given")] string tagNames = "",
            [Description("regexName: optional regular expression over the tag names; matching tags are deleted too, e.g. '^MCP_TEST_'")] string regexName = "")
        {
            return Guarded(nameof(DeleteHmiTags), () =>
            {
                var result = Portal.DeleteHmiTags(softwarePath, SplitNames(tagNames), regexName, nameof(DeleteHmiTags));
                var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

                return new ResponseHmiTagBatch
                {
                    Message = $"{result.Created.Count} HMI tag(s) deleted. {SaveHint}" + BackupNote(backup),
                    Deleted = result.Created,
                    Backup = result.Backup,
                    BackupWarning = result.BackupWarning,
                    Meta = OkMeta()
                };
            });
        }

        /// <summary>The tag list from the 'tags' JSON or from 'tagsFile' (.json array or .csv/.txt with a header line).</summary>
        private static List<Portal.HmiTagSpec> ReadHmiTagList(string tags, string tagsFile)
        {
            var hasJson = !string.IsNullOrWhiteSpace(tags);
            var hasFile = !string.IsNullOrWhiteSpace(tagsFile);

            if (hasJson == hasFile)
            {
                throw new McpException("Give the tags either as JSON in 'tags' or as a file in 'tagsFile' (not both, not neither).");
            }

            if (hasJson)
            {
                return ParseHmiTagJson(tags, "'tags'");
            }

            Operation.RequireAbsolutePath(tagsFile, nameof(tagsFile));
            if (!File.Exists(tagsFile))
            {
                throw new McpException($"tagsFile '{tagsFile}' does not exist on the PC running this server.");
            }

            var text = File.ReadAllText(tagsFile);
            return Path.GetExtension(tagsFile).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? ParseHmiTagJson(text, $"'{tagsFile}'")
                : ParseHmiTagCsv(text, tagsFile);
        }

        private static List<Portal.HmiTagSpec> ParseHmiTagJson(string json, string source)
        {
            try
            {
                // A backup of DeleteHmiTags or UpdateHmiTags is an object whose 'tags' holds the array.
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("tags", out var inner))
                {
                    json = inner.GetRawText();
                }

                return JsonSerializer.Deserialize<List<Portal.HmiTagSpec>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<Portal.HmiTagSpec>();
            }
            catch (JsonException ex)
            {
                throw new McpException($"{source} is not a JSON array of tags ({ex.Message}). Example: [{{\"name\":\"Tag_1\",\"connection\":\"HMI_Connection_1\",\"plcTag\":\"\\\"DB1\\\".Value\"}}]", ex);
            }
        }

        private static List<Portal.HmiTagSpec> ParseHmiTagCsv(string text, string file)
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                .ToList();

            if (lines.Count == 0)
            {
                throw new McpException($"'{file}' is empty.");
            }

            var header = lines[0].TrimStart('﻿');
            var separator = header.Contains('\t') ? '\t' : header.Contains(';') ? ';' : ',';
            var columns = header.Split(separator).Select(c => c.Trim().ToLowerInvariant()).ToList();

            int Column(string name) => columns.IndexOf(name.ToLowerInvariant());
            var nameColumn = Column("name");
            if (nameColumn < 0)
            {
                throw new McpException($"'{file}' needs a header line with a 'name' column, e.g. 'name;dataType;connection;plcTag'. Found: '{header}'.");
            }

            var dataTypeColumn = Column("dataType");
            var connectionColumn = Column("connection");
            var plcTagColumn = Column("plcTag");

            string? Cell(string[] cells, int index) =>
                index >= 0 && index < cells.Length && cells[index].Trim().Length > 0 ? cells[index].Trim() : null;

            return lines.Skip(1).Select(line =>
            {
                var cells = line.Split(separator);
                return new Portal.HmiTagSpec
                {
                    Name = Cell(cells, nameColumn) ?? string.Empty,
                    DataType = Cell(cells, dataTypeColumn),
                    Connection = Cell(cells, connectionColumn),
                    PlcTag = Cell(cells, plcTagColumn)
                };
            }).ToList();
        }
    }
}
