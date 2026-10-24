using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Screen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Classic WinCC (Comfort/Basic panels, HmiTarget) screens, read-only: listing the screens,
        // templates and pop-up screens, and reading the items of one of them.
        //
        // Callers: GetComfortScreens and GetComfortScreenItems in McpServer.HmiComfort.cs.
        // Affected API: additive. Writes one temporary XML export per item read and deletes it.
        //
        // Openness constraints that shape this file (V20):
        // - Classic screen items have no object model of their own in Openness: Screen,
        //   ScreenTemplate and ScreenPopup only offer Export. The items are therefore read from the
        //   exported XML, where every object is an element named after its type
        //   ('Hmi.Screen.Button'), with an AttributeList (values), a LinkList (references by name,
        //   e.g. tags) and an ObjectList (child objects: layers, items, texts, events, dynamics).
        // - The XML schema differs per item type and is not documented item by item, so the parser
        //   is generic: every element below a layer whose type starts with 'Hmi.Screen.' and has a
        //   position is an item; its links are reported as bindings and its events as the list of
        //   functions they call.

        public sealed class ComfortScreenInfo
        {
            public string Name { get; set; } = string.Empty;

            /// <summary>Screen, Template or Popup.</summary>
            public string Kind { get; set; } = string.Empty;

            /// <summary>User folder path, e.g. 'Production/Reports'; empty at the root.</summary>
            public string Folder { get; set; } = string.Empty;
        }

        public sealed class ComfortScreenRead
        {
            public string Name { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string? Template { get; set; }
            public int? ScreenNumber { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }
            public Dictionary<string, string>? Attributes { get; set; }
            public List<ComfortScreenItemDescription> Items { get; set; } = new List<ComfortScreenItemDescription>();
            public List<ComfortEventDescription>? ScreenEvents { get; set; }

            /// <summary>The export file, when the caller asked to keep it.</summary>
            public string? ExportFile { get; set; }
        }

        public sealed class ComfortScreenItemDescription
        {
            public string Name { get; set; } = string.Empty;

            /// <summary>Type without the 'Hmi.Screen.' prefix, e.g. Button, IOField, TextField, Rectangle, Faceplate.</summary>
            public string Kind { get; set; } = string.Empty;

            public int? Layer { get; set; }
            public bool Visible { get; set; } = true;
            public int? Left { get; set; }
            public int? Top { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }

            /// <summary>The text the item shows (Text, or TextOff for buttons and switches), plain, English when present.</summary>
            public string? Text { get; set; }

            /// <summary>Every multilingual text of the item by composition name (Text, TextOn, TextOff, HelpText, ToolTipText, ...), unless 'text' is the only one.</summary>
            public Dictionary<string, string>? Texts { get; set; }

            /// <summary>Names the item refers to: tags, text lists, graphic lists, faceplate types, ...</summary>
            public List<ComfortLinkDescription> Dynamizations { get; set; } = new List<ComfortLinkDescription>();

            public List<ComfortEventDescription>? Events { get; set; }

            /// <summary>All plain attributes (colors, fonts, formats, ...), with includeAttributes only.</summary>
            public Dictionary<string, string>? Attributes { get; set; }

            /// <summary>Items inside a group, or the content of a faceplate instance, with their own positions.</summary>
            public List<ComfortScreenItemDescription>? Children { get; set; }
        }

        public sealed class ComfortLinkDescription
        {
            /// <summary>Where the link sits, e.g. 'ProcessValue' or 'AppearanceDynamic/Tag'.</summary>
            public string PropertyName { get; set; } = string.Empty;

            /// <summary>Type of the object that holds the link, e.g. 'IOField', 'Dynamic.AppearanceDynamic'.</summary>
            public string Type { get; set; } = string.Empty;

            /// <summary>The linked object's name: a tag, text list, graphic list, screen, ...</summary>
            public string? Tag { get; set; }
        }

        public sealed class ComfortEventDescription
        {
            /// <summary>e.g. Click, Press, Release, Loaded.</summary>
            public string EventType { get; set; } = string.Empty;

            /// <summary>One line per function call, e.g. 'ActivateScreen(Screen_2, 0)' or 'SetBit(Tag_1)'.</summary>
            public List<string> Functions { get; set; } = new List<string>();
        }

        private const string ComfortKindScreen = "Screen";
        private const string ComfortKindTemplate = "Template";
        private const string ComfortKindPopup = "Popup";

        #region list

        public List<ComfortScreenInfo> GetComfortScreens(string softwarePath, string regexName = "")
        {
            return Operation.Run(_logger, nameof(GetComfortScreens), PortalErrorCode.NotFound,
                () =>
                {
                    var target = RequireHmiTarget(softwarePath);
                    var regex = ComfortNameRegex(regexName);
                    var list = new List<ComfortScreenInfo>();

                    foreach (var (screen, folder) in ComfortScreens(target))
                    {
                        if (regex == null || regex.IsMatch(screen.Name))
                        {
                            list.Add(new ComfortScreenInfo { Name = screen.Name, Kind = ComfortKindScreen, Folder = folder });
                        }
                    }

                    foreach (var (template, folder) in ComfortTemplates(target))
                    {
                        if (regex == null || regex.IsMatch(template.Name))
                        {
                            list.Add(new ComfortScreenInfo { Name = template.Name, Kind = ComfortKindTemplate, Folder = folder });
                        }
                    }

                    foreach (var (popup, folder) in ComfortPopups(target))
                    {
                        if (regex == null || regex.IsMatch(popup.Name))
                        {
                            list.Add(new ComfortScreenInfo { Name = popup.Name, Kind = ComfortKindPopup, Folder = folder });
                        }
                    }

                    return list;
                },
                ("softwarePath", softwarePath));
        }

        #endregion

        #region items

        /// <param name="kind">Screen, Template or Popup; empty looks in that order.</param>
        /// <param name="keepExportPath">Absolute directory to keep the export file in; empty deletes it.</param>
        public ComfortScreenRead GetComfortScreenItems(string softwarePath, string screenName, string kind = "", string regexName = "",
            bool includeAttributes = false, string keepExportPath = "")
        {
            return Operation.Run(_logger, nameof(GetComfortScreenItems), PortalErrorCode.NotFound,
                () =>
                {
                    var target = RequireHmiTarget(softwarePath);
                    var (name, foundKind, export) = FindComfortScreen(target, screenName, kind);
                    var regex = ComfortNameRegex(regexName);

                    if (!string.IsNullOrWhiteSpace(keepExportPath) && !Path.IsPathRooted(keepExportPath))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"keepExportPath '{keepExportPath}' must be an absolute directory.");
                    }

                    var directory = CreateTempExportDirectory();

                    try
                    {
                        var file = new FileInfo(Path.Combine(directory, SafeFileName(name) + ".xml"));
                        export(file);

                        var read = ParseComfortScreen(XDocument.Load(file.FullName), name, foundKind, regex, includeAttributes);

                        if (!string.IsNullOrWhiteSpace(keepExportPath))
                        {
                            Directory.CreateDirectory(keepExportPath);
                            var kept = Path.Combine(keepExportPath, SafeFileName(name) + ".xml");
                            File.Copy(file.FullName, kept, true);
                            read.ExportFile = kept;
                        }

                        return read;
                    }
                    finally
                    {
                        DeleteTempExportDirectory(directory);
                    }
                },
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private (string Name, string Kind, Action<FileInfo> Export) FindComfortScreen(HmiTarget target, string screenName, string kind)
        {
            if (string.IsNullOrWhiteSpace(screenName))
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "screenName is required.");
            }

            // 'Folder/Screen' as GetComfortScreens shows it, or the bare name.
            var slash = screenName.LastIndexOf('/');
            var wantedFolder = slash < 0 ? null : screenName.Substring(0, slash);
            var wantedName = slash < 0 ? screenName : screenName.Substring(slash + 1);

            bool Matches(string name, string folder) =>
                string.Equals(name, wantedName, StringComparison.OrdinalIgnoreCase) &&
                (wantedFolder == null || string.Equals(folder, wantedFolder, StringComparison.OrdinalIgnoreCase));

            bool Wanted(string k) => string.IsNullOrWhiteSpace(kind) || string.Equals(kind, k, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(kind) && !new[] { ComfortKindScreen, ComfortKindTemplate, ComfortKindPopup }
                    .Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"kind '{kind}' is not valid. Use Screen, Template, Popup or an empty string.");
            }

            if (Wanted(ComfortKindScreen))
            {
                foreach (var (screen, folder) in ComfortScreens(target))
                {
                    if (Matches(screen.Name, folder))
                    {
                        return (screen.Name, ComfortKindScreen, f => screen.Export(f, ExportOptions.WithDefaults));
                    }
                }
            }

            if (Wanted(ComfortKindTemplate))
            {
                foreach (var (template, folder) in ComfortTemplates(target))
                {
                    if (Matches(template.Name, folder))
                    {
                        return (template.Name, ComfortKindTemplate, f => template.Export(f, ExportOptions.WithDefaults));
                    }
                }
            }

            if (Wanted(ComfortKindPopup))
            {
                foreach (var (popup, folder) in ComfortPopups(target))
                {
                    if (Matches(popup.Name, folder))
                    {
                        return (popup.Name, ComfortKindPopup, f => popup.Export(f, ExportOptions.WithDefaults));
                    }
                }
            }

            throw new PortalException(PortalErrorCode.NotFound,
                $"No classic HMI screen '{screenName}' in '{target.Name}'. Use 'GetComfortScreens' to list the screens, templates and pop-ups.");
        }

        #endregion

        #region xml parsing

        private static ComfortScreenRead ParseComfortScreen(XDocument document, string name, string kind, Regex? regex, bool includeAttributes)
        {
            var root = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("Hmi.", StringComparison.Ordinal))
                ?? throw new PortalException(PortalErrorCode.ExportFailed, $"The export of '{name}' has no HMI screen object.");

            var attributes = ComfortAttributes(root);
            var read = new ComfortScreenRead
            {
                Name = name,
                Kind = kind,
                Template = ComfortLinks(root).FirstOrDefault(l => l.Link == "Template").Target,
                ScreenNumber = ComfortInt(attributes, "Number"),
                Width = ComfortInt(attributes, "Width"),
                Height = ComfortInt(attributes, "Height"),
                Attributes = includeAttributes ? attributes : null
            };

            var screenEvents = ComfortEvents(root);
            read.ScreenEvents = screenEvents.Count > 0 ? screenEvents : null;

            foreach (var child in ComfortChildObjects(root))
            {
                if (child.Name.LocalName == "Hmi.Screen.ScreenLayer")
                {
                    var layer = ComfortInt(ComfortAttributes(child), "Index");

                    foreach (var item in ComfortChildObjects(child).Where(IsComfortItem))
                    {
                        AddComfortItem(read.Items, item, layer, regex, includeAttributes);
                    }
                }
                else if (IsComfortItem(child))
                {
                    AddComfortItem(read.Items, child, null, regex, includeAttributes);
                }
            }

            return read;
        }

        private static void AddComfortItem(List<ComfortScreenItemDescription> list, XElement element, int? layer, Regex? regex, bool includeAttributes)
        {
            var item = DescribeComfortItem(element, layer, includeAttributes);

            if (regex == null || regex.IsMatch(item.Name))
            {
                list.Add(item);
            }
        }

        private static bool IsComfortItem(XElement element)
        {
            var type = element.Name.LocalName;

            return type.StartsWith("Hmi.Screen.", StringComparison.Ordinal) &&
                   type != "Hmi.Screen.ScreenLayer" &&
                   element.Element("AttributeList")?.Element("Left") != null;
        }

        private static ComfortScreenItemDescription DescribeComfortItem(XElement element, int? layer, bool includeAttributes)
        {
            var attributes = ComfortAttributes(element);
            var item = new ComfortScreenItemDescription
            {
                Name = attributes.TryGetValue("ObjectName", out var objectName) ? objectName
                     : attributes.TryGetValue("Name", out var plainName) ? plainName : string.Empty,
                Kind = element.Name.LocalName.Substring("Hmi.Screen.".Length),
                Layer = layer,
                Visible = !attributes.TryGetValue("Visible", out var visible) || !string.Equals(visible, "false", StringComparison.OrdinalIgnoreCase),
                Left = ComfortInt(attributes, "Left"),
                Top = ComfortInt(attributes, "Top"),
                Width = ComfortInt(attributes, "Width"),
                Height = ComfortInt(attributes, "Height"),
                Attributes = includeAttributes ? attributes : null
            };

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var children = new List<ComfortScreenItemDescription>();
            var events = ComfortEvents(element);

            foreach (var (link, target, owner, path) in ComfortLinksBelow(element))
            {
                item.Dynamizations.Add(new ComfortLinkDescription
                {
                    // A tag on a dynamic is named after the property it drives ('ProcessValue');
                    // other links keep their own name ('TextList', 'Visibility/Tag').
                    PropertyName = path.Length == 0 ? link : link == "Tag" && owner == "Dynamic.TagConnectionDynamic" ? path : path + "/" + link,
                    Type = owner,
                    Tag = target
                });
            }

            foreach (var child in ComfortChildObjects(element))
            {
                var type = child.Name.LocalName;

                if (IsComfortItem(child))
                {
                    children.Add(DescribeComfortItem(child, layer, includeAttributes));
                }
                else if (type.Equals("MultilingualText", StringComparison.OrdinalIgnoreCase))
                {
                    var text = ComfortText(child);

                    if (text != null)
                    {
                        texts[(string?)child.Attribute("CompositionName") ?? type] = text;
                    }
                }
            }

            if (texts.Count > 0)
            {
                // Help and tooltip texts are not what the item shows, so they never become 'text'.
                item.Text = texts.TryGetValue("Text", out var main) ? main
                          : texts.TryGetValue("TextOff", out var off) ? off
                          : null;
                item.Texts = texts.Count > 1 || item.Text == null ? texts : null;
            }

            if (attributes.TryGetValue("Text", out var plainText) && item.Text == null)
            {
                item.Text = PlainText(plainText);
            }

            item.Events = events.Count > 0 ? events : null;
            item.Children = children.Count > 0 ? children : null;

            return item;
        }

        /// <summary>The plain attributes of one object: AttributeList children without child elements.</summary>
        private static Dictionary<string, string> ComfortAttributes(XElement element)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var attribute in element.Element("AttributeList")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (!attribute.HasElements)
                {
                    result[attribute.Name.LocalName] = attribute.Value;
                }
            }

            return result;
        }

        private static IEnumerable<XElement> ComfortChildObjects(XElement element)
        {
            return element.Element("ObjectList")?.Elements() ?? Enumerable.Empty<XElement>();
        }

        private static int? ComfortInt(Dictionary<string, string> attributes, string name)
        {
            return attributes.TryGetValue(name, out var value) &&
                   int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : (int?)null;
        }

        /// <summary>The direct links of one object: link element name and the target's name.</summary>
        private static IEnumerable<(string Link, string? Target)> ComfortLinks(XElement element)
        {
            foreach (var link in element.Element("LinkList")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                yield return (link.Name.LocalName, ComfortLinkTarget(link));
            }
        }

        private static string? ComfortLinkTarget(XElement link)
        {
            // An open link names its target in a child <Name>; a link inside the file points at an ID.
            var name = link.Element("Name")?.Value;

            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            return string.IsNullOrEmpty(link.Value) ? (string?)link.Attribute("TargetID") : link.Value;
        }

        /// <summary>
        /// The links of an item and of its non-item, non-event child objects (dynamics, texts,
        /// faceplate interface values), with the composition path from the item to the holder.
        /// </summary>
        private static IEnumerable<(string Link, string? Target, string Owner, string Path)> ComfortLinksBelow(XElement item)
        {
            var stack = new Stack<(XElement Element, string Path)>();
            stack.Push((item, string.Empty));

            while (stack.Count > 0)
            {
                var (element, path) = stack.Pop();
                var owner = element.Name.LocalName.StartsWith("Hmi.", StringComparison.Ordinal)
                    ? element.Name.LocalName.Substring(4)
                    : element.Name.LocalName;

                if (element == item)
                {
                    owner = owner.StartsWith("Screen.", StringComparison.Ordinal) ? owner.Substring(7) : owner;
                }

                foreach (var (link, target) in ComfortLinks(element))
                {
                    yield return (link, target, owner, path);
                }

                foreach (var child in ComfortChildObjects(element).Reverse())
                {
                    if (IsComfortItem(child) || IsComfortEvent(child) ||
                        child.Name.LocalName.Equals("MultilingualText", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // A property object ('Properties') is named by its Name attribute ('ProcessValue');
                    // the 'Dynamic' composition below it adds nothing to the path.
                    var composition = (string?)child.Attribute("CompositionName") ?? child.Name.LocalName;
                    var name = child.Name.LocalName == "Hmi.Screen.Property" && ComfortAttributes(child).TryGetValue("Name", out var property)
                        ? property
                        : composition;

                    stack.Push((child, composition == "Dynamic" ? path : path.Length == 0 ? name : path + "/" + name));
                }
            }
        }

        private static bool IsComfortEvent(XElement element)
        {
            return element.Name.LocalName.StartsWith("Hmi.Event.", StringComparison.Ordinal);
        }

        private static List<ComfortEventDescription> ComfortEvents(XElement element)
        {
            var result = new List<ComfortEventDescription>();

            foreach (var child in ComfortChildObjects(element).Where(IsComfortEvent))
            {
                var attributes = ComfortAttributes(child);
                var description = new ComfortEventDescription
                {
                    EventType = attributes.TryGetValue("Name", out var named) ? named
                              : attributes.TryGetValue("EventType", out var type) ? type
                              : ((string?)child.Attribute("CompositionName") ?? child.Name.LocalName)
                };

                foreach (var call in child.Descendants().Where(IsComfortInvocation))
                {
                    description.Functions.Add(DescribeComfortInvocation(call));
                }

                if (description.Functions.Count > 0)
                {
                    result.Add(description);
                }
            }

            return result;
        }

        // V20 exports a function list as Hmi.Event.FunctionListEntry (Name = function, Type =
        // SystemFunction or Script) with Hmi.Event.FunctionListEntryParameter children.
        private static bool IsComfortInvocation(XElement element)
        {
            var type = element.Name.LocalName;

            return type == "Hmi.Event.FunctionListEntry" ||
                   (type.StartsWith("Hmi.", StringComparison.Ordinal) && type.EndsWith("Invocation", StringComparison.Ordinal));
        }

        /// <summary>'Function(name=value, ...)': the called function, its parameters in order.</summary>
        private static string DescribeComfortInvocation(XElement call)
        {
            var function = (ComfortAttributes(call).TryGetValue("Name", out var named) ? named : null)
                ?? ComfortLinks(call).Select(l => l.Target).FirstOrDefault(t => !string.IsNullOrEmpty(t))
                ?? call.Name.LocalName.Substring(4);

            var parameters = new List<string>();

            foreach (var parameter in ComfortChildObjects(call))
            {
                var attributes = ComfortAttributes(parameter);
                var linked = ComfortLinks(parameter).Select(l => l.Target).FirstOrDefault(t => !string.IsNullOrEmpty(t));
                var value = linked
                    ?? (attributes.TryGetValue("Value", out var v) ? v : null)
                    ?? ComfortChildObjects(parameter).SelectMany(ComfortLinks).Select(l => l.Target).FirstOrDefault(t => !string.IsNullOrEmpty(t))
                    ?? ComfortText(parameter.Descendants().FirstOrDefault(d => d.Name.LocalName.Equals("MultilingualText", StringComparison.OrdinalIgnoreCase)));

                var name = (string?)parameter.Attribute("Name") ?? (attributes.TryGetValue("Name", out var n) ? n : null);
                parameters.Add(name == null ? value ?? string.Empty : $"{name}={value}");
            }

            return $"{function}({string.Join(", ", parameters)})";
        }

        /// <summary>The plain text of a MultilingualText element, preferring English, else the first non-empty language.</summary>
        private static string? ComfortText(XElement? text)
        {
            if (text == null)
            {
                return null;
            }

            var items = text.Descendants()
                .Where(d => d.Name.LocalName.Equals("MultilingualTextItem", StringComparison.OrdinalIgnoreCase))
                .Select(d =>
                {
                    var attributes = d.Element("AttributeList");
                    return (Culture: attributes?.Element("Culture")?.Value ?? string.Empty,
                            Text: PlainText(attributes?.Element("Text")?.Value));
                })
                .Where(t => t.Text != null)
                .ToList();

            if (items.Count == 0)
            {
                return null;
            }

            return items.FirstOrDefault(t => t.Culture.StartsWith("en", StringComparison.OrdinalIgnoreCase)).Text ?? items[0].Text;
        }

        #endregion

        #region project access

        private HmiTarget RequireHmiTarget(string softwarePath)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");
            }

            var software = GetSoftwareContainer(softwarePath)?.Software;

            return software as HmiTarget
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    software is global::Siemens.Engineering.HmiUnified.HmiSoftware
                        ? $"'{softwarePath}' is a WinCC Unified HMI; use 'GetHmiScreens' and 'GetHmiScreenItems' for it."
                        : $"No classic WinCC (Comfort/Basic panel) HMI software at '{softwarePath}'. HMI software paths look like 'HMI_1/HMI_RT_1'; 'GetProjectTree' shows them as 'HmiTarget'.");
        }

        private static Regex? ComfortNameRegex(string regexName)
        {
            if (string.IsNullOrEmpty(regexName))
            {
                return null;
            }

            try
            {
                return new Regex(regexName, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{regexName}' is not a valid regular expression: {ex.Message}", null, ex);
            }
        }

        private static IEnumerable<(Screen Screen, string Folder)> ComfortScreens(HmiTarget target)
        {
            foreach (var screen in target.ScreenFolder.Screens)
            {
                yield return (screen, string.Empty);
            }

            foreach (var entry in ComfortScreensIn(target.ScreenFolder.Folders, string.Empty))
            {
                yield return entry;
            }
        }

        private static IEnumerable<(Screen Screen, string Folder)> ComfortScreensIn(ScreenUserFolderComposition folders, string path)
        {
            foreach (var folder in folders)
            {
                var folderPath = path.Length == 0 ? folder.Name : path + "/" + folder.Name;

                foreach (var screen in folder.Screens)
                {
                    yield return (screen, folderPath);
                }

                foreach (var entry in ComfortScreensIn(folder.Folders, folderPath))
                {
                    yield return entry;
                }
            }
        }

        private static IEnumerable<(ScreenTemplate Template, string Folder)> ComfortTemplates(HmiTarget target)
        {
            foreach (var template in target.ScreenTemplateFolder.ScreenTemplates)
            {
                yield return (template, string.Empty);
            }

            foreach (var entry in ComfortTemplatesIn(target.ScreenTemplateFolder.Folders, string.Empty))
            {
                yield return entry;
            }
        }

        private static IEnumerable<(ScreenTemplate Template, string Folder)> ComfortTemplatesIn(ScreenTemplateUserFolderComposition folders, string path)
        {
            foreach (var folder in folders)
            {
                var folderPath = path.Length == 0 ? folder.Name : path + "/" + folder.Name;

                foreach (var template in folder.ScreenTemplates)
                {
                    yield return (template, folderPath);
                }

                foreach (var entry in ComfortTemplatesIn(folder.Folders, folderPath))
                {
                    yield return entry;
                }
            }
        }

        private static IEnumerable<(ScreenPopup Popup, string Folder)> ComfortPopups(HmiTarget target)
        {
            // Basic panels have no pop-up screens; the folder is then missing or refuses access.
            ScreenPopupSystemFolder? root;

            try
            {
                root = target.ScreenPopupFolder;
            }
            catch (Exception)
            {
                yield break;
            }

            if (root == null)
            {
                yield break;
            }

            foreach (var popup in root.ScreenPopups)
            {
                yield return (popup, string.Empty);
            }

            foreach (var entry in ComfortPopupsIn(root.Folders, string.Empty))
            {
                yield return entry;
            }
        }

        private static IEnumerable<(ScreenPopup Popup, string Folder)> ComfortPopupsIn(ScreenPopupUserFolderComposition folders, string path)
        {
            foreach (var folder in folders)
            {
                var folderPath = path.Length == 0 ? folder.Name : path + "/" + folder.Name;

                foreach (var popup in folder.ScreenPopups)
                {
                    yield return (popup, folderPath);
                }

                foreach (var entry in ComfortPopupsIn(folder.Folders, folderPath))
                {
                    yield return entry;
                }
            }
        }

        #endregion
    }
}
