using Siemens.Engineering.HmiUnified;
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
        //
        // Reading screens through Openness is slow (about one second per screen on a real HMI), so
        // every reference found is kept in an index per HMI software. A call that runs out of time
        // keeps what it read and the next call continues; once the index is complete any tag is
        // found at once. The index lives until the server restarts, another project is opened, or
        // 'refreshIndex' is given - screens changed after they were read are searched as they were.
        // Read-only.

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
            /// <summary>Screens read in this call (the rest came from the index).</summary>
            public int ScreensReadNow { get; set; }
            public int AlarmsSearched { get; set; }
            public string? StoppedReason { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        /// <summary>One place that names a tag: a tag reference, or script text to search.</summary>
        private sealed class HmiReference
        {
            public string Kind { get; set; } = string.Empty;
            public string? Screen { get; set; }
            public string? Item { get; set; }
            public string? Where { get; set; }
            public string? Reference { get; set; }
            public string? Script { get; set; }
        }

        private sealed class HmiUsageIndex
        {
            public string Key { get; set; } = string.Empty;
            public List<HmiTagUsageTag>? Tags { get; set; }
            public List<HmiReference>? Alarms { get; set; }
            public int AlarmCount { get; set; }
            public Dictionary<string, List<HmiReference>> Screens { get; } = new Dictionary<string, List<HmiReference>>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, HmiUsageIndex> HmiUsageIndexes = new Dictionary<string, HmiUsageIndex>(StringComparer.OrdinalIgnoreCase);

        public HmiTagUsageResult GetHmiTagUsage(string softwarePath, string tag, bool useRegex, string screenRegex,
            bool includeScripts, bool includeAlarms, bool refreshIndex, TimeSpan budget)
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
                    var matches = TagMatcher(tag, useRegex);
                    var result = new HmiTagUsageResult();

                    var key = (_project?.Path?.FullName ?? string.Empty) + "|" + softwarePath;
                    if (refreshIndex || !HmiUsageIndexes.TryGetValue(key, out var index))
                    {
                        index = new HmiUsageIndex { Key = key };
                        HmiUsageIndexes[key] = index;
                    }

                    // 1. HMI tags. Tag tables are read straight from the object model (no path
                    //    lookup), so tables with '/' in their name, e.g. 'I/O', are included too.
                    if (index.Tags == null)
                    {
                        var tags = new List<HmiTagUsageTag>();
                        foreach (var table in GetHmiTagTables(softwarePath))
                        {
                            foreach (var hmiTag in table.Tags)
                            {
                                var plcTag = ReadPart(() => hmiTag.PlcTag, "PlcTag", result.Warnings);
                                tags.Add(new HmiTagUsageTag
                                {
                                    Name = hmiTag.Name,
                                    TagTable = table.Name,
                                    PlcTag = string.IsNullOrEmpty(plcTag) ? null : plcTag,
                                    Connection = ReadPart(() => hmiTag.Connection, "Connection", result.Warnings)
                                });
                            }
                        }

                        index.Tags = tags;
                    }

                    result.Tags.AddRange(index.Tags.Where(t => matches(t.Name) || (t.PlcTag != null && matches(t.PlcTag))));
                    if (result.Tags.Count == 0)
                    {
                        return result;
                    }

                    var names = result.Tags.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var scriptPattern = new Regex("[\"'](" + string.Join("|", names.Select(Regex.Escape)) + @")(?=[""'.\[])",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                    // 2. Screens first (what people ask about most), then alarms, all within the budget.
                    var screens = GetHmiScreens(softwarePath, screenRegex);
                    result.ScreensTotal = screens.Count;

                    foreach (var screen in screens)
                    {
                        if (!index.Screens.TryGetValue(screen.Name, out var references))
                        {
                            if (clock.Elapsed > budget)
                            {
                                continue;
                            }

                            references = ReadScreenReferences(screen, result.Warnings);
                            index.Screens[screen.Name] = references;
                            result.ScreensReadNow++;
                        }

                        result.ScreensSearched++;
                        Match(references, names, includeScripts, scriptPattern, result);
                    }

                    if (includeAlarms)
                    {
                        if (index.Alarms == null && clock.Elapsed <= budget)
                        {
                            index.Alarms = ReadAlarmReferences(hmi, result.Warnings, out var count);
                            index.AlarmCount = count;
                        }

                        if (index.Alarms != null)
                        {
                            result.AlarmsSearched = index.AlarmCount;
                            Match(index.Alarms, names, false, scriptPattern, result);
                        }
                    }

                    var screensLeft = result.ScreensTotal - result.ScreensSearched;
                    var alarmsLeft = includeAlarms && index.Alarms == null;
                    if (screensLeft > 0 || alarmsLeft)
                    {
                        result.StoppedReason = $"time budget of {budget.TotalSeconds:0} s used up: " +
                            (screensLeft > 0 ? $"{screensLeft} of {result.ScreensTotal} screen(s)" : string.Empty) +
                            (screensLeft > 0 && alarmsLeft ? " and " : string.Empty) +
                            (alarmsLeft ? "the alarms" : string.Empty) +
                            " not read yet. Call again with the same arguments to continue; what was read is kept, so each call gets further";
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

        private static void Match(List<HmiReference> references, List<string> names, bool includeScripts, Regex scriptPattern, HmiTagUsageResult result)
        {
            foreach (var reference in references)
            {
                if (reference.Script == null)
                {
                    var owner = OwnerTag(reference.Reference, names);
                    if (owner != null)
                    {
                        result.Uses.Add(new HmiTagUse { Kind = reference.Kind, Screen = reference.Screen, Item = reference.Item, Where = reference.Where, Reference = reference.Reference!, HmiTag = owner });
                    }

                    continue;
                }

                if (!includeScripts)
                {
                    continue;
                }

                // One use per script and tag; 'Line' is the first line that names the tag in quotes.
                var text = reference.Script;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in scriptPattern.Matches(text))
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
                        Kind = reference.Kind,
                        Screen = reference.Screen,
                        Item = reference.Item,
                        Where = reference.Where,
                        Reference = match.Groups[1].Value,
                        HmiTag = owner,
                        Line = line.Length > 200 ? line.Substring(0, 200) + "..." : line
                    });
                }
            }
        }

        /// <summary>Every tag reference and script of one screen and its items.</summary>
        private static List<HmiReference> ReadScreenReferences(global::Siemens.Engineering.HmiUnified.UI.Screens.HmiScreen screen, List<string> warnings)
        {
            var references = new List<HmiReference>();

            void AddScripts(IEnumerable<HmiEventDescription> events, string kind, string? itemName)
            {
                foreach (var e in events)
                {
                    references.Add(new HmiReference
                    {
                        Kind = kind,
                        Screen = screen.Name,
                        Item = itemName,
                        Where = e.EventType,
                        Script = (e.GlobalDefinitionAreaScriptCode ?? string.Empty) + "\n" + (e.ScriptCode ?? string.Empty)
                    });
                }
            }

            try
            {
                AddScripts(DescribeHmiEvents(screen), "ScreenEvent", null);
            }
            catch (Exception ex)
            {
                warnings.Add($"{screen.Name}: screen events not readable ({ex.Message})");
            }

            foreach (var item in screen.ScreenItems)
            {
                if (item == null)
                {
                    continue;
                }

                try
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

                        if (!string.IsNullOrEmpty(reference))
                        {
                            references.Add(new HmiReference { Kind = "Dynamization", Screen = screen.Name, Item = item.Name, Where = dynamization.PropertyName, Reference = reference });
                        }

                        if (dynamization is ScriptDynamization script)
                        {
                            references.Add(new HmiReference
                            {
                                Kind = "ScriptDynamization",
                                Screen = screen.Name,
                                Item = item.Name,
                                Where = dynamization.PropertyName,
                                Script = (script.GlobalDefinitionAreaScriptCode ?? string.Empty) + "\n" + (script.ScriptCode ?? string.Empty)
                            });
                        }
                    }

                    if (item is HmiFaceplateContainer faceplate)
                    {
                        foreach (var property in faceplate.Interface)
                        {
                            foreach (var binding in property.Dynamizations.OfType<TagDynamization>())
                            {
                                if (!string.IsNullOrEmpty(binding.Tag))
                                {
                                    references.Add(new HmiReference { Kind = "FaceplateInterface", Screen = screen.Name, Item = item.Name, Where = property.PropertyName, Reference = binding.Tag });
                                }
                            }
                        }
                    }

                    AddScripts(DescribeHmiEvents(item), "ItemEvent", item.Name);
                }
                catch (Exception ex)
                {
                    warnings.Add($"{screen.Name}/{item.Name}: not readable ({ex.Message})");
                }
            }

            return references;
        }

        private static List<HmiReference> ReadAlarmReferences(HmiSoftware hmi, List<string> warnings, out int count)
        {
            var references = new List<HmiReference>();
            var alarms = 0;

            void Add(string alarm, string where, string? reference)
            {
                if (!string.IsNullOrEmpty(reference))
                {
                    references.Add(new HmiReference { Kind = "Alarm", Item = alarm, Where = where, Reference = reference });
                }
            }

            foreach (var alarm in hmi.DiscreteAlarms)
            {
                alarms++;
                Add(alarm.Name, "RaisedStateTag", ReadPart(() => alarm.RaisedStateTag, "RaisedStateTag", warnings));
                Add(alarm.Name, "AcknowledgmentControlTag", ReadPart(() => alarm.AcknowledgmentControlTag, "AcknowledgmentControlTag", warnings));
                Add(alarm.Name, "AcknowledgmentStateTag", ReadPart(() => alarm.AcknowledgmentStateTag, "AcknowledgmentStateTag", warnings));
            }

            foreach (var alarm in hmi.AnalogAlarms)
            {
                alarms++;
                Add(alarm.Name, "RaisedStateTag", ReadPart(() => alarm.RaisedStateTag, "RaisedStateTag", warnings));
            }

            count = alarms;
            return references;
        }
    }
}
