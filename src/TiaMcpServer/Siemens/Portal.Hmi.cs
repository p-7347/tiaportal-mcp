using Microsoft.Extensions.Logging;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.TextGraphicList;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        #region hmi tag tables

        // Unified Comfort/Advanced Panels only (Siemens.Engineering.HmiUnified.HmiTags) - classic
        // WinCC Comfort/Basic panels use a different, older API (Siemens.Engineering.Hmi.Tag) not
        // covered here. Read-only: HmiTagTable has no Export() method in this Openness version
        // (unlike PlcTagTable), confirmed empirically in the original port, not just undocumented.
        // softwarePath for an HMI device is typically "HMI_n/HMI_RT_n" - the HmiSoftware lives on
        // a nested DeviceItem, not the top-level HMI device itself (GetSoftwareContainer, shared
        // with the PLC-software path resolver, already handles this generically).

        public List<HmiTagTable> GetHmiTagTables(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI tag tables...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiTagTable>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    CollectHmiTagTablesFromComposition(hmiSoftware.TagTables, list, regexName);

                    foreach (var subgroup in hmiSoftware.TagTableGroups)
                    {
                        GetHmiTagTablesRecursive(subgroup, list, regexName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiTagTables failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        public HmiTagTable? GetHmiTagTable(string softwarePath, string tagTablePath)
        {
            _logger?.LogInformation($"Getting HMI tag table by path: {tagTablePath}");

            if (IsProjectNull())
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(tagTablePath))
            {
                return null;
            }

            var softwareContainer = GetSoftwareContainer(softwarePath);
            if (softwareContainer?.Software is HmiSoftware hmiSoftware)
            {
                var parts = tagTablePath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return null;
                }

                var tableName = parts[parts.Length - 1];

                if (parts.Length == 1)
                {
                    // Search root-level tables first, then recurse into groups
                    var found = hmiSoftware.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                    return FindHmiTagTableRecursive(hmiSoftware.TagTableGroups, tableName);
                }
                else
                {
                    HmiTagTableGroup? current = hmiSoftware.TagTableGroups.FirstOrDefault(g => g.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    for (int i = 1; i < parts.Length - 1 && current != null; i++)
                    {
                        current = current.Groups.FirstOrDefault(g => g.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                    }

                    if (current == null)
                    {
                        return null;
                    }

                    return current.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                }
            }

            return null;
        }

        public List<HmiTag> GetHmiTags(string softwarePath, string tagTablePath, string regexName = "")
        {
            _logger?.LogInformation($"Getting HMI tags for table: {tagTablePath}");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiTag>();

            try
            {
                var table = GetHmiTagTable(softwarePath, tagTablePath);
                if (table != null)
                {
                    foreach (var tag in table.Tags)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(tag.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(tag);
                    }
                }
            }
            catch (Exception)
            {
                // Same swallow style as GetTags (PLC)
            }

            return list;
        }

        private static HmiTagTable? FindHmiTagTableRecursive(HmiTagTableGroupComposition groups, string tableName)
        {
            foreach (var group in groups)
            {
                var found = group.TagTables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
                found = FindHmiTagTableRecursive(group.Groups, tableName);
                if (found != null) return found;
            }
            return null;
        }

        private static void CollectHmiTagTablesFromComposition(HmiTagTableComposition tables, List<HmiTagTable> list, string regexName)
        {
            foreach (var table in tables)
            {
                try
                {
                    if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(table.Name, regexName, RegexOptions.IgnoreCase))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                list.Add(table);
            }
        }

        private static void GetHmiTagTablesRecursive(HmiTagTableGroup group, List<HmiTagTable> list, string regexName = "")
        {
            CollectHmiTagTablesFromComposition(group.TagTables, list, regexName);

            foreach (var subgroup in group.Groups)
            {
                GetHmiTagTablesRecursive(subgroup, list, regexName);
            }
        }

        #endregion

        #region hmi screens/alarms/text lists (Unified Comfort/Advanced Panels only)

        // Same Unified-only scope as the hmi tag tables region above. All read-only - no write
        // API found for any of these.

        public List<HmiScreen> GetHmiScreens(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI screens...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiScreen>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    CollectHmiScreensFromComposition(hmiSoftware.Screens, list, regexName);

                    foreach (var subgroup in hmiSoftware.ScreenGroups)
                    {
                        GetHmiScreensRecursive(subgroup, list, regexName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiScreens failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        private static void CollectHmiScreensFromComposition(HmiScreenComposition screens, List<HmiScreen> list, string regexName)
        {
            foreach (var screen in screens)
            {
                try
                {
                    if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(screen.Name, regexName, RegexOptions.IgnoreCase))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                list.Add(screen);
            }
        }

        private static void GetHmiScreensRecursive(HmiScreenGroup group, List<HmiScreen> list, string regexName = "")
        {
            CollectHmiScreensFromComposition(group.Screens, list, regexName);

            foreach (var subgroup in group.Groups)
            {
                GetHmiScreensRecursive(subgroup, list, regexName);
            }
        }

        public List<HmiDiscreteAlarm> GetHmiDiscreteAlarms(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI discrete alarms...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiDiscreteAlarm>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    foreach (var alarm in hmiSoftware.DiscreteAlarms)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(alarm.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(alarm);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiDiscreteAlarms failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        public List<HmiAnalogAlarm> GetHmiAnalogAlarms(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI analog alarms...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiAnalogAlarm>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    foreach (var alarm in hmiSoftware.AnalogAlarms)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(alarm.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(alarm);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiAnalogAlarms failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        public List<HmiAlarmClass> GetHmiAlarmClasses(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI alarm classes...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiAlarmClass>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    foreach (var alarmClass in hmiSoftware.AlarmClasses)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(alarmClass.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(alarmClass);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiAlarmClasses failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        // Lists text list names only - Openness in this version has no type exposing individual
        // text list entries/values (verified in the original port: no HmiTextListEntry-shaped
        // type exists in the installed Siemens.Engineering.dll), so entry contents aren't
        // reachable via this tool.
        public List<HmiTextList> GetHmiTextLists(string softwarePath, string regexName = "")
        {
            _logger?.LogInformation("Getting HMI text lists...");

            if (IsProjectNull())
            {
                return [];
            }

            var list = new List<HmiTextList>();

            try
            {
                var softwareContainer = GetSoftwareContainer(softwarePath);
                if (softwareContainer?.Software is HmiSoftware hmiSoftware)
                {
                    foreach (var textList in hmiSoftware.HmiTextLists)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(regexName) && !Regex.IsMatch(textList.Name, regexName, RegexOptions.IgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        list.Add(textList);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetHmiTextLists failed for {SoftwarePath}", softwarePath);
                throw;
            }

            return list;
        }

        #endregion
    }
}
