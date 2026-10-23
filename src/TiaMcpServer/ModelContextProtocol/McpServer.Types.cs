using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region types

        [McpServerTool(Name = "GetTypeInfo", Title = "Get type info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show one PLC data type: path, namespace, consistency, know-how protection, modified date and attributes")]
        public static ResponseTypeInfo GetTypeInfo(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("typePath: defines the path in the project structure to the type")] string typePath)
        {
            try
            {
                var type = Portal.GetType(softwarePath, typePath);
                if (type is not null)
                {
                    var attributes = Helper.GetAttributeList(type);

                    return new ResponseTypeInfo
                    {
                        Path = Portal.GetTypePath(type),
                        Message = $"Type info retrieved from '{typePath}' in '{softwarePath}'",
                        Name = type.Name,
                        TypeName = type.GetType().Name,
                        Namespace = type.Namespace,
                        IsConsistent = type.IsConsistent,
                        ModifiedDate = type.ModifiedDate,
                        IsKnowHowProtected = type.IsKnowHowProtected,
                        Attributes = attributes,
                        Description = type.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Type not found at '{typePath}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving type info from '{typePath}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetTypes", Title = "Get types", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("List the PLC data types of a PLC software, optionally filtered by a name or regular expression")]
        public static ResponseTypes GetTypes(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("regexName: defines the name or regular expression to find the type, matched anywhere in the type name; anchor with ^...$ for an exact name. Use empty string (default) to find all")] string regexName = "",
            [Description("includeAttributes: return every Openness attribute of each type (about 30 each). Default true. Pass false on a large PLC when only names and paths are needed - reading the attributes is most of this tool's run time")] bool includeAttributes = true)
        {
            try
            {
                var list = Portal.GetTypes(softwarePath, regexName);

                var responseList = new List<ResponseTypeInfo>();
                foreach (var type in list)
                {
                    if (type != null)
                    {
                        var attributes = includeAttributes ? Helper.GetAttributeList(type) : null;

                        responseList.Add(new ResponseTypeInfo
                        {
                            Path = Portal.GetTypePath(type),
                            Name = type.Name,
                            TypeName = type.GetType().Name,
                            Namespace = type.Namespace,
                            IsConsistent = type.IsConsistent,
                            ModifiedDate = type.ModifiedDate,
                            IsKnowHowProtected = type.IsKnowHowProtected,
                            Attributes = attributes,
                            Description = type.ToString()
                        });
                    }
                }

                if (list != null)
                {
                    return new ResponseTypes
                    {
                        Message = $"Types with regex '{regexName}' retrieved from '{softwarePath}'",
                        Items = responseList,
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed retrieving user defined types with regex '{regexName}' in '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving user defined types with regex '{regexName}' in '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportXmlType", Title = "Export type as XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export one PLC data type as an XML file named after the type into exportPath, below its group folders when preservePath is true. Inconsistent types are rejected")]
        public static ResponseExportXmlType ExportXmlType(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("exportPath: absolute directory on the PC running this server; relative paths are rejected. The file is named after the type")] string exportPath,
            [Description("typePath: defines the path in the project structure to the type")] string typePath,
            [Description("preservePath: preserves the path/structure of the PLC software")] bool preservePath = false)
        {
            try
            {
                var type = Portal.ExportXmlType(softwarePath, typePath, exportPath, preservePath);
                if (type is not null)
                {
                    return new ResponseExportXmlType
                    {
                        Message = $"Type exported from '{typePath}' to '{exportPath}'",
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed exporting type from '{typePath}' to '{exportPath}'");
                }
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                switch (pex.Code)
                {
                    case TiaMcpServer.Siemens.PortalErrorCode.NotFound:
                        throw new McpException("Type not found.");
                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidState:
                    case TiaMcpServer.Siemens.PortalErrorCode.InvalidParams:
                        throw new McpException(pex.Message);
                    case TiaMcpServer.Siemens.PortalErrorCode.ExportFailed:
                        {
                            var reason = pex.InnerException?.Message?.Trim();
                            var msg = "Failed to export type.";
                            if (!string.IsNullOrEmpty(reason)) msg += $" Reason: {reason}";
                            Logger?.LogError(pex, "MCP ExportXmlType failed for {SoftwarePath} {TypePath} -> {ExportPath}",
                                pex.Data?["softwarePath"], pex.Data?["typePath"], pex.Data?["exportPath"]);
                            throw new McpException(msg);
                        }
                }
                throw new McpException(pex.Message);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting type from '{typePath}' to '{exportPath}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "ImportXmlType", Title = "Import type from XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Import one PLC data type from an XML file into a type group. An existing type of the same name is replaced; its previous XML is saved first and returned as 'backup'. Refused when that type was changed after the file was written, unless 'allowOlderFile'")]
        public static ResponseImportXmlType ImportXmlType(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: type group that receives the type, relative to 'PLC data types' and '/'-separated; empty string for the PLC data types root")] string groupPath,
            [Description("importPath: absolute path of the .xml file on the PC running this server, e.g. one written by 'ExportXmlType'")] string importPath,
            [Description("allowOlderFile: import even when the block or type it replaces was changed in TIA Portal after the file was last written (default false: refused, because that usually means an older copy of the file would undo a change)")] bool allowOlderFile = false)
        {
            WritePolicy.EnsureEnabled(nameof(ImportXmlType));

            try
            {
                var backup = Portal.BackupTypeReplacedByImport(softwarePath, importPath, nameof(ImportXmlType), allowOlderFile);

                if (Portal.ImportXmlType(softwarePath, groupPath, importPath))
                {
                    return new ResponseImportXmlType
                    {
                        Backup = backup.Path,
                        BackupWarning = backup.Warning,
                        Message = $"Type imported from '{importPath}' to '{groupPath}'" + BackupNote(backup),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Failed importing type from '{importPath}' to '{groupPath}'");
                }
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error importing type from '{importPath}' to '{groupPath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportXmlTypes", Title = "Export types as XML", Destructive = true, Idempotent = true, OpenWorld = false), Description("Export the PLC data types of a PLC software as XML files, all of them or those whose name matches a regular expression. With preservePath the group folders are recreated below exportPath")]
        public static async Task<ResponseExportXmlTypes> ExportXmlTypes(
            IProgress<ProgressNotificationValue> progress,
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("exportPath: absolute directory on the PC running this server; relative paths are rejected. Files are named after the types")] string exportPath,
            [Description("regexName: defines the name or regular expression to find the type, matched anywhere in the type name; anchor with ^...$ for an exact name. Use empty string (default) to find all")] string regexName = "",
            [Description("preservePath: preserves the path/structure of the PLC software")] bool preservePath = false,
            [Description("includeAttributes: return every Openness attribute of each exported object (about 30 each). Default true. Pass false for a large export - the files are written either way, and the attribute lists can exceed the reply size")] bool includeAttributes = true)
        {
            var startTime = DateTime.Now;
            
            try
            {
                // First, get the list of types to determine total count
                Logger?.LogInformation($"Starting export of types from '{softwarePath}' to '{exportPath}'");
                
                var allTypes = await Task.Run(() => Portal.GetTypes(softwarePath, regexName));
                var totalTypes = allTypes?.Count ?? 0;

                if (totalTypes == 0)
                {
                    progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = "No types found to export" });
                    
                    return new ResponseExportXmlTypes
                    {
                        Message = $"No types found with regex '{regexName}' in '{softwarePath}'",
                        Items = new List<ResponseTypeInfo>(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true,
                            ["totalTypes"] = 0,
                            ["exportedTypes"] = 0,
                            ["duration"] = (DateTime.Now - startTime).TotalSeconds
                        }
                    };
                }

                // Send initial progress notification
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = totalTypes, Message = $"Starting export of {totalTypes} types..." });

                // Export types asynchronously
                var (exportedTypes, failures) = await Task.Run(() => Portal.ExportXmlTypes(softwarePath, exportPath, regexName, preservePath));

                // Build list of inconsistent (skipped) types for reporting
                var inconsistentTypeInfos = new List<ResponseTypeInfo>();
                if (allTypes != null)
                {
                    foreach (var t in allTypes)
                    {
                        if (t != null && t.IsConsistent == false)
                        {
                            var attrs = includeAttributes ? Helper.GetAttributeList(t) : null;
                            inconsistentTypeInfos.Add(new ResponseTypeInfo
                            {
                                Path = Portal.GetTypePath(t),
                                Name = t.Name,
                                TypeName = t.GetType().Name,
                                Namespace = t.Namespace,
                                IsConsistent = t.IsConsistent,
                                ModifiedDate = t.ModifiedDate,
                                IsKnowHowProtected = t.IsKnowHowProtected,
                                Attributes = attrs,
                                Description = t.ToString()
                            });
                        }
                    }
                }

                // Send progress update after export completion
                var exportedCount = exportedTypes.Count;
                progress.Report(new ProgressNotificationValue { Progress = exportedCount, Total = totalTypes, Message = $"Exported {exportedCount} of {totalTypes} types" });

                var responseList = new List<ResponseTypeInfo>();

                foreach (var type in exportedTypes)
                {
                    if (type != null)
                    {
                        var attributes = includeAttributes ? Helper.GetAttributeList(type) : null;

                        responseList.Add(new ResponseTypeInfo
                        {
                            Path = Portal.GetTypePath(type),
                            Name = type.Name,
                            TypeName = type.GetType().Name,
                            Namespace = type.Namespace,
                            IsConsistent = type.IsConsistent,
                            ModifiedDate = type.ModifiedDate,
                            IsKnowHowProtected = type.IsKnowHowProtected,
                            Attributes = attributes,
                            Description = type.ToString()
                        });
                    }
                }

                // Send final progress notification
                progress.Report(new ProgressNotificationValue { Progress = exportedCount, Total = totalTypes, Message = $"Export completed: {exportedCount} types exported successfully" });

                var duration = (DateTime.Now - startTime).TotalSeconds;
                Logger?.LogInformation($"Type export completed: {exportedCount} types exported in {duration:F2} seconds");

                var success = failures.Count == 0;
                return new ResponseExportXmlTypes
                {
                    Message = success
                        ? $"Export completed: {exportedCount} types with regex '{regexName}' exported from '{softwarePath}' to '{exportPath}'"
                        : $"Export completed with {failures.Count} failure(s): {exportedCount} of {totalTypes} types exported from '{softwarePath}' to '{exportPath}'. First failure: {failures[0]}",
                    Items = responseList,
                    Inconsistent = inconsistentTypeInfos,
                    Failures = success ? null : failures,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = success,
                        ["totalTypes"] = totalTypes,
                        ["exportedTypes"] = exportedCount,
                        ["inconsistentTypes"] = inconsistentTypeInfos.Count,
                        ["failureCount"] = failures.Count,
                        ["duration"] = duration
                    }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                // Send error progress notification if we have a progress token
                progress.Report(new ProgressNotificationValue { Progress = 0, Total = 0, Message = $"Type export failed: {ex.Message}" });
                
                Logger?.LogError(ex, $"Failed exporting types '{regexName}' from '{softwarePath}' to {exportPath}");
                throw new McpException($"Unexpected error exporting types '{regexName}' from '{softwarePath}' to {exportPath}: {ex.Message}", ex);
            }
        }

        #endregion

        // From the former McpServerWrite.Types.cs:

        #region groups (write)

        [WriteTool]
        [McpServerTool(Name = "CreateTypeGroup", Title = "Create type group", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Create a group below the PLC data types root of the PLC software")]
        public static ResponseCreated CreateTypeGroup(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("parentGroupPath: root-relative path of the parent group; empty creates directly below PLC data types")] string parentGroupPath,
            [Description("name: name of the new group, without a slash")] string name)
        {
            return Guarded(nameof(CreateTypeGroup), () =>
            {
                Portal.CreateTypeGroup(softwarePath, parentGroupPath, name);
                return Created("Type group", name, JoinPath(parentGroupPath, name));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteTypeGroup", Title = "Delete type group", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a PLC data type group and everything inside it. The PLC data types system group itself cannot be deleted")]
        public static ResponseDeleted DeleteTypeGroup(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: root-relative path of the group to delete")] string groupPath)
        {
            return Guarded(nameof(DeleteTypeGroup), () =>
            {
                Portal.DeleteTypeGroup(softwarePath, groupPath);
                return Deleted("Type group", groupPath);
            });
        }

        #endregion

        #region blocks and types (write)

        [WriteTool]
        [McpServerTool(Name = "DeleteType", Title = "Delete type", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a PLC data type (UDT). Its XML is saved first and returned as 'backup', for re-import with 'ImportXmlType'. Know-how protected types are rejected")]
        public static ResponseDeleted DeleteType(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("typePath: root-relative path of the type, e.g. Common/CarrierRegister/ML_SubstratState")] string typePath)
        {
            return Guarded(nameof(DeleteType), () =>
            {
                var backup = Portal.BackupType(softwarePath, typePath, nameof(DeleteType));
                Portal.DeleteType(softwarePath, typePath);
                return WithBackup(Deleted("Type", typePath), backup);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "RenameType", Title = "Rename type", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a PLC data type (UDT). Know-how protected types are rejected")]
        public static ResponseRenamed RenameType(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("typePath: root-relative path of the type")] string typePath,
            [Description("newName: the new type name, without a slash")] string newName)
        {
            return Guarded(nameof(RenameType), () =>
            {
                Portal.RenameType(softwarePath, typePath, newName);
                return Renamed("Type", typePath, newName, ReplaceLeaf(typePath, newName));
            });
        }

        #endregion

        #region move copy (write)

        [WriteTool]
        [McpServerTool(Name = "CopyType", Title = "Copy type", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Copy a PLC data type (UDT) into another type group of the same PLC software. Implemented as export plus import because Openness has no copy operation, so the type must be consistent")]
        public static ResponseCreated CopyType(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("typePath: root-relative path of the type to copy, e.g. Common/CarrierRegister/ML_SubstratState")] string typePath,
            [Description("targetGroupPath: root-relative type group that receives the copy; empty means the PLC data types root")] string targetGroupPath,
            [Description("overwrite: replace a type of the same name already in the target group (default false)")] bool overwrite = false)
        {
            return Guarded(nameof(CopyType), () =>
            {
                var type = Portal.CopyType(softwarePath, typePath, targetGroupPath, overwrite);
                var newPath = JoinPath(targetGroupPath, type.Name);

                return new ResponseCreated
                {
                    Kind = "Type",
                    Name = type.Name,
                    Path = newPath,
                    Message = $"Type '{typePath}' copied to '{newPath}'. {SaveHint}",
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "MoveType", Title = "Move type", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Move a PLC data type (UDT) into another type group of the same PLC software. Implemented as export, import and deleting the original; the original is only removed after the import succeeds")]
        public static ResponseRenamed MoveType(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("typePath: root-relative path of the type to move")] string typePath,
            [Description("targetGroupPath: root-relative type group that receives the type; empty means the PLC data types root")] string targetGroupPath,
            [Description("overwrite: replace a type of the same name already in the target group (default false)")] bool overwrite = false)
        {
            return Guarded(nameof(MoveType), () =>
            {
                var type = Portal.MoveType(softwarePath, typePath, targetGroupPath, overwrite);
                var newPath = JoinPath(targetGroupPath, type.Name);

                return new ResponseRenamed
                {
                    Kind = "Type",
                    OldPath = typePath,
                    NewName = type.Name,
                    NewPath = newPath,
                    Message = $"Type '{typePath}' moved to '{newPath}'. {SaveHint}",
                    Meta = OkMeta()
                };
            });
        }

        #endregion
    }
}
