using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Parts;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // WinCC Unified screens: reading the items of one screen, and creating screens, items,
        // tag bindings and HMI tags (REQUESTS.md P9-2, "start small").
        //
        // Callers: GetHmiScreenItems and the HMI write tools in McpServer.HmiWrite.cs; the write
        // tools are only registered with '--allow-write'. Affected API: additive.
        // Reads/writes no data files; writes change the open project in memory until SaveProject.
        //
        // Openness constraints that shape this file (V20, read from the installed
        // Siemens.Engineering.dll):
        // - Screen items are created with ScreenItems.Create<T>(name); there is no untyped
        //   Create, so the supported item kinds are an explicit list (HmiItemKinds).
        // - Position and size live on two unrelated bases: HmiWidgetBase (buttons, IO fields,
        //   labels) and HmiSurfaceShapeBase (text, rectangle).
        // - Texts are MultilingualText with one item per project language and no setter of
        //   their own; SetHmiText writes every language.
        // - A tag binding is a TagDynamization created on the item's property name
        //   (Dynamizations.Create<TagDynamization>("ProcessValue")).

        #region read

        public List<HmiScreenItemBase> GetHmiScreenItems(string softwarePath, string screenName, string regexName = "")
        {
            return GetHmiScreenItems(softwarePath, screenName, regexName, out _);
        }

        /// <param name="unreadable">items the screen holds that Openness returns as null - kinds V20 Openness has no
        /// type for, e.g. a PLC code view - so a caller can say they exist instead of leaving them out silently</param>
        public List<HmiScreenItemBase> GetHmiScreenItems(string softwarePath, string screenName, string regexName, out int unreadable)
        {
            var nulls = 0;

            var result = Operation.Run(_logger, nameof(GetHmiScreenItems), PortalErrorCode.NotFound,
                () =>
                {
                    var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);
                    var list = new List<HmiScreenItemBase>();

                    foreach (var item in screen.ScreenItems)
                    {
                        if (item == null)
                        {
                            nulls++;
                            continue;
                        }

                        if (!string.IsNullOrEmpty(regexName))
                        {
                            try
                            {
                                if (!Regex.IsMatch(item.Name, regexName, RegexOptions.IgnoreCase))
                                {
                                    continue;
                                }
                            }
                            catch (ArgumentException ex)
                            {
                                throw new PortalException(PortalErrorCode.InvalidParams,
                                    $"'{regexName}' is not a valid regular expression: {ex.Message}", null, ex);
                            }
                        }

                        list.Add(item);
                    }

                    return list;
                },
                ("softwarePath", softwarePath), ("screenName", screenName));

            unreadable = nulls;

            return result;
        }

        /// <summary>Flat description of one screen item, used by the read tool and as the read-back of every write.</summary>
        public HmiScreenItemDescription DescribeHmiScreenItem(HmiScreenItemBase item) => DescribeHmiScreenItem(item, false);

        /// <summary>
        /// With 'includeScripts' also the event handler scripts and script dynamization code. Only the
        /// read tool asks for them: the write tools' read-back runs inside their transaction, where
        /// any unexpected exception would make TIA Portal refuse the commit.
        /// </summary>
        public HmiScreenItemDescription DescribeHmiScreenItem(HmiScreenItemBase item, bool includeScripts)
        {
            var description = new HmiScreenItemDescription
            {
                Name = item.Name,
                Kind = KindOf(item),
                Visible = item.Visible
            };

            switch (item)
            {
                case HmiWidgetBase widget:
                    description.Left = widget.Left;
                    description.Top = widget.Top;
                    description.Width = widget.Width;
                    description.Height = widget.Height;
                    break;
                case HmiSurfaceShapeBase shape:
                    description.Left = shape.Left;
                    description.Top = shape.Top;
                    description.Width = shape.Width;
                    description.Height = shape.Height;
                    break;
                case HmiWindowBase window:
                    description.Left = window.Left;
                    description.Top = window.Top;
                    description.Width = window.Width;
                    description.Height = window.Height;
                    break;
            }

            if (item is HmiFaceplateContainer faceplate)
            {
                description.FaceplateType = faceplate.ContainedType;
                description.FaceplateInterface = faceplate.Interface.Select(DescribeFaceplateInterface).ToList();
            }

            description.Text = item switch
            {
                HmiButton button => Text(button.Text),
                HmiLabel label => Text(label.Text),
                HmiText text => Text(text.Text),
                _ => null
            };

            if (item is HmiIOField ioField)
            {
                description.ProcessValue = ioField.ProcessValue;
                description.OutputFormat = ioField.OutputFormat;
            }

            foreach (var dynamization in item.Dynamizations)
            {
                description.Dynamizations.Add(new HmiDynamizationDescription
                {
                    PropertyName = dynamization.PropertyName,
                    Type = dynamization.DynamizationType.ToString(),
                    Tag = (dynamization as TagDynamization)?.Tag,
                    ScriptCode = includeScripts
                        ? (dynamization as global::Siemens.Engineering.HmiUnified.UI.Dynamization.Script.ScriptDynamization)?.ScriptCode
                        : null
                });
            }

            if (includeScripts)
            {
                var events = DescribeHmiEvents(item);
                description.Events = events.Count > 0 ? events : null;
            }

            return description;
        }

        /// <summary>Event handler scripts of the screen itself (Loaded, Cleared, ...).</summary>
        public List<HmiEventDescription> DescribeHmiScreenEvents(string softwarePath, string screenName)
        {
            return Operation.Run(_logger, nameof(DescribeHmiScreenEvents), PortalErrorCode.NotFound,
                () => DescribeHmiEvents(RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName)),
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        /// <summary>
        /// Event handlers are typed per item kind (HmiButtonEventHandlerComposition, ...) with no
        /// common base, so they are read by reflection: EventType and Script on each handler.
        /// </summary>
        public static List<HmiEventDescription> DescribeHmiEvents(object owner)
        {
            var result = new List<HmiEventDescription>();

            if (owner.GetType().GetProperty("EventHandlers")?.GetValue(owner) is not System.Collections.IEnumerable handlers)
            {
                return result;
            }

            foreach (var handler in handlers)
            {
                var eventType = handler.GetType().GetProperty("EventType")?.GetValue(handler);

                if (handler.GetType().GetProperty("Script")?.GetValue(handler) is not global::Siemens.Engineering.HmiUnified.UI.Dynamization.Script.IHmiScript script)
                {
                    continue;
                }

                result.Add(new HmiEventDescription
                {
                    EventType = eventType?.ToString() ?? string.Empty,
                    ScriptCode = script.ScriptCode,
                    GlobalDefinitionAreaScriptCode = string.IsNullOrWhiteSpace(script.GlobalDefinitionAreaScriptCode) ? null : script.GlobalDefinitionAreaScriptCode,
                    Async = script.Async
                });
            }

            return result;
        }

        /// <summary>The plain text of a Unified rich text value such as '&lt;body&gt;&lt;p&gt;Main&lt;/p&gt;&lt;/body&gt;'; null when empty.</summary>
        public static string? PlainText(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var text = Regex.Replace(value, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            text = System.Net.WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", string.Empty)).Trim();

            return text.Length == 0 ? null : text;
        }

        #endregion

        #region write

        /// <summary>Screen item kinds the create tool accepts, mapped to their Openness type name.</summary>
        public static readonly IReadOnlyDictionary<string, string> HmiItemKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Button"] = nameof(HmiButton),
            ["IOField"] = nameof(HmiIOField),
            // HmiLabel itself is refused by Create ("Not supported"); TIA Portal's "Text field" is
            // HmiTextBox, which derives from HmiLabel, so that is what a Label is created as.
            ["Label"] = nameof(HmiTextBox),
            ["Text"] = nameof(HmiText),
            ["Rectangle"] = nameof(HmiRectangle)
        };

        public HmiScreen CreateHmiScreen(string softwarePath, string name, uint? width = null, uint? height = null, ushort? screenNumber = null)
        {
            return Operation.Run(_logger, nameof(CreateHmiScreen), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    var hmi = RequireHmiSoftware(softwarePath);

                    if (FindHmiScreen(hmi, name) != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"A screen named '{name}' already exists in '{softwarePath}'.");
                    }

                    var screen = hmi.Screens.Create(name);

                    if (width.HasValue) screen.Width = width.Value;
                    if (height.HasValue) screen.Height = height.Value;
                    if (screenNumber.HasValue) screen.ScreenNumber = screenNumber.Value;

                    return screen;
                },
                ("softwarePath", softwarePath), ("name", name));
        }

        public bool DeleteHmiScreen(string softwarePath, string screenName)
        {
            return Operation.Run(_logger, nameof(DeleteHmiScreen), PortalErrorCode.DeleteFailed,
                () =>
                {
                    RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName).Delete();
                    return true;
                },
                ("softwarePath", softwarePath), ("screenName", screenName));
        }

        public HmiScreenItemBase CreateHmiScreenItem(
            string softwarePath,
            string screenName,
            string kind,
            string name,
            int? left = null,
            int? top = null,
            uint? width = null,
            uint? height = null,
            string? text = null,
            string? tag = null,
            string? eventType = null,
            string? scriptCode = null)
        {
            return Operation.Run(_logger, nameof(CreateHmiScreenItem), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    if (string.IsNullOrWhiteSpace(eventType) != string.IsNullOrWhiteSpace(scriptCode))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'eventType' and 'scriptCode' go together: give both to add an event script, e.g. 'Tapped' with the script body.");
                    }

                    if (!HmiItemKinds.ContainsKey(kind))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Unknown screen item kind '{kind}'. Supported: {string.Join(", ", HmiItemKinds.Keys)}.");
                    }

                    var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);

                    if (screen.ScreenItems.Find(name) != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Screen '{screenName}' already has an item named '{name}'.");
                    }

                    HmiScreenItemBase item = HmiItemKinds[kind] switch
                    {
                        nameof(HmiButton) => screen.ScreenItems.Create<HmiButton>(name),
                        nameof(HmiIOField) => screen.ScreenItems.Create<HmiIOField>(name),
                        nameof(HmiTextBox) => screen.ScreenItems.Create<HmiTextBox>(name),
                        nameof(HmiText) => screen.ScreenItems.Create<HmiText>(name),
                        _ => screen.ScreenItems.Create<HmiRectangle>(name)
                    };

                    ApplyHmiItemProperties(item, left, top, width, height, text);

                    if (!string.IsNullOrEmpty(tag))
                    {
                        if (item is not HmiIOField)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"'tag' on create binds an IO field's ProcessValue; for a {kind} use 'BindHmiTag' with the property to dynamize.");
                        }

                        BindTag(item, "ProcessValue", tag!);
                    }

                    if (!string.IsNullOrWhiteSpace(scriptCode))
                    {
                        SetEventScript(item, name, eventType!, scriptCode!);
                    }

                    return item;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("kind", kind), ("name", name));
        }

        /// <summary>Every argument left null keeps the item's current value.</summary>
        public HmiScreenItemBase UpdateHmiScreenItem(
            string softwarePath,
            string screenName,
            string itemName,
            int? left = null,
            int? top = null,
            uint? width = null,
            uint? height = null,
            string? text = null,
            bool? visible = null)
        {
            return Operation.Run(_logger, nameof(UpdateHmiScreenItem), PortalErrorCode.RenameFailed,
                () =>
                {
                    var item = RequireHmiScreenItem(softwarePath, screenName, itemName);

                    ApplyHmiItemProperties(item, left, top, width, height, text);

                    if (visible.HasValue) item.Visible = visible.Value;

                    return item;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName));
        }

        public bool DeleteHmiScreenItem(string softwarePath, string screenName, string itemName)
        {
            return Operation.Run(_logger, nameof(DeleteHmiScreenItem), PortalErrorCode.DeleteFailed,
                () =>
                {
                    RequireHmiScreenItem(softwarePath, screenName, itemName).Delete();
                    return true;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName));
        }

        /// <summary>
        /// Binds an HMI tag to one property of a screen item, e.g. an IO field's ProcessValue or a
        /// rectangle's BackColor. An existing tag binding on that property is re-pointed.
        /// </summary>
        public HmiScreenItemBase BindHmiTag(string softwarePath, string screenName, string itemName, string propertyName, string tagName)
        {
            return Operation.Run(_logger, nameof(BindHmiTag), PortalErrorCode.CreateFailed,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(tagName))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "'propertyName' and 'tagName' are both required.");
                    }

                    var item = RequireHmiScreenItem(softwarePath, screenName, itemName);

                    BindTag(item, propertyName, tagName);

                    return item;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName),
                ("propertyName", propertyName), ("tagName", tagName));
        }

        /// <summary>
        /// Places a faceplate on a screen. 'faceplateType' is the faceplate type as TIA Portal names
        /// it in the container's "Contained type", e.g. 'V0.0.7\\Servo_Faceplate_2' - read an existing one with
        /// 'GetHmiScreenItems'. The type must already be in the project library.
        /// </summary>
        public HmiScreenItemBase CreateHmiFaceplate(
            string softwarePath,
            string screenName,
            string name,
            string faceplateType,
            int? left = null,
            int? top = null,
            uint? width = null,
            uint? height = null)
        {
            return Operation.Run(_logger, nameof(CreateHmiFaceplate), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    if (string.IsNullOrWhiteSpace(faceplateType))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'faceplateType' is required, e.g. 'V0.0.7\\Servo_Faceplate_2'. 'GetHmiScreenItems' shows the type of faceplates already placed.");
                    }

                    var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);

                    if (screen.ScreenItems.Find(name) != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"Screen '{screenName}' already has an item named '{name}'.");
                    }

                    var container = screen.ScreenItems.Create<HmiFaceplateContainer>(name);

                    try
                    {
                        container.ContainedType = faceplateType;
                    }
                    catch (Exception ex) when (ex is not PortalException)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"TIA Portal refused faceplate type '{faceplateType}': {Operation.DescribeChain(ex)}. " +
                            "Use the exact name and version shown as 'faceplateType' by 'GetHmiScreenItems' on a screen that already has it.",
                            null, ex);
                    }

                    ApplyHmiItemProperties(container, left, top, width, height, null);

                    return container;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("name", name), ("faceplateType", faceplateType));
        }

        /// <summary>
        /// Sets one interface property of a placed faceplate: a static 'value', or a 'tag' binding.
        /// </summary>
        public HmiScreenItemBase SetHmiFaceplateInterface(
            string softwarePath,
            string screenName,
            string itemName,
            string propertyName,
            string? value = null,
            string? tag = null)
        {
            return Operation.Run(_logger, nameof(SetHmiFaceplateInterface), PortalErrorCode.InvalidParams,
                () =>
                {
                    if (string.IsNullOrEmpty(value) == string.IsNullOrEmpty(tag))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "Pass exactly one of 'value' or 'tag'.");
                    }

                    var container = RequireHmiScreenItem(softwarePath, screenName, itemName) as HmiFaceplateContainer
                        ?? throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{itemName}' on screen '{screenName}' is not a faceplate.");

                    var property = container.Interface.Find(propertyName)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Faceplate '{itemName}' ({container.ContainedType}) has no interface property '{propertyName}'. " +
                            $"Available: {string.Join(", ", container.Interface.Select(p => p.PropertyName))}.");

                    if (!string.IsNullOrEmpty(tag))
                    {
                        BindFaceplateInterfaceTag(property, itemName, tag!);
                        return container;
                    }

                    SetInterfaceValue(property, value!);

                    return container;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("propertyName", propertyName));
        }

        /// <summary>
        /// Faceplate interface properties take an HMI tag as their value: TIA Portal refuses a tag
        /// dynamization on them ("Dynamization type not supported"), and existing faceplates in the
        /// CPU01 project hold the tag name as the value. The value is set directly - trying the
        /// dynamization first is not an option, because any exception inside the tool's transaction
        /// makes TIA Portal refuse the commit (live, 2026-10-13).
        /// </summary>
        private static void BindFaceplateInterfaceTag(HmiFaceplateInterface property, string itemName, string tagName)
        {
            try
            {
                property.Value = tagName;
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"TIA Portal refused HMI tag '{tagName}' for faceplate interface '{itemName}.{property.PropertyName}': " +
                    $"{Operation.DescribeChain(ex)}.", null, ex);
            }
        }

        /// <summary>
        /// The interface value is typed by the faceplate (Bool, Int, String, ...) but exposed as object.
        /// The text is converted to the type of the current value and written once: a failed write
        /// inside the tool's transaction would make TIA Portal refuse the commit, so there is no
        /// trial and error here.
        /// </summary>
        private static void SetInterfaceValue(HmiFaceplateInterface property, string value)
        {
            var current = property.Value;
            object converted;

            try
            {
                converted = current == null || current is string
                    ? value
                    : Convert.ChangeType(value, current.GetType(), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{value}' is not a valid {current!.GetType().Name} for faceplate interface '{property.PropertyName}'.", null, ex);
            }

            try
            {
                property.Value = converted;
            }
            catch (Exception ex)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"TIA Portal refused value '{value}' for faceplate interface '{property.PropertyName}': {Operation.DescribeChain(ex)}.",
                    null, ex);
            }
        }

        /// <summary>
        /// Text-typed interface values come back as a full ProjectText XML document; only the
        /// &lt;body&gt; part, as screen item texts show it, is worth returning.
        /// </summary>
        private static string? ShortProjectText(string? value)
        {
            if (value == null || !value.StartsWith("<?xml", StringComparison.Ordinal))
            {
                return value;
            }

            var start = value.IndexOf("<body", StringComparison.Ordinal);
            var end = value.LastIndexOf("</body>", StringComparison.Ordinal);

            return start >= 0 && end > start ? value.Substring(start, end - start + "</body>".Length) : value;
        }

        private static HmiFaceplateInterfaceDescription DescribeFaceplateInterface(HmiFaceplateInterface property)
        {
            var binding = property.Dynamizations.FirstOrDefault();

            return new HmiFaceplateInterfaceDescription
            {
                PropertyName = property.PropertyName,
                Value = binding == null ? ShortProjectText(property.Value?.ToString()) : null,
                Tag = (binding as TagDynamization)?.Tag,
                DynamizationType = binding?.DynamizationType.ToString()
            };
        }

        /// <summary>
        /// Creates an HMI tag in a tag table. With 'connection' and 'plcTag' it is linked to a PLC
        /// tag (e.g. connection 'HMI_Connection_1', plcTag '"MainAssy".Mode.Dry_Run'); without them
        /// it is an internal tag of 'dataType'.
        /// </summary>
        public HmiTag CreateHmiTag(
            string softwarePath,
            string tagTablePath,
            string name,
            string? dataType = null,
            string? connection = null,
            string? plcTag = null)
        {
            return Operation.Run(_logger, nameof(CreateHmiTag), PortalErrorCode.CreateFailed,
                () =>
                {
                    EnsureValidName(name);

                    var hmi = RequireHmiSoftware(softwarePath);

                    var table = GetHmiTagTable(softwarePath, tagTablePath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"HMI tag table not found at '{tagTablePath}'. Use 'GetHmiTagTables' to list them.");

                    if (hmi.Tags.Find(name) != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"An HMI tag named '{name}' already exists in '{softwarePath}'.");
                    }

                    if (string.IsNullOrEmpty(connection) != string.IsNullOrEmpty(plcTag))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'connection' and 'plcTag' go together: pass both for a PLC-linked tag, or neither for an internal tag.");
                    }

                    var tag = table.Tags.Create(name);

                    // Connection before PlcTag: the PLC tag is resolved against the connection.
                    if (!string.IsNullOrEmpty(connection)) tag.Connection = connection;
                    if (!string.IsNullOrEmpty(plcTag)) tag.PlcTag = plcTag;
                    if (!string.IsNullOrEmpty(dataType)) tag.DataType = dataType;

                    return tag;
                },
                ("softwarePath", softwarePath), ("tagTablePath", tagTablePath), ("name", name));
        }

        #endregion

        #region helpers

        /// <summary>
        /// Whether an HMI tag of that name exists in any tag table. TIA Portal accepts a binding to
        /// a missing tag without complaint (it only shows red in the editor), so the bind tools use
        /// this to warn rather than to refuse - system tags are not in the tag tables.
        /// </summary>
        public bool HmiTagExists(string softwarePath, string tagName)
        {
            return Operation.Run(_logger, nameof(HmiTagExists), PortalErrorCode.NotFound,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);

                    return hmi.TagTables.Any(t => TableHasTag(t, tagName))
                        || hmi.TagTableGroups.Any(g => GroupHasTag(g, tagName));
                });
        }

        private static bool TableHasTag(HmiTagTable table, string tagName) =>
            table.Tags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));

        private static bool GroupHasTag(HmiTagTableGroup group, string tagName) =>
            group.TagTables.Any(t => TableHasTag(t, tagName)) || group.Groups.Any(g => GroupHasTag(g, tagName));

        private HmiSoftware RequireHmiSoftware(string softwarePath)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");
            }

            return GetSoftwareContainer(softwarePath)?.Software as HmiSoftware
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"No WinCC Unified HMI software at '{softwarePath}'. HMI software paths look like 'HMI_1/HMI_RT_1'; " +
                    "classic Comfort/Basic panels are not supported.");
        }

        private static HmiScreen? FindHmiScreen(HmiSoftware hmi, string screenName)
        {
            return hmi.Screens.Find(screenName) ?? FindHmiScreenInGroups(hmi.ScreenGroups, screenName);
        }

        private static HmiScreen? FindHmiScreenInGroups(HmiScreenGroupComposition groups, string screenName)
        {
            foreach (var group in groups)
            {
                var found = group.Screens.Find(screenName) ?? FindHmiScreenInGroups(group.Groups, screenName);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static HmiScreen RequireHmiScreen(HmiSoftware hmi, string screenName)
        {
            return FindHmiScreen(hmi, screenName)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen '{screenName}' not found. Use 'GetHmiScreens' to list the screens.");
        }

        private HmiScreenItemBase RequireHmiScreenItem(string softwarePath, string screenName, string itemName)
        {
            var screen = RequireHmiScreen(RequireHmiSoftware(softwarePath), screenName);

            return screen.ScreenItems.Find(itemName)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen '{screenName}' has no item named '{itemName}'. Use 'GetHmiScreenItems' to list them.");
        }

        private static void ApplyHmiItemProperties(HmiScreenItemBase item, int? left, int? top, uint? width, uint? height, string? text)
        {
            switch (item)
            {
                case HmiWidgetBase widget:
                    if (left.HasValue) widget.Left = left.Value;
                    if (top.HasValue) widget.Top = top.Value;
                    if (width.HasValue) widget.Width = width.Value;
                    if (height.HasValue) widget.Height = height.Value;
                    break;
                case HmiSurfaceShapeBase shape:
                    if (left.HasValue) shape.Left = left.Value;
                    if (top.HasValue) shape.Top = top.Value;
                    if (width.HasValue) shape.Width = width.Value;
                    if (height.HasValue) shape.Height = height.Value;
                    break;
                case HmiWindowBase window:
                    if (left.HasValue) window.Left = left.Value;
                    if (top.HasValue) window.Top = top.Value;
                    if (width.HasValue) window.Width = width.Value;
                    if (height.HasValue) window.Height = height.Value;
                    break;
            }

            if (text == null)
            {
                return;
            }

            var target = item switch
            {
                HmiButton button => button.Text,
                HmiLabel label => label.Text,
                HmiText shapeText => shapeText.Text,
                _ => throw new PortalException(PortalErrorCode.InvalidParams,
                    $"A {KindOf(item)} has no text; 'text' applies to Button, Label and Text.")
            };

            SetHmiText(target, text);
        }

        private static void SetHmiText(MultilingualText target, string text)
        {
            if (target.Items.Count == 0)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    "The item's text has no project language to write to. Check the project languages in TIA Portal.");
            }

            // Unified screen texts are rich text: TIA Portal stores "<body><p>USE</p></body>" and
            // rejects a plain string. Plain input is wrapped; input that is already "<body>..." is
            // written as given so formatted text can be passed through.
            var value = text.TrimStart().StartsWith("<body", StringComparison.OrdinalIgnoreCase)
                ? text
                : $"<body><p>{System.Security.SecurityElement.Escape(text)}</p></body>";

            foreach (var entry in target.Items)
            {
                entry.Text = value;
            }
        }

        private static void BindTag(HmiScreenItemBase item, string propertyName, string tagName) =>
            BindTag(item, item.Name, KindOf(item), propertyName, tagName);

        /// <summary>Tag dynamization on any UI object: a screen item, or a faceplate interface property.</summary>
        private static void BindTag(UIBase target, string targetName, string targetKind, string propertyName, string tagName)
        {
            var existing = target.Dynamizations.Find(propertyName);

            if (existing is TagDynamization tagDynamization)
            {
                tagDynamization.Tag = tagName;
                return;
            }

            if (existing != null)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"'{targetName}.{propertyName}' already has a {existing.DynamizationType} dynamization; remove it in TIA Portal first.");
            }

            try
            {
                var created = target.Dynamizations.Create<TagDynamization>(propertyName);
                created.Tag = tagName;
            }
            catch (Exception ex) when (ex is not PortalException)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"TIA Portal refused to bind '{targetName}.{propertyName}' to HMI tag '{tagName}': {Operation.DescribeChain(ex)}. " +
                    $"Check that a {targetKind} has a property '{propertyName}' (e.g. a Rectangle has BackColor and BorderColor, no ForeColor) " +
                    "and that the tag exists ('GetHmiTags').", null, ex);
            }
        }

        private static string KindOf(HmiScreenItemBase item)
        {
            var typeName = item.GetType().Name;

            return HmiItemKinds.FirstOrDefault(k => k.Value == typeName).Key ?? typeName;
        }

        #endregion
    }

    public class HmiScreenItemDescription
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Button, IOField, Label, Text, Rectangle, or the Openness type name for other items.</summary>
        public string Kind { get; set; } = string.Empty;

        public bool Visible { get; set; }

        public int? Left { get; set; }

        public int? Top { get; set; }

        public uint? Width { get; set; }

        public uint? Height { get; set; }

        public string? Text { get; set; }

        public string? ProcessValue { get; set; }

        public string? OutputFormat { get; set; }

        public List<HmiDynamizationDescription> Dynamizations { get; set; } = new List<HmiDynamizationDescription>();

        /// <summary>Faceplate containers only: the contained faceplate type, e.g. 'V0.0.7\\Servo_Faceplate_2'.</summary>
        public string? FaceplateType { get; set; }

        /// <summary>Faceplate containers only: the interface properties with their static value or bound tag.</summary>
        public List<HmiFaceplateInterfaceDescription>? FaceplateInterface { get; set; }

        /// <summary>Event handler scripts of the item (read tool only).</summary>
        public List<HmiEventDescription>? Events { get; set; }
    }

    public class HmiFaceplateInterfaceDescription
    {
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>Static value, when the property is not dynamized.</summary>
        public string? Value { get; set; }

        /// <summary>The bound HMI tag, for a tag dynamization.</summary>
        public string? Tag { get; set; }

        /// <summary>Dynamization type when bound (Tag, Script, ...); null for a static value.</summary>
        public string? DynamizationType { get; set; }
    }

    public class HmiDynamizationDescription
    {
        public string PropertyName { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        /// <summary>The bound HMI tag, for a tag dynamization.</summary>
        public string? Tag { get; set; }

        /// <summary>The script, for a script dynamization (read tool only).</summary>
        public string? ScriptCode { get; set; }
    }

    public class HmiEventDescription
    {
        /// <summary>e.g. Activated, Up, Down, Loaded.</summary>
        public string EventType { get; set; } = string.Empty;

        public string? ScriptCode { get; set; }
        public string? GlobalDefinitionAreaScriptCode { get; set; }
        public bool Async { get; set; }
    }
}
