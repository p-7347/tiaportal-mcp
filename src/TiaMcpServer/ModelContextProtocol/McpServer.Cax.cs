using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.Cax;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region CAx (AutomationML)

        [McpServerTool(Name = "ExportCax", Title = "Export CAx (AutomationML)", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export the project's hardware config (devices, modules, IO addresses) as an AutomationML (.aml) file for round-tripping with an ECAD tool like EPLAN. Omit devicePath to export the whole project; give it to export a single device only. This only writes a file - the project itself isn't changed.")]
        public static ResponseCaxTransfer ExportCax(
            [Description("exportPath: directory to write the .aml file into")] string exportPath,
            [Description("devicePath: full path to a single device to export; omit to export the entire project")] string? devicePath = null)
        {
            try
            {
                var result = Portal.ExportCax(devicePath, exportPath);
                return BuildCaxResponse(result, $"CAx export of {(string.IsNullOrEmpty(devicePath) ? "the whole project" : $"'{devicePath}'")} to '{exportPath}'");
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting CAx to '{exportPath}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "ImportCax", Title = "Import CAx (AutomationML)", Destructive = true, Idempotent = false, OpenWorld = false),
         Description("Import an AutomationML (.aml) file (e.g. edited in EPLAN after 'ExportCax') into the project; it can restructure the hardware configuration and has no preview, so confirm=true is required: get the user's explicit go-ahead and prefer a test project. mergeOption on conflicts: 'MoveToParkingLot' (default, safest: conflicting items go to TIA's Parking Lot), 'OverwriteTiaDevice' (AML wins) or 'RetainTiaDevice' (TIA wins)")]
        public static ResponseCaxTransfer ImportCax(
            [Description("importPath: full path to the .aml file to import")] string importPath,
            [Description("confirm: must be explicitly true to actually import - false (default) refuses with an explanation instead of importing, since there's no preview available")] bool confirm = false,
            [Description("mergeOption: 'MoveToParkingLot' (default), 'OverwriteTiaDevice', or 'RetainTiaDevice'")] string mergeOption = "MoveToParkingLot")
        {
            if (!Enum.TryParse<CaxImportOptions>(mergeOption, ignoreCase: true, out var parsedOption))
            {
                throw new McpException($"Invalid mergeOption '{mergeOption}' - must be one of: MoveToParkingLot, OverwriteTiaDevice, RetainTiaDevice");
            }

            return Guarded(nameof(ImportCax), () =>
            {
                var (success, logText, logPath) = Portal.ImportCax(importPath, parsedOption, confirm);

                const int maxLogChars = 12000;
                var truncated = logText.Length > maxLogChars;
                var shownLog = truncated
                    ? logText.Substring(0, maxLogChars) + $"\n...(truncated, {logText.Length - maxLogChars} more chars - full log at {logPath})"
                    : logText;

                return new ResponseCaxTransfer
                {
                    Message = $"CAx import from '{importPath}' (mergeOption={parsedOption}): {(success ? "Success" : "Error")} - see LogText/LogPath for detail",
                    State = success ? "Success" : "Error",
                    LogPath = logPath,
                    LogText = shownLog,
                    Meta = Ok(new JsonObject { ["success"] = success })
                };
            });
        }

        private static ResponseCaxTransfer BuildCaxResponse(TransferResult result, string actionDescription)
        {
            var messages = result.Messages
                .Select(m => new ResponseCaxMessage { Message = m.Message, State = m.State.ToString(), DateTime = m.DateTime })
                .ToList();

            return new ResponseCaxTransfer
            {
                Message = $"{actionDescription}: {result.State} ({result.ErrorCount} error(s), {result.WarningCount} warning(s))",
                State = result.State.ToString(),
                ErrorCount = result.ErrorCount,
                WarningCount = result.WarningCount,
                Messages = messages,
                Meta = Ok(new JsonObject { ["success"] = result.State != TransferResultState.Error })
            };
        }

        #endregion
    }
}
