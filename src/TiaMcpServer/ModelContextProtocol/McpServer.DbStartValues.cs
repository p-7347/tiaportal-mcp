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
        // Start values of data block members without exporting the DB. See Portal.DbStartValues.cs.

        [McpServerTool(Name = "GetDbStartValues", Title = "Get DB start values", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the members of a data block (global or instance DB) with data type and start value, including members inside structs, UDTs and arrays, e.g. 'S[3].OriginOffset'. Needs no export. Filter with 'regexName' on the member path. Reads only; change one value with 'SetDbStartValue'")]
        public static ResponseDbStartValues GetDbStartValues(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the data block, e.g. 'Servo/SM_UserData_Origin'")] string blockPath,
            [Description("regexName: optional regular expression on the member path, e.g. 'OriginOffset'")] string regexName = "",
            [Description("maxMembers: maximum number of members listed (default 500)")] int maxMembers = 500)
        {
            try
            {
                var result = Portal.GetDbStartValues(softwarePath, blockPath, regexName, Math.Max(1, maxMembers), TimeSpan.FromSeconds(40));

                return new ResponseDbStartValues
                {
                    BlockPath = result.BlockPath,
                    Total = result.Total,
                    Members = result.Members.Select(m => new ResponseDbStartValue
                    {
                        Path = m.Path,
                        DataType = m.DataType,
                        StartValue = m.StartValue,
                        Error = m.Error
                    }).ToList(),
                    StoppedReason = result.StoppedReason,
                    Message = $"{result.Members.Count} member(s) of '{result.BlockPath}' listed ({result.Total} looked at)." +
                              (result.StoppedReason != null ? $" Incomplete: {result.StoppedReason}." : string.Empty),
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true,
                        ["complete"] = result.StoppedReason == null
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the start values of '{blockPath}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "SetDbStartValue", Title = "Set DB start value", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Change the start value of one data block member in the offline project, e.g. 'S[3].OriginOffset' to '2.0', without touching anything else in the DB. The DB is backed up first. The PLC uses it only after compile and a download that reinitializes the DB (actual values stay until then)")]
        public static ResponseDbStartValueSet SetDbStartValue(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: root-relative path of the data block")] string blockPath,
            [Description("memberPath: member path as listed by 'GetDbStartValues', e.g. 'S[3].OriginOffset'")] string memberPath,
            [Description("value: new start value in TIA notation, e.g. 2.0, TRUE or T#5s; a string value includes its single quotes")] string value)
        {
            return Guarded(nameof(SetDbStartValue), () =>
            {
                var (path, member, oldValue, newValue, backup) = Portal.SetDbStartValue(softwarePath, blockPath, memberPath, value);

                return new ResponseDbStartValueSet
                {
                    BlockPath = path,
                    MemberPath = member,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Backup = backup.Path,
                    BackupWarning = backup.Warning,
                    Message = $"'{path}' member '{member}' start value changed from {oldValue ?? "empty"} to {newValue ?? "empty"}. " +
                              $"Compile it ('CompileCodeBlock'; also before the next change, or that change cannot be backed up), then download with reinitialization for the PLC to use it. {SaveHint}" + BackupNote(backup),
                    Meta = OkMeta()
                };
            });
        }
    }
}
