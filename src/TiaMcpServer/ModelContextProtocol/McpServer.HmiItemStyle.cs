using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // Static style properties of WinCC Unified screen items: list, set by name, copy from one
        // item to others. Property names are TIA Portal's ('BackColor', 'Font.Size').

        private const string ItemPropertyNames =
            "Names as TIA Portal's property list and 'GetHmiScreenItemProperties' show them, e.g. 'BackColor', 'BorderColor', 'BorderWidth', 'ForeColor', 'OutputFormat', 'Font.Size'";

        [McpServerTool(Name = "GetHmiScreenItemProperties", Title = "Get HMI screen item properties", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the settable properties of one WinCC Unified screen item with their current value and type (colors as '#RRGGBB', enums with their allowed values), including part objects such as 'Font.Size'. Use it to find the property names for 'SetHmiScreenItemProperties' and 'CopyHmiItemStyle'")]
        public static ResponseHmiItemProperties GetHmiScreenItemProperties(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the item")] string itemName,
            [Description("regexName: optional name or regular expression to filter the property names, e.g. 'Color|Font'. Empty (default) lists all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiScreenItemProperties(softwarePath, screenName, itemName, regexName);

                return new ResponseHmiItemProperties
                {
                    Message = $"{list.Count} propert(ies) of '{itemName}' on screen '{screenName}'",
                    Item = itemName,
                    Properties = list,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["count"] = list.Count }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the properties of '{itemName}' on screen '{screenName}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiScreenItemProperties", Title = "Set HMI screen item properties", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to set static properties (colors, border, font, output format, alignment, ...) of one or many WinCC Unified screen items in one call, e.g. every IO field's 'OutputFormat' or the back color of a row of rectangles. Values are text converted to the property's type: colors '#RRGGBB', '#AARRGGBB', 'R, G, B' or a name; enums by name; numbers; true/false. Every item and property is checked first and nothing changes when one is wrong. The previous values are saved as a .json backup and returned with the new ones")]
        public static ResponseHmiItemPropertyWrite SetHmiScreenItemProperties(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("properties: JSON object of property name to value, e.g. '{\"BackColor\":\"#FFFFFF\",\"Font.Size\":20,\"OutputFormat\":\"{I}\"}'. " + ItemPropertyNames)] string properties,
            [Description("itemNames: item names separated by commas or line breaks, e.g. 'IOField_1, IOField_2'. May be empty when 'itemRegex' is given")] string itemNames = "",
            [Description("itemRegex: optional regular expression over the item names; matching items are changed too, e.g. '^IO_Count_'")] string itemRegex = "")
        {
            return Guarded(nameof(SetHmiScreenItemProperties), () =>
            {
                var result = Portal.SetHmiScreenItemProperties(softwarePath, screenName, SplitNames(itemNames), itemRegex,
                    ParsePropertyObject(properties), nameof(SetHmiScreenItemProperties));

                return ItemPropertiesWritten(result, $"{result.Changes.Count} value(s) changed on {result.Items.Count} item(s) of screen '{screenName}'.");
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CopyHmiItemStyle", Title = "Copy HMI item style", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to give WinCC Unified screen items the look of another item (format painter): copies its static properties - colors, border, font, alignment, formats - but not position, size, name, texts or tag bindings. Targets on the same or another screen, by name or regular expression; a target of another kind gets the properties it has, the rest is listed as skipped. 'properties' limits the copy to named properties. The previous values are saved as a .json backup")]
        public static ResponseHmiItemPropertyWrite CopyHmiItemStyle(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: screen of the source item")] string screenName,
            [Description("sourceItem: the item whose style is copied")] string sourceItem,
            [Description("targetItems: target item names separated by commas or line breaks. May be empty when 'targetRegex' is given")] string targetItems = "",
            [Description("targetRegex: optional regular expression over the target item names, e.g. '^Label_Header'")] string targetRegex = "",
            [Description("properties: optional property names to copy, separated by commas, e.g. 'BackColor, ForeColor, Font.Size'. Empty (default) copies every style property")] string properties = "",
            [Description("targetScreenName: optional screen of the targets; empty (default) means the source item's screen")] string targetScreenName = "")
        {
            return Guarded(nameof(CopyHmiItemStyle), () =>
            {
                var result = Portal.CopyHmiItemStyle(softwarePath, screenName, sourceItem, SplitNames(targetItems), targetRegex,
                    SplitNames(properties), targetScreenName, nameof(CopyHmiItemStyle));

                return ItemPropertiesWritten(result,
                    $"Style of '{sourceItem}' copied to {result.Items.Count} item(s): {result.Changes.Count} value(s) changed" +
                    (result.Skipped.Count > 0 ? $", some properties skipped where a target does not have them" : string.Empty) + ".");
            });
        }

        private static ResponseHmiItemPropertyWrite ItemPropertiesWritten(Portal.HmiItemPropertyResult result, string message)
        {
            var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

            return new ResponseHmiItemPropertyWrite
            {
                Message = (result.Changes.Count == 0 ? message + " Every item already had these values." : message + $" {SaveHint}") + BackupNote(backup),
                Items = result.Items,
                Changes = result.Changes,
                Skipped = result.Skipped.Count > 0 ? result.Skipped : null,
                Backup = result.Backup,
                BackupWarning = result.BackupWarning,
                Meta = OkMeta()
            };
        }

        private static List<string> SplitNames(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .ToList();
        }

        /// <summary>'{"BackColor":"#FFFFFF","Font.Size":20}' as name/value text pairs, in the given order.</summary>
        private static List<KeyValuePair<string, string>> ParsePropertyObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new McpException("'properties' is required: a JSON object such as {\"BackColor\":\"#FFFFFF\"}.");
            }

            try
            {
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new McpException("'properties' must be a JSON object of property name to value, e.g. {\"BackColor\":\"#FFFFFF\"}.");
                }

                return document.RootElement.EnumerateObject()
                    .Select(p => new KeyValuePair<string, string>(p.Name,
                        p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText()))
                    .ToList();
            }
            catch (JsonException ex)
            {
                throw new McpException($"'properties' is not valid JSON: {ex.Message}", ex);
            }
        }
    }
}
