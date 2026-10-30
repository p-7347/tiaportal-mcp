using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Field tool for WinCC Unified HMI tags without an MCP client: the user exports the tags
        // from the TIA Portal tag editor (.xlsx), edits the sheet in Excel and runs
        // 'TiaMcpServer.exe hmitags <file.xlsx>'. Unlike TIA Portal's own import, which keeps an
        // existing tag unchanged and so needs the tag deleted first, this changes existing tags in
        // place (their names stay, so screen, script and alarm bindings keep working) and creates
        // the new ones. Nothing is ever deleted or renamed: a tag missing from the sheet is left
        // alone, and a changed name is a new tag.
        //
        // PlanHmiTagSheet only reads; ApplyHmiTagSheet writes the plan in one transaction (all or
        // nothing) after saving the previous values of the changed tags as .json.

        /// <summary>One column of the sheet that this tool writes back.</summary>
        private sealed class SheetField
        {
            public SheetField(string header, Func<HmiTag, string> read, Action<HmiTag, string> write, bool blankKeeps)
            {
                Header = header;
                Read = read;
                Write = write;
                BlankKeeps = blankKeeps;
            }

            public string Header { get; }
            public Func<HmiTag, string> Read { get; }
            public Action<HmiTag, string> Write { get; }

            /// <summary>An empty cell keeps the value (a tag cannot be unlinked or have no type here).</summary>
            public bool BlankKeeps { get; }
        }

        // TIA Portal's English display texts for HmiAcquisitionMode, as the export writes them.
        private static readonly (string Text, HmiAcquisitionMode Mode)[] AcquisitionModeTexts =
        {
            ("Cyclic in operation", HmiAcquisitionMode.CyclicOnUse),
            ("Cyclic continuous", HmiAcquisitionMode.CyclicContinuous),
            ("On demand", HmiAcquisitionMode.OnDemand),
            ("None", HmiAcquisitionMode.None)
        };

        /// <summary>The columns written back, in write order: connection before PLC tag (the PLC tag is resolved against it), PLC tag before data type.</summary>
        public static readonly string[] HmiTagSheetColumns =
            { "Connection", "PLC tag", "DataType", "Address", "Acquisition mode", "Acquisition cycle", "Persistency", "Comment [<language>]" };

        private static List<SheetField> SheetFields(IEnumerable<string> headers)
        {
            var fields = new List<SheetField>
            {
                new SheetField("Connection", t => IsInternalHmiTag(t) ? string.Empty : t.Connection, (t, v) => t.Connection = v, true),
                new SheetField("PLC tag", t => t.PlcTag ?? string.Empty, (t, v) => t.PlcTag = v, true),
                new SheetField("DataType", t => t.DataType ?? string.Empty, (t, v) => t.DataType = v, true),
                new SheetField("Address", t => t.Address ?? string.Empty, (t, v) => t.Address = v, true),
                new SheetField("Acquisition mode", t => AcquisitionModeText(t.AcquisitionMode), (t, v) => t.AcquisitionMode = ParseAcquisitionMode(v), true),
                new SheetField("Acquisition cycle", t => t.AcquisitionCycle ?? string.Empty, (t, v) => t.AcquisitionCycle = v, true),
                new SheetField("Persistency", t => t.Persistent ? "True" : "False", (t, v) => t.Persistent = ParseBool(v), true)
            };

            // One comment column per project language, e.g. "Comment [en-US]"; an empty cell clears the comment.
            foreach (var header in headers.Where(h => h.StartsWith("Comment [", StringComparison.OrdinalIgnoreCase) && h.EndsWith("]", StringComparison.Ordinal)))
            {
                var culture = header.Substring("Comment [".Length, header.Length - "Comment [".Length - 1);
                fields.Add(new SheetField(header, t => PlainComment(CommentItem(t, culture)?.Text), (t, v) => SetComment(t, culture, v), false));
            }

            return fields.Where(f => headers.Contains(f.Header, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        public sealed class HmiTagSheetChange
        {
            public string Column { get; set; } = string.Empty;
            public string Before { get; set; } = string.Empty;
            public string After { get; set; } = string.Empty;
        }

        public sealed class HmiTagSheetItem
        {
            public int Line { get; set; }
            public string Name { get; set; } = string.Empty;

            /// <summary>"new" or "change".</summary>
            public string Kind { get; set; } = string.Empty;

            /// <summary>Tag table path for a new tag ('/'-separated).</summary>
            public string? TagTable { get; set; }

            public List<HmiTagSheetChange> Changes { get; set; } = new List<HmiTagSheetChange>();
        }

        public sealed class HmiTagSheetPlan
        {
            public string HmiPath { get; set; } = string.Empty;
            public List<HmiTagSheetItem> Items { get; set; } = new List<HmiTagSheetItem>();
            public int Unchanged { get; set; }

            /// <summary>Tags of the HMI that the sheet does not list; they are left as they are.</summary>
            public int NotInSheet { get; set; }

            public List<string> Problems { get; set; } = new List<string>();

            /// <summary>Differences the tool does not write (e.g. another tag table), for information.</summary>
            public List<string> Notes { get; set; } = new List<string>();

            public List<string> UsedColumns { get; set; } = new List<string>();
        }

        public sealed class HmiTagSheetResult
        {
            public int Created { get; set; }
            public int Changed { get; set; }
            public string? Backup { get; set; }
        }

        /// <summary>The WinCC Unified HMIs of the open project as 'Device/Software' paths, e.g. 'HMI_1/HMI_RT_1'.</summary>
        public List<string> ListUnifiedHmiPaths()
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");
            }

            var paths = new List<string>();

            void Visit(DeviceComposition devices)
            {
                foreach (var device in devices)
                {
                    foreach (var item in device.DeviceItems)
                    {
                        if (item.GetService<SoftwareContainer>()?.Software is HmiSoftware hmi)
                        {
                            paths.Add($"{device.Name}/{hmi.Name}");
                        }
                    }
                }
            }

            void VisitGroups(DeviceUserGroupComposition groups)
            {
                foreach (var group in groups)
                {
                    Visit(group.Devices);
                    VisitGroups(group.Groups);
                }
            }

            Visit(_project!.Devices);
            VisitGroups(_project.DeviceGroups);

            return paths;
        }

        public HmiTagSheetPlan PlanHmiTagSheet(string hmiPath, HmiTagSheet sheet, Action<int, int>? progress = null)
        {
            var hmi = RequireHmiSoftware(hmiPath);
            var fields = SheetFields(sheet.Headers);
            var plan = new HmiTagSheetPlan { HmiPath = hmiPath, UsedColumns = fields.Select(f => f.Header).ToList() };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matched = 0;
            var hasPath = sheet.Headers.Contains("Path", StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < sheet.Rows.Count; i++)
            {
                progress?.Invoke(i, sheet.Rows.Count);

                var row = sheet.Rows[i];
                var name = row.Name;

                if (name.Length == 0)
                {
                    plan.Problems.Add($"row {row.Line}: no name");
                    continue;
                }

                if (!seen.Add(name))
                {
                    plan.Problems.Add($"row {row.Line}: '{name}' is listed twice");
                    continue;
                }

                var tag = hmi.Tags.Find(name);

                if (tag == null)
                {
                    PlanNewTag(hmiPath, row, fields, hasPath, plan);
                    continue;
                }

                matched++;
                var item = new HmiTagSheetItem { Line = row.Line, Name = tag.Name, Kind = "change" };

                foreach (var field in fields)
                {
                    var wanted = row.Get(field.Header);
                    if (wanted.Length == 0 && field.BlankKeeps)
                    {
                        continue;
                    }

                    var current = field.Read(tag);
                    if (SameSheetValue(field.Header, current, wanted))
                    {
                        continue;
                    }

                    var invalid = ValidateSheetValue(field.Header, wanted);
                    if (invalid != null)
                    {
                        plan.Problems.Add($"row {row.Line} '{name}' {field.Header}: {invalid}");
                        continue;
                    }

                    item.Changes.Add(new HmiTagSheetChange { Column = field.Header, Before = current, After = wanted });
                }

                if (hasPath)
                {
                    var table = TagTablePathOf(row);
                    var actual = tag.TagTableName ?? string.Empty;
                    var wantedTable = table.Split('/').Last();
                    if (table.Length > 0 && !wantedTable.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    {
                        plan.Notes.Add($"row {row.Line} '{name}': is in tag table '{actual}', the sheet says '{table}' - moving tags between tables is not done here (do it in TIA Portal)");
                    }
                }

                if (item.Changes.Count == 0)
                {
                    plan.Unchanged++;
                }
                else
                {
                    plan.Items.Add(item);
                }
            }

            progress?.Invoke(sheet.Rows.Count, sheet.Rows.Count);
            plan.NotInSheet = Math.Max(0, hmi.Tags.Count - matched);

            return plan;
        }

        private void PlanNewTag(string hmiPath, HmiTagSheetRow row, List<SheetField> fields, bool hasPath, HmiTagSheetPlan plan)
        {
            var name = row.Name;

            try
            {
                EnsureValidName(name);
            }
            catch (PortalException ex)
            {
                plan.Problems.Add($"row {row.Line} '{name}': {ex.Message}");
                return;
            }

            var table = hasPath ? TagTablePathOf(row) : string.Empty;
            if (table.Length == 0)
            {
                table = "Default tag table";
            }

            if (GetHmiTagTable(hmiPath, table) == null)
            {
                plan.Problems.Add($"row {row.Line} '{name}': new tag, but tag table '{table}' does not exist in {hmiPath} (create the table in TIA Portal first)");
                return;
            }

            if (row.Get("PLC tag").Length > 0 && row.Get("Connection").Length == 0)
            {
                plan.Problems.Add($"row {row.Line} '{name}': new tag with a PLC tag but no Connection");
                return;
            }

            var item = new HmiTagSheetItem { Line = row.Line, Name = name, Kind = "new", TagTable = table };

            foreach (var field in fields)
            {
                var value = row.Get(field.Header);
                if (value.Length == 0)
                {
                    continue;
                }

                var invalid = ValidateSheetValue(field.Header, value);
                if (invalid != null)
                {
                    plan.Problems.Add($"row {row.Line} '{name}' {field.Header}: {invalid}");
                    return;
                }

                item.Changes.Add(new HmiTagSheetChange { Column = field.Header, After = value });
            }

            plan.Items.Add(item);
        }

        /// <summary>
        /// Writes the plan: changes existing tags in place and creates the new ones, in one
        /// transaction, after saving the previous values of the changed tags to <paramref name="backupPath"/>.
        /// </summary>
        public HmiTagSheetResult ApplyHmiTagSheet(HmiTagSheetPlan plan, string backupPath)
        {
            if (plan.Problems.Count > 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"The sheet has {plan.Problems.Count} problem(s); nothing was changed.");
            }

            var hmi = RequireHmiSoftware(plan.HmiPath);
            var fieldsByHeader = SheetFields(plan.Items.SelectMany(i => i.Changes).Select(c => c.Column).Distinct(StringComparer.OrdinalIgnoreCase))
                .ToDictionary(f => f.Header, StringComparer.OrdinalIgnoreCase);

            // Previous values of every tag that is changed, in the 'tags' shape UpdateHmiTags reads back.
            var before = plan.Items.Where(i => i.Kind == "change")
                .Select(i => hmi.Tags.Find(i.Name))
                .Where(t => t != null)
                .Select(t => new Dictionary<string, string?>
                {
                    ["name"] = t!.Name,
                    ["dataType"] = t.DataType,
                    ["connection"] = IsInternalHmiTag(t) ? null : t.Connection,
                    ["plcTag"] = string.IsNullOrEmpty(t.PlcTag) ? null : t.PlcTag,
                    ["address"] = t.Address,
                    ["acquisitionMode"] = AcquisitionModeText(t.AcquisitionMode),
                    ["acquisitionCycle"] = t.AcquisitionCycle,
                    ["persistent"] = t.Persistent ? "True" : "False",
                    ["comment"] = t.Comment?.Items.Select(c => PlainComment(c.Text)).FirstOrDefault()
                })
                .ToList();

            var created = plan.Items.Where(i => i.Kind == "new").Select(i => i.Name).ToList();
            System.IO.File.WriteAllText(backupPath,
                JsonSerializer.Serialize(new { softwarePath = plan.HmiPath, time = DateTime.Now, tags = before, createdTags = created },
                    new JsonSerializerOptions { WriteIndented = true }));

            var result = new HmiTagSheetResult { Backup = backupPath };

            InTransaction($"HMI tag sheet: {plan.Items.Count} tag(s)", () =>
            {
                foreach (var item in plan.Items)
                {
                    HmiTag tag;

                    if (item.Kind == "new")
                    {
                        var table = GetHmiTagTable(plan.HmiPath, item.TagTable!)
                            ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag table '{item.TagTable}' disappeared.");
                        tag = Write(item, "create", () => table.Tags.Create(item.Name));
                        result.Created++;
                    }
                    else
                    {
                        tag = hmi.Tags.Find(item.Name)
                            ?? throw new PortalException(PortalErrorCode.NotFound, $"Tag '{item.Name}' disappeared.");
                        result.Changed++;
                    }

                    foreach (var change in item.Changes)
                    {
                        // A new PLC tag sets the data type itself; write the sheet's type only when it still differs.
                        if (change.Column == "DataType" && SameSheetValue("DataType", tag.DataType ?? string.Empty, change.After))
                        {
                            continue;
                        }

                        Write(item, change.Column, () => { fieldsByHeader[change.Column].Write(tag, change.After); return true; });
                    }
                }
            });

            return result;
        }

        /// <summary>Runs one write and names the tag, row and column when TIA Portal refuses it.</summary>
        private static T Write<T>(HmiTagSheetItem item, string what, Func<T> write)
        {
            try
            {
                return write();
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"row {item.Line} '{item.Name}' {what}: {Operation.DescribeChain(ex)} - nothing was changed (all writes were undone).", inner: ex);
            }
        }

        private static string TagTablePathOf(HmiTagSheetRow row) =>
            string.Join("/", row.Get("Path").Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));

        private static bool SameSheetValue(string column, string current, string wanted)
        {
            switch (column)
            {
                case "PLC tag":
                    // TIA Portal writes PLC tags with or without quotes depending on the name.
                    return string.Equals(current.Replace("\"", string.Empty), wanted.Replace("\"", string.Empty), StringComparison.OrdinalIgnoreCase);
                case "Persistency":
                    return TryParseBool(current, out var a) && TryParseBool(wanted, out var b) && a == b;
                case "Acquisition mode":
                    return TryParseAcquisitionMode(current, out var x) && TryParseAcquisitionMode(wanted, out var y) && x == y;
                case "Connection":
                case "DataType":
                case "Acquisition cycle":
                    return string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase);
                default:
                    return string.Equals(current, wanted, StringComparison.Ordinal);
            }
        }

        /// <summary>Why the value cannot be written, or null when it can.</summary>
        private static string? ValidateSheetValue(string column, string value)
        {
            switch (column)
            {
                case "Persistency":
                    return TryParseBool(value, out _) ? null : $"'{value}' is not True/False";
                case "Acquisition mode":
                    return TryParseAcquisitionMode(value, out _)
                        ? null
                        : $"'{value}' is not one of {string.Join(", ", AcquisitionModeTexts.Select(m => m.Text))}";
                default:
                    return null;
            }
        }

        private static string AcquisitionModeText(HmiAcquisitionMode mode) =>
            AcquisitionModeTexts.Where(m => m.Mode == mode).Select(m => m.Text).FirstOrDefault() ?? mode.ToString();

        private static bool TryParseAcquisitionMode(string text, out HmiAcquisitionMode mode)
        {
            foreach (var (label, value) in AcquisitionModeTexts)
            {
                if (label.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    mode = value;
                    return true;
                }
            }

            return Enum.TryParse(text.Trim(), true, out mode) && Enum.IsDefined(typeof(HmiAcquisitionMode), mode);
        }

        private static HmiAcquisitionMode ParseAcquisitionMode(string text) =>
            TryParseAcquisitionMode(text, out var mode) ? mode : throw new FormatException($"'{text}' is not an acquisition mode");

        private static bool TryParseBool(string text, out bool value)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "true": case "1": case "yes": value = true; return true;
                case "false": case "0": case "no": value = false; return true;
                default: value = false; return false;
            }
        }

        private static bool ParseBool(string text) =>
            TryParseBool(text, out var value) ? value : throw new FormatException($"'{text}' is not True/False");

        private static MultilingualTextItem? CommentItem(HmiTag tag, string culture) =>
            tag.Comment?.Items.FirstOrDefault(i => string.Equals(i.Language?.Culture?.Name, culture, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Comment text without the rich-text wrapper ("&lt;body&gt;&lt;p&gt;..&lt;/p&gt;&lt;/body&gt;") that Unified
        /// texts may carry, so a plain cell compares equal to the same comment.
        /// </summary>
        private static string PlainComment(string? text)
        {
            if (string.IsNullOrEmpty(text) || !text!.TrimStart().StartsWith("<body", StringComparison.OrdinalIgnoreCase))
            {
                return text ?? string.Empty;
            }

            try
            {
                return string.Join("\n", System.Xml.Linq.XElement.Parse(text).Elements().Select(p => p.Value));
            }
            catch (System.Xml.XmlException)
            {
                return text;
            }
        }

        private static void SetComment(HmiTag tag, string culture, string text)
        {
            var item = CommentItem(tag, culture)
                ?? throw new PortalException(PortalErrorCode.InvalidParams, $"the project has no comment language '{culture}'");
            item.Text = text;
        }
    }
}
