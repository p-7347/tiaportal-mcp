using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // HMI tag usage search (BACKLOG A6), WinCC Unified only. Two steps:
        // 1. HMI tags: every tag whose name matches, or whose PLC tag is the given PLC tag or a
        //    member of it ('SM_HMIData' finds 'SM_HMIData.S[10].CMD').
        // 2. Where those HMI tags are used: tag, resource list and tag parameter dynamizations of
        //    screen items, faceplate interface bindings, script code (event handlers of items and
        //    screens, script dynamizations) that names the tag in quotes, and alarm trigger and
        //    acknowledgement tags.
        // Read-only. Screens are walked with a time budget; 'screenRegex' narrows a big HMI.

        public sealed class HmiTagUsageTag
        {
            public string Name { get; set; } = string.Empty;
            public string? TagTable { get; set; }
            public string? PlcTag { get; set; }
            public string? Connection { get; set; }
        }

        public sealed class HmiTagUse
        {
            /// <summary>Dynamization, FaceplateInterface, ItemEvent, ScreenEvent, ScriptDynamization or Alarm.</summary>
            public string Kind { get; set; } = string.Empty;
            public string? Screen { get; set; }
            public string? Item { get; set; }
            /// <summary>Property, event or alarm field, e.g. 'BackColor', 'Tapped', 'RaisedStateTag'.</summary>
            public string? Where { get; set; }
            /// <summary>The reference as written, e.g. 'Lamp_Door1' or 'Motor.Speed'.</summary>
            public string Reference { get; set; } = string.Empty;
            /// <summary>The HMI tag the reference belongs to.</summary>
            public string HmiTag { get; set; } = string.Empty;
            /// <summary>For scripts: the first line naming the tag.</summary>
            public string? Line { get; set; }
        }

        public sealed class HmiTagUsageResult
        {
            public List<HmiTagUsageTag> Tags { get; } = new List<HmiTagUsageTag>();
            public List<HmiTagUse> Uses { get; } = new List<HmiTagUse>();
            public int ScreensTotal { get; set; }
            public int ScreensSearched { get; set; }
            public int AlarmsSearched { get; set; }
            public string? StoppedReason { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        public HmiTagUsageResult GetHmiTagUsage(string softwarePath, string tag, bool useRegex, string screenRegex,
            bool includeScripts, bool includeAlarms, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetHmiTagUsage), PortalErrorCode.NotFound,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "'tag' is empty: give a PLC tag (e.g. 'SM_HMIData.S[10].CMD' or a DB name) or an HMI tag name.");
                    }

                    var clock = Stopwatch.StartNew();
                    var hmi = RequireHmiSoftware(softwarePath);
                    var result = new HmiTagUsageResult();
                    var matches = TagMatcher(tag, useRegex);

                    // Tag tables are read straight from the object model (no path lookup), so tables
                    // with '/' in their name, e.g. 'I/O', are included too.
                    foreach (var table in GetHmiTagTables(softwarePath))
                    {
                        if (clock.Elapsed > budget)
                        {
                            result.StoppedReason = $"stopped after {budget.TotalSeconds:0} s while reading HMI tag table '{table.Name}'; the screens were not searched. Give the full PLC tag or HMI tag name, or narrow with 'useRegex'";
                            return result;
                        }

                        foreach (var hmiTag in table.Tags)
                        {
                            var plcTag = ReadPart(() => hmiTag.PlcTag, "PlcTag", result.Warnings);
                            if (matches(hmiTag.Name) || (!string.IsNullOrEmpty(plcTag) && matches(plcTag!)))
                            {
                                result.Tags.Add(new HmiTagUsageTag
                                {
                                    Name = hmiTag.Name,
                                    TagTable = table.Name,
                                    PlcTag = string.IsNullOrEmpty(plcTag) ? null : plcTag,
                                    Connection = ReadPart(() => hmiTag.Connection, "Connection", result.Warnings)
                                });
                            }
                        }
                    }

                    if (result.Tags.Count == 0)
                    {
                        return result;
                    }

                    var names = result.Tags.Select(t => t.Name).ToList();
                    var scriptPattern = new Regex("[\"'](" + string.Join("|", names.Select(Regex.Escape)) + @")(?=[""'.\[])",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                    if (includeAlarms)
                    {
                        SearchAlarms(hmi, names, result);
                    }

                    var screens = GetHmiScreens(softwarePath, screenRegex);
                    result.ScreensTotal = screens.Count;

                    foreach (var screen in screens)
                    {
                        if (clock.Elapsed > budget)
                        {
                            result.StoppedReason = $"stopped after {budget.TotalSeconds:0} s at screen {result.ScreensSearched + 1} of {screens.Count}; narrow 'screenRegex' for the rest";
                            break;
                        }

                        result.ScreensSearched++;

                        if (includeScripts)
                        {
                            AddScriptUses(DescribeHmiEvents(screen).Select(e => (e.EventType, e.ScriptCode, e.GlobalDefinitionAreaScriptCode)),
                                "ScreenEvent", screen.Name, null, scriptPattern, names, result);
                        }

                        foreach (var item in screen.ScreenItems)
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            try
                            {
                                SearchItem(screen.Name, item, names, includeScripts, scriptPattern, result);
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"{screen.Name}/{item.Name}: not readable ({ex.Message})");
                            }
                        }
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("tag", tag));
        }

        /// <summary>
        /// Plain text: equal to the tag, or a member of it ('DB' matches 'DB.x' and 'DB[1]'), case-insensitive,
        /// double quotes ignored. Regex: matched against the name as it is.
        /// </summary>
        private static Func<string, bool> TagMatcher(string tag, bool useRegex)
        {
            if (useRegex)
            {
                Regex regex;
                try
                {
                    regex = new Regex(tag, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams, $"'{tag}' is not a valid regular expression: {ex.Message}", null, ex);
                }

                return name => regex.IsMatch(name);
            }

            var wanted = Unquote(tag);
            return name => IsSameOrMember(Unquote(name), wanted);
        }

        private static string Unquote(string text) => text.Replace("\"", string.Empty).Trim();

        private static bool IsSameOrMember(string reference, string name) =>
            reference.Equals(name, StringComparison.OrdinalIgnoreCase)
            || (reference.Length > name.Length
                && reference.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && (reference[name.Length] == '.' || reference[name.Length] == '['));

        /// <summary>The HMI tag a reference such as 'Motor.Speed' belongs to, or null.</summary>
        private static string? OwnerTag(string? reference, List<string> names)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            var text = Unquote(reference!);
            return names.FirstOrDefault(name => IsSameOrMember(text, name));
        }

        private static void SearchItem(string screenName, HmiScreenItemBase item, List<string> names, bool includeScripts, Regex scriptPattern, HmiTagUsageResult result)
        {
            foreach (var dynamization in item.Dynamizations)
            {
                if (dynamization == null)
                {
                    continue;
                }

                string? reference = dynamization switch
                {
                    TagDynamization tagDynamization => tagDynamization.Tag,
                    ResourceListDynamization resourceList => resourceList.Tag,
                    TagParameterDynamization tagParameter => tagParameter.DynamicTagName,
                    _ => null
                };

                var owner = OwnerTag(reference, names);
                if (owner != null)
                {
                    result.Uses.Add(new HmiTagUse { Kind = "Dynamization", Screen = screenName, Item = item.Name, Where = dynamization.PropertyName, Reference = reference!, HmiTag = owner });
                }

                if (includeScripts && dynamization is ScriptDynamization script)
                {
                    AddScriptUses(new (string, string?, string?)[] { (dynamization.PropertyName, script.ScriptCode, script.GlobalDefinitionAreaScriptCode) },
                        "ScriptDynamization", screenName, item.Name, scriptPattern, names, result);
                }
            }

            if (item is HmiFaceplateContainer faceplate)
            {
                foreach (var property in faceplate.Interface)
                {
                    foreach (var binding in property.Dynamizations.OfType<TagDynamization>())
                    {
                        var owner = OwnerTag(binding.Tag, names);
                        if (owner != null)
                        {
                            result.Uses.Add(new HmiTagUse { Kind = "FaceplateInterface", Screen = screenName, Item = item.Name, Where = property.PropertyName, Reference = binding.Tag, HmiTag = owner });
                        }
                    }
                }
            }

            if (includeScripts)
            {
                AddScriptUses(DescribeHmiEvents(item).Select(e => (e.EventType, e.ScriptCode, e.GlobalDefinitionAreaScriptCode)),
                    "ItemEvent", screenName, item.Name, scriptPattern, names, result);
            }
        }

        /// <summary>One use per script and tag; 'Line' is the first line that names the tag in quotes.</summary>
        private static void AddScriptUses(IEnumerable<(string Where, string? Code, string? GlobalCode)> scripts, string kind, string screenName, string? itemName,
            Regex scriptPattern, List<string> names, HmiTagUsageResult result)
        {
            foreach (var (where, code, globalCode) in scripts)
            {
                var text = (globalCode ?? string.Empty) + "\n" + (code ?? string.Empty);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match match in scriptPattern.Matches(text))
                {
                    var owner = names.First(n => n.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                    if (!seen.Add(owner))
                    {
                        continue;
                    }

                    var lineStart = text.LastIndexOf('\n', match.Index) + 1;
                    var lineEnd = text.IndexOf('\n', match.Index);
                    var line = text.Substring(lineStart, (lineEnd < 0 ? text.Length : lineEnd) - lineStart).Trim();

                    result.Uses.Add(new HmiTagUse
                    {
                        Kind = kind,
                        Screen = screenName,
                        Item = itemName,
                        Where = where,
                        Reference = match.Groups[1].Value,
                        HmiTag = owner,
                        Line = line.Length > 200 ? line.Substring(0, 200) + "..." : line
                    });
                }
            }
        }

        private void SearchAlarms(HmiSoftware hmi, List<string> names, HmiTagUsageResult result)
        {
            void Check(string alarm, string where, string? reference)
            {
                var owner = OwnerTag(reference, names);
                if (owner != null)
                {
                    result.Uses.Add(new HmiTagUse { Kind = "Alarm", Item = alarm, Where = where, Reference = reference!, HmiTag = owner });
                }
            }

            foreach (var alarm in hmi.DiscreteAlarms)
            {
                result.AlarmsSearched++;
                Check(alarm.Name, "RaisedStateTag", ReadPart(() => alarm.RaisedStateTag, "RaisedStateTag", result.Warnings));
                Check(alarm.Name, "AcknowledgmentControlTag", ReadPart(() => alarm.AcknowledgmentControlTag, "AcknowledgmentControlTag", result.Warnings));
                Check(alarm.Name, "AcknowledgmentStateTag", ReadPart(() => alarm.AcknowledgmentStateTag, "AcknowledgmentStateTag", result.Warnings));
            }

            foreach (var alarm in hmi.AnalogAlarms)
            {
                result.AlarmsSearched++;
                Check(alarm.Name, "RaisedStateTag", ReadPart(() => alarm.RaisedStateTag, "RaisedStateTag", result.Warnings));
            }
        }
    }
}
