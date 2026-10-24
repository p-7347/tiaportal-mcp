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
        // WinCC Unified screen items: one read tool, and the write tools of REQUESTS.md P9-2.
        // Every write returns the object as read back after the change, so the caller sees what
        // TIA Portal actually stored rather than what was asked for.

        private const string HmiPathDescription = "softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'";

        #region read

        [McpServerTool(Name = "GetHmiScreenItems", Title = "Get HMI screen items", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the items of one WinCC Unified screen with their kind, position, size, text, IO field process value and tag bindings (dynamizations); for faceplates also the faceplate type and interface values/tags; with includeScripts the event scripts of each item and of the screen itself (e.g. what a button does when pressed)")]
        public static ResponseHmiScreenItems GetHmiScreenItems(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, as 'GetHmiScreens' lists it")] string screenName,
            [Description("regexName: optional name or regular expression to filter the items. Empty (default) lists all")] string regexName = "",
            [Description("includeScripts: also return event handler scripts (button press, screen loaded, ...) and script dynamization code. Default true; pass false for a shorter reply")] bool includeScripts = true)
        {
            try
            {
                var items = Portal.GetHmiScreenItems(softwarePath, screenName, regexName)
                    .Select(item => Portal.DescribeHmiScreenItem(item, includeScripts))
                    .ToList();

                var screenEvents = includeScripts ? Portal.DescribeHmiScreenEvents(softwarePath, screenName) : null;

                return new ResponseHmiScreenItems
                {
                    Message = $"{items.Count} item(s) on screen '{screenName}' in '{softwarePath}'" +
                              (includeScripts ? $", {items.Sum(i => i.Events?.Count ?? 0) + (screenEvents?.Count ?? 0)} event script(s)" : string.Empty),
                    Items = items,
                    ScreenEvents = screenEvents?.Count > 0 ? screenEvents : null,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["count"] = items.Count }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the items of screen '{screenName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region write

        [WriteTool]
        [McpServerTool(Name = "CreateHmiScreen", Title = "Create HMI screen", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Create an empty WinCC Unified screen at the root of the HMI software's screens")]
        public static ResponseCreated CreateHmiScreen(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("name: name of the new screen")] string name,
            [Description("width: optional width in pixels; default is the device's screen width")] uint? width = null,
            [Description("height: optional height in pixels; default is the device's screen height")] uint? height = null,
            [Description("screenNumber: optional screen number")] ushort? screenNumber = null)
        {
            return Guarded(nameof(CreateHmiScreen), () =>
            {
                var screen = Portal.CreateHmiScreen(softwarePath, name, width, height, screenNumber);
                var created = Created("HMI screen", screen.Name, screen.Name);

                created.Message += $" Read back: {screen.Width}x{screen.Height}, screen number {screen.ScreenNumber}.";

                return created;
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiScreen", Title = "Delete HMI screen", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a WinCC Unified screen and all its items")]
        public static ResponseDeleted DeleteHmiScreen(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen to delete")] string screenName)
        {
            return Guarded(nameof(DeleteHmiScreen), () =>
            {
                Portal.DeleteHmiScreen(softwarePath, screenName);
                return Deleted("HMI screen", screenName);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateHmiScreenItem", Title = "Create HMI screen item", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Add an item to a WinCC Unified screen: Button, IOField, Label, Text or Rectangle. Optionally set its position, size and text, and for an IOField bind its ProcessValue to an HMI tag. Returns the item as read back")]
        public static ResponseHmiScreenItemWrite CreateHmiScreenItem(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("kind: Button, IOField, Label, Text or Rectangle. Label is created as TIA Portal's 'Text field' (HmiTextBox)")] string kind,
            [Description("name: name of the new item, unique on the screen")] string name,
            [Description("left: optional x position in pixels")] int? left = null,
            [Description("top: optional y position in pixels")] int? top = null,
            [Description("width: optional width in pixels")] uint? width = null,
            [Description("height: optional height in pixels")] uint? height = null,
            [Description("text: optional text for a Button, Label or Text, written to every project language. Plain text is stored as '<body><p>...</p></body>'; pass '<body>...' yourself for formatted text")] string? text = null,
            [Description("tag: optional HMI tag to bind to an IOField's ProcessValue. For other properties use 'BindHmiTag'")] string? tag = null)
        {
            return Guarded(nameof(CreateHmiScreenItem), () =>
            {
                var item = Portal.CreateHmiScreenItem(softwarePath, screenName, kind, name, left, top, width, height, text, tag);
                return HmiItemWritten(MissingTagWarning(softwarePath, tag) + $"{kind} '{name}' created on screen '{screenName}'.", item);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "UpdateHmiScreenItem", Title = "Update HMI screen item", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Change the position, size, text or visibility of an item on a WinCC Unified screen. Every argument left empty keeps the current value. Returns the item as read back. For colors, border, font or output format use 'SetHmiScreenItemProperties'")]
        public static ResponseHmiScreenItemWrite UpdateHmiScreenItem(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the item")] string itemName,
            [Description("left: optional new x position in pixels")] int? left = null,
            [Description("top: optional new y position in pixels")] int? top = null,
            [Description("width: optional new width in pixels")] uint? width = null,
            [Description("height: optional new height in pixels")] uint? height = null,
            [Description("text: optional new text for a Button, Label or Text, written to every project language. Plain text is stored as '<body><p>...</p></body>'")] string? text = null,
            [Description("visible: optional new visibility")] bool? visible = null)
        {
            return Guarded(nameof(UpdateHmiScreenItem), () =>
            {
                var item = Portal.UpdateHmiScreenItem(softwarePath, screenName, itemName, left, top, width, height, text, visible);
                return HmiItemWritten($"Item '{itemName}' on screen '{screenName}' updated.", item);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiScreenItem", Title = "Delete HMI screen item", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete one item from a WinCC Unified screen")]
        public static ResponseDeleted DeleteHmiScreenItem(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the item to delete")] string itemName)
        {
            return Guarded(nameof(DeleteHmiScreenItem), () =>
            {
                Portal.DeleteHmiScreenItem(softwarePath, screenName, itemName);
                return Deleted("HMI screen item", $"{screenName}/{itemName}");
            });
        }

        [WriteTool]
        [McpServerTool(Name = "BindHmiTag", Title = "Bind HMI tag to screen item", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Bind an HMI tag to a property of a WinCC Unified screen item (a tag dynamization), e.g. an IOField's 'ProcessValue' or a Rectangle's 'BackColor'. An existing tag binding on that property is re-pointed. Returns the item as read back")]
        public static ResponseHmiScreenItemWrite BindHmiTag(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the item")] string itemName,
            [Description("propertyName: the item property to dynamize, as named in TIA Portal's property list, e.g. 'ProcessValue', 'Visible', 'BackColor'")] string propertyName,
            [Description("tagName: the HMI tag to bind, as 'GetHmiTags' lists it")] string tagName)
        {
            return Guarded(nameof(BindHmiTag), () =>
            {
                var item = Portal.BindHmiTag(softwarePath, screenName, itemName, propertyName, tagName);
                return HmiItemWritten(MissingTagWarning(softwarePath, tagName) + $"'{itemName}.{propertyName}' on screen '{screenName}' bound to HMI tag '{tagName}'.", item);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateHmiFaceplate", Title = "Create HMI faceplate", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Place a faceplate (faceplate container) on a WinCC Unified screen. The faceplate type must already be in the project library; 'GetHmiScreenItems' shows the 'faceplateType' and interface of faceplates already placed. Set its interface with 'SetHmiFaceplateInterface'. Returns the item as read back, with its interface properties")]
        public static ResponseHmiScreenItemWrite CreateHmiFaceplate(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("name: name of the new faceplate container, unique on the screen")] string name,
            [Description("faceplateType: the faceplate type with version, exactly as 'GetHmiScreenItems' shows it, e.g. 'V0.0.7\\Servo_Faceplate_2'")] string faceplateType,
            [Description("left: optional x position in pixels")] int? left = null,
            [Description("top: optional y position in pixels")] int? top = null,
            [Description("width: optional width in pixels")] uint? width = null,
            [Description("height: optional height in pixels")] uint? height = null)
        {
            return Guarded(nameof(CreateHmiFaceplate), () =>
            {
                var item = Portal.CreateHmiFaceplate(softwarePath, screenName, name, faceplateType, left, top, width, height);
                return HmiItemWritten($"Faceplate '{name}' ({faceplateType}) created on screen '{screenName}'.", item);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiFaceplateInterface", Title = "Set HMI faceplate interface", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set one interface property of a faceplate on a WinCC Unified screen, either to a static 'value' or to an HMI 'tag' (pass exactly one). A tag-typed property (e.g. a UDT tag) stores the tag name as its value, as TIA Portal itself does; texts are given as plain text. Returns the faceplate as read back, with all its interface properties")]
        public static ResponseHmiScreenItemWrite SetHmiFaceplateInterface(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the faceplate container")] string itemName,
            [Description("propertyName: the interface property, as listed under 'faceplateInterface' by 'GetHmiScreenItems'")] string propertyName,
            [Description("value: optional static value, e.g. 'true', '5' or a text")] string? value = null,
            [Description("tag: optional HMI tag to bind to the property, as 'GetHmiTags' lists it")] string? tag = null)
        {
            return Guarded(nameof(SetHmiFaceplateInterface), () =>
            {
                var item = Portal.SetHmiFaceplateInterface(softwarePath, screenName, itemName, propertyName, value, tag);
                var what = string.IsNullOrEmpty(tag)
                    ? $"Faceplate '{itemName}' interface '{propertyName}' set to '{value}'."
                    : $"Faceplate '{itemName}' interface '{propertyName}' bound to HMI tag '{tag}'.";

                return HmiItemWritten(MissingTagWarning(softwarePath, tag) + what, item);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateHmiTag", Title = "Create HMI tag", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Create a WinCC Unified HMI tag in a tag table. Pass 'connection' and 'plcTag' to link it to a PLC tag; otherwise it is an internal tag of 'dataType'. Returns the tag as read back")]
        public static ResponseHmiTagWrite CreateHmiTag(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("tagTablePath: HMI tag table, as 'GetHmiTagTables' lists it, e.g. 'Default tag table'")] string tagTablePath,
            [Description("name: name of the new HMI tag")] string name,
            [Description("dataType: optional data type, e.g. 'Bool' or 'Int'. An internal tag without one becomes Int; a PLC-linked tag takes the PLC tag's type by default")] string? dataType = null,
            [Description("connection: optional HMI connection name, e.g. 'HMI_Connection_1'")] string? connection = null,
            [Description("plcTag: optional PLC tag, e.g. '\"MainAssy\".Mode.Dry_Run' or 'Tag_1'")] string? plcTag = null)
        {
            return Guarded(nameof(CreateHmiTag), () =>
            {
                var tag = Portal.CreateHmiTag(softwarePath, tagTablePath, name, dataType, connection, plcTag);

                return new ResponseHmiTagWrite
                {
                    Message = $"HMI tag '{name}' created in '{tagTablePath}'. {SaveHint}",
                    Tag = new ResponseHmiTagInfo
                    {
                        Name = tag.Name,
                        DataType = tag.DataType,
                        HmiDataType = tag.HmiDataType,
                        Address = tag.Address,
                        Connection = tag.Connection,
                        PlcName = tag.PlcName,
                        PlcTag = tag.PlcTag,
                        AccessMode = tag.AccessMode.ToString(),
                        AcquisitionMode = tag.AcquisitionMode.ToString(),
                        Scope = tag.Scope.ToString(),
                        TagType = tag.TagType.ToString(),
                        Comment = Helper.FirstText(tag.Comment)
                    },
                    Meta = OkMeta()
                };
            });
        }

        private static string MissingTagWarning(string softwarePath, string? tagName) =>
            string.IsNullOrEmpty(tagName) || Portal.HmiTagExists(softwarePath, tagName!)
                ? string.Empty
                : $"WARNING: no HMI tag named '{tagName}' in the tag tables of '{softwarePath}' (TIA Portal keeps the binding anyway; it shows red until the tag exists - check 'GetHmiTags'). ";

        private static ResponseHmiScreenItemWrite HmiItemWritten(string what, global::Siemens.Engineering.HmiUnified.UI.Base.HmiScreenItemBase item) => new ResponseHmiScreenItemWrite
        {
            Message = $"{what} {SaveHint}",
            Item = Portal.DescribeHmiScreenItem(item),
            Meta = OkMeta()
        };

        #endregion
    }
}
