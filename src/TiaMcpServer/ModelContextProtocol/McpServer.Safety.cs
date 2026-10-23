using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // Read-only safety program status; see Portal.Safety.cs. No login, no password, no change.
        // 'saveFile' / 'compareFile' (BACKLOG D5) keep the F-block signatures in a plain JSON file:
        // { "softwarePath", "groupPath", "regexName", "takenAt", "complete", "signatureHash", "blocks": { path: "16#..." } }

        [McpServerTool(Name = "GetSafetyInfo", Title = "Get safety program info", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Read the safety program status of an F-CPU without changing anything: whether a safety password is set and logged on, the safety settings and runtime groups (F-OB, main safety block, cycle times), and the offline signature of every F-block. 'signatureHash' combines all F-block signatures, so comparing it with an earlier call shows whether any F-block changed (it is not TIA's F-collective signature). A signature of 16#00000000 means the block has no valid signature, e.g. changed since the last compile. Use 'saveFile' before a change and 'compareFile' after it (and a compile) to list exactly which F-block signatures changed")]
        public static ResponseSafetyInfo GetSafetyInfo(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("includeSignatures: read the F-block signatures (default true); false returns only the status, settings and runtime groups")] bool includeSignatures = true,
            [Description("groupPath: optional block group to look in, e.g. '005_Safety'; empty checks every block (slower on a large PLC)")] string groupPath = "",
            [Description("regexName: optional regular expression on the block name")] string regexName = "",
            [Description("saveFile: optional absolute path of a JSON file to write the F-block signatures to, for a later 'compareFile', e.g. 'C:\\Temp\\safety_before.json'")] string saveFile = "",
            [Description("compareFile: optional absolute path of a file written earlier with 'saveFile'; the reply then lists the F-blocks whose signature changed, was added or removed")] string compareFile = "")
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(saveFile))
                {
                    Operation.RequireAbsolutePath(saveFile, nameof(saveFile));
                }

                JsonObject? baseline = null;

                if (!string.IsNullOrWhiteSpace(compareFile))
                {
                    Operation.RequireAbsolutePath(compareFile, nameof(compareFile));

                    if (!File.Exists(compareFile))
                    {
                        throw new McpException($"compareFile '{compareFile}' does not exist. Write one first with 'saveFile'.");
                    }

                    baseline = JsonNode.Parse(File.ReadAllText(compareFile, Encoding.UTF8)) as JsonObject;
                    if (baseline?["blocks"] is not JsonObject)
                    {
                        throw new McpException($"compareFile '{compareFile}' is not a safety signature file written with 'saveFile'.");
                    }
                }

                if (!includeSignatures && (baseline != null || !string.IsNullOrWhiteSpace(saveFile)))
                {
                    throw new McpException("'saveFile' and 'compareFile' need the signatures; leave 'includeSignatures' true.");
                }

                var info = Portal.GetSafetyInfo(softwarePath, includeSignatures, groupPath, regexName, TimeSpan.FromSeconds(40));

                if (!info.IsFailsafeCpu)
                {
                    return new ResponseSafetyInfo
                    {
                        Message = $"'{softwarePath}' is not an F-CPU (no safety administration), so it has no safety program.",
                        IsFailsafeCpu = false,
                        Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                    };
                }

                var blocks = info.Blocks.Select(b => new ResponseSafetyBlockSignature { Path = b.Path, Signature = b.Signature, Valid = b.Valid }).ToList();
                var invalid = blocks.Count(b => !b.Valid);

                var message = $"Safety program of '{softwarePath}': password {(info.PasswordSet == true ? "set" : info.PasswordSet == false ? "not set" : "unknown")}, " +
                              $"{(info.LoggedOn == true ? "logged on" : "not logged on")}, {info.RuntimeGroups.Count} runtime group(s)";

                if (includeSignatures)
                {
                    message += $"; {blocks.Count} F-block(s) among {info.BlocksChecked} block(s) checked, {invalid} without a valid signature";
                    message += info.StoppedReason != null
                        ? $" - INCOMPLETE: {info.StoppedReason}"
                        : info.SignatureHash != null ? $", signature hash {info.SignatureHash}" : string.Empty;
                }

                var complete = info.StoppedReason == null;
                var warnings = new List<string>(info.Warnings);
                string? savedTo = null;

                if (!string.IsNullOrWhiteSpace(saveFile))
                {
                    var blocksNode = new JsonObject();
                    foreach (var block in blocks)
                    {
                        blocksNode[block.Path] = block.Signature;
                    }

                    var file = new JsonObject
                    {
                        ["softwarePath"] = softwarePath,
                        ["groupPath"] = groupPath,
                        ["regexName"] = regexName,
                        ["takenAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["complete"] = complete,
                        ["signatureHash"] = info.SignatureHash,
                        ["blocks"] = blocksNode
                    };

                    var directory = Path.GetDirectoryName(saveFile);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(saveFile, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                    savedTo = saveFile;
                    message += $"; saved to '{saveFile}'";

                    if (!complete)
                    {
                        warnings.Add("The saved file is incomplete (the walk stopped early); a later compare will only cover the blocks in it.");
                    }
                }

                ResponseSafetyCompare? comparison = null;
                if (baseline != null)
                {
                    comparison = CompareSafetySignatures(baseline, blocks, complete, groupPath, regexName, warnings);
                    message += $"; compared with signatures from {(string?)baseline["takenAt"] ?? "an earlier call"}: " +
                               $"{comparison.Changed.Count} changed, {comparison.Added.Count} added" +
                               (comparison.Removed != null ? $", {comparison.Removed.Count} removed" : string.Empty) +
                               $", {comparison.Unchanged} unchanged";
                }

                return new ResponseSafetyInfo
                {
                    Message = message + ".",
                    IsFailsafeCpu = true,
                    PasswordSet = info.PasswordSet,
                    LoggedOn = info.LoggedOn,
                    Settings = info.Settings,
                    RuntimeGroups = info.RuntimeGroups.Select(g => new ResponseSafetyRuntimeGroup
                    {
                        Name = g.Name,
                        FobName = g.FobName,
                        FobEventClass = g.FobEventClass,
                        MainSafetyBlockName = g.MainSafetyBlockName,
                        MainSafetyBlockIDbName = g.MainSafetyBlockIDbName,
                        InfoDbName = g.InfoDbName,
                        MaximumCycleTime = g.MaximumCycleTime,
                        WarnCycleTime = g.WarnCycleTime
                    }).ToList(),
                    Blocks = includeSignatures ? blocks : null,
                    BlocksChecked = info.BlocksChecked,
                    FBlockCount = blocks.Count,
                    InvalidCount = invalid,
                    SignatureHash = info.SignatureHash,
                    StoppedReason = info.StoppedReason,
                    Warnings = warnings.Count > 0 ? warnings : null,
                    SavedTo = savedTo,
                    Comparison = comparison,
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
                throw new McpException($"Unexpected error reading the safety program of '{softwarePath}': {ex.Message}", ex);
            }
        }

        private static ResponseSafetyCompare CompareSafetySignatures(JsonObject baseline, List<ResponseSafetyBlockSignature> blocks, bool complete,
            string groupPath, string regexName, List<string> warnings)
        {
            if (!string.Equals((string?)baseline["groupPath"] ?? string.Empty, groupPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals((string?)baseline["regexName"] ?? string.Empty, regexName ?? string.Empty, StringComparison.Ordinal))
            {
                warnings.Add($"compareFile was saved with groupPath '{baseline["groupPath"]}' and regexName '{baseline["regexName"]}', not the ones given now; F-blocks outside either selection show up as added or removed.");
            }

            var saved = (JsonObject)baseline["blocks"]!;
            var result = new ResponseSafetyCompare
            {
                TakenAt = (string?)baseline["takenAt"],
                SignatureHashBefore = (string?)baseline["signatureHash"]
            };

            foreach (var block in blocks)
            {
                if (!saved.ContainsKey(block.Path))
                {
                    result.Added.Add(block.Path);
                    continue;
                }

                var before = (string?)saved[block.Path] ?? string.Empty;
                if (string.Equals(before, block.Signature, StringComparison.OrdinalIgnoreCase))
                {
                    result.Unchanged++;
                }
                else
                {
                    result.Changed.Add(new ResponseSafetySignatureChange { Path = block.Path, Before = before, After = block.Signature });
                }
            }

            if (complete)
            {
                var current = new HashSet<string>(blocks.Select(b => b.Path), StringComparer.Ordinal);
                result.Removed = saved.Select(p => p.Key).Where(path => !current.Contains(path)).OrderBy(p => p, StringComparer.Ordinal).ToList();
            }
            else
            {
                warnings.Add("Removed F-blocks are not reported because the walk stopped early.");
            }

            return result;
        }
    }
}
