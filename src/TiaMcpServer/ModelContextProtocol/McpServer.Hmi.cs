using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region hmi tag tables

        [McpServerTool(Name = "GetHmiTagTables", Title = "Get HMI tag tables", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of HMI tag tables from a device's HMI software (Unified Comfort/Advanced Panels only - classic WinCC Comfort/Basic panels aren't supported by this tool). Read-only: there's no export tool for HMI tag tables, this Openness version doesn't expose one.")]
        public static ResponseHmiTagTables GetHmiTagTables(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the tag table. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiTagTables(softwarePath, regexName);

                var responseList = list
                    .Where(table => table != null)
                    .Select(table => new ResponseHmiTagTableInfo { Name = table.Name })
                    .ToList();

                return new ResponseHmiTagTables
                {
                    Message = $"HMI tag tables with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI tag tables with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiTags", Title = "Get HMI tags", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of tags from a specific HMI tag table (Unified Comfort/Advanced Panels only), including address/PLC-link/connection info")]
        public static ResponseHmiTags GetHmiTags(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("tagTablePath: full path to the HMI tag table, e.g. 'Group/Subgroup/Name' (single names allowed at root level)")] string tagTablePath,
            [Description("regexName: defines the name or regular expression to find the tag. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiTags(softwarePath, tagTablePath, regexName);

                var responseList = list
                    .Where(tag => tag != null)
                    .Select(tag => new ResponseHmiTagInfo
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
                    })
                    .ToList();

                return new ResponseHmiTags
                {
                    Message = $"HMI tags with regex '{regexName}' retrieved from tag table '{tagTablePath}' in '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI tags from '{tagTablePath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion

        #region hmi screens/alarms/text lists

        [McpServerTool(Name = "GetHmiScreens", Title = "Get HMI screens", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Use to list the screens of an HMI. Get a list of screens in a device's HMI software (Unified Comfort/Advanced Panels only)")]
        public static ResponseHmiScreens GetHmiScreens(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the screen. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiScreens(softwarePath, regexName);

                var responseList = list
                    .Where(screen => screen != null)
                    .Select(screen => new ResponseHmiScreenInfo
                    {
                        Name = screen.Name,
                        DisplayName = Helper.FirstText(screen.DisplayName),
                        DisplayText = TiaMcpServer.Siemens.Portal.PlainText(Helper.FirstText(screen.DisplayName)),
                        ScreenNumber = screen.ScreenNumber,
                        Width = screen.Width,
                        Height = screen.Height
                    })
                    .ToList();

                return new ResponseHmiScreens
                {
                    Message = $"HMI screens with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI screens with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        private static readonly TimeSpan AlarmBudget = TimeSpan.FromSeconds(40);

        /// <summary>
        /// Reads alarms from 'offset' on until about 40 s have passed. Every alarm property is a call into
        /// TIA Portal; an unfiltered list of a big HMI ran past the client's timeout and the server was cut off.
        /// </summary>
        private static (List<ResponseHmiAlarmInfo> Items, int? Next) AlarmPage<T>(List<T> alarms, int offset, Func<T, ResponseHmiAlarmInfo> read)
        {
            if (offset < 0)
            {
                throw new McpException("'offset' must not be negative.");
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var items = new List<ResponseHmiAlarmInfo>();

            for (var i = offset; i < alarms.Count; i++)
            {
                if (items.Count > 0 && clock.Elapsed > AlarmBudget)
                {
                    return (items, i);
                }

                items.Add(read(alarms[i]));
            }

            return (items, null);
        }

        [McpServerTool(Name = "GetHmiDiscreteAlarms", Title = "Get HMI discrete alarms", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of discrete (bit-triggered) alarms from a device's HMI software (Unified Comfort/Advanced Panels only). A long list stops after about 40 s with 'nextOffset'; call again with offset=nextOffset for the rest")]
        public static ResponseHmiAlarms GetHmiDiscreteAlarms(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the alarm. Use empty string (default) to find all")] string regexName = "",
            [Description("offset: continue a list that stopped on time: the 'nextOffset' of the previous call with the same regexName (default 0)")] int offset = 0)
        {
            try
            {
                var list = Portal.GetHmiDiscreteAlarms(softwarePath, regexName).Where(alarm => alarm != null).ToList();

                var (responseList, next) = AlarmPage(list, offset, alarm => new ResponseHmiAlarmInfo
                    {
                        Name = alarm.Name,
                        EventText = Helper.FirstText(alarm.EventText),
                        InfoText = Helper.FirstText(alarm.InfoText),
                        AlarmClass = alarm.AlarmClass,
                        Area = alarm.Area,
                        Priority = alarm.Priority,
                        TriggerAddress = alarm.TriggerBitAddress
                    });

                return new ResponseHmiAlarms
                {
                    Message = $"HMI discrete alarms with regex '{regexName}' retrieved from '{softwarePath}': {responseList.Count} of {list.Count}" +
                              (offset > 0 ? $" from offset {offset}" : string.Empty) +
                              (next.HasValue ? $". Stopped after about 40 s to stay inside the client timeout - call again with offset={next.Value} for the rest, or narrow 'regexName'" : string.Empty),
                    Items = responseList,
                    Total = list.Count,
                    NextOffset = next,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI discrete alarms with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiAnalogAlarms", Title = "Get HMI analog alarms", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of analog (limit-triggered) alarms from a device's HMI software (Unified Comfort/Advanced Panels only). A long list stops after about 40 s with 'nextOffset'; call again with offset=nextOffset for the rest")]
        public static ResponseHmiAlarms GetHmiAnalogAlarms(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the alarm. Use empty string (default) to find all")] string regexName = "",
            [Description("offset: continue a list that stopped on time: the 'nextOffset' of the previous call with the same regexName (default 0)")] int offset = 0)
        {
            try
            {
                var list = Portal.GetHmiAnalogAlarms(softwarePath, regexName).Where(alarm => alarm != null).ToList();

                var (responseList, next) = AlarmPage(list, offset, alarm => new ResponseHmiAlarmInfo
                    {
                        Name = alarm.Name,
                        EventText = Helper.FirstText(alarm.EventText),
                        InfoText = Helper.FirstText(alarm.InfoText),
                        AlarmClass = alarm.AlarmClass,
                        Area = alarm.Area,
                        Priority = alarm.Priority,
                        TriggerAddress = alarm.TriggerAddress,
                        Condition = alarm.Condition.ToString()
                    });

                return new ResponseHmiAlarms
                {
                    Message = $"HMI analog alarms with regex '{regexName}' retrieved from '{softwarePath}': {responseList.Count} of {list.Count}" +
                              (offset > 0 ? $" from offset {offset}" : string.Empty) +
                              (next.HasValue ? $". Stopped after about 40 s to stay inside the client timeout - call again with offset={next.Value} for the rest, or narrow 'regexName'" : string.Empty),
                    Items = responseList,
                    Total = list.Count,
                    NextOffset = next,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI analog alarms with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiAlarmClasses", Title = "Get HMI alarm classes", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Use to list the alarm classes defined in an HMI software. Get a list of alarm classes (Unified Comfort/Advanced Panels only), including priority, state machine, acknowledgment and state colors")]
        public static ResponseHmiAlarmClasses GetHmiAlarmClasses(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the alarm class. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiAlarmClasses(softwarePath, regexName);

                var responseList = list
                    .Where(alarmClass => alarmClass != null)
                    .Select(alarmClass => new ResponseHmiAlarmClassInfo
                    {
                        Name = alarmClass.Name,
                        Id = alarmClass.Id,
                        IsSystem = alarmClass.IsSystem,
                        Priority = alarmClass.Priority,
                        StateMachine = alarmClass.StateMachine.ToString(),
                        Log = alarmClass.Log,
                        CommonAlarmClass = alarmClass.CommonAlarmClass,
                        RaisedState = ToAlarmStateInfo(alarmClass.RaisedState),
                        AcknowledgedState = ToAlarmStateInfo(alarmClass.AcknowledgedState),
                        ClearedState = ToAlarmStateInfo(alarmClass.ClearedState),
                        AcknowledgedClearedState = ToAlarmStateInfo(alarmClass.AcknowledgedClearedState)
                    })
                    .ToList();

                return new ResponseHmiAlarmClasses
                {
                    Message = $"HMI alarm classes with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI alarm classes with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        private static ResponseHmiAlarmStateInfo? ToAlarmStateInfo(AlarmStatusVisuals? visuals)
        {
            if (visuals == null) return null;
            try
            {
                return new ResponseHmiAlarmStateInfo
                {
                    BackColor = FormatAlarmColor(visuals.BackColor),
                    TextColor = FormatAlarmColor(visuals.TextColor),
                    Flashing = visuals.Flashing
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? FormatAlarmColor(System.Drawing.Color color)
        {
            if (color.IsEmpty) return null;
            return color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        [McpServerTool(Name = "GetHmiTextLists", Title = "Get HMI text lists", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Get a list of text list names from a device's HMI software (Unified Comfort/Advanced Panels only). Names only - this Openness version has no way to read individual text list entries/values.")]
        public static ResponseHmiTextLists GetHmiTextLists(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the text list. Use empty string (default) to find all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetHmiTextLists(softwarePath, regexName);

                var responseList = list
                    .Where(textList => textList != null)
                    .Select(textList => new ResponseHmiTextListInfo { Name = textList.Name })
                    .ToList();

                return new ResponseHmiTextLists
                {
                    Message = $"HMI text lists with regex '{regexName}' retrieved from '{softwarePath}'",
                    Items = responseList,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving HMI text lists with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
