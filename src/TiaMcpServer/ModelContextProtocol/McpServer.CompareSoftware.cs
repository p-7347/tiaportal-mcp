using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // TIA's own software compare and the project archive; see Portal.CompareSoftware.cs and
        // Portal.Archive.cs. Neither changes the project.

        [McpServerTool(Name = "CompareSoftware", Title = "Compare PLC software", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true),
         Description("Compare a PLC software with the connected PLC (online, the default) or with another PLC software in the project, using TIA Portal's own compare, and list the blocks, types and tags that differ or exist on one side only. Use it before a download or after an upload to see whether project and PLC match. Reads only; the online compare needs the PLC to be reachable")]
        public static ResponseCompareSoftware CompareSoftware(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("otherSoftwarePath: optional second PLC software in the project to compare with; empty compares with the online PLC")] string otherSoftwarePath = "",
            [Description("maxItems: maximum number of differences listed (default 200); 'differenceCount' is always the full count")] int maxItems = 200)
        {
            try
            {
                var comparison = Portal.CompareSoftware(softwarePath, otherSoftwarePath, maxItems, TimeSpan.FromSeconds(40));
                var target = string.IsNullOrWhiteSpace(otherSoftwarePath) ? "online" : otherSoftwarePath;
                var identical = comparison.DifferenceCount == 0 && comparison.StoppedReason == null;

                var message = identical
                    ? $"'{softwarePath}' and {(target == "online" ? "the online PLC" : $"'{target}'")} are identical ({comparison.RootState})"
                    : $"{comparison.DifferenceCount} difference(s) between '{softwarePath}' and {(target == "online" ? "the online PLC" : $"'{target}'")}" +
                      (comparison.DifferenceCount > comparison.Differences.Count ? $" (first {comparison.Differences.Count} listed)" : string.Empty);

                if (comparison.StoppedReason != null)
                {
                    message += $" - INCOMPLETE: {comparison.StoppedReason}";
                }

                return new ResponseCompareSoftware
                {
                    Message = message + ".",
                    ComparedWith = target,
                    RootState = comparison.RootState,
                    Identical = identical,
                    DifferenceCount = comparison.DifferenceCount,
                    Differences = comparison.Differences.Select(d => new ResponseSoftwareDifference
                    {
                        Path = d.Path,
                        State = d.State,
                        LeftName = d.LeftName,
                        RightName = d.RightName,
                        Detail = d.Detail
                    }).ToList(),
                    StoppedReason = comparison.StoppedReason,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["complete"] = comparison.StoppedReason == null
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error comparing '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ArchiveProject", Title = "Archive project", Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Write the saved project as a TIA Portal archive (.zapXX) into a folder, e.g. as a backup before importing or changing blocks. The open project stays where it is (unlike SaveAs). Fails while the project has unsaved changes, because TIA only archives the saved state")]
        public static ResponseArchiveProject ArchiveProject(
            [Description("targetDirectory: absolute path of the folder for the archive")] string targetDirectory,
            [Description("archiveName: optional file name; default '<project>_<yyyyMMdd_HHmm>.zap20' (for a V20 project)")] string archiveName = "",
            [Description("mode: 'Compressed' (default, one .zap file), 'DiscardRestorableDataAndCompressed' (smaller, drops undo history), 'None' or 'DiscardRestorableData' (a project folder, not retrievable as an archive)")] string mode = "Compressed")
        {
            try
            {
                if (!Enum.TryParse<ProjectArchivationMode>(mode, ignoreCase: true, out var parsedMode))
                {
                    throw new McpException($"Invalid mode '{mode}'. Allowed values are: {string.Join(", ", Enum.GetNames(typeof(ProjectArchivationMode)))}.");
                }

                var file = Portal.ArchiveProject(targetDirectory, archiveName, parsedMode);

                return new ResponseArchiveProject
                {
                    Message = file.Exists
                        ? $"Project archived to '{file.FullName}' ({file.Length / 1024 / 1024.0:0.0} MB)."
                        : $"Project archived into '{file.FullName}'.",
                    ArchiveFile = file.FullName,
                    SizeBytes = file.Exists ? file.Length : (long?)null,
                    Mode = parsedMode.ToString(),
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error archiving the project: {ex.Message}", ex);
            }
        }
    }
}
