using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.Compiler;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        #region plc software

        [McpServerTool(Name = "GetSoftwareInfo", Title = "Get software info", ReadOnly = true, OpenWorld = false, UseStructuredContent = true), Description("Show name, description and attributes of one PLC software")]
        public static ResponseSoftwareInfo GetSoftwareInfo(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath)
        {
            try
            {
                var software = Portal.GetPlcSoftware(softwarePath);
                if (software is not null)
                {

                    var attributes = Helper.GetAttributeList(software);

                    return new ResponseSoftwareInfo
                    {
                        Message = $"Software info retrieved from '{softwarePath}'",
                        Name = software.Name,
                        Attributes = attributes,
                        Description = software.ToString(),
                        Meta = new JsonObject
                        {
                            ["timestamp"] = DateTime.Now,
                            ["success"] = true
                        }
                    };
                }
                else
                {
                    throw new McpException($"Software not found at '{softwarePath}'");
                }
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving software info from '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CompileSoftware", Title = "Compile software", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Compile the PLC software and report every compiler message with the object it belongs to, so errors can be fixed without re-reading the whole PLC. Warnings are reported as a successful compile with detail; only errors fail the call")]
        public static ResponseCompileSoftware CompileSoftware(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("password: the password to access adminsitration, default: no password")] string password = "")
        {
            try
            {
                var result = Portal.CompileSoftware(softwarePath, password);

                if (result == null)
                {
                    throw new McpException(
                        $"Failed compiling software '{softwarePath}'. Check that the path names a PLC software " +
                        "('GetProjectTree' lists them) and, for a safety program, that 'password' is correct.");
                }

                return BuildCompileResponse(result, softwarePath);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error compiling software '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "CompileCodeBlock", Title = "Compile code block", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use after every block change to check it. Compile one code block (OB, FC, FB or DB) instead of the whole PLC, for example right after importing it, and report its compiler messages like 'CompileSoftware' does. Warnings are reported as a successful compile with detail; only errors fail the call")]
        public static ResponseCompileSoftware CompileCodeBlock(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("blockPath: path of the block below 'Program blocks', e.g. '009_Servo/FB_PitchHome_Inch'")] string blockPath,
            [Description("password: the password to access adminsitration, default: no password")] string password = "")
        {
            try
            {
                var result = Portal.CompileCodeBlock(softwarePath, blockPath, password);

                if (result == null)
                {
                    throw new McpException(
                        $"Failed compiling block '{blockPath}' in '{softwarePath}'. Check the block path ('GetBlocks' lists them) " +
                        "and, for a safety program, that 'password' is correct.");
                }

                return BuildCompileResponse(result, blockPath);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error compiling block '{blockPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// The structured compile report shared by CompileSoftware and CompileCodeBlock; target
        /// is the software or block path named in the message.
        /// </summary>
        private static ResponseCompileSoftware BuildCompileResponse(CompilerResult result, string target)
        {
            var messages = FlattenCompilerMessages(result.Messages, 0).ToList();
            var state = result.State.ToString();
            var failed = result.State == CompilerResultState.Error;

            var response = new ResponseCompileSoftware
            {
                Message = $"Compiling '{target}' finished with state '{state}': " +
                          $"{result.ErrorCount} error(s), {result.WarningCount} warning(s)",
                State = state,
                ErrorCount = result.ErrorCount,
                WarningCount = result.WarningCount,
                Messages = messages,
                Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = !failed,
                    ["state"] = state,
                    ["errorCount"] = result.ErrorCount,
                    ["warningCount"] = result.WarningCount,
                    ["messageCount"] = messages.Count
                }
            };

            if (failed)
            {
                // Live-tested: throwing here (as this used to) discards the fully-built
                // response - a caller debugging a real compile failure only ever saw up to 5
                // offender lines in the exception text, never the full Messages[]/ErrorCount
                // this method already has in hand. An error-fixing loop needs the actual
                // locations, so return the structured result (success=false in Meta) exactly
                // like the success path, instead of throwing away the detail into a truncated
                // exception string - matching the Failures-surfacing pattern used by the
                // export tools (ExportXmlBlocks/ExportXmlTypes/ExportBlocksAsDocuments) rather
                // than treating "the compile reported errors" as an exceptional condition.
                var offenders = messages
                    .Where(m => string.Equals(m.State, nameof(CompilerResultState.Error), StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .Select(m => $"{m.Path}: {m.Description}");

                response.Message = $"Compiling '{target}' failed with {result.ErrorCount} error(s). " +
                                    string.Join(" | ", offenders);
            }

            return response;
        }

        /// <summary>
        /// Walks the recursive CompilerResultMessage tree depth-first. Openness nests messages
        /// per object and then per detail, and the previous implementation discarded all of it
        /// in favour of CompilerResult.ToString().
        /// </summary>
        private static IEnumerable<CompileMessage> FlattenCompilerMessages(CompilerResultMessageComposition? messages, int depth)
        {
            if (messages == null)
            {
                yield break;
            }

            foreach (var message in messages)
            {
                if (message == null)
                {
                    continue;
                }

                yield return new CompileMessage
                {
                    Path = message.Path,
                    State = message.State.ToString(),
                    Description = message.Description,
                    ErrorCount = message.ErrorCount,
                    WarningCount = message.WarningCount,
                    Depth = depth
                };

                foreach (var child in FlattenCompilerMessages(message.Messages, depth + 1))
                {
                    yield return child;
                }
            }
        }

        #endregion
    }
}
