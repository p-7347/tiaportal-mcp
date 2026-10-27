using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.Scripts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Writing WinCC Unified discrete and analog alarms, and reading and importing global script
        // modules (BACKLOG C1 alarms part, C7).
        //
        // Callers: SetHmiAlarms, DeleteHmiAlarms, GetHmiScriptModules and ImportHmiScriptModules in
        // McpServer.HmiAlarmWrite.cs. Affected API: additive. Every write call saves the previous
        // state as a .json backup.
        //
        // Alarm properties are set generically by their Openness attribute name ('EventText',
        // 'AlarmClass', 'RaisedStateTag', 'TriggerBitAddress', 'Priority', ...), converted to the
        // attribute's own type, so new properties need no code. Texts are written to every project
        // language. As in the other HMI write tools, every entry is checked before the first write.
        //
        // Script modules have no object model for their code: Openness exports and imports them as
        // files, so the read tool exports into a scratch folder and returns the text, and the write
        // tool imports a folder the caller prepared (typically an edited export).

        public sealed class HmiAlarmChange
        {
            public string Name { get; set; } = string.Empty;

            /// <summary>Created, Updated, Unchanged or Deleted.</summary>
            public string Action { get; set; } = string.Empty;

            /// <summary>Property name to value after the call (before it, for Deleted).</summary>
            public Dictionary<string, string?> Values { get; set; } = new Dictionary<string, string?>();
        }

        public sealed class HmiAlarmWriteResult
        {
            public List<HmiAlarmChange> Changes { get; set; } = new List<HmiAlarmChange>();
            public string? Backup { get; set; }
            public string? BackupWarning { get; set; }
        }

        public sealed class HmiScriptModuleInfo
        {
            public string Name { get; set; } = string.Empty;
            public List<HmiScriptFile> Files { get; set; } = new List<HmiScriptFile>();
        }

        public sealed class HmiScriptFile
        {
            public string Name { get; set; } = string.Empty;
            public string? Text { get; set; }
            public bool Truncated { get; set; }
        }

        private const int MaxScriptFileChars = 20000;

        /// <summary>One alarm of a SetHmiAlarms call: its name and property name to value text.</summary>
        public sealed class HmiAlarmSpec
        {
            public string Name { get; set; } = string.Empty;
            public List<KeyValuePair<string, string>> Properties { get; set; } = new List<KeyValuePair<string, string>>();
        }

        /// <param name="kind">'discrete' or 'analog'.</param>
        /// <param name="createMissing">Create an alarm that does not exist yet; otherwise it is an error.</param>
        public HmiAlarmWriteResult SetHmiAlarms(string softwarePath, string kind, IReadOnlyList<HmiAlarmSpec> alarms, bool createMissing, string toolName)
        {
            return Operation.Run(_logger, nameof(SetHmiAlarms), PortalErrorCode.InvalidParams,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);
                    var analog = IsAnalogKind(kind);

                    if (alarms.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No alarms given.");
                    }

                    var problems = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var existing = new List<(HmiAlarmSpec Spec, IEngineeringObject? Alarm)>();

                    foreach (var spec in alarms)
                    {
                        var name = (spec.Name ?? string.Empty).Trim();
                        if (name.Length == 0)
                        {
                            problems.Add("an entry has no name");
                            continue;
                        }

                        if (!seen.Add(name))
                        {
                            problems.Add($"'{name}': given twice");
                            continue;
                        }

                        var alarm = FindHmiAlarm(hmi, analog, name);
                        if (alarm == null && !createMissing)
                        {
                            problems.Add($"'{name}': no such {(analog ? "analog" : "discrete")} alarm (set 'createMissing' to create it)");
                            continue;
                        }

                        existing.Add((spec, alarm));
                    }

                    // Properties of existing alarms are checked now; new ones are checked against
                    // an existing alarm of the same kind when there is one, else after creating them.
                    var sample = existing.Select(e => e.Alarm).FirstOrDefault(a => a != null)
                        ?? (analog ? (IEngineeringObject?)hmi.AnalogAlarms.FirstOrDefault() : hmi.DiscreteAlarms.FirstOrDefault());

                    foreach (var (spec, alarm) in existing)
                    {
                        var target = alarm ?? sample;
                        if (target != null)
                        {
                            ResolveAlarmWrites(target, spec, problems);
                        }
                    }

                    ThrowIfProblems(problems, "Nothing was changed");

                    var before = existing.Where(e => e.Alarm != null)
                        .Select(e => new { name = e.Spec.Name, values = e.Spec.Properties.ToDictionary(p => p.Key, p => AlarmValueText(e.Alarm!, p.Key)) })
                        .ToList();

                    var result = new HmiAlarmWriteResult();
                    if (before.Count > 0)
                    {
                        var json = JsonSerializer.Serialize(new { softwarePath, kind, alarms = before }, new JsonSerializerOptions { WriteIndented = true });
                        var backup = WriteBackup(toolName, $"{before.Count} HMI alarm(s)", true, file => File.WriteAllText(file.FullName, json), "json");
                        result.Backup = backup.Path;
                        result.BackupWarning = backup.Warning;
                    }

                    var writes = new List<(HmiAlarmSpec Spec, IEngineeringObject Alarm, bool Created)>();
                    foreach (var (spec, alarm) in existing)
                    {
                        var target = alarm ?? (analog ? (IEngineeringObject)hmi.AnalogAlarms.Create(spec.Name.Trim()) : hmi.DiscreteAlarms.Create(spec.Name.Trim()));
                        writes.Add((spec, target, alarm == null));
                    }

                    // New alarms without a sample to check against are checked here, still before any property write.
                    if (sample == null)
                    {
                        foreach (var write in writes)
                        {
                            ResolveAlarmWrites(write.Alarm, write.Spec, problems);
                        }

                        ThrowIfProblems(problems, "Nothing was changed");
                    }

                    foreach (var (spec, alarm, created) in writes)
                    {
                        var changed = false;
                        foreach (var write in ResolveAlarmWrites(alarm, spec, problems))
                        {
                            if (write.Text != null)
                            {
                                if (!string.Equals(PropertyText(write.Before), PlainText(write.Text) ?? string.Empty, StringComparison.Ordinal))
                                {
                                    SetHmiText((MultilingualText)write.Before!, write.Text);
                                    changed = true;
                                }
                            }
                            else if (!Equals(write.Before, write.Value))
                            {
                                alarm.SetAttribute(write.Attribute, write.Value);
                                changed = true;
                            }
                        }

                        result.Changes.Add(new HmiAlarmChange
                        {
                            Name = spec.Name.Trim(),
                            Action = created ? "Created" : changed ? "Updated" : "Unchanged",
                            Values = spec.Properties.ToDictionary(p => p.Key, p => AlarmValueText(alarm, p.Key))
                        });
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("kind", kind), ("count", alarms.Count.ToString()));
        }

        public HmiAlarmWriteResult DeleteHmiAlarms(string softwarePath, string kind, IReadOnlyList<string> names, string regexName, string toolName)
        {
            return Operation.Run(_logger, nameof(DeleteHmiAlarms), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);
                    var analog = IsAnalogKind(kind);
                    var regex = string.IsNullOrWhiteSpace(regexName) ? null : new Regex(regexName);

                    if (names.Count == 0 && regex == null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "Name the alarms to delete, or give a regular expression over their names.");
                    }

                    var problems = new List<string>();
                    var found = new List<IEngineeringObject>();

                    foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var alarm = FindHmiAlarm(hmi, analog, name);
                        if (alarm == null)
                        {
                            problems.Add($"'{name}': no such {(analog ? "analog" : "discrete")} alarm");
                        }
                        else
                        {
                            found.Add(alarm);
                        }
                    }

                    if (regex != null)
                    {
                        IEnumerable<IEngineeringObject> all = analog ? hmi.AnalogAlarms.Cast<IEngineeringObject>() : hmi.DiscreteAlarms.Cast<IEngineeringObject>();
                        found.AddRange(all.Where(a => regex.IsMatch(AlarmName(a)) && !found.Contains(a)));
                    }

                    ThrowIfProblems(problems, "Nothing was deleted");

                    if (found.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"No {(analog ? "analog" : "discrete")} alarm matches '{regexName}'.");
                    }

                    var result = new HmiAlarmWriteResult();
                    var before = found.Select(a => new { name = AlarmName(a), values = AlarmValues(a) }).ToList();
                    var json = JsonSerializer.Serialize(new { softwarePath, kind, alarms = before }, new JsonSerializerOptions { WriteIndented = true });
                    var backup = WriteBackup(toolName, $"{found.Count} HMI alarm(s)", true, file => File.WriteAllText(file.FullName, json), "json");
                    result.Backup = backup.Path;
                    result.BackupWarning = backup.Warning;

                    for (var i = 0; i < found.Count; i++)
                    {
                        result.Changes.Add(new HmiAlarmChange { Name = before[i].name, Action = "Deleted", Values = before[i].values });
                        if (found[i] is HmiAnalogAlarm analogAlarm) analogAlarm.Delete(); else ((HmiDiscreteAlarm)found[i]).Delete();
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("kind", kind));
        }

        public List<HmiScriptModuleInfo> GetHmiScriptModules(string softwarePath, string regexName, bool includeCode)
        {
            return Operation.Run(_logger, nameof(GetHmiScriptModules), PortalErrorCode.ExportFailed,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);
                    var regex = string.IsNullOrWhiteSpace(regexName) ? null : new Regex(regexName, RegexOptions.IgnoreCase);
                    var result = new List<HmiScriptModuleInfo>();

                    using var scope = new SourceScope(_logger);

                    foreach (var module in hmi.Scripts)
                    {
                        if (regex != null && !regex.IsMatch(module.Name))
                        {
                            continue;
                        }

                        var info = new HmiScriptModuleInfo { Name = module.Name };
                        if (includeCode)
                        {
                            var directory = Directory.CreateDirectory(Path.Combine(scope.Directory, FileNames.Safe(module.Name)));
                            foreach (var file in module.Export(directory))
                            {
                                info.Files.Add(ReadScriptFile(file));
                            }
                        }

                        result.Add(info);
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("regexName", regexName));
        }

        /// <summary>
        /// Imports the script files in 'sourceDirectory' (as 'GetHmiScriptModules' with
        /// 'exportPath' writes them). Every existing module is exported to the backup folder first.
        /// </summary>
        public (List<string> Modules, BackupResult Backup) ImportHmiScriptModules(string softwarePath, string sourceDirectory, string name, string toolName)
        {
            return Operation.Run(_logger, nameof(ImportHmiScriptModules), PortalErrorCode.ImportFailed,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);

                    if (!Directory.Exists(sourceDirectory))
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"sourceDirectory '{sourceDirectory}' does not exist on the PC running this server.");
                    }

                    var before = new List<object>();
                    using (var scope = new SourceScope(_logger))
                    {
                        foreach (var module in hmi.Scripts)
                        {
                            var directory = Directory.CreateDirectory(Path.Combine(scope.Directory, FileNames.Safe(module.Name)));
                            before.Add(new { name = module.Name, files = module.Export(directory).Select(ReadScriptFile).ToList() });
                        }
                    }

                    var json = JsonSerializer.Serialize(new { softwarePath, modules = before }, new JsonSerializerOptions { WriteIndented = true });
                    var backup = WriteBackup(toolName, "HMI script modules", true, file => File.WriteAllText(file.FullName, json), "json");

                    var source = new DirectoryInfo(sourceDirectory);
                    var ok = string.IsNullOrWhiteSpace(name) ? hmi.Scripts.Import(source) : hmi.Scripts.Import(source, name.Trim());
                    if (!ok)
                    {
                        throw new PortalException(PortalErrorCode.ImportFailed,
                            $"TIA Portal did not import the script files in '{sourceDirectory}'. Use a folder as 'GetHmiScriptModules' exports it.");
                    }

                    return (hmi.Scripts.Select(m => m.Name).ToList(), backup);
                },
                ("softwarePath", softwarePath), ("sourceDirectory", sourceDirectory));
        }

        /// <summary>Exports the script modules into 'exportPath' for editing and returns the folder of each.</summary>
        public List<string> ExportHmiScriptModules(string softwarePath, string regexName, string exportPath)
        {
            return Operation.Run(_logger, nameof(ExportHmiScriptModules), PortalErrorCode.ExportFailed,
                () =>
                {
                    var hmi = RequireHmiSoftware(softwarePath);
                    var regex = string.IsNullOrWhiteSpace(regexName) ? null : new Regex(regexName, RegexOptions.IgnoreCase);
                    var directory = Directory.CreateDirectory(exportPath);
                    var files = new List<string>();

                    foreach (var module in hmi.Scripts)
                    {
                        if (regex == null || regex.IsMatch(module.Name))
                        {
                            files.AddRange(module.Export(directory).Select(f => f.FullName));
                        }
                    }

                    return files;
                },
                ("softwarePath", softwarePath), ("exportPath", exportPath));
        }

        private static HmiScriptFile ReadScriptFile(FileInfo file)
        {
            var text = File.ReadAllText(file.FullName);
            return new HmiScriptFile
            {
                Name = file.Name,
                Text = text.Length > MaxScriptFileChars ? text.Substring(0, MaxScriptFileChars) : text,
                Truncated = text.Length > MaxScriptFileChars
            };
        }

        private static bool IsAnalogKind(string kind)
        {
            var value = (kind ?? string.Empty).Trim();
            if (value.Equals("analog", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Length == 0 || value.Equals("discrete", StringComparison.OrdinalIgnoreCase)) return false;

            throw new PortalException(PortalErrorCode.InvalidParams, $"Unknown alarm kind '{kind}'. Use 'discrete' or 'analog'.");
        }

        private static IEngineeringObject? FindHmiAlarm(HmiSoftware hmi, bool analog, string name) =>
            analog ? hmi.AnalogAlarms.Find(name) : hmi.DiscreteAlarms.Find(name);

        private static string AlarmName(IEngineeringObject alarm) => alarm.GetAttribute("Name") as string ?? string.Empty;

        /// <summary>Every readable plain or text attribute of an alarm, for backups.</summary>
        private static Dictionary<string, string?> AlarmValues(IEngineeringObject alarm)
        {
            var values = new Dictionary<string, string?>();
            foreach (var info in alarm.GetAttributeInfos())
            {
                try
                {
                    var value = alarm.GetAttribute(info.Name);
                    if (value is MultilingualText || (value != null && IsPlainValue(value)))
                    {
                        values[info.Name] = PropertyText(value);
                    }
                }
                catch (Exception)
                {
                    // Attributes that cannot be read in this state are left out of the backup.
                }
            }

            return values;
        }

        private static string? AlarmValueText(IEngineeringObject alarm, string attribute)
        {
            try
            {
                return PropertyText(alarm.GetAttribute(attribute));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Converts every property of one entry; problems are collected, not thrown.</summary>
        private static List<ItemPropertyWrite> ResolveAlarmWrites(IEngineeringObject alarm, HmiAlarmSpec spec, List<string> problems)
        {
            var writes = new List<ItemPropertyWrite>();
            var infos = alarm.GetAttributeInfos().ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in spec.Properties)
            {
                if (pair.Key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!infos.TryGetValue(pair.Key, out var info))
                {
                    problems.Add($"'{spec.Name}': alarms have no property '{pair.Key}'. Some: {string.Join(", ", infos.Keys.OrderBy(k => k).Take(25))}");
                    continue;
                }

                object? current;
                try
                {
                    current = alarm.GetAttribute(info.Name);
                }
                catch (Exception ex)
                {
                    problems.Add($"'{spec.Name}.{info.Name}': not readable ({ex.Message})");
                    continue;
                }

                var write = new ItemPropertyWrite { Item = spec.Name, Path = info.Name, Owner = alarm, Attribute = info.Name, Before = current };

                if (current is MultilingualText)
                {
                    write.Text = pair.Value;
                }
                else if (info.AccessMode != EngineeringAttributeAccessMode.ReadWrite)
                {
                    problems.Add($"'{spec.Name}.{info.Name}' is read-only");
                    continue;
                }
                else
                {
                    try
                    {
                        write.Value = current == null || current is string
                            ? pair.Value
                            : ConvertPropertyValue(pair.Value, current.GetType(), $"{spec.Name}.{info.Name}");
                    }
                    catch (PortalException pex)
                    {
                        problems.Add(pex.Message);
                        continue;
                    }
                }

                writes.Add(write);
            }

            return writes;
        }
    }
}
