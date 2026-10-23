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
        // HMI tag usage search (BACKLOG A6); see Portal.HmiTagUsage.cs.

        [McpServerTool(Name = "GetHmiTagUsage", Title = "Get HMI tag usage", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to find where a PLC tag or HMI tag shows up on a WinCC Unified HMI before changing or renaming it: the HMI tags connected to the PLC tag, then every screen item dynamization, faceplate interface binding, event or dynamization script and alarm that uses those HMI tags. A DB or struct name also finds its members. Changes nothing")]
        public static ResponseHmiTagUsage GetHmiTagUsage(
            [Description("softwarePath: defines the path in the project structure to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'")] string softwarePath,
            [Description("tag: the PLC tag as the HMI tag shows it, e.g. 'SM_HMIData.SwLamp_S[10].Origin_Sw', a DB or struct name such as 'SM_HMIData', or an HMI tag name")] string tag,
            [Description("useRegex: treat 'tag' as a regular expression on HMI tag names and PLC tags (default false)")] bool useRegex = false,
            [Description("screenRegex: optional regular expression on screen names to search fewer screens, e.g. '^Servo'")] string screenRegex = "",
            [Description("includeScripts: also search event and dynamization scripts for the tag name in quotes (default true)")] bool includeScripts = true,
            [Description("includeAlarms: also check the trigger and acknowledgement tags of discrete and analog alarms (default true)")] bool includeAlarms = true)
        {
            try
            {
                var usage = Portal.GetHmiTagUsage(softwarePath, tag, useRegex, screenRegex, includeScripts, includeAlarms, TimeSpan.FromSeconds(40));
                var complete = usage.StoppedReason == null;

                string message;
                if (usage.Tags.Count == 0 && !complete)
                {
                    message = $"No matching HMI tag found so far in '{softwarePath}' - INCOMPLETE: {usage.StoppedReason}.";
                }
                else if (usage.Tags.Count == 0)
                {
                    message = $"No HMI tag in '{softwarePath}' is named '{tag}' or connected to it, so nothing on the HMI uses it.";
                }
                else
                {
                    var screens = usage.Uses.Where(u => u.Screen != null).Select(u => u.Screen).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    message = $"{usage.Tags.Count} HMI tag(s) match '{tag}'; {usage.Uses.Count} use(s) on {screens} screen(s)" +
                              (includeAlarms ? $" and in {usage.Uses.Count(u => u.Kind == "Alarm")} alarm field(s)" : string.Empty) +
                              $", {usage.ScreensSearched} of {usage.ScreensTotal} screen(s) searched";
                    if (!complete)
                    {
                        message += $" - INCOMPLETE: {usage.StoppedReason}";
                    }

                    message += ".";
                }

                return new ResponseHmiTagUsage
                {
                    Message = message,
                    Tags = usage.Tags.Select(t => new ResponseHmiTagUsageTag { Name = t.Name, TagTable = t.TagTable, PlcTag = t.PlcTag, Connection = t.Connection }).ToList(),
                    Uses = usage.Uses.Select(u => new ResponseHmiTagUse
                    {
                        Kind = u.Kind,
                        Screen = u.Screen,
                        Item = u.Item,
                        Where = u.Where,
                        Reference = u.Reference,
                        HmiTag = u.HmiTag,
                        Line = u.Line
                    }).ToList(),
                    ScreensTotal = usage.ScreensTotal,
                    ScreensSearched = usage.ScreensSearched,
                    AlarmsSearched = usage.AlarmsSearched,
                    StoppedReason = usage.StoppedReason,
                    Warnings = usage.Warnings.Count > 0 ? usage.Warnings.Distinct().Take(20).ToList() : null,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["complete"] = complete
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error searching HMI tag usage in '{softwarePath}': {ex.Message}", ex);
            }
        }
    }
}
