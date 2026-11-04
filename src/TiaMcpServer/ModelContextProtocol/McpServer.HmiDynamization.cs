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
        // WinCC Unified screen item dynamizations in full (BACKLOG C6). See Portal.HmiDynamization.cs.

        #region read

        [McpServerTool(Name = "GetHmiDynamizations", Title = "Get HMI dynamizations", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("Use to find out what drives a property of a WinCC Unified screen item, e.g. which tag value or bit turns a lamp green. Lists each dynamization in full: the tag with its mapping table (value, range or bit -> property value, colors as '#RRGGBB') or formula, script code with its trigger, flashing colors and rate, resource list. Only items that have a dynamization are listed")]
        public static ResponseHmiDynamizations GetHmiDynamizations(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, as 'GetHmiScreens' lists it, e.g. 'Door Overview'")] string screenName,
            [Description("regexName: optional name or regular expression to filter the items, e.g. 'Button_1$'. Empty (default) reads all items")] string regexName = "",
            [Description("propertyName: optional property to show, e.g. 'BackColor'. Empty (default) shows every dynamized property")] string propertyName = "")
        {
            try
            {
                var result = Portal.GetHmiDynamizations(softwarePath, screenName, regexName, propertyName, TimeSpan.FromSeconds(40));
                var count = result.Items.Sum(i => i.Dynamizations.Count);

                return new ResponseHmiDynamizations
                {
                    Message = $"{count} dynamization(s) on {result.Items.Count} of {result.ItemsLookedAt} item(s) of screen '{screenName}' in '{softwarePath}'." +
                              (result.StoppedReason != null ? $" Incomplete: {result.StoppedReason}." : string.Empty),
                    Items = result.Items,
                    StoppedReason = result.StoppedReason,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["count"] = count,
                        ["complete"] = result.StoppedReason == null
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the dynamizations of screen '{screenName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region write

        [WriteTool]
        [McpServerTool(Name = "SetHmiTagDynamization", Title = "Set HMI tag dynamization", Destructive = true, Idempotent = true, OpenWorld = false),
         Description("Use to make a property of a WinCC Unified screen item follow an HMI tag, e.g. a lamp's BackColor green at 0 and red at 1. Binds the tag and, when 'mapping' or 'formula' is given, replaces how tag values map to property values. A property that already has another kind of dynamization (script, flashing) must be cleared with 'DeleteHmiDynamization' first. The previous dynamization is backed up as JSON. Returns the dynamization as read back")]
        public static ResponseHmiDynamizationWrite SetHmiTagDynamization(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, e.g. 'Door Overview'")] string screenName,
            [Description("itemName: name of the item, e.g. 'Lamp_Door1'")] string itemName,
            [Description("propertyName: the property to dynamize, as named in TIA Portal's property list, e.g. 'BackColor', 'Visible', 'ProcessValue'")] string propertyName,
            [Description("tagName: the HMI tag, as 'GetHmiTags' lists it, e.g. 'Door1_Closed'")] string tagName,
            [Description("mapping: optional JSON array of range entries from tag value to property value, e.g. '[{\"from\":0,\"to\":0,\"value\":\"#00FF00\"},{\"from\":1,\"to\":1,\"value\":\"#FF0000\"}]'. 'from' and 'to' are both required (open ranges cannot be written); colors as '#RRGGBB'; optional per entry: flashing (true), alternateValue, flashingRate (Slow, Medium, Fast). '[]' clears the table; empty (default) keeps the current mapping")] string? mapping = null,
            [Description("formula: optional value converter formula instead of a mapping; empty (default) keeps the current one")] string? formula = null)
        {
            return Guarded(nameof(SetHmiTagDynamization), () =>
            {
                var result = Portal.SetHmiTagDynamization(softwarePath, screenName, itemName, propertyName, tagName, mapping, formula);
                return DynamizationWritten(MissingTagWarning(softwarePath, tagName) + $"'{itemName}.{propertyName}' on screen '{screenName}' follows HMI tag '{tagName}'.", result);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiScriptDynamization", Title = "Set HMI script dynamization", Destructive = true, Idempotent = true, OpenWorld = false),
         Description("Use to compute a property of a WinCC Unified screen item with a script, e.g. a text or color from several tags. Creates the script dynamization or changes its code and trigger. A property that already has another kind of dynamization must be cleared with 'DeleteHmiDynamization' first. The previous dynamization is backed up as JSON. Returns the dynamization as read back")]
        public static ResponseHmiDynamizationWrite SetHmiScriptDynamization(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, e.g. 'Door Overview'")] string screenName,
            [Description("itemName: name of the item, e.g. 'Lamp_Door1'")] string itemName,
            [Description("propertyName: the property to dynamize, e.g. 'BackColor'")] string propertyName,
            [Description("scriptCode: the JavaScript body that returns the property value, e.g. 'return Tags(\"Door1_Closed\").Read() ? 0xFF00FF00 : 0xFFFF0000;'")] string scriptCode,
            [Description("triggerType: optional trigger: Disabled, T100ms, T250ms, T500ms, T1s, T2s, T5s, T10s, CustomCycle, Tags or AutomaticTags. Empty (default) keeps the current one")] string? triggerType = null,
            [Description("triggerTags: optional comma separated HMI tags that trigger the script, e.g. 'Door1_Closed,Door1_Locked'; implies triggerType 'Tags'")] string? triggerTags = null,
            [Description("triggerCycle: optional cycle for triggerType 'CustomCycle', as TIA Portal shows it")] string? triggerCycle = null,
            [Description("globalDefinitionCode: optional code for the script's global definition area; empty (default) keeps it")] string? globalDefinitionCode = null)
        {
            return Guarded(nameof(SetHmiScriptDynamization), () =>
            {
                var result = Portal.SetHmiScriptDynamization(softwarePath, screenName, itemName, propertyName, scriptCode, triggerType, triggerTags, triggerCycle, globalDefinitionCode);
                return DynamizationWritten($"'{itemName}.{propertyName}' on screen '{screenName}' is computed by a script.", result);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiFlashingDynamization", Title = "Set HMI flashing dynamization", Destructive = true, Idempotent = true, OpenWorld = false),
         Description("Use to make a color property of a WinCC Unified screen item flash between two colors. Creates the flashing dynamization or changes it; arguments left empty keep their value. A property that already has another kind of dynamization must be cleared with 'DeleteHmiDynamization' first (a tag mapping entry can flash on its own: see 'SetHmiTagDynamization'). The previous dynamization is backed up as JSON. Returns the dynamization as read back")]
        public static ResponseHmiDynamizationWrite SetHmiFlashingDynamization(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, e.g. 'Door Overview'")] string screenName,
            [Description("itemName: name of the item, e.g. 'Lamp_Door1'")] string itemName,
            [Description("propertyName: the color property, e.g. 'BackColor'")] string propertyName,
            [Description("flashingCondition: optional Never, Always or RangeViolation")] string? flashingCondition = null,
            [Description("flashingRate: optional Slow (0.5 Hz), Medium (1 Hz) or Fast (2 Hz)")] string? flashingRate = null,
            [Description("color: optional first color, '#RRGGBB' or a name, e.g. '#FF0000'")] string? color = null,
            [Description("alternateColor: optional second color, e.g. '#FFFF00'")] string? alternateColor = null)
        {
            return Guarded(nameof(SetHmiFlashingDynamization), () =>
            {
                var result = Portal.SetHmiFlashingDynamization(softwarePath, screenName, itemName, propertyName, flashingCondition, flashingRate, color, alternateColor);
                return DynamizationWritten($"'{itemName}.{propertyName}' on screen '{screenName}' flashing set.", result);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiDynamization", Title = "Delete HMI dynamization", Destructive = true, Idempotent = true, OpenWorld = false),
         Description("Use to remove the dynamization (tag, script, flashing, ...) of one property of a WinCC Unified screen item; the property keeps its static value. The removed dynamization is returned and backed up as JSON")]
        public static ResponseHmiDynamizationWrite DeleteHmiDynamization(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, e.g. 'Door Overview'")] string screenName,
            [Description("itemName: name of the item, e.g. 'Lamp_Door1'")] string itemName,
            [Description("propertyName: the dynamized property, e.g. 'BackColor'")] string propertyName)
        {
            return Guarded(nameof(DeleteHmiDynamization), () =>
            {
                var result = Portal.DeleteHmiDynamization(softwarePath, screenName, itemName, propertyName);
                return DynamizationWritten($"{result.Previous?.Type} dynamization of '{itemName}.{propertyName}' on screen '{screenName}' deleted.", result);
            });
        }

        private static ResponseHmiDynamizationWrite DynamizationWritten(string what, Portal.HmiDynamizationWriteResult result) => new ResponseHmiDynamizationWrite
        {
            Message = $"{what} {SaveHint}" + BackupNote(result.Backup),
            ItemName = result.ItemName,
            Dynamization = result.Dynamization,
            Previous = result.Previous,
            Backup = result.Backup.Path,
            BackupWarning = result.Backup.Warning,
            Meta = OkMeta()
        };

        #endregion
    }
}
