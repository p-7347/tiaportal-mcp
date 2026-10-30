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
        // Change detection without exports: save the block fingerprints once, compare later.
        // See Portal.Fingerprints.cs. The saved file is plain JSON:
        // { "softwarePath", "groupPath", "regexName", "takenAt", "complete", "blocks": { path: { id: value } } }

        [McpServerTool(Name = "GetBlockFingerprints", Title = "Get block fingerprints", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Read the fingerprints (checksums of interface, program code, comments, properties, ...) of the program blocks in a group, without exporting anything. Save them with 'saveFile' and later pass that file as 'compareFile' to list exactly which blocks changed and in which aspect, e.g. 'what changed since yesterday'. Fingerprints reflect what users edited, not compile results; inconsistent blocks must be compiled first")]
        public static ResponseBlockFingerprints GetBlockFingerprints(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("groupPath: optional block group, e.g. '009_Servo'; empty reads every block (may stop after about 40 s on a large PLC)")] string groupPath = "",
            [Description("regexName: optional regular expression on the block name")] string regexName = "",
            [Description("saveFile: optional absolute path of a JSON file to write the fingerprints to, for a later 'compareFile'")] string saveFile = "",
            [Description("compareFile: optional absolute path of a file written earlier with 'saveFile'; the reply then lists changed, added and removed blocks instead of all fingerprints")] string compareFile = "",
            [Description("maxBlocks: maximum number of blocks listed with their fingerprints when there is no 'compareFile' (default 100)")] int maxBlocks = 100)
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

                    baseline = JsonNode.Parse(File.ReadAllText(compareFile, Encoding.UTF8)) as JsonObject
                        ?? throw new McpException($"compareFile '{compareFile}' is not a fingerprint file.");
                }

                var scan = Portal.GetBlockFingerprints(softwarePath, groupPath, regexName, TimeSpan.FromSeconds(40));
                var complete = scan.StoppedReason == null;
                var warnings = new List<string>();
                var errors = scan.Blocks.Count(b => b.Error != null);

                var response = new ResponseBlockFingerprints
                {
                    Total = scan.Total,
                    Read = scan.Blocks.Count,
                    StoppedReason = scan.StoppedReason
                };

                if (!string.IsNullOrWhiteSpace(saveFile))
                {
                    var blocksNode = new JsonObject();
                    foreach (var block in scan.Blocks.Where(b => b.Error == null))
                    {
                        var values = new JsonObject();
                        foreach (var pair in block.Values)
                        {
                            values[pair.Key] = pair.Value;
                        }

                        blocksNode[block.Path] = values;
                    }

                    var file = new JsonObject
                    {
                        ["softwarePath"] = softwarePath,
                        ["groupPath"] = groupPath,
                        ["regexName"] = regexName,
                        ["takenAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["complete"] = complete,
                        ["blocks"] = blocksNode
                    };

                    var directory = Path.GetDirectoryName(saveFile);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(saveFile, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                    response.SavedTo = saveFile;

                    if (!complete)
                    {
                        warnings.Add("The saved file is incomplete (the walk stopped early); a later compare will only cover the blocks in it.");
                    }
                }

                string message;

                if (baseline != null)
                {
                    message = CompareFingerprints(baseline, scan, complete, groupPath, regexName, response, warnings);
                }
                else
                {
                    var limit = Math.Max(0, maxBlocks);
                    response.Blocks = scan.Blocks.Take(limit).Select(b => new ResponseBlockFingerprint
                    {
                        Path = b.Path,
                        Fingerprints = b.Error == null ? b.Values : null,
                        Error = b.Error
                    }).ToList();

                    message = $"Fingerprints of {scan.Blocks.Count} of {scan.Total} block(s) read" +
                              (scan.Blocks.Count > limit ? $" (first {limit} listed)" : string.Empty);
                }

                if (errors > 0)
                {
                    message += $"; {errors} block(s) without fingerprints (see 'error', e.g. inconsistent)";
                }

                if (!complete)
                {
                    message += $" - INCOMPLETE: {scan.StoppedReason}";
                }

                if (response.SavedTo != null)
                {
                    message += $"; saved to '{response.SavedTo}'";
                }

                response.Message = message + ".";
                response.Warnings = warnings.Count > 0 ? warnings : null;
                response.Meta = new JsonObject
                {
                    ["timestamp"] = DateTime.Now,
                    ["success"] = true,
                    ["complete"] = complete
                };

                return response;
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading block fingerprints of '{softwarePath}': {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetSoftwareChecksum", Title = "Get software checksum", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Read the checksum of the compiled program of a PLC software (and of its text lists), as TIA Portal shows it. Two equal checksums mean the same compiled program, e.g. before and after a change, or project and CPU. Empty until the program is compiled")]
        public static ResponseSoftwareChecksum GetSoftwareChecksum(
            [Description("softwarePath: PLC software as 'GetProjectTree' names it, e.g. 'PLC_1' or 'PC-System_1/Software PLC_1'")] string softwarePath)
        {
            try
            {
                var checksums = Portal.GetSoftwareChecksum(softwarePath);

                string message;
                if (!checksums.Supported)
                {
                    message = $"'{softwarePath}' does not support a software checksum.";
                }
                else if (checksums.Software == null)
                {
                    message = $"'{softwarePath}' has no software checksum yet: compile it first ('CompileSoftware').";
                }
                else
                {
                    message = $"Software checksum of '{softwarePath}': {checksums.Software}.";
                }

                return new ResponseSoftwareChecksum
                {
                    Supported = checksums.Supported,
                    Software = checksums.Software,
                    TextLists = checksums.TextLists,
                    Message = message,
                    Meta = new JsonObject
                    {
                        ["timestamp"] = DateTime.Now,
                        ["success"] = true
                    }
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error reading the software checksum of '{softwarePath}': {ex.Message}", ex);
            }
        }

        private static string CompareFingerprints(JsonObject baseline, TiaMcpServer.Siemens.Portal.FingerprintScan scan, bool complete, string groupPath, string regexName,
            ResponseBlockFingerprints response, List<string> warnings)
        {
            if (!string.Equals((string?)baseline["groupPath"] ?? string.Empty, groupPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals((string?)baseline["regexName"] ?? string.Empty, regexName ?? string.Empty, StringComparison.Ordinal))
            {
                warnings.Add($"compareFile was saved with groupPath '{baseline["groupPath"]}' and regexName '{baseline["regexName"]}', not the ones given now; blocks outside either selection show up as added or removed.");
            }

            var saved = baseline["blocks"] as JsonObject ?? new JsonObject();
            var changed = new List<ResponseFingerprintChange>();
            var added = new List<string>();
            var unchanged = 0;

            foreach (var block in scan.Blocks.Where(b => b.Error == null))
            {
                if (saved[block.Path] is not JsonObject old)
                {
                    added.Add(block.Path);
                    continue;
                }

                var aspects = block.Values.Keys.Union(old.Select(p => p.Key), StringComparer.Ordinal)
                    .Where(id => !string.Equals(block.Values.TryGetValue(id, out var now) ? now : null, (string?)old[id], StringComparison.Ordinal))
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();

                if (aspects.Count > 0)
                {
                    changed.Add(new ResponseFingerprintChange { Path = block.Path, Aspects = aspects });
                }
                else
                {
                    unchanged++;
                }
            }

            response.Changed = changed;
            response.Added = added;
            response.Unchanged = unchanged;

            if (complete)
            {
                var current = new HashSet<string>(scan.Blocks.Select(b => b.Path), StringComparer.Ordinal);
                response.Removed = saved.Select(p => p.Key).Where(path => !current.Contains(path)).OrderBy(p => p, StringComparer.Ordinal).ToList();
            }
            else
            {
                warnings.Add("Removed blocks are not reported because the walk stopped early.");
            }

            return $"Compared with fingerprints from {(string?)baseline["takenAt"] ?? "an earlier call"}: {changed.Count} changed, {added.Count} added" +
                   (response.Removed != null ? $", {response.Removed.Count} removed" : string.Empty) +
                   $", {unchanged} unchanged";
        }
    }
}
