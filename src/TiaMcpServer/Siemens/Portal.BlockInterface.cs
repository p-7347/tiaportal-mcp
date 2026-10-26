using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Block interface member edits: add, change or delete one variable of an FB, FC, OB or
        // global DB without hand-editing the block XML. Openness has no writable interface object
        // (PlcBlockInterface is read-only), so this works like EditBlockNetwork: export the block,
        // change one <Member> in its <Interface>, back the block up and import it again.
        //
        // - Member paths are '.'-separated inside the section, e.g. 'aux.bNew' for a member of the
        //   Struct 'aux'. A name that itself contains dots (e.g. "command.Busy") goes in double
        //   quotes: 'S."command.Busy"'.
        // - Instance DBs take their interface from the FB and are refused; change the FB and
        //   compile it, TIA then updates the instances. UDTs are types, not blocks.
        // - EditTypeInterface does the same for a UDT (one section, 'None' in the XML).
        // - Callers of a changed FB/FC and code that used a deleted member become inconsistent;
        //   the tool says to compile ('CompileCodeBlock').

        public sealed class InterfaceEditResult
        {
            public string BlockPath { get; set; } = string.Empty;
            public string Section { get; set; } = string.Empty;
            public string MemberPath { get; set; } = string.Empty;
            public string? Before { get; set; }
            public string? After { get; set; }
            public string? Backup { get; set; }
            public string? BackupWarning { get; set; }
            public string? PreviewFile { get; set; }

            /// <summary>Every member edit of the call, in order; the first one is also in Section/MemberPath/Before/After.</summary>
            public List<InterfaceEditItem> Edits { get; set; } = new List<InterfaceEditItem>();
        }

        /// <summary>One further member edit applied in the same export/import as the first one.</summary>
        public sealed class InterfaceEditSpec
        {
            public string Action { get; set; } = string.Empty;
            public string Section { get; set; } = string.Empty;
            public string MemberPath { get; set; } = string.Empty;
            public string DataType { get; set; } = string.Empty;
            public string StartValue { get; set; } = string.Empty;
            public string Comment { get; set; } = string.Empty;
            public string AfterMember { get; set; } = string.Empty;
        }

        public sealed class InterfaceEditItem
        {
            public string Action { get; set; } = string.Empty;
            public string Section { get; set; } = string.Empty;
            public string MemberPath { get; set; } = string.Empty;
            public string? Before { get; set; }
            public string? After { get; set; }
        }

        private static readonly string[] InterfaceSections = { "Input", "Output", "InOut", "Static", "Temp", "Constant", "Return" };

        /// <param name="action">"add", "update" or "delete".</param>
        /// <param name="section">Input, Output, InOut, Static, Temp, Constant (Return for an FC's return value, update only).</param>
        /// <param name="memberPath">'.'-separated path inside the section; quote names that contain dots.</param>
        /// <param name="dataType">add: required; update: optional new type. E.g. 'Bool', 'Array[1..10] of Int', '"UDT_Axis"', 'Struct'.</param>
        /// <param name="startValue">add/update: optional start (or constant) value, e.g. 'false', '10', '1.5'.</param>
        /// <param name="comment">add/update: optional member comment.</param>
        /// <param name="afterMember">add: optional sibling to insert after; empty appends at the end of the section or Struct.</param>
        /// <param name="previewFile">When set, the edited block XML is written there and nothing is imported.</param>
        public InterfaceEditResult EditBlockInterface(string softwarePath, string blockPath, string action, string section, string memberPath,
            string dataType = "", string startValue = "", string comment = "", string afterMember = "", string previewFile = "",
            IReadOnlyList<InterfaceEditSpec>? moreEdits = null)
        {
            return Operation.Run(_logger, nameof(EditBlockInterface), PortalErrorCode.ImportFailed,
                () =>
                {
                    var edits = CheckInterfaceEdits(action, section, memberPath, dataType, startValue, comment, afterMember, moreEdits, isType: false);

                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'ResolveObjectPath' or 'GetBlocks' to find its path.");

                    if (block is InstanceDB)
                    {
                        throw new PortalException(PortalErrorCode.NotSupported,
                            $"'{block.Name}' is an instance DB; its interface comes from its FB. Change the FB's interface and compile it ('CompileCodeBlock').");
                    }

                    // TIA keeps no start values for temporary data, nor for an FC's parameters; checked before the
                    // consistency check so a wrong argument is reported without a compile first.
                    foreach (var edit in edits.Where(e => !string.IsNullOrEmpty(e.StartValue)))
                    {
                        if (edit.Section == "Temp" || edit.Section == "Return"
                            || (block is FC && (edit.Section == "Input" || edit.Section == "Output" || edit.Section == "InOut")))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"A {edit.Section} member of {(block is FC ? "an FC" : "this block")} has no start value ('{edit.MemberPath}'); leave 'startValue' empty.");
                        }
                    }

                    if (!block.IsConsistent)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"Block '{block.Name}' is inconsistent; TIA Portal does not export inconsistent blocks. Compile it first ('CompileCodeBlock').");
                    }

                    if (block.Parent is not PlcBlockGroup parentGroup)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, $"Block '{block.Name}' has no block group to import into.");
                    }

                    EnsureNotKnowHowProtected(block);

                    var result = new InterfaceEditResult { BlockPath = GetBlockPath(block) };

                    using var scope = new SourceScope(_logger);
                    var exportFile = Path.Combine(scope.Directory, $"{FileNames.Safe(block.Name)}.xml");
                    block.Export(new FileInfo(exportFile), ExportOptions.None);

                    var doc = XDocument.Load(exportFile);
                    ApplyInterfaceEdits(doc, $"Block '{block.Name}'", edits, result);

                    if (!string.IsNullOrWhiteSpace(previewFile))
                    {
                        var directory = Path.GetDirectoryName(previewFile);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        doc.Save(previewFile);
                        result.PreviewFile = previewFile;
                        return result;
                    }

                    var editedFile = Path.Combine(scope.Directory, "edited", $"{FileNames.Safe(block.Name)}.xml");
                    Directory.CreateDirectory(Path.GetDirectoryName(editedFile)!);
                    doc.Save(editedFile);

                    var backup = BackupBlock(softwarePath, blockPath, nameof(EditBlockInterface));
                    result.Backup = backup.Path;
                    result.BackupWarning = backup.Warning;

                    ImportXmlBlock(softwarePath, GetPlcBlockGroupPath(parentGroup, includeSystemRoot: false), editedFile);

                    return result;
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("action", action), ("section", section), ("memberPath", memberPath));
        }

        /// <summary>
        /// The same member edit for a PLC data type (UDT): its members live in one section,
        /// so there is no section argument. Blocks that use the type become inconsistent until
        /// they are compiled.
        /// </summary>
        public InterfaceEditResult EditTypeInterface(string softwarePath, string typePath, string action, string memberPath,
            string dataType = "", string startValue = "", string comment = "", string afterMember = "", string previewFile = "",
            IReadOnlyList<InterfaceEditSpec>? moreEdits = null)
        {
            return Operation.Run(_logger, nameof(EditTypeInterface), PortalErrorCode.ImportFailed,
                () =>
                {
                    var edits = CheckInterfaceEdits(action, "None", memberPath, dataType, startValue, comment, afterMember, moreEdits, isType: true);

                    var type = GetType(softwarePath, typePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Type not found at '{typePath}'. Use 'GetTypes' to find its path.");

                    if (!type.IsConsistent)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"Type '{type.Name}' is inconsistent; TIA Portal does not export inconsistent types. Compile the PLC software first.");
                    }

                    if (type.Parent is not PlcTypeGroup parentGroup)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, $"Type '{type.Name}' has no type group to import into.");
                    }

                    EnsureNotKnowHowProtected(type);

                    var result = new InterfaceEditResult { BlockPath = GetTypePath(type) };

                    using var scope = new SourceScope(_logger);
                    var exportFile = Path.Combine(scope.Directory, $"{FileNames.Safe(type.Name)}.xml");
                    type.Export(new FileInfo(exportFile), ExportOptions.None);

                    var doc = XDocument.Load(exportFile);
                    ApplyInterfaceEdits(doc, $"Type '{type.Name}'", edits, result);

                    if (!string.IsNullOrWhiteSpace(previewFile))
                    {
                        var directory = Path.GetDirectoryName(previewFile);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        doc.Save(previewFile);
                        result.PreviewFile = previewFile;
                        return result;
                    }

                    var editedFile = Path.Combine(scope.Directory, "edited", $"{FileNames.Safe(type.Name)}.xml");
                    Directory.CreateDirectory(Path.GetDirectoryName(editedFile)!);
                    doc.Save(editedFile);

                    var backup = BackupType(softwarePath, typePath, nameof(EditTypeInterface));
                    result.Backup = backup.Path;
                    result.BackupWarning = backup.Warning;

                    ImportXmlType(softwarePath, GetPlcTypeGroupPath(parentGroup, includeSystemRoot: false), editedFile);

                    return result;
                },
                ("softwarePath", softwarePath), ("typePath", typePath), ("action", action), ("memberPath", memberPath));
        }

        /// <summary>
        /// The first edit (the call's own arguments) followed by 'moreEdits', each checked and
        /// normalized: lower-case action, canonical section name. An edit in 'moreEdits' without
        /// action or section takes the first edit's. Nothing is exported before all edits pass.
        /// </summary>
        private static List<InterfaceEditSpec> CheckInterfaceEdits(string action, string section, string memberPath, string dataType,
            string startValue, string comment, string afterMember, IReadOnlyList<InterfaceEditSpec>? moreEdits, bool isType)
        {
            var first = new InterfaceEditSpec
            {
                Action = action ?? string.Empty,
                Section = section ?? string.Empty,
                MemberPath = memberPath ?? string.Empty,
                DataType = dataType ?? string.Empty,
                StartValue = startValue ?? string.Empty,
                Comment = comment ?? string.Empty,
                AfterMember = afterMember ?? string.Empty
            };

            var edits = new List<InterfaceEditSpec> { first };
            edits.AddRange((moreEdits ?? Array.Empty<InterfaceEditSpec>()).Select(e => new InterfaceEditSpec
            {
                Action = string.IsNullOrWhiteSpace(e.Action) ? first.Action : e.Action,
                Section = isType || string.IsNullOrWhiteSpace(e.Section) ? first.Section : e.Section,
                MemberPath = e.MemberPath ?? string.Empty,
                DataType = e.DataType ?? string.Empty,
                StartValue = e.StartValue ?? string.Empty,
                Comment = e.Comment ?? string.Empty,
                AfterMember = e.AfterMember ?? string.Empty
            }));

            for (var i = 0; i < edits.Count; i++)
            {
                var edit = edits[i];
                var where = i == 0 ? string.Empty : $" (moreMembers item {i})";

                edit.Action = edit.Action.Trim().ToLowerInvariant();
                if (edit.Action != "add" && edit.Action != "update" && edit.Action != "delete")
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"Unknown action '{edit.Action}'{where}. Use 'add', 'update' or 'delete'.");
                }

                if (!isType)
                {
                    edit.Section = InterfaceSections.FirstOrDefault(s => string.Equals(s, edit.Section.Trim(), StringComparison.OrdinalIgnoreCase))
                        ?? throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Unknown section '{edit.Section}'{where}. Use one of: {string.Join(", ", InterfaceSections)}.");
                }

                SplitMemberPath(edit.MemberPath);
                if (edit.Action == "add" && string.IsNullOrWhiteSpace(edit.DataType))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'dataType' is required to add a member{where}, e.g. 'Bool' or 'Array[1..10] of Int'.");
                }

                if (edit.Action == "update" && string.IsNullOrWhiteSpace(edit.DataType) && string.IsNullOrEmpty(edit.StartValue) && string.IsNullOrEmpty(edit.Comment))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"Nothing to update{where}: give 'dataType', 'startValue' or 'comment'.");
                }

                if (edit.Action != "update" && edit.Section == "Return")
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, "An FC has exactly one return value; only 'update' (its data type) is possible.");
                }
            }

            return edits;
        }

        /// <summary>Applies the edits in order to one exported document and records each one.</summary>
        private static void ApplyInterfaceEdits(XDocument doc, string owner, List<InterfaceEditSpec> edits, InterfaceEditResult result)
        {
            foreach (var edit in edits)
            {
                var (before, after) = EditInterfaceMember(doc, owner, edit.Action, edit.Section, SplitMemberPath(edit.MemberPath),
                    edit.MemberPath, edit.DataType, edit.StartValue, edit.Comment, edit.AfterMember);

                result.Edits.Add(new InterfaceEditItem
                {
                    Action = edit.Action,
                    Section = edit.Section,
                    MemberPath = edit.MemberPath.Trim(),
                    Before = before,
                    After = after
                });
            }

            var first = result.Edits[0];
            result.Section = first.Section;
            result.MemberPath = first.MemberPath;
            result.Before = first.Before;
            result.After = first.After;
        }

        /// <summary>
        /// Adds, updates or deletes one member in an exported block or type document. Returns the
        /// member as text before and after the change. Shared by EditBlockInterface and
        /// EditTypeInterface.
        /// </summary>
        private static (string? Before, string? After) EditInterfaceMember(XDocument doc, string owner, string mode, string sectionName,
            List<string> path, string memberPath, string dataType, string startValue, string comment, string afterMember)
        {
            string? before = null;
            string? after = null;

            var sectionElement = doc.Descendants()
                .Where(e => e.Name.LocalName == "Section" && (string?)e.Attribute("Name") == sectionName)
                .FirstOrDefault(e => e.Parent?.Parent?.Name.LocalName == "Interface")
                ?? throw new PortalException(PortalErrorCode.InvalidParams,
                    $"{owner} has no '{sectionName}' section (e.g. an FC has no Static, a global DB only Static).");

            var ns = sectionElement.Name.Namespace;
            var parent = ResolveMemberParent(sectionElement, path, memberPath);
            var name = path[path.Count - 1];
            var existing = MemberChildren(parent).FirstOrDefault(m => (string?)m.Attribute("Name") == name);

            switch (mode)
            {
                case "add":
                    if (existing != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{memberPath}' already exists in {sectionName} ({existing.Attribute("Datatype")?.Value}). Use action 'update' to change it.");
                    }

                    var member = new XElement(ns + "Member", new XAttribute("Name", name), new XAttribute("Datatype", dataType.Trim()));
                    SetMemberComment(member, comment, CommentLanguage(doc));
                    SetMemberStartValue(member, startValue);

                    if (string.IsNullOrWhiteSpace(afterMember))
                    {
                        var last = MemberChildren(parent).LastOrDefault();
                        if (last != null) last.AddAfterSelf(member);
                        else InsertFirstMember(parent, member);
                    }
                    else
                    {
                        var anchor = MemberChildren(parent).FirstOrDefault(m => (string?)m.Attribute("Name") == afterMember.Trim().Trim('"'))
                            ?? throw new PortalException(PortalErrorCode.InvalidParams,
                                $"afterMember '{afterMember}' is not a member next to '{name}'. Members there: {string.Join(", ", MemberChildren(parent).Select(m => (string?)m.Attribute("Name")))}.");
                        anchor.AddAfterSelf(member);
                    }

                    after = DescribeMember(member);
                    break;

                case "update":
                    if (existing == null) throw MemberNotFound(parent, memberPath, sectionName);
                    before = DescribeMember(existing);

                    if (!string.IsNullOrWhiteSpace(dataType))
                    {
                        if (MemberChildren(existing).Any() && !string.Equals(dataType.Trim(), "Struct", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"'{memberPath}' is a Struct with members; delete it and add it again to change its type.");
                        }

                        existing.SetAttributeValue("Datatype", dataType.Trim());
                    }

                    SetMemberComment(existing, comment, CommentLanguage(doc));
                    SetMemberStartValue(existing, startValue);
                    after = DescribeMember(existing);
                    break;

                default:
                    if (existing == null) throw MemberNotFound(parent, memberPath, sectionName);
                    before = DescribeMember(existing);
                    existing.Remove();
                    break;
            }


            return (before, after);
        }

        /// <summary>'aux.bNew' -> [aux, bNew]; '"command.Busy"' stays one name.</summary>
        internal static List<string> SplitMemberPath(string memberPath)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            var quoted = false;

            foreach (var c in (memberPath ?? string.Empty).Trim())
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == '.' && !quoted)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            parts.Add(current.ToString());

            if (quoted || parts.Any(p => p.Trim().Length == 0))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"memberPath '{memberPath}' is not valid. Use e.g. 'bNew', 'aux.bNew', or 'S.\"command.Busy\"' for a name with dots.");
            }

            return parts.Select(p => p.Trim()).ToList();
        }

        private static IEnumerable<XElement> MemberChildren(XElement parent) =>
            parent.Elements().Where(e => e.Name.LocalName == "Member");

        /// <summary>The element that holds the last path segment: the section, or the Struct member above it.</summary>
        private static XElement ResolveMemberParent(XElement section, List<string> path, string memberPath)
        {
            var parent = section;

            foreach (var name in path.Take(path.Count - 1))
            {
                var next = MemberChildren(parent).FirstOrDefault(m => (string?)m.Attribute("Name") == name)
                    ?? throw MemberNotFound(parent, memberPath, (string?)section.Attribute("Name") ?? string.Empty);

                if (!string.Equals((string?)next.Attribute("Datatype"), "Struct", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PortalException(PortalErrorCode.NotSupported,
                        $"'{name}' in '{memberPath}' is a {(string?)next.Attribute("Datatype")}, not a Struct; members of UDTs and FB instances are changed in the UDT or FB itself.");
                }

                parent = next;
            }

            return parent;
        }

        private static PortalException MemberNotFound(XElement parent, string memberPath, string section)
        {
            var names = MemberChildren(parent).Select(m => (string?)m.Attribute("Name")).ToList();
            return new PortalException(PortalErrorCode.NotFound,
                $"'{memberPath}' is not in {section}. Members at that level: {(names.Count == 0 ? "(none)" : string.Join(", ", names))}.");
        }

        /// <summary>A new member of an empty Struct or section goes after its AttributeList/Comment.</summary>
        private static void InsertFirstMember(XElement parent, XElement member)
        {
            var before = parent.Elements().LastOrDefault(e => e.Name.LocalName == "AttributeList" || e.Name.LocalName == "Comment");
            if (before != null) before.AddAfterSelf(member);
            else parent.AddFirst(member);
        }

        /// <summary>The language the block's existing member comments use, else en-US.</summary>
        private static string CommentLanguage(XDocument doc) =>
            doc.Descendants().Where(e => e.Name.LocalName == "MultiLanguageText" && e.Parent?.Name.LocalName == "Comment" && e.Parent.Parent?.Name.LocalName == "Member")
                .Select(e => (string?)e.Attribute("Lang")).FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "en-US";

        private static void SetMemberComment(XElement member, string comment, string language)
        {
            if (string.IsNullOrEmpty(comment))
            {
                return;
            }

            var ns = member.Name.Namespace;
            var element = member.Elements().FirstOrDefault(e => e.Name.LocalName == "Comment");
            if (element == null)
            {
                element = new XElement(ns + "Comment");
                var attributes = member.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList");
                if (attributes != null) attributes.AddAfterSelf(element);
                else member.AddFirst(element);
            }

            var text = element.Elements().FirstOrDefault(e => (string?)e.Attribute("Lang") == language);
            if (text == null)
            {
                element.Add(new XElement(ns + "MultiLanguageText", new XAttribute("Lang", language), comment));
            }
            else
            {
                text.Value = comment;
            }
        }

        /// <summary>StartValue is the last child of a member.</summary>
        private static void SetMemberStartValue(XElement member, string startValue)
        {
            if (string.IsNullOrEmpty(startValue))
            {
                return;
            }

            var element = member.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue");
            if (element == null)
            {
                member.Add(new XElement(member.Name.Namespace + "StartValue", startValue.Trim()));
            }
            else
            {
                element.Value = startValue.Trim();
            }
        }

        private static string DescribeMember(XElement member)
        {
            var text = $"{(string?)member.Attribute("Name")} : {(string?)member.Attribute("Datatype")}";
            var start = member.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue")?.Value;
            var comment = member.Elements().FirstOrDefault(e => e.Name.LocalName == "Comment")?.Elements().Select(e => e.Value).FirstOrDefault(v => v.Length > 0);
            if (!string.IsNullOrEmpty(start)) text += $" := {start}";
            if (!string.IsNullOrEmpty(comment)) text += $" // {comment}";
            return text;
        }
    }
}
