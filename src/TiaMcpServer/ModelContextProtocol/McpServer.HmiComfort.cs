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
        // Classic WinCC (Comfort/Basic panel) screens, read-only. Openness has no object model for
        // classic screen items, so GetComfortScreenItems exports the screen and reads the XML.

        private const string ComfortPathDescription = "softwarePath: path of the classic WinCC (Comfort/Basic panel) HMI software, e.g. 'HMI_1/HMI_RT_1'; 'GetProjectTree' shows it as 'HmiTarget'";

        [McpServerTool(Name = "GetComfortScreens", Title = "Get Comfort screens", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the screens, screen templates and pop-up screens of a classic WinCC HMI (Comfort/Basic panels), including those in subfolders, with their folder. For WinCC Unified use 'GetHmiScreens'")]
        public static ResponseComfortScreens GetComfortScreens(
            [Description(ComfortPathDescription)] string softwarePath,
            [Description("regexName: optional name or regular expression to filter the screens. Empty (default) lists all")] string regexName = "")
        {
            try
            {
                var list = Portal.GetComfortScreens(softwarePath, regexName);

                return new ResponseComfortScreens
                {
                    Message = $"{list.Count(s => s.Kind == "Screen")} screen(s), {list.Count(s => s.Kind == "Template")} template(s), " +
                              $"{list.Count(s => s.Kind == "Popup")} pop-up(s) in '{softwarePath}'",
                    Items = list,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["count"] = list.Count }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing the classic HMI screens in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetComfortScreenItems", Title = "Get Comfort screen items", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the items of one classic WinCC screen, template or pop-up (Comfort/Basic panels) with their kind, layer, position, size, texts, linked tags/text lists/graphic lists and event functions (e.g. what a button does when clicked), read from an XML export of the screen. Group and faceplate contents are under 'children'. Use it to rebuild a Comfort screen in WinCC Unified. For WinCC Unified screens use 'GetHmiScreenItems'")]
        public static ResponseComfortScreenItems GetComfortScreenItems(
            [Description(ComfortPathDescription)] string softwarePath,
            [Description("screenName: name of the screen, template or pop-up as 'GetComfortScreens' lists it; 'Folder/Name' when the name is not unique")] string screenName,
            [Description("kind: optional Screen, Template or Popup; empty (default) looks for a screen first, then a template, then a pop-up")] string kind = "",
            [Description("regexName: optional name or regular expression to filter the top-level items. Empty (default) lists all")] string regexName = "",
            [Description("includeAttributes: also return every plain attribute of each item and of the screen (colors, fonts, formats, ...). Default false; the reply gets much longer")] bool includeAttributes = false,
            [Description("keepExportPath: optional absolute directory on the PC running this server to keep the screen's XML export in, for details this tool does not report. Empty (default) deletes it")] string keepExportPath = "")
        {
            try
            {
                var read = Portal.GetComfortScreenItems(softwarePath, screenName, kind, regexName, includeAttributes, keepExportPath);
                var events = read.Items.Sum(i => i.Events?.Count ?? 0) + (read.ScreenEvents?.Count ?? 0);

                return new ResponseComfortScreenItems
                {
                    Message = $"{read.Items.Count} item(s), {events} event(s) on {read.Kind.ToLowerInvariant()} '{read.Name}' in '{softwarePath}'" +
                              (read.ExportFile != null ? $"; export kept at '{read.ExportFile}'" : string.Empty),
                    Kind = read.Kind,
                    Template = read.Template,
                    ScreenNumber = read.ScreenNumber,
                    Width = read.Width,
                    Height = read.Height,
                    Attributes = read.Attributes,
                    Items = read.Items,
                    ScreenEvents = read.ScreenEvents,
                    ExportFile = read.ExportFile,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true, ["count"] = read.Items.Count }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the items of classic HMI screen '{screenName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }
    }
}
