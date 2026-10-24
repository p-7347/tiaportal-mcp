using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // Event scripts of WinCC Unified screen items and screens: create or replace one event's
        // script on many items at once, and delete it. Reading them: 'GetHmiScreenItems' with
        // includeScripts.

        private const string HmiEventTypeDescription =
            "eventType: the event, as TIA Portal names it: 'Tapped' (click), 'Down' (press), 'Up' (release), 'Activated', 'Deactivated', 'KeyDown', 'KeyUp', 'ContextTapped'; for a screen 'Loaded', 'Unloaded'. 'Click', 'Press' and 'Release' are accepted too";

        [WriteTool]
        [McpServerTool(Name = "SetHmiItemEventScript", Title = "Set HMI item event script", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to create or replace the JavaScript of one event (click, press, release, ...) on one or many WinCC Unified screen items, or on the screen itself, in one call - e.g. buttons that write a tag when pressed. With 'itemValues' each item gets its own script from one template: '{item}' becomes the item name and '{value}' its value. Every item and event is checked first and nothing changes when one is wrong. Previous scripts are saved as a .json backup. For property scripts use 'SetHmiScriptDynamization'")]
        public static ResponseHmiEventScriptWrite SetHmiItemEventScript(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description(HmiEventTypeDescription)] string eventType,
            [Description("scriptCode: JavaScript body of the event function, as in TIA Portal's script editor, e.g. 'Tags(\"PC_Vw_SelOP\").Write(1);' or with 'itemValues' 'Tags(\"PC_Vw_SelOP\").Write({value});'")] string scriptCode,
            [Description("itemNames: item names separated by commas or line breaks, e.g. 'Btn_OP1, Btn_OP2'. May be empty when 'itemRegex' is given or 'onScreen' is true")] string itemNames = "",
            [Description("itemRegex: optional regular expression over the item names; matching items get the script too, e.g. '^Btn_OP'")] string itemRegex = "",
            [Description("itemValues: optional JSON object of item name to value for '{value}' in 'scriptCode', e.g. '{\"Btn_OP1\":\"1\",\"Btn_OP2\":\"2\"}'. When given, '{item}' and '{value}' are replaced")] string itemValues = "",
            [Description("onScreen: true to set the event of the screen itself (e.g. 'Loaded'); items named as well get it too. Default false")] bool onScreen = false,
            [Description("globalDefinitionAreaScriptCode: optional code of the script's global definition area; empty keeps it")] string? globalDefinitionAreaScriptCode = null,
            [Description("async: optional; true runs the script asynchronously. Empty keeps the current setting")] bool? async = null)
        {
            return Guarded(nameof(SetHmiItemEventScript), () =>
            {
                var values = string.IsNullOrWhiteSpace(itemValues)
                    ? null
                    : ParsePropertyObject(itemValues).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

                var result = Portal.SetHmiItemEventScript(softwarePath, screenName, SplitNames(itemNames), itemRegex, onScreen, eventType, scriptCode,
                    string.IsNullOrEmpty(globalDefinitionAreaScriptCode) ? null : globalDefinitionAreaScriptCode, async, values, nameof(SetHmiItemEventScript));

                var created = result.Changes.Count(c => c.Action == "Created");
                var updated = result.Changes.Count(c => c.Action == "Updated");

                return EventScriptsWritten(result, created + updated == 0,
                    $"Event '{eventType}' on screen '{screenName}': {created} created, {updated} updated, {result.Changes.Count - created - updated} unchanged.");
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiItemEventScript", Title = "Delete HMI item event script", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete the script of one event on one or many WinCC Unified screen items, or on the screen itself. Items without that event are left as they are. Deleted scripts are saved as a .json backup and returned")]
        public static ResponseHmiEventScriptWrite DeleteHmiItemEventScript(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description(HmiEventTypeDescription)] string eventType,
            [Description("itemNames: item names separated by commas or line breaks. May be empty when 'itemRegex' is given or 'onScreen' is true")] string itemNames = "",
            [Description("itemRegex: optional regular expression over the item names")] string itemRegex = "",
            [Description("onScreen: true to delete the event of the screen itself. Default false")] bool onScreen = false)
        {
            return Guarded(nameof(DeleteHmiItemEventScript), () =>
            {
                var result = Portal.DeleteHmiItemEventScript(softwarePath, screenName, SplitNames(itemNames), itemRegex, onScreen, eventType,
                    nameof(DeleteHmiItemEventScript));

                return EventScriptsWritten(result, result.Changes.Count == 0,
                    $"Event '{eventType}' on screen '{screenName}': {result.Changes.Count} script(s) deleted.");
            });
        }

        private static ResponseHmiEventScriptWrite EventScriptsWritten(Portal.HmiEventScriptResult result, bool nothingChanged, string message)
        {
            var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

            return new ResponseHmiEventScriptWrite
            {
                Message = (nothingChanged ? message + " Nothing changed." : message + $" {SaveHint}") + BackupNote(backup),
                Changes = result.Changes,
                Backup = result.Backup,
                BackupWarning = result.BackupWarning,
                Meta = OkMeta()
            };
        }
    }
}
