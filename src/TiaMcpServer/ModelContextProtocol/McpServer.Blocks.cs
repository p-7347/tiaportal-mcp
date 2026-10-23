using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region blocks

        [McpServerTool(Name = "GetBlockInfo", Title = "Get block info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show one program block: path, type, programming language, memory layout, consistency, know-how protection, modified date and attributes")]
        public static ResponseBlockInfo GetBlockInfo(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: path of the block relative to 'Program blocks', '/'-separated, e.g. '009_Servo/zGDBs/SM_UserData_Origin'")] string blockPath)
        {
            try
            {
                var block = Portal.GetBlock(softwarePath, blockPath);
                if (block is not null)
                {
                    var attributes = Helper.GetAttributeList(block);

                    return new ResponseBlockInfo
                    {
                        Path = Portal.GetBlockPath(block),
                        Message = $"Block info retrieved from '{blockPath}' in '{softwarePath}'",
                        Name = block.Name,
                        TypeName = block.GetType().Name,
                        Namespace = block.Namespace,
                        ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage),block.ProgrammingLanguage),
                        MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                        IsConsistent = block.IsConsistent,
                        HeaderName = block.HeaderName,
                        ModifiedDate = block.ModifiedDate,
                        IsKnowHowProtected = block.IsKnowHowProtected,
                        Attributes = attributes,
                        Description = block.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Block not found at '{blockPath}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving block info from '{blockPath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetBlocks", Title = "Get blocks", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Use to find a block's path from its name. Flat list with each block's path; for the group tree use 'GetBlocksWithHierarchy'. List the program blocks of a PLC software, optionally filtered by a name or regular expression")]
        public static ResponseBlocks GetBlocks(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the block, matched anywhere in the block name ('G3U' also matches G3U10..G3U12; anchor with ^...$ for an exact name). Use empty string (default) to find all")] string regexName = "",
            [Description("includeAttributes: return every Openness attribute of each block (about 30 each). Default true. Pass false on a large PLC when only names and paths are needed - reading the attributes is most of this tool's run time")] bool includeAttributes = true,
            [Description("offset: number of matching blocks to skip, for paging through a large PLC. Default 0")] int offset = 0,
            [Description("limit: maximum number of blocks to return; 0 (default) returns all. 'totalCount' in the reply is the number of matches before paging")] int limit = 0,
            [Description("modifiedSince: optional date/time; only blocks changed in TIA Portal after it are returned (REQUESTS.md P6). ISO 8601, e.g. '2026-10-13T09:00:00' (no offset = local time of the server PC) or '2026-10-13T00:00:00Z'. 'modifiedDate' in the reply is UTC")] string modifiedSince = "")
        {
            try
            {
                if (offset < 0 || limit < 0)
                {
                    throw new McpException("offset and limit must not be negative.");
                }

                var list = Portal.GetBlocks(softwarePath, regexName);

                DateTime? since = null;

                if (!string.IsNullOrWhiteSpace(modifiedSince))
                {
                    if (!DateTime.TryParse(modifiedSince, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeLocal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                    {
                        throw new McpException($"modifiedSince '{modifiedSince}' is not a date/time; use ISO 8601 such as '2026-10-13T09:00:00'.");
                    }

                    since = parsed;
                }

                var matches = list?.Where(b => b != null)
                    .Where(b => since == null || AsUtc(b.ModifiedDate) > since.Value)
                    .ToList() ?? new List<PlcBlock>();
                var page = matches.Skip(offset).Take(limit > 0 ? limit : int.MaxValue).ToList();
                var paged = offset > 0 || (limit > 0 && matches.Count > limit);

                var responseList = new List<ResponseBlockInfo>();
                foreach (var block in page)
                {
                    if (block != null)
                    {
                        var attributes = includeAttributes ? Helper.GetAttributeList(block) : null;

                        responseList.Add(new ResponseBlockInfo
                        {
                            Path = Portal.GetBlockPath(block),
                            Name = block.Name,
                            TypeName = block.GetType().Name,
                            Namespace = block.Namespace,
                            ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                            MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                            IsConsistent = block.IsConsistent,
                            HeaderName = block.HeaderName,
                            ModifiedDate = block.ModifiedDate,
                            IsKnowHowProtected = block.IsKnowHowProtected,
                            Attributes = attributes,
                            Description = block.ToString()
                        });
                    }
                }

                if (list != null)
                {
                    return new ResponseBlocks
                    {
                        Message = $"Blocks with regex '{regexName}' retrieved from '{softwarePath}'" +
                                  (since != null ? $", modified after {since.Value:yyyy-MM-dd HH:mm:ss} UTC" : string.Empty) +
                                  (paged ? $" - returned {responseList.Count} of {matches.Count} starting at {offset}" : string.Empty),
                        Items = responseList,
                        TotalCount = matches.Count,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed retrieving blocks with regex '{regexName}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving blocks with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        // Openness reports ModifiedDate in UTC; an Unspecified kind is taken as UTC too.
        private static DateTime AsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        [McpServerTool(Name = "GetBlocksWithHierarchy", Title = "Get blocks with hierarchy", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Use to see the whole block tree with its group nesting in one call. Get a list of all blocks with their group hierarchy from the PLC software. To find one block by name use 'GetBlocks'; to see types, tags, tables and sources as well use 'GetSoftwareTree'")]
        public static ResponseBlocksWithHierarchy GetBlocksWithHierarchy(
        [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath)
        {
            try
            {
                var rootGroup = Portal.GetBlockRootGroup(softwarePath);
                if (rootGroup is not null)
                {
                    var hierarchy = Helper.BuildBlockHierarchy(rootGroup, Portal);
                    return new ResponseBlocksWithHierarchy
                    {
                        Message = $"Block hierarchy retrieved from '{softwarePath}'",
                        Root = hierarchy,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    // Specific failure: root group could not be resolved
                    throw new McpException($"Block root group not found for '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                // Generic unexpected failure wrapper
                throw new McpException($"Unexpected error retrieving block hierarchy for '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportXmlBlock", Title = "Export block as XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Use to get a block's full SimaticML, e.g. as the source of a network for 'EditBlockNetwork'; to read code prefer 'GetBlockSource'. Export one program block as an XML file named after the block into exportPath, below its group folders when preservePath is true. Formats: XML (SimaticML) is complete and lossless and is what 'ImportXmlBlock' and 'EditBlockNetwork' use; documents ('ExportAsDocuments', .s7dcl/.s7res, V20+) are readable text for review; external source ('ExportSourceBlock') exists only for SCL, STL and DB blocks. An existing file is replaced; inconsistent blocks are rejected")]
        public static ResponseExportXmlBlock ExportXmlBlock(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: path of the block relative to 'Program blocks', '/'-separated, e.g. '009_Servo/zGDBs/SM_UserData_Origin' (a bare name works only when it is unique). 'GetBlocks' or 'ResolveObjectPath' give it")] string blockPath,
            [Description("exportPath: absolute directory on the PC running this server, e.g. 'C:\\Temp\\export'; relative paths are rejected. The file is named after the block")] string exportPath,
            [Description("preservePath: preserves the path/structure of the PLC software")] bool preservePath = false)
        {
            try
            {
                var block = Portal.ExportXmlBlock(softwarePath, blockPath, exportPath, preservePath);
                if (block is not null)
                {
                    return new ResponseExportXmlBlock
                    {
                        Message = $"Block exported from '{blockPath}' to '{exportPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                // Should not be reachable because Portal.ExportXmlBlock throws on failure
                throw new McpException($"Failed exporting block from '{blockPath}' to '{exportPath}'");
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                // Map known portal errors to sharper MCP errors and messages.
                switch (pex.Code)
                {
                    case TiaMcpServer.Siemens.PortalErrorCode.NotFound:
                        {
                            var suggestionNote = string.Empty;
                            // If the path has no '/', it may be incomplete; build suggestions using Portal's regex search and path resolver
                            if (!string.IsNullOrEmpty(blockPath) && !blockPath.Contains('/'))
                            {
                                try
                                {
                                    var escaped = Regex.Escape(blockPath);
                                    var blocks = Portal.GetBlocks(softwarePath, $"^{escaped}$");
                                    if (blocks == null || blocks.Count == 0)
                                    {
                                        blocks = Portal.GetBlocks(softwarePath, escaped);
                                    }

                                    var candidates = blocks
                                        .Take(10)
                                        .Select(b => Portal.GetBlockPath(b))
                                        .Where(p => !string.IsNullOrWhiteSpace(p))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToList();

                                    if (candidates.Count > 0)
                                    {
                                        suggestionNote = $" Did you mean: {string.Join(", ", candidates)}?";
                                    }
                                }
                                catch
                                {
                                    // Best-effort suggestions only
                                }
                            }

                            var msg = $"Block not found.{suggestionNote}".Trim();
                            throw new McpException(msg);
                        }

                    case TiaMcpServer.Siemens.PortalErrorCode.ExportFailed:
                        {
                            // Relay underlying portal error with concise reason; log full details
                            var reason = pex.InnerException?.Message?.Trim();
                            var msg = "Failed to export block.";
                            if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";

                            Logger?.LogError(pex, "MCP ExportXmlBlock failed for {SoftwarePath} {BlockPath} -> {ExportPath}",
                                pex.Data?["softwarePath"], pex.Data?["blockPath"], pex.Data?["exportPath"]);

                            throw new McpException(msg);
                        }

                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidParams:
                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidState:
                        {
                            throw new McpException(pex.Message);
                        }
                }

                // Fallback
                throw new McpException(pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting block from '{blockPath}' to '{exportPath}': {ex.Message}", ex);
            }
        }

        private static string BuildBlockPathSuggestion(string softwarePath, string blockPath)
        {
            if (string.IsNullOrEmpty(blockPath) || blockPath.Contains('/')) return string.Empty;
            try
            {
                var escaped = Regex.Escape(blockPath);
                var blocks = Portal.GetBlocks(softwarePath, $"^{escaped}$");
                if (blocks == null || blocks.Count == 0)
                {
                    blocks = Portal.GetBlocks(softwarePath, escaped);
                }

                var candidates = blocks
                    .Take(10)
                    .Select(b =>
                    {
                        var name = b.Name;
                        var parts = new List<string> { name };
                        var parent = b.Parent;
                        while (parent != null)
                        {
                            if (parent is PlcBlockSystemGroup) break;
                            if (parent is PlcBlockGroup grp)
                            {
                                parts.Insert(0, grp.Name);
                                parent = grp.Parent;
                            }
                            else break;
                        }
                        if (parts.Count > 1) parts.RemoveAt(0);
                        return string.Join("/", parts);
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return candidates.Count > 0 ? $" Did you mean: {string.Join(", ", candidates)}?" : string.Empty;
            }
            catch
            {
                return string.Empty; // best effort only
            }
        }

        [WriteTool]
        [McpServerTool(Name = "ImportXmlBlock", Title = "Import block from XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Use to bring in a whole block from XML; to change one network prefer 'EditBlockNetwork'. Import one program block from an XML file into a block group. An existing block of the same name is replaced; its previous XML is saved first and returned as 'backup'. Refused when that block was changed after the file was written (e.g. an older copy of the file), unless 'allowOlderFile'")]
        public static ResponseImportXmlBlock ImportXmlBlock(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: block group that receives the block, relative to 'Program blocks' and '/'-separated, e.g. '009_Servo/zGDBs'; empty string for the Program blocks root")] string groupPath,
            [Description("importPath: absolute path of the .xml file on the PC running this server, e.g. one written by 'ExportXmlBlock'")] string importPath,
            [Description("allowOlderFile: import even when the block or type it replaces was changed in TIA Portal after the file was last written (default false: refused, because that usually means an older copy of the file would undo a change)")] bool allowOlderFile = false)
        {
            WritePolicy.EnsureEnabled(nameof(ImportXmlBlock));

            try
            {
                var backup = Portal.BackupBlockReplacedByImport(softwarePath, importPath, nameof(ImportXmlBlock), allowOlderFile);

                if (Portal.ImportXmlBlock(softwarePath, groupPath, importPath))
                {
                    return new ResponseImportXmlBlock
                    {
                        Backup = backup.Path,
                        BackupWarning = backup.Warning,
                        Message = $"Block imported from '{importPath}' to '{groupPath}'" + BackupNote(backup),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed importing block from '{importPath}' to '{groupPath}'");
                }
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing block from '{importPath}' to '{groupPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportXmlBlocks", Title = "Export blocks as XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export the program blocks of a PLC software as XML files, all of them or those whose name matches a regular expression. With preservePath the group folders are recreated below exportPath")]
        public static async Task<ResponseExportXmlBlocks> ExportXmlBlocks(
            IProgress<ProgressNotificationValue> progress,
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("exportPath: absolute directory on the PC running this server, e.g. 'C:\\Temp\\export'; relative paths are rejected. Files are named after the blocks")] string exportPath,
            [Description("regexName: defines the name or regular expression to find the block, matched anywhere in the block name ('G3U' also matches G3U10..G3U12; anchor with ^...$ for an exact name). Use empty string (default) to find all")] string regexName = "",
            [Description("preservePath: preserves the path/structure of the PLC software")] bool preservePath = false,
            [Description("includeAttributes: return every Openness attribute of each exported object (about 30 each). Default true. Pass false for a large export - the files are written either way, and the attribute lists can exceed the reply size")] bool includeAttributes = true)
        {
            var startTime = DateTime.Now;
            
            try
            {
                // First, get the list of blocks to determine total count
                Logger?.LogInformation($"Starting export of blocks from '{softwarePath}' to '{exportPath}'");
                
                var allBlocks = await Task.Run(() => Portal.GetBlocks(softwarePath, regexName));
                var totalBlocks = allBlocks?.Count ?? 0;

                if (totalBlocks == 0)
                {
                    progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = "No blocks found to export" });
                    
                    return new ResponseExportXmlBlocks
                    {
                        Message = $"No blocks found with regex '{regexName}' in '{softwarePath}'",
                        Items = new List<ResponseBlockInfo>(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalBlocks"] = 0,
                            ["exportedBlocks"] = 0,
                            ["duration"] = (DateTime.Now - startTime).TotalSeconds
                        }
                    };
                }

                // Send initial progress notification
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = totalBlocks, Message = $"Starting export of {totalBlocks} blocks..." });

                // Export blocks asynchronously
                var (exportedBlocks, failures) = await Task.Run(() => Portal.ExportXmlBlocks(softwarePath, exportPath, regexName, preservePath));

                // Build list of inconsistent (skipped) blocks for reporting
                var inconsistentInfos = new List<ResponseBlockInfo>();
                if (allBlocks != null)
                {
                    foreach (var b in allBlocks)
                    {
                        if (b != null && b.IsConsistent == false)
                        {
                            var attrs = includeAttributes ? Helper.GetAttributeList(b) : null;
                            inconsistentInfos.Add(new ResponseBlockInfo
                            {
                                Path = Portal.GetBlockPath(b),
                                Name = b.Name,
                                TypeName = b.GetType().Name,
                                Namespace = b.Namespace,
                                ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), b.ProgrammingLanguage),
                                MemoryLayout = Enum.GetName(typeof(MemoryLayout), b.MemoryLayout),
                                IsConsistent = b.IsConsistent,
                                HeaderName = b.HeaderName,
                                ModifiedDate = b.ModifiedDate,
                                IsKnowHowProtected = b.IsKnowHowProtected,
                                Attributes = attrs,
                                Description = b.ToString()
                            });
                        }
                    }
                }

                // Send progress update after export completion
                var exportedCount = exportedBlocks.Count;
                progress.Report(new ProgressNotificationValue { Progress = exportedCount, Total = totalBlocks, Message = $"Exported {exportedCount} of {totalBlocks} blocks" });

                var responseList = new List<ResponseBlockInfo>();

                foreach (var block in exportedBlocks)
                {
                    if (block != null)
                    {
                        var attributes = includeAttributes ? Helper.GetAttributeList(block) : null;

                        responseList.Add(new ResponseBlockInfo
                        {
                            Path = Portal.GetBlockPath(block),
                            Name = block.Name,
                            TypeName = block.GetType().Name,
                            Namespace = block.Namespace,
                            ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                            MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                            IsConsistent = block.IsConsistent,
                            HeaderName = block.HeaderName,
                            ModifiedDate = block.ModifiedDate,
                            IsKnowHowProtected = block.IsKnowHowProtected,
                            Attributes = attributes,
                            Description = block.ToString()
                        });
                    }
                }

                // Send final progress notification
                progress.Report(new ProgressNotificationValue { Progress = exportedCount, Total = totalBlocks, Message = $"Export completed: {exportedCount} blocks exported successfully" });

                var duration = (DateTime.Now - startTime).TotalSeconds;
                Logger?.LogInformation($"Export completed: {exportedCount} blocks exported in {duration:F2} seconds");

                var success = failures.Count == 0;
                return new ResponseExportXmlBlocks
                {
                    Message = success
                        ? $"Export completed: {exportedCount} blocks with regex '{regexName}' exported from '{softwarePath}' to '{exportPath}'"
                        : $"Export completed with {failures.Count} failure(s): {exportedCount} of {totalBlocks} blocks exported from '{softwarePath}' to '{exportPath}'. First failure: {failures[0]}",
                    Items = responseList,
                    Inconsistent = inconsistentInfos,
                    Failures = success ? null : failures,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = success,
                        ["totalBlocks"] = totalBlocks,
                        ["exportedBlocks"] = exportedCount,
                        ["inconsistentBlocks"] = inconsistentInfos.Count,
                        ["failureCount"] = failures.Count,
                        ["duration"] = duration
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                // Send error progress notification if we have a progress token
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = $"Export failed: {ex.Message}" });
                
                Logger?.LogError(ex, $"Failed exporting blocks with '{regexName}' from '{softwarePath}' to {exportPath}");
                throw new McpException($"Unexpected error exporting blocks with '{regexName}' from '{softwarePath}' to {exportPath}: {ex.Message}", ex);
            }
        }

        #endregion

        #region source

        [McpServerTool(Name = "GetBlockInterface", Title = "Get block interface", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the members of a data block with their data type and every attribute TIA Portal reports. Needs no export and works on inconsistent blocks. Data blocks only: Openness offers no interface accessor for FB, FC or OB, whose declarations come from 'GetBlockSource' instead")]
        public static ResponseBlockInterface GetBlockInterface(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the data block, e.g. '1_Tests/DB_Block_1'")] string blockPath)
        {
            try
            {
                var members = Portal.GetBlockInterface(softwarePath, blockPath);

                return new ResponseBlockInterface
                {
                    Message = $"'{blockPath}' has {members.Count} member(s)",
                    Path = blockPath,
                    Items = members.Select(m => new ResponseInterfaceMember
                    {
                        Name = m.Name,
                        DataTypeName = m.DataTypeName,
                        Attributes = m.Attributes
                    }).ToList(),
                    Meta = Ok(new JsonObject { ["memberCount"] = members.Count })
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the interface of '{blockPath}': {ex.Message}", ex);
            }
        }

        #endregion

        // From the former McpServerWrite.Blocks.cs:
        // Write tools for program blocks, PLC data types and their groups.
        //
        // Callers: registered by Program.BuildTools() under '--allow-write'; also invoked
        // directly by the write test class. Affected API: additive - a partial of McpServer; the tools are marked [WriteTool].
        // No file I/O; changes are in memory until SaveProject / SaveSession.
        //
        // Openness offers no generic "create block" and no move/copy, so the creation surface here
        // is CreateFB and CreateInstanceDB only - anything else arrives through ImportXmlBlock.

        // From the former McpServerWrite.MoveCopy.cs:
        // Write tools that copy or move program blocks and PLC data types between groups.
        //
        // Callers: registered by Program.BuildTools() under '--allow-write'; also invoked
        // directly by the write test class. Affected API: additive - a partial of McpServer; the tools are marked [WriteTool].
        // File I/O: none of its own; Portal writes and removes a temporary XML per call.
        //
        // Openness exposes no move or copy operation for these objects, so each tool is composed
        // from export, import and (for a move) deleting the source. The tool descriptions say so,
        // because the composition is observable: the object must be consistent, and its block
        // number travels with it, so importing into the same PLC can collide.

        #region groups (write)

        [WriteTool]
        [McpServerTool(Name = "CreateBlockGroup", Title = "Create block group", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Create a group below the Program blocks root of the PLC software")]
        public static ResponseCreated CreateBlockGroup(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("parentGroupPath: root-relative path of the parent group; empty creates directly below Program blocks")] string parentGroupPath,
            [Description("name: name of the new group, without a slash")] string name)
        {
            return Guarded(nameof(CreateBlockGroup), () =>
            {
                Portal.CreateBlockGroup(softwarePath, parentGroupPath, name);
                return Created("Block group", name, JoinPath(parentGroupPath, name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteBlockGroup", Title = "Delete block group", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a block group and everything inside it. The Program blocks system group itself cannot be deleted")]
        public static ResponseDeleted DeleteBlockGroup(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: root-relative path of the group to delete, e.g. Common/CarrierRegister")] string groupPath)
        {
            return Guarded(nameof(DeleteBlockGroup), () =>
            {
                Portal.DeleteBlockGroup(softwarePath, groupPath);
                return Deleted("Block group", groupPath);
            });
        }

        #endregion

        #region blocks and types (write)

        [WriteTool]
        [McpServerTool(Name = "EditBlockNetwork", Title = "Replace, insert or delete a network", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Use to change a block (add, replace or remove a network) instead of editing the whole block XML. Change one network of a program block without hand-editing the whole block XML: replace or insert a network taken from an XML file (e.g. another block's export) or written as SCL text ('sclFile'), or delete one. LAD/FBD parts are put into the flow order TIA requires and IDs are renumbered; the block is backed up first. Use 'previewFile' to only write the edited block XML. The block must be consistent (compile it first)")]
        public static ResponseNetworkEdit EditBlockNetwork(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the block, e.g. 009_Servo/FB_PitchHome_Inch")] string blockPath,
            [Description("action: 'replace', 'insert' (before networkIndex; the network count appends at the end) or 'delete'")] string action,
            [Description("networkIndex: 0-based network number in the block (network 1 in TIA is 0), as 'GetNetworkSource' numbers them")] int networkIndex,
            [Description("networkFile: for replace/insert, absolute path of an XML file holding the network (<SW.Blocks.CompileUnit>), e.g. a block export")] string networkFile = "",
            [Description("sourceNetworkIndex: which network of networkFile to take, 0-based (default 0)")] int sourceNetworkIndex = 0,
            [Description("previewFile: optional absolute path; the edited block XML is written there and nothing is imported")] string previewFile = "",
            [Description("sclFile: for replace/insert, instead of networkFile: absolute path of a text file with the SCL statements of the new network ('#' names refer to this block's interface). TIA compiles it in a scratch FB that is deleted again, also in preview")] string sclFile = "",
            [Description("title: optional network title for replace/insert, e.g. 'Vision : 2D Result Check'. Empty (default) keeps the replaced network's title and comment when the new network has none (an SCL network never has)")] string title = "")
        {
            WritePolicy.EnsureEnabled(nameof(EditBlockNetwork));

            try
            {
                if (!string.IsNullOrWhiteSpace(networkFile))
                {
                    Operation.RequireAbsolutePath(networkFile, nameof(networkFile));
                }

                if (!string.IsNullOrWhiteSpace(previewFile))
                {
                    Operation.RequireAbsolutePath(previewFile, nameof(previewFile));
                }

                if (!string.IsNullOrWhiteSpace(sclFile))
                {
                    Operation.RequireAbsolutePath(sclFile, nameof(sclFile));
                }

                var result = Portal.EditBlockNetwork(softwarePath, blockPath, action, networkIndex, networkFile, sourceNetworkIndex, previewFile, sclFile, title);
                var verb = action.Trim().ToLowerInvariant() switch
                {
                    "delete" => "deleted",
                    "insert" => "inserted",
                    _ => "replaced"
                };

                var message = result.PreviewFile != null
                    ? $"Preview: network {networkIndex} {verb} in '{result.BlockPath}' ({result.NetworksBefore} -> {result.NetworksAfter} networks), written to '{result.PreviewFile}'. Nothing was imported."
                    : $"Network {networkIndex} {verb} in '{result.BlockPath}' ({result.NetworksBefore} -> {result.NetworksAfter} networks). Compile the block to check it ('CompileCodeBlock'). {SaveHint}";

                if (result.PartsReordered > 0)
                {
                    message += $" {result.PartsReordered} LAD/FBD part(s) were moved into flow order.";
                }

                if (result.SclCompiled)
                {
                    message += " The SCL text compiled in a scratch FB, which was deleted again.";
                }

                if (result.TitleSet)
                {
                    message += $" Network title set to '{title}'.";
                }
                else if (result.TextsKept.Count > 0)
                {
                    message += $" Kept the replaced network's {string.Join(" and ", result.TextsKept.Select(t => t.ToLowerInvariant()))}.";
                }

                var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

                return new ResponseNetworkEdit
                {
                    BlockPath = result.BlockPath,
                    Action = verb,
                    NetworkIndex = networkIndex,
                    NetworksBefore = result.NetworksBefore,
                    NetworksAfter = result.NetworksAfter,
                    PartsReordered = result.PartsReordered,
                    Backup = result.Backup,
                    BackupWarning = result.BackupWarning,
                    PreviewFile = result.PreviewFile,
                    Message = message + BackupNote(backup),
                    Meta = result.PreviewFile != null
                        ? new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                        : OkMeta()
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error editing a network of '{blockPath}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "EditBlockInterface", Title = "Add, change or delete a block interface member", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Use to add, change or delete one variable in the interface of an FB, FC, OB or global DB (Input, Output, InOut, Static, Temp, Constant) instead of editing the whole block XML, e.g. a new Static Bool before writing a network that uses it. The block is exported, the one member changed, backed up and imported again. Instance DBs follow their FB: change the FB and compile it. Use 'previewFile' to only write the edited block XML. The block must be consistent (compile it first); compile it afterwards ('CompileCodeBlock')")]
        public static ResponseInterfaceEdit EditBlockInterface(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the block, e.g. 009_Servo/FB_PitchHome_Inch")] string blockPath,
            [Description("action: 'add', 'update' (change data type, start value or comment) or 'delete'")] string action,
            [Description("section: Input, Output, InOut, Static, Temp or Constant (Return: an FC's return type, update only), e.g. 'Static'")] string section,
            [Description("memberPath: the member inside the section, '.'-separated into Structs, e.g. 'bNew' or 'aux.bNew'; quote a name that contains dots, e.g. 'S.\"command.Busy\"'")] string memberPath,
            [Description("dataType: required for add, optional for update, e.g. 'Bool', 'Int', 'Array[1..10] of Int', '\"UDT_Axis\"', 'Struct', 'TON_TIME'")] string dataType = "",
            [Description("startValue: optional start value (constant value for Constant), e.g. 'false', '10', '1.5'")] string startValue = "",
            [Description("comment: optional member comment, e.g. 'Homing done'")] string comment = "",
            [Description("afterMember: add only, optional name of the member to insert after (same level), e.g. 'Result_Data_Quantity'; empty appends at the end")] string afterMember = "",
            [Description("previewFile: optional absolute path; the edited block XML is written there and nothing is imported")] string previewFile = "")
        {
            WritePolicy.EnsureEnabled(nameof(EditBlockInterface));

            try
            {
                if (!string.IsNullOrWhiteSpace(previewFile))
                {
                    Operation.RequireAbsolutePath(previewFile, nameof(previewFile));
                }

                var result = Portal.EditBlockInterface(softwarePath, blockPath, action, section, memberPath, dataType, startValue, comment, afterMember, previewFile);
                return InterfaceEdited(result, action, "block", "Compile the block ('CompileCodeBlock'); callers of an FB/FC and code using a deleted member may need it too.");
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error editing the interface of '{blockPath}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "EditTypeInterface", Title = "Add, change or delete a UDT member", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Use to add, change or delete one member of a PLC data type (UDT) instead of editing the whole type XML, e.g. a new field in a settings struct. The type is exported, the one member changed, backed up and imported again. Blocks and DBs that use the type become inconsistent until compiled ('CompileSoftware'). Use 'previewFile' to only write the edited type XML")]
        public static ResponseInterfaceEdit EditTypeInterface(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("typePath: root-relative path of the type in 'PLC data types', e.g. 'Servo/UDT_Origin'")] string typePath,
            [Description("action: 'add', 'update' (change data type, start value or comment) or 'delete'")] string action,
            [Description("memberPath: the member, '.'-separated into Structs, e.g. 'OriginOffset' or 'Speed.Creep'; quote a name that contains dots, e.g. '\"command.Busy\"'")] string memberPath,
            [Description("dataType: required for add, optional for update, e.g. 'Real', 'Bool', 'Array[1..10] of Int', '\"UDT_Axis\"', 'Struct'")] string dataType = "",
            [Description("startValue: optional default value, e.g. '2.0', 'false'")] string startValue = "",
            [Description("comment: optional member comment, e.g. 'Offset after homing [mm]'")] string comment = "",
            [Description("afterMember: add only, optional name of the member to insert after (same level), e.g. 'OriginOffset'; empty appends at the end")] string afterMember = "",
            [Description("previewFile: optional absolute path; the edited type XML is written there and nothing is imported")] string previewFile = "")
        {
            WritePolicy.EnsureEnabled(nameof(EditTypeInterface));

            try
            {
                if (!string.IsNullOrWhiteSpace(previewFile))
                {
                    Operation.RequireAbsolutePath(previewFile, nameof(previewFile));
                }

                var result = Portal.EditTypeInterface(softwarePath, typePath, action, memberPath, dataType, startValue, comment, afterMember, previewFile);
                return InterfaceEdited(result, action, "type", "Compile the PLC software ('CompileSoftware') so the blocks and DBs using the type are updated.");
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error editing type '{typePath}': {ex.Message}", ex);
            }
        }

        private static ResponseInterfaceEdit InterfaceEdited(Portal.InterfaceEditResult result, string action, string what, string compileHint)
        {
            var verb = action.Trim().ToLowerInvariant() switch
            {
                "add" => "added",
                "delete" => "deleted",
                _ => "updated"
            };

            var change = result.Before != null && result.After != null
                ? $"'{result.Before}' -> '{result.After}'"
                : $"'{result.After ?? result.Before}'";

            var section = result.Section == "None" ? "Member" : $"{result.Section} member";

            var message = result.PreviewFile != null
                ? $"Preview: {section} {change} {verb} in {what} '{result.BlockPath}', written to '{result.PreviewFile}'. Nothing was imported."
                : $"{section} {change} {verb} in {what} '{result.BlockPath}'. {compileHint} {SaveHint}";

            var backup = new Portal.BackupResult { Path = result.Backup, Warning = result.BackupWarning };

            return new ResponseInterfaceEdit
            {
                BlockPath = result.BlockPath,
                Action = verb,
                Section = result.Section,
                MemberPath = result.MemberPath,
                Before = result.Before,
                After = result.After,
                Backup = result.Backup,
                BackupWarning = result.BackupWarning,
                PreviewFile = result.PreviewFile,
                Message = message + BackupNote(backup),
                Meta = result.PreviewFile != null
                    ? new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    : OkMeta()
            };
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteBlock", Title = "Delete block", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a program block. Its XML is saved first and returned as 'backup', for re-import with 'ImportXmlBlock'. Know-how protected blocks are rejected: remove the protection in TIA Portal first")]
        public static ResponseDeleted DeleteBlock(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the block, e.g. 1_Tests/FC_Block_1")] string blockPath)
        {
            return Guarded(nameof(DeleteBlock), () =>
            {
                var backup = Portal.BackupBlock(softwarePath, blockPath, nameof(DeleteBlock));
                Portal.DeleteBlock(softwarePath, blockPath);
                return WithBackup(Deleted("Block", blockPath), backup);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "RenameBlock", Title = "Rename block", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a program block. Know-how protected blocks are rejected")]
        public static ResponseRenamed RenameBlock(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the block, e.g. 1_Tests/FC_Block_1")] string blockPath,
            [Description("newName: the new block name, without a slash")] string newName)
        {
            return Guarded(nameof(RenameBlock), () =>
            {
                Portal.RenameBlock(softwarePath, blockPath, newName);
                return Renamed("Block", blockPath, newName, ReplaceLeaf(blockPath, newName));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateFB", Title = "Create function block", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Create an empty function block. Openness has no generic create-block operation: FB and instance DB are the only kinds creatable without importing XML")]
        public static ResponseCreated CreateFB(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: root-relative block group that receives the FB; empty uses the Program blocks root")] string groupPath,
            [Description("name: name of the new function block, without a slash")] string name,
            [Description("language: programming language such as LAD (default), FBD, STL, SCL or GRAPH")] string language = "LAD",
            [Description("autoNumber: let TIA Portal assign the block number (default true)")] bool autoNumber = true,
            [Description("number: explicit block number, used only when autoNumber is false")] int number = 0)
        {
            return Guarded(nameof(CreateFB), () =>
            {
                var block = Portal.CreateFB(softwarePath, groupPath, name, autoNumber, number, language);
                return Created("FB", block.Name, JoinPath(groupPath, block.Name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "CreateInstanceDB", Title = "Create instance data block", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Create an instance data block for an existing function block")]
        public static ResponseCreated CreateInstanceDB(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: root-relative block group that receives the DB; empty uses the Program blocks root")] string groupPath,
            [Description("name: name of the new instance data block, without a slash")] string name,
            [Description("instanceOfName: name of the function block this instance DB belongs to")] string instanceOfName,
            [Description("autoNumber: let TIA Portal assign the block number (default true)")] bool autoNumber = true,
            [Description("number: explicit block number, used only when autoNumber is false")] int number = 0)
        {
            return Guarded(nameof(CreateInstanceDB), () =>
            {
                var block = Portal.CreateInstanceDB(softwarePath, groupPath, name, instanceOfName, autoNumber, number);
                return Created("InstanceDB", block.Name, JoinPath(groupPath, block.Name));
            });
        }

        #endregion

        #region move copy (write)

        [WriteTool]
        [McpServerTool(Name = "CopyBlock", Title = "Copy block", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Copy a program block into another block group of the same PLC software. Implemented as export plus import because Openness has no copy operation, so the block must be consistent and keeps its block number")]
        public static ResponseCreated CopyBlock(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the block to copy, e.g. 1_Tests/FC_Block_1")] string blockPath,
            [Description("targetGroupPath: root-relative block group that receives the copy; empty means the Program blocks root")] string targetGroupPath,
            [Description("overwrite: replace a block of the same name already in the target group (default false)")] bool overwrite = false)
        {
            return Guarded(nameof(CopyBlock), () =>
            {
                var block = Portal.CopyBlock(softwarePath, blockPath, targetGroupPath, overwrite);
                var newPath = JoinPath(targetGroupPath, block.Name);

                return new ResponseCreated
                {
                    Kind = "Block",
                    Name = block.Name,
                    Path = newPath,
                    Message = $"Block '{blockPath}' copied to '{newPath}'. {SaveHint}",
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "MoveBlock", Title = "Move block", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Move a program block into another block group of the same PLC software. Implemented as export, import and deleting the original; the original is only removed after the import succeeds")]
        public static ResponseRenamed MoveBlock(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the block to move, e.g. 1_Tests/FC_Block_1")] string blockPath,
            [Description("targetGroupPath: root-relative block group that receives the block; empty means the Program blocks root")] string targetGroupPath,
            [Description("overwrite: replace a block of the same name already in the target group (default false)")] bool overwrite = false)
        {
            return Guarded(nameof(MoveBlock), () =>
            {
                var block = Portal.MoveBlock(softwarePath, blockPath, targetGroupPath, overwrite);
                var newPath = JoinPath(targetGroupPath, block.Name);

                return new ResponseRenamed
                {
                    Kind = "Block",
                    OldPath = blockPath,
                    NewName = block.Name,
                    NewPath = newPath,
                    Message = $"Block '{blockPath}' moved to '{newPath}'. {SaveHint}",
                    Meta = OkMeta()
                };
            });
        }

        #endregion
    }
}
