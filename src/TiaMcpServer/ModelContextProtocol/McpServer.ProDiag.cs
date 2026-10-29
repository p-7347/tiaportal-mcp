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
        // ProDiag supervisions and their assignment to instance DBs. See Portal.ProDiag.cs.

        [McpServerTool(Name = "GetProDiagInfo", Title = "Get ProDiag info", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to see how ProDiag is set up in a PLC before configuring alarms or the WinCC PLC code view: lists the ProDiag FBs (version, settings, instance DBs), the FBs that define supervisions on their members (count and the attributes of the first ones), and the instance DBs assigned to a ProDiag FB. Walks the blocks of a group; reads only")]
        public static ResponseProDiagInfo GetProDiagInfo(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: optional root-relative block group to search, e.g. 'Libraries/06.Communication'; empty searches the whole program")] string groupPath = "",
            [Description("regexName: optional regular expression on the block name, e.g. 'ProDiag|G2U5'")] string regexName = "",
            [Description("maxSupervisions: attributes listed per supervised FB (default 5); 'count' is always the full number")] int maxSupervisions = 5)
        {
            try
            {
                var scan = Portal.GetProDiagInfo(softwarePath, groupPath, regexName, maxSupervisions, TimeSpan.FromSeconds(40));
                var complete = scan.StoppedReason == null;

                var message = $"{scan.ProDiagBlocks.Count} ProDiag FB(s), {scan.SupervisedBlocks.Count} FB(s) with supervisions and {scan.Assignments.Count} assigned instance DB(s) among {scan.Total} block(s)";
                if (!complete)
                {
                    message += $" - INCOMPLETE: {scan.StoppedReason}";
                }

                return new ResponseProDiagInfo
                {
                    Total = scan.Total,
                    ProDiagBlocks = scan.ProDiagBlocks.Select(b => new ResponseProDiagBlock
                    {
                        Path = b.Path,
                        Version = b.Version,
                        InitialValueAcquisition = b.InitialValueAcquisition,
                        UseCentralTimeStamp = b.UseCentralTimeStamp,
                        InstanceDbs = b.InstanceDbs,
                        Error = b.Error
                    }).ToList(),
                    SupervisedBlocks = scan.SupervisedBlocks.Select(b => new ResponseSupervisedBlock
                    {
                        Path = b.Path,
                        Count = b.Count,
                        Supervisions = b.Supervisions,
                        Error = b.Error
                    }).ToList(),
                    Assignments = scan.Assignments.Select(a => new ResponseProDiagAssignment { Path = a.Path, InstanceOf = a.InstanceOf, ProDiagFb = a.ProDiagFb }).ToList(),
                    StoppedReason = scan.StoppedReason,
                    Message = message + ".",
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
                throw new McpException($"Unexpected error reading ProDiag info of '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "ExportProDiagSupervisions", Title = "Export ProDiag supervisions", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to get ProDiag supervisions as an Excel file to review or edit them in bulk: exports the global supervisions of the PLC (TIA 'Global overview' of supervisions), or those of one ProDiag FB, with their settings to .xlsx. Edit the file and bring it back with 'ImportProDiagSupervisions'. Does not change the project")]
        public static ResponseProDiagSupervisionsXlsx ExportProDiagSupervisions(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("exportPath: absolute path of the .xlsx file to write, e.g. 'C:/Temp/Supervisions.xlsx'")] string exportPath,
            [Description("blockPath: optional root-relative path of a ProDiag FB as listed by 'GetProDiagInfo'; empty exports the global supervisions of the PLC")] string blockPath = "")
        {
            try
            {
                var outcome = Portal.ExportProDiagSupervisions(softwarePath, blockPath, exportPath);

                return new ResponseProDiagSupervisionsXlsx
                {
                    Scope = outcome.Scope,
                    File = outcome.File,
                    State = outcome.State,
                    LogFile = outcome.LogFile,
                    Message = outcome.Success
                        ? $"Exported the {outcome.Scope} to '{outcome.File}'."
                        : $"TIA Portal reported '{outcome.State}' exporting the {outcome.Scope}." + (outcome.LogFile != null ? $" Details in '{outcome.LogFile}'." : string.Empty),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = outcome.Success
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error exporting ProDiag supervisions of '{softwarePath}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "ImportProDiagSupervisions", Title = "Import ProDiag supervisions", Destructive = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to add or change ProDiag supervisions in bulk from an .xlsx file made by 'ExportProDiagSupervisions': imports into the global supervisions of the PLC or into one ProDiag FB. The current supervisions of that scope are exported to the backup folder first; a failed import is rolled back. Compile and download afterwards")]
        public static ResponseProDiagSupervisionsXlsx ImportProDiagSupervisions(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("importPath: absolute path of the .xlsx file, e.g. 'C:/Temp/Supervisions.xlsx'")] string importPath,
            [Description("blockPath: optional root-relative path of a ProDiag FB as listed by 'GetProDiagInfo'; empty imports into the global supervisions of the PLC")] string blockPath = "",
            [Description("settingsOnly: true imports only the supervision settings stored in the file, not the supervisions (default false)")] bool settingsOnly = false,
            [Description("overwrite: true replaces existing supervisions/settings of the same name (ImportOptions.Override); false keeps them (default false)")] bool overwrite = false)
        {
            return Guarded(nameof(ImportProDiagSupervisions), () =>
            {
                var (outcome, backup) = Portal.ImportProDiagSupervisions(softwarePath, blockPath, importPath, settingsOnly, overwrite);

                return new ResponseProDiagSupervisionsXlsx
                {
                    Scope = outcome.Scope,
                    File = outcome.File,
                    State = outcome.State,
                    LogFile = outcome.LogFile,
                    Backup = backup.Path,
                    BackupWarning = backup.Warning,
                    Message = $"Imported {(settingsOnly ? "supervision settings" : "supervisions")} from '{outcome.File}' into the {outcome.Scope}. Compile the PLC software, then download. {SaveHint}" + BackupNote(backup),
                    Meta = OkMeta()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "SetAssignedProDiagFB", Title = "Set assigned ProDiag FB", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to put an FB instance (or a UDT instance in a global DB) under ProDiag supervision: sets which ProDiag FB evaluates its supervisions (TIA attribute 'AssignedProDiagFB'). An empty proDiagFb removes the assignment. The DB is backed up first. Compile and download afterwards")]
        public static ResponseProDiagAssignmentSet SetAssignedProDiagFB(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the instance DB, or of the global DB when 'memberPath' is given")] string blockPath,
            [Description("proDiagFb: name of the ProDiag FB as listed by 'GetProDiagInfo', e.g. 'ProDiag_FB'; empty removes the assignment")] string proDiagFb,
            [Description("memberPath: optional UDT member of a global DB, e.g. 'Station[1].Clamp'; empty for an instance DB")] string memberPath = "")
        {
            return Guarded(nameof(SetAssignedProDiagFB), () =>
            {
                var (path, oldValue, newValue, backup) = Portal.SetAssignedProDiagFb(softwarePath, blockPath, memberPath, proDiagFb);
                var target = string.IsNullOrWhiteSpace(memberPath) ? $"'{path}'" : $"'{path}' member '{memberPath}'";

                return new ResponseProDiagAssignmentSet
                {
                    BlockPath = path,
                    MemberPath = string.IsNullOrWhiteSpace(memberPath) ? null : memberPath,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Backup = backup.Path,
                    BackupWarning = backup.Warning,
                    Message = $"{target} ProDiag FB changed from {(string.IsNullOrEmpty(oldValue) ? "none" : oldValue)} to {(string.IsNullOrEmpty(newValue) ? "none" : newValue)}. Compile the PLC software, then download. {SaveHint}" + BackupNote(backup),
                    Meta = OkMeta()
                };
            });
        }
    }
}
