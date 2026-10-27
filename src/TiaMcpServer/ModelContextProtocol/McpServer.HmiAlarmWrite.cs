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
        // WinCC Unified alarm writing and global script modules; see Portal.HmiAlarmWrite.cs.

        private const string HmiAlarmKindDescription = "kind: 'discrete' (default) or 'analog'";

        [WriteTool]
        [McpServerTool(Name = "SetHmiAlarms", Title = "Create or change HMI alarms", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to create or change WinCC Unified discrete or analog alarms, one or many per call: texts, alarm class, trigger tag and bit, priority, area and any other alarm property by its Openness name, as 'GetHmiDiscreteAlarms'/'GetHmiAnalogAlarms' and the error for an unknown property list them. Texts are written to every project language. Every entry is checked first and nothing is changed when one is wrong. The previous values are saved as a .json backup")]
        public static ResponseHmiAlarmWrite SetHmiAlarms(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("alarms: JSON array, one object per alarm with 'name' and the properties to set, e.g. '[{\"name\":\"Alarm_U1_Door\",\"EventText\":\"Unit 1 door open\",\"AlarmClass\":\"Alarm\",\"RaisedStateTag\":\"HMI_Alarm_Word_1\",\"RaisedStateTagBitNumber\":3}]'")] string alarms,
            [Description(HmiAlarmKindDescription)] string kind = "discrete",
            [Description("createMissing: create alarms that do not exist yet (default false: an unknown name is an error)")] bool createMissing = false)
        {
            return Guarded(nameof(SetHmiAlarms), () =>
            {
                var result = Portal.SetHmiAlarms(softwarePath, kind, ParseHmiAlarms(alarms), createMissing, nameof(SetHmiAlarms));
                var counts = string.Join(", ", result.Changes.GroupBy(c => c.Action).Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}"));
                var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

                return new ResponseHmiAlarmWrite
                {
                    Message = $"HMI alarms: {counts}. {SaveHint}" + (result.Backup == null && result.BackupWarning == null ? string.Empty : BackupNote(backup)),
                    Alarms = result.Changes,
                    Backup = result.Backup,
                    BackupWarning = result.BackupWarning,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiAlarms", Title = "Delete HMI alarms", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Delete WinCC Unified discrete or analog alarms by name and/or regular expression. Every name is checked first and nothing is deleted when one does not exist. The deleted alarms' properties are saved as a .json backup")]
        public static ResponseHmiAlarmWrite DeleteHmiAlarms(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("alarmNames: alarm names separated by commas or line breaks. May be empty when 'regexName' is given")] string alarmNames = "",
            [Description("regexName: optional regular expression over the alarm names; matching alarms are deleted too, e.g. '^MCP_TEST_'")] string regexName = "",
            [Description(HmiAlarmKindDescription)] string kind = "discrete")
        {
            return Guarded(nameof(DeleteHmiAlarms), () =>
            {
                var result = Portal.DeleteHmiAlarms(softwarePath, kind, SplitNames(alarmNames), regexName, nameof(DeleteHmiAlarms));
                var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

                return new ResponseHmiAlarmWrite
                {
                    Message = $"{result.Changes.Count} HMI alarm(s) deleted: {string.Join(", ", result.Changes.Select(c => c.Name).Take(20))}. {SaveHint}" + BackupNote(backup),
                    Alarms = result.Changes,
                    Backup = result.Backup,
                    BackupWarning = result.BackupWarning,
                    Meta = OkMeta()
                };
            });
        }

        [McpServerTool(Name = "GetHmiScriptModules", Title = "Get HMI script modules", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to read the global script modules (JavaScript functions and global definitions) of a WinCC Unified HMI. Returns the module names and, with 'includeCode', the exported files with their text. With 'exportPath' the files are also written to that folder, ready to edit and bring back with 'ImportHmiScriptModules'. Changes nothing in the project")]
        public static ResponseHmiScriptModules GetHmiScriptModules(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("regexName: optional regular expression over the module names (default: all)")] string regexName = "",
            [Description("includeCode: return the exported file texts (default true; each file is cut at 20000 characters)")] bool includeCode = true,
            [Description("exportPath: optional absolute folder on the PC running this server to write the exported files to")] string exportPath = "")
        {
            return Guarded(nameof(GetHmiScriptModules), () =>
            {
                var modules = Portal.GetHmiScriptModules(softwarePath, regexName, includeCode);
                var exported = new List<string>();
                if (!string.IsNullOrWhiteSpace(exportPath))
                {
                    Operation.RequireAbsolutePath(exportPath, nameof(exportPath));
                    exported = Portal.ExportHmiScriptModules(softwarePath, regexName, exportPath);
                }

                return new ResponseHmiScriptModules
                {
                    Message = $"{modules.Count} script module(s) in '{softwarePath}'." + (exported.Count == 0 ? string.Empty : $" {exported.Count} file(s) written to '{exportPath}'."),
                    Modules = modules,
                    ExportedFiles = exported.Count == 0 ? null : exported,
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "ImportHmiScriptModules", Title = "Import HMI script modules", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to create or replace WinCC Unified global script modules from files, typically a folder written by 'GetHmiScriptModules' with 'exportPath' and then edited. All existing modules are saved as a .json backup first")]
        public static ResponseHmiScriptModules ImportHmiScriptModules(
            [Description(HmiPathDescription)] string softwarePath,
            [Description("sourceDirectory: absolute folder on the PC running this server with the script module files")] string sourceDirectory,
            [Description("name: optional module name to import from the folder (default: everything in it)")] string name = "")
        {
            return Guarded(nameof(ImportHmiScriptModules), () =>
            {
                Operation.RequireAbsolutePath(sourceDirectory, nameof(sourceDirectory));
                var (names, backup) = Portal.ImportHmiScriptModules(softwarePath, sourceDirectory, name, nameof(ImportHmiScriptModules));

                return new ResponseHmiScriptModules
                {
                    Message = $"Script files from '{sourceDirectory}' imported. Modules now: {string.Join(", ", names)}. {SaveHint}" + BackupNote(backup),
                    Modules = names.Select(n => new Portal.HmiScriptModuleInfo { Name = n }).ToList(),
                    Backup = backup.Path,
                    BackupWarning = backup.Warning,
                    Meta = OkMeta()
                };
            });
        }

        private static List<Portal.HmiAlarmSpec> ParseHmiAlarms(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new McpException("'alarms' is required: a JSON array such as [{\"name\":\"Alarm_1\",\"EventText\":\"Door open\"}].");
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new McpException("'alarms' must be a JSON array of objects, e.g. [{\"name\":\"Alarm_1\",\"EventText\":\"Door open\"}].");
                }

                return document.RootElement.EnumerateArray().Select(element =>
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        throw new McpException("Every entry of 'alarms' must be an object with 'name'.");
                    }

                    var spec = new Portal.HmiAlarmSpec();
                    foreach (var p in element.EnumerateObject())
                    {
                        var text = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText();
                        if (p.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                        {
                            spec.Name = text;
                        }
                        else
                        {
                            spec.Properties.Add(new KeyValuePair<string, string>(p.Name, text));
                        }
                    }

                    return spec;
                }).ToList();
            }
            catch (JsonException ex)
            {
                throw new McpException($"'alarms' is not valid JSON: {ex.Message}", ex);
            }
        }
    }
}
