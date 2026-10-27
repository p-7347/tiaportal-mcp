using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Network-level block edits. Openness has no network object (see GetNetworkSource), so a
        // network is changed the way a person would do it by hand: export the block, replace,
        // insert or delete one SW.Blocks.CompileUnit in the XML, and import the block again.
        // What this adds over doing that by hand:
        //
        // - LAD/FBD part order. TIA refuses an import whose <Parts> are not "sorted according to
        //   the current flow" (live 2026-10-15). SortFlgNetParts rebuilds that order from the
        //   wires: depth-first from the power rail, in wire order, and a part with several inputs
        //   (e.g. an O box joining two branches) only after all parts feeding it. Checked against
        //   128 networks exported by TIA: it reproduced all 125 unedited ones exactly; the 3 it
        //   changed were our hand-edited test files with a part appended at the end - the case
        //   TIA rejected.
        // - IDs. Every ID attribute of the document is renumbered, so a network copied from
        //   another export cannot collide with the IDs already in the block.
        // - Backup. The block is exported to the backup folder first, like DeleteBlock.
        // - SCL networks. SimaticML stores SCL as tokens, not text, so a network written as SCL
        //   text is turned into XML by TIA itself: a scratch FB with the target block's
        //   interface and the text as its body is generated from a source, compiled, exported,
        //   and deleted again; its network is what gets inserted (the manual trick used for the
        //   Vision_2D Cell ID network, 2026-10-16).
        // - Title and comment. A replaced network keeps its title and comment when the new one
        //   brings none; an SCL network always arrives without them (the scratch FB has none), which
        //   dropped "Vision : 2D Result Check" from a replaced network (2026-10-20). 'title' sets
        //   the title of a replaced or inserted network explicitly.
        // - New Static members. 'addStatics' adds SCL declarations to the FB's Static section in the
        //   same import, so a new network can use '#sNew' without a separate interface edit; the
        //   scratch FB of an SCL network sees them too.
        // - Scratch compile errors are reported as such: the scratch FB is a copy of the interface
        //   only, and some errors it reports never show in the real block (a DB length error on
        //   19_G2U5_Vision_2D_Interface, 2026-10-22), so the message says where they come from.
        // - CopyNetwork takes a network from another block, renames text in it ('G2U5' -> 'G2U6')
        //   and inserts or replaces it through the same path.

        public sealed class NetworkEditResult
        {
            public string BlockPath { get; set; } = string.Empty;
            public int NetworksBefore { get; set; }
            public int NetworksAfter { get; set; }
            public int PartsReordered { get; set; }
            public string? Backup { get; set; }
            public string? BackupWarning { get; set; }
            public string? PreviewFile { get; set; }
            public bool SclCompiled { get; set; }

            /// <summary>"Title" and/or "Comment" when they were taken over from the replaced network.</summary>
            public List<string> TextsKept { get; } = new List<string>();

            public bool TitleSet { get; set; }

            /// <summary>Static members added with 'addStatics'.</summary>
            public List<string> StaticsAdded { get; } = new List<string>();

            /// <summary>CopyNetwork: each rename 'from' with the number of places it was replaced.</summary>
            public Dictionary<string, int> Renamed { get; } = new Dictionary<string, int>();
        }

        /// <param name="action">"replace", "insert" (before networkIndex; the network count appends) or "delete".</param>
        /// <param name="networkFile">For replace/insert: an XML file holding a SW.Blocks.CompileUnit, e.g. a block export; its first network is used.</param>
        /// <param name="sourceNetworkIndex">Which network of networkFile to take (0-based, default 0).</param>
        /// <param name="previewFile">When set, the edited block XML is written there and nothing is imported.</param>
        /// <param name="sclFile">For replace/insert, instead of networkFile: a text file with the SCL statements of the new network.</param>
        /// <param name="title">For replace/insert: the network title; empty keeps the replaced network's title (or the new network's own).</param>
        /// <param name="addStatics">SCL declarations of new Static members, e.g. 'sDone : Bool; sCnt : Int := 0;'.</param>
        /// <param name="incomingUnit">CopyNetwork: the network to put in, instead of networkFile/sclFile.</param>
        public NetworkEditResult EditBlockNetwork(string softwarePath, string blockPath, string action, int networkIndex,
            string networkFile, int sourceNetworkIndex, string previewFile, string sclFile = "", string title = "",
            string addStatics = "", XElement? incomingUnit = null, NetworkEditResult? result = null)
        {
            return Operation.Run(_logger, nameof(EditBlockNetwork), PortalErrorCode.ImportFailed,
                () =>
                {
                    var mode = (action ?? string.Empty).Trim().ToLowerInvariant();
                    if (mode != "replace" && mode != "insert" && mode != "delete")
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"Unknown action '{action}'. Use 'replace', 'insert' or 'delete'.");
                    }

                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'ResolveObjectPath' or 'GetBlocks' to find its path.");

                    if (!block.IsConsistent)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"Block '{block.Name}' is inconsistent; TIA Portal does not export inconsistent blocks. Compile it first ('CompileCodeBlock').");
                    }

                    if (block.Parent is not PlcBlockGroup parentGroup)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState, $"Block '{block.Name}' has no block group to import into.");
                    }

                    var useScl = !string.IsNullOrWhiteSpace(sclFile);
                    if (useScl && !string.IsNullOrWhiteSpace(networkFile))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "Give either 'networkFile' or 'sclFile', not both.");
                    }

                    if (useScl && !File.Exists(sclFile))
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"sclFile '{sclFile}' does not exist.");
                    }

                    EnsureNotKnowHowProtected(block);

                    XElement? incoming = incomingUnit == null ? null : new XElement(incomingUnit);
                    if (mode != "delete" && !useScl && incoming == null)
                    {
                        incoming = LoadCompileUnit(networkFile, sourceNetworkIndex);
                    }

                    result ??= new NetworkEditResult();
                    result.BlockPath = GetBlockPath(block);

                    using var scope = new SourceScope(_logger);
                    var exportFile = Path.Combine(scope.Directory, $"{FileNames.Safe(block.Name)}.xml");
                    block.Export(new FileInfo(exportFile), ExportOptions.None);

                    var doc = XDocument.Load(exportFile);

                    if (!string.IsNullOrWhiteSpace(addStatics))
                    {
                        result.StaticsAdded.AddRange(AddStaticMembers(doc, block.Name, addStatics));
                    }

                    if (mode != "delete" && useScl)
                    {
                        incoming = BuildSclNetwork(softwarePath, doc, sclFile, scope.Directory);
                        result.SclCompiled = true;
                    }

                    var units = CompileUnits(doc);
                    result.NetworksBefore = units.Count;

                    var limit = mode == "insert" ? units.Count : units.Count - 1;
                    if (networkIndex < 0 || networkIndex > limit)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"networkIndex {networkIndex} is out of range: block '{block.Name}' has {units.Count} network(s), numbered from 0" +
                            (mode == "insert" ? $"; {units.Count} appends." : "."));
                    }

                    switch (mode)
                    {
                        case "delete":
                            if (units.Count == 1)
                            {
                                throw new PortalException(PortalErrorCode.InvalidParams, "A block needs at least one network; replace it instead.");
                            }

                            units[networkIndex].Remove();
                            break;

                        case "replace":
                            result.TextsKept.AddRange(KeepNetworkTexts(units[networkIndex], incoming!));
                            units[networkIndex].ReplaceWith(incoming!);
                            break;

                        default:
                            if (units.Count == 0)
                            {
                                throw new PortalException(PortalErrorCode.InvalidParams, $"Block '{block.Name}' has no networks to insert next to.");
                            }

                            if (networkIndex == units.Count)
                            {
                                units[units.Count - 1].AddAfterSelf(incoming!);
                            }
                            else
                            {
                                units[networkIndex].AddBeforeSelf(incoming!);
                            }

                            break;
                    }

                    if (incoming != null && !string.IsNullOrWhiteSpace(title))
                    {
                        SetNetworkTitle(incoming, title);
                        result.TitleSet = true;
                    }

                    if (incoming != null)
                    {
                        foreach (var net in incoming.Descendants().Where(e => e.Name.LocalName == "FlgNet"))
                        {
                            result.PartsReordered += SortFlgNetParts(net);
                        }
                    }

                    RenumberIds(doc);
                    result.NetworksAfter = CompileUnits(doc).Count;

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

                    var backup = BackupBlock(softwarePath, blockPath, nameof(EditBlockNetwork));
                    result.Backup = backup.Path;
                    result.BackupWarning = backup.Warning;

                    ImportXmlBlock(softwarePath, GetPlcBlockGroupPath(parentGroup, includeSystemRoot: false), editedFile);

                    return result;
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("action", action), ("networkIndex", networkIndex));
        }

        /// <summary>
        /// Lets TIA turn SCL text into a network: generates a scratch FB whose interface repeats
        /// the target block's (so '#' names resolve the same way), compiles it, exports it and
        /// returns its first network. The scratch FB and its source are always removed again.
        /// </summary>
        private XElement BuildSclNetwork(string softwarePath, XDocument blockDoc, string sclFile, string workDirectory)
        {
            var body = File.ReadAllText(sclFile);
            var name = $"_McpSclNet_{Guid.NewGuid():N}".Substring(0, 23);
            var optimized = !string.Equals(
                blockDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "MemoryLayout")?.Value, "Standard", StringComparison.OrdinalIgnoreCase);

            var source = new StringBuilder();
            source.AppendLine($"FUNCTION_BLOCK \"{name}\"");
            source.AppendLine($"{{ S7_Optimized_Access := '{(optimized ? "TRUE" : "FALSE")}' }}");
            source.AppendLine("VERSION : 0.1");

            var sections = blockDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Interface")?
                .Descendants().Where(e => e.Name.LocalName == "Section") ?? Enumerable.Empty<XElement>();

            foreach (var section in sections)
            {
                var keyword = (string?)section.Attribute("Name") switch
                {
                    "Input" => "VAR_INPUT",
                    "Output" => "VAR_OUTPUT",
                    "InOut" => "VAR_IN_OUT",
                    "Static" => "VAR",
                    "Temp" => "VAR_TEMP",
                    "Constant" => "VAR CONSTANT",
                    _ => null // Return: an FB has none
                };

                var members = section.Elements().Where(e => e.Name.LocalName == "Member").ToList();
                if (keyword == null || members.Count == 0)
                {
                    continue;
                }

                source.AppendLine(keyword);
                foreach (var member in members)
                {
                    AppendSclDeclaration(source, member, "   ", keyword == "VAR CONSTANT");
                }

                source.AppendLine("END_VAR");
            }

            source.AppendLine();
            source.AppendLine("BEGIN");
            source.AppendLine(body.TrimEnd());
            source.AppendLine("END_FUNCTION_BLOCK");

            var file = Path.Combine(workDirectory, name + ".scl");
            File.WriteAllText(file, source.ToString(), new UTF8Encoding(true));

            var generated = ImportOneSource(softwarePath, file, string.Empty, GenerateBlockOption.None, isType: false);
            var path = generated.FirstOrDefault(i => i.Kind == "block")?.Path
                ?? throw new PortalException(PortalErrorCode.ImportFailed, "TIA generated no block from the SCL text.");

            try
            {
                var scratch = GetBlock(softwarePath, path)
                    ?? throw new PortalException(PortalErrorCode.ImportFailed, $"Generated SCL block '{path}' was not found.");

                var compiled = scratch.GetService<ICompilable>()?.Compile();
                if (compiled != null && compiled.State == CompilerResultState.Error)
                {
                    var errors = CompilerErrors(compiled.Messages).Take(10).ToList();
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        "The SCL text did not compile in the scratch FB (a temporary FB with a copy of this block's interface; your block is unchanged): " +
                        (errors.Count > 0 ? string.Join(" | ", errors) : $"{compiled.ErrorCount} error(s).") +
                        " " + ScratchErrorAdvice(errors));
                }

                var exportFile = Path.Combine(workDirectory, name + ".xml");
                scratch.Export(new FileInfo(exportFile), ExportOptions.None);

                var unit = CompileUnits(XDocument.Load(exportFile)).FirstOrDefault()
                    ?? throw new PortalException(PortalErrorCode.ImportFailed, "The compiled SCL block exported no network.");

                return new XElement(unit);
            }
            finally
            {
                try
                {
                    GetBlock(softwarePath, path)?.Delete();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not delete scratch SCL block '{Path}'", path);
                }
            }
        }

        /// <summary>
        /// One interface member as an SCL declaration; Struct members recurse. Names are always
        /// quoted: members such as "DT" are SCL keywords and break the source unquoted (live 2026-10-19).
        /// </summary>
        private static void AppendSclDeclaration(StringBuilder source, XElement member, string indent, bool withStartValue)
        {
            var name = (string?)member.Attribute("Name") ?? string.Empty;
            var datatype = (string?)member.Attribute("Datatype") ?? string.Empty;
            var quoted = "\"" + name + "\"";

            if (datatype.EndsWith("Struct", StringComparison.OrdinalIgnoreCase))
            {
                source.AppendLine($"{indent}{quoted} : {datatype}");
                foreach (var child in member.Elements().Where(e => e.Name.LocalName == "Member"))
                {
                    AppendSclDeclaration(source, child, indent + "   ", false);
                }

                source.AppendLine($"{indent}END_STRUCT;");
                return;
            }

            var start = withStartValue ? member.Elements().FirstOrDefault(e => e.Name.LocalName == "StartValue")?.Value : null;
            source.AppendLine(start == null ? $"{indent}{quoted} : {datatype};" : $"{indent}{quoted} : {datatype} := {start};");
        }

        /// <summary>
        /// What the scratch errors most likely mean. A missing '#' name is real: the member is not in
        /// the interface yet. Errors about DB lengths, instances or addresses can come from the
        /// scratch FB itself and not show in the real block.
        /// </summary>
        private static string ScratchErrorAdvice(List<string> errors)
        {
            var advice = new List<string>();

            if (errors.Any(e => e.IndexOf("not defined", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                e.IndexOf("nicht definiert", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                advice.Add("An undefined '#' name is a real error: add the member with 'addStatics' (Static) or 'EditBlockInterface' first.");
            }

            if (errors.Any(e => e.IndexOf("exceeds the length", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                e.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                e.IndexOf("address", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                advice.Add("Errors about DB lengths, instances or addresses can be artifacts of the scratch FB and may not occur in the real block: " +
                           "export the real block ('ExportXmlBlock'), put the statements in there by hand and use 'networkFile', or 'ImportXmlBlock' the whole block.");
            }

            return advice.Count > 0 ? string.Join(" ", advice) : "Fix the SCL text and retry; do not repeat the call unchanged.";
        }

        /// <summary>
        /// Adds 'name : type [:= start];' declarations to the Static section of an exported FB. A
        /// name that already exists is refused, as is a Struct (use EditBlockInterface for those).
        /// </summary>
        private static List<string> AddStaticMembers(XDocument doc, string blockName, string declarations)
        {
            var section = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Section" && (string?)e.Attribute("Name") == "Static")
                ?? throw new PortalException(PortalErrorCode.InvalidParams,
                    $"Block '{blockName}' has no Static section (only FBs have one). Use 'EditBlockInterface' for Temp or other sections.");

            var ns = section.Name.Namespace;
            var existing = new HashSet<string>(section.Elements().Where(e => e.Name.LocalName == "Member")
                .Select(e => (string?)e.Attribute("Name") ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            var added = new List<string>();

            foreach (var raw in declarations.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var text = raw.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var colon = text.IndexOf(':');
                if (colon <= 0 || (colon + 1 < text.Length && text[colon + 1] == '='))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'addStatics': '{text}' is not 'name : type' (e.g. 'sDone : Bool').");
                }

                var name = text.Substring(0, colon).Trim().Trim('"', '#');
                var rest = text.Substring(colon + 1);
                var assign = rest.IndexOf(":=", StringComparison.Ordinal);
                var datatype = (assign >= 0 ? rest.Substring(0, assign) : rest).Trim();
                var start = assign >= 0 ? rest.Substring(assign + 2).Trim() : null;

                if (name.Length == 0 || datatype.Length == 0)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'addStatics': '{text}' needs a name and a data type.");
                }

                if (datatype.StartsWith("Struct", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'addStatics': '{name}' is a Struct; add it with 'EditBlockInterface' first.");
                }

                if (!existing.Add(name))
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'addStatics': block '{blockName}' already has a member '{name}'.");
                }

                var member = new XElement(ns + "Member", new XAttribute("Name", name), new XAttribute("Datatype", datatype));
                if (!string.IsNullOrEmpty(start))
                {
                    member.Add(new XElement(ns + "StartValue", start));
                }

                section.Add(member);
                added.Add($"{name} : {datatype}");
            }

            if (added.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "'addStatics' holds no declaration, e.g. 'sDone : Bool; sCnt : Int := 0'.");
            }

            return added;
        }

        /// <summary>
        /// Copies one network of a block into another (or the same) block: export the source,
        /// replace text in the network as 'renames' says (literally, in order, also in titles and
        /// comments), then insert or replace it in the target like EditBlockNetwork.
        /// </summary>
        public NetworkEditResult CopyNetwork(string softwarePath, string sourceBlockPath, int sourceNetworkIndex, string targetBlockPath,
            string action, int networkIndex, IReadOnlyList<KeyValuePair<string, string>> renames, string addStatics, string previewFile, string title)
        {
            return Operation.Run(_logger, nameof(CopyNetwork), PortalErrorCode.ImportFailed,
                () =>
                {
                    var mode = (action ?? string.Empty).Trim().ToLowerInvariant();
                    if (mode != "replace" && mode != "insert")
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"Unknown action '{action}'. Use 'insert' or 'replace'.");
                    }

                    var source = GetBlock(softwarePath, sourceBlockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Source block not found at '{sourceBlockPath}'. Use 'ResolveObjectPath' or 'GetBlocks' to find its path.");

                    if (!source.IsConsistent)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"Source block '{source.Name}' is inconsistent; TIA Portal does not export inconsistent blocks. Compile it first ('CompileCodeBlock').");
                    }

                    EnsureNotKnowHowProtected(source);

                    using var scope = new SourceScope(_logger);
                    var exportFile = Path.Combine(scope.Directory, $"source_{FileNames.Safe(source.Name)}.xml");
                    source.Export(new FileInfo(exportFile), ExportOptions.None);

                    var unit = LoadCompileUnit(exportFile, sourceNetworkIndex);
                    var result = new NetworkEditResult();

                    if (renames.Count > 0)
                    {
                        var text = unit.ToString(SaveOptions.DisableFormatting);
                        foreach (var pair in renames)
                        {
                            if (pair.Key.Length == 0)
                            {
                                continue;
                            }

                            var count = CountOccurrences(text, pair.Key);
                            result.Renamed[pair.Key] = count;
                            text = text.Replace(pair.Key, pair.Value);
                        }

                        unit = XElement.Parse(text);
                    }

                    return EditBlockNetwork(softwarePath, targetBlockPath, mode, networkIndex, string.Empty, 0, previewFile, string.Empty, title,
                        addStatics, unit, result);
                },
                ("softwarePath", softwarePath), ("sourceBlockPath", sourceBlockPath), ("targetBlockPath", targetBlockPath));
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        private static IEnumerable<string> CompilerErrors(CompilerResultMessageComposition? messages)
        {
            if (messages == null)
            {
                yield break;
            }

            foreach (var message in messages)
            {
                if (message.State == CompilerResultState.Error && !string.IsNullOrWhiteSpace(message.Description))
                {
                    yield return message.Description;
                }

                foreach (var inner in CompilerErrors(message.Messages))
                {
                    yield return inner;
                }
            }
        }

        private static List<XElement> CompileUnits(XDocument doc) =>
            doc.Descendants().Where(e => e.Name.LocalName == "SW.Blocks.CompileUnit").ToList();

        private static XElement LoadCompileUnit(string networkFile, int sourceNetworkIndex)
        {
            if (string.IsNullOrWhiteSpace(networkFile))
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "'networkFile' is required for replace and insert.");
            }

            if (!File.Exists(networkFile))
            {
                throw new PortalException(PortalErrorCode.NotFound, $"networkFile '{networkFile}' does not exist.");
            }

            XDocument source;
            try
            {
                source = XDocument.Load(networkFile);
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"networkFile '{networkFile}' is not readable XML: {ex.Message}", null, ex);
            }

            var units = CompileUnits(source);
            if (units.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"networkFile '{networkFile}' holds no <SW.Blocks.CompileUnit>. Use a block export (ExportXmlBlock) or a file with one network element.");
            }

            if (sourceNetworkIndex < 0 || sourceNetworkIndex >= units.Count)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"sourceNetworkIndex {sourceNetworkIndex} is out of range: networkFile has {units.Count} network(s).");
            }

            return new XElement(units[sourceNetworkIndex]);
        }

        /// <summary>Renumbers every ID attribute in document order (hex, from 0), as TIA's own exports do.</summary>
        /// <summary>The network's Title or Comment (MultilingualText), if it has one.</summary>
        private static XElement? NetworkText(XElement unit, string composition) =>
            unit.Elements().FirstOrDefault(e => e.Name.LocalName == "ObjectList")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "MultilingualText" && (string?)e.Attribute("CompositionName") == composition);

        private static bool HasText(XElement? text) =>
            text != null && text.Descendants().Any(e => e.Name.LocalName == "Text" && !string.IsNullOrWhiteSpace(e.Value));

        /// <summary>
        /// Copies the replaced network's title and comment onto the new network where the new one
        /// has none. Returns which were copied.
        /// </summary>
        private static IEnumerable<string> KeepNetworkTexts(XElement original, XElement incoming)
        {
            var kept = new List<string>();

            foreach (var composition in new[] { "Title", "Comment" })
            {
                var old = NetworkText(original, composition);
                var now = NetworkText(incoming, composition);
                if (!HasText(old) || HasText(now))
                {
                    continue;
                }

                var copy = new XElement(old!);
                if (now != null)
                {
                    now.ReplaceWith(copy);
                }
                else
                {
                    ObjectListOf(incoming).Add(copy);
                }

                kept.Add(composition);
            }

            return kept;
        }

        /// <summary>
        /// Sets the network title in every language the title has. A network without a title
        /// element gets one shaped like its comment (same languages).
        /// </summary>
        private static void SetNetworkTitle(XElement unit, string title)
        {
            var text = NetworkText(unit, "Title");
            if (text == null)
            {
                var comment = NetworkText(unit, "Comment")
                    ?? throw new PortalException(PortalErrorCode.InvalidParams, "The new network has neither a title nor a comment element to take the title from.");

                text = new XElement(comment);
                text.SetAttributeValue("CompositionName", "Title");
                comment.AddAfterSelf(text);
            }

            var items = text.Descendants().Where(e => e.Name.LocalName == "Text").ToList();
            if (items.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "The network title has no language entries to write the title into.");
            }

            foreach (var item in items)
            {
                item.Value = title;
            }
        }

        private static XElement ObjectListOf(XElement unit)
        {
            var list = unit.Elements().FirstOrDefault(e => e.Name.LocalName == "ObjectList");
            if (list == null)
            {
                list = new XElement(unit.Name.Namespace + "ObjectList");
                unit.Add(list);
            }

            return list;
        }

        private static void RenumberIds(XDocument doc)
        {
            var next = 0;
            foreach (var attribute in doc.Descendants().Select(e => e.Attribute("ID")).Where(a => a != null))
            {
                attribute!.Value = next.ToString("X", CultureInfo.InvariantCulture);
                next++;
            }
        }

        /// <summary>
        /// Puts the parts of a LAD/FBD network into flow order (see the header). Access elements
        /// keep their places; parts and calls are re-sorted within their own slots. Returns how
        /// many changed position (0 when the order was already right).
        /// </summary>
        internal static int SortFlgNetParts(XElement flgNet)
        {
            var parts = flgNet.Elements().FirstOrDefault(e => e.Name.LocalName == "Parts");
            var wires = flgNet.Elements().FirstOrDefault(e => e.Name.LocalName == "Wires");
            if (parts == null || wires == null)
            {
                return 0;
            }

            var children = parts.Elements().ToList();
            var slots = children.Select((e, i) => (e, i)).Where(x => x.e.Name.LocalName != "Access").Select(x => x.i).ToList();
            var nodes = slots.Select(i => children[i]).ToList();
            var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                var uid = (string?)node.Attribute("UId");
                if (uid != null && !byId.ContainsKey(uid))
                {
                    byId[uid] = node;
                }
            }

            var roots = new List<string>();
            var successors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var predecessors = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var wire in wires.Elements())
            {
                var ends = wire.Elements().ToList();
                if (ends.Count < 2)
                {
                    continue;
                }

                var targets = ends.Skip(1)
                    .Where(e => e.Name.LocalName == "NameCon")
                    .Select(e => (string?)e.Attribute("UId"))
                    .Where(id => id != null && byId.ContainsKey(id))
                    .Select(id => id!)
                    .ToList();

                var source = ends[0];
                if (source.Name.LocalName == "Powerrail")
                {
                    roots.AddRange(targets);
                }
                else if (source.Name.LocalName == "NameCon" && (string?)source.Attribute("UId") is string from && byId.ContainsKey(from))
                {
                    if (!successors.TryGetValue(from, out var list))
                    {
                        successors[from] = list = new List<string>();
                    }

                    foreach (var to in targets)
                    {
                        list.Add(to);
                        if (!predecessors.TryGetValue(to, out var set))
                        {
                            predecessors[to] = set = new HashSet<string>(StringComparer.Ordinal);
                        }

                        set.Add(from);
                    }
                }
            }

            var order = new List<XElement>();
            var placed = new HashSet<string>(StringComparer.Ordinal);

            // Explicit stack instead of recursion: a network can hold 900+ parts.
            void Visit(string start, bool force)
            {
                var stack = new Stack<(string Id, bool Force)>();
                stack.Push((start, force));

                while (stack.Count > 0)
                {
                    var (id, f) = stack.Pop();
                    if (placed.Contains(id))
                    {
                        continue;
                    }

                    if (!f && predecessors.TryGetValue(id, out var before) && before.Any(p => !placed.Contains(p)))
                    {
                        continue;
                    }

                    placed.Add(id);
                    order.Add(byId[id]);

                    if (successors.TryGetValue(id, out var next))
                    {
                        for (var i = next.Count - 1; i >= 0; i--)
                        {
                            stack.Push((next[i], false));
                        }
                    }
                }
            }

            foreach (var root in roots)
            {
                Visit(root, false);
            }

            foreach (var id in byId.Keys.ToList())
            {
                Visit(id, false);
            }

            foreach (var id in byId.Keys.ToList())
            {
                Visit(id, true);
            }

            // Nodes without a UId keep their relative order at the end.
            order.AddRange(nodes.Where(n => !order.Contains(n)));

            var moved = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                if (!ReferenceEquals(children[slots[i]], order[i]))
                {
                    moved++;
                }

                children[slots[i]] = order[i];
            }

            if (moved > 0)
            {
                foreach (var child in parts.Elements().ToList())
                {
                    child.Remove();
                }

                foreach (var child in children)
                {
                    parts.Add(child);
                }
            }

            return moved;
        }
    }
}
