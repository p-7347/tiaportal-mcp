using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Supervision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // ProDiag (Openness manual V19, 5.11.3.12 - 5.11.3.16 and 6.4.2.23 - 6.4.2.24):
        // - a ProDiag FB (ProgrammingLanguage.ProDiag) holds supervisions; every other FB lists the
        //   supervisions defined on its own members in FB.Supervisions,
        // - an instance DB (or a UDT member of a global DB, or a PLC tag of UDT type) names the
        //   ProDiag FB that supervises it in the attribute "AssignedProDiagFB",
        // - SupervisionProvider exports/imports supervisions as xlsx: on the PlcSoftware for the
        //   global supervisions (Global overview editor), on a ProDiag FB for that block's ones.
        // The PLC code view of WinCC (Unified) jumps from a ProDiag alarm to the LAD/FBD network,
        // so this is what an HMI needs before it can show PLC code.

        public sealed class ProDiagBlockInfo
        {
            public string Path { get; set; } = string.Empty;
            public string? Version { get; set; }
            public bool? InitialValueAcquisition { get; set; }
            public bool? UseCentralTimeStamp { get; set; }
            public List<string> InstanceDbs { get; } = new List<string>();
            public string? Error { get; set; }
        }

        public sealed class SupervisedBlockInfo
        {
            public string Path { get; set; } = string.Empty;
            public int Count { get; set; }

            /// <summary>The first supervisions, parsed from the FB attribute "Supervisions" (key -> value).</summary>
            public List<Dictionary<string, string?>> Supervisions { get; } = new List<Dictionary<string, string?>>();
            public string? Error { get; set; }
        }

        public sealed class ProDiagAssignment
        {
            /// <summary>Instance DB path.</summary>
            public string Path { get; set; } = string.Empty;
            public string? InstanceOf { get; set; }
            public string ProDiagFb { get; set; } = string.Empty;
        }

        public sealed class ProDiagScan
        {
            public int Total { get; set; }
            public List<ProDiagBlockInfo> ProDiagBlocks { get; } = new List<ProDiagBlockInfo>();
            public List<SupervisedBlockInfo> SupervisedBlocks { get; } = new List<SupervisedBlockInfo>();
            public List<ProDiagAssignment> Assignments { get; } = new List<ProDiagAssignment>();
            public string? StoppedReason { get; set; }
        }

        public sealed class SupervisionXlsxOutcome
        {
            /// <summary>"Global supervisions of PLC_1" or the ProDiag FB path.</summary>
            public string Scope { get; set; } = string.Empty;
            public string File { get; set; } = string.Empty;
            public string? State { get; set; }
            public string? LogFile { get; set; }
            public bool Success { get; set; }
        }

        public ProDiagScan GetProDiagInfo(string softwarePath, string groupPath, string regexName, int maxSupervisions, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetProDiagInfo), PortalErrorCode.NotFound,
                () =>
                {
                    RequireSoftwareContainer(softwarePath);

                    var clock = Stopwatch.StartNew();
                    var blocks = CollectBlocks(softwarePath, groupPath, regexName);
                    var scan = new ProDiagScan { Total = blocks.Count };
                    var proDiagByName = new Dictionary<string, ProDiagBlockInfo>(StringComparer.OrdinalIgnoreCase);
                    var plainInstances = new List<(string Path, string InstanceOf)>();
                    var seen = 0;

                    BeginProgress(nameof(GetProDiagInfo), blocks.Count);
                    try
                    {
                        foreach (var block in blocks)
                        {
                            if (clock.Elapsed > budget)
                            {
                                scan.StoppedReason = $"stopped after {budget.TotalSeconds:0} s at block {seen + 1} of {blocks.Count}; narrow 'groupPath' or 'regexName' for the rest";
                                break;
                            }

                            seen++;
                            var path = GetBlockPath(block);
                            ReportProgress(seen, path);

                            if (block is FB fb)
                            {
                                if (fb.ProgrammingLanguage == ProgrammingLanguage.ProDiag)
                                {
                                    var info = DescribeProDiagFb(fb, path);
                                    scan.ProDiagBlocks.Add(info);
                                    proDiagByName[fb.Name] = info;
                                }
                                else
                                {
                                    var supervised = DescribeSupervisions(fb, path, maxSupervisions);
                                    if (supervised != null)
                                    {
                                        scan.SupervisedBlocks.Add(supervised);
                                    }
                                }
                            }
                            else if (block is InstanceDB idb)
                            {
                                var assigned = ReadAttributeText(idb, "AssignedProDiagFB");
                                if (!string.IsNullOrEmpty(assigned))
                                {
                                    scan.Assignments.Add(new ProDiagAssignment
                                    {
                                        Path = path,
                                        InstanceOf = ReadAttributeText(idb, "InstanceOfName"),
                                        ProDiagFb = assigned!
                                    });
                                }
                                else if (ReadAttributeText(idb, "InstanceOfName") is string instanceOf)
                                {
                                    // Maybe the instance DB of a ProDiag FB; matched after the walk,
                                    // because the FB can come later in the block order.
                                    plainInstances.Add((path, instanceOf));
                                }
                            }
                        }
                    }
                    finally
                    {
                        EndProgress();
                    }

                    foreach (var (path, instanceOf) in plainInstances)
                    {
                        if (proDiagByName.TryGetValue(instanceOf, out var owner))
                        {
                            owner.InstanceDbs.Add(path);
                        }
                    }

                    return scan;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath));
        }

        private static ProDiagBlockInfo DescribeProDiagFb(FB fb, string path)
        {
            var info = new ProDiagBlockInfo { Path = path };

            try
            {
                info.Version = fb.GetAttribute("ProDiagVersion")?.ToString();
                info.InitialValueAcquisition = fb.GetAttribute("InitialValueAcquisition") as bool?;
                info.UseCentralTimeStamp = fb.GetAttribute("UseCentralTimeStamp") as bool?;
            }
            catch (Exception ex)
            {
                info.Error = Operation.DescribeChain(ex);
            }

            return info;
        }

        /// <returns>Null when the FB has no supervisions.</returns>
        private static SupervisedBlockInfo? DescribeSupervisions(FB fb, string path, int maxSupervisions)
        {
            try
            {
                var supervisions = fb.Supervisions;
                if (supervisions == null || supervisions.Count == 0)
                {
                    return null;
                }

                var info = new SupervisedBlockInfo { Path = path, Count = supervisions.Count };

                // The Supervision objects expose no attributes in V20 (live 2026-10-29: GetAttributeInfos
                // is empty). The details are in the FB attribute "Supervisions", the same
                // BlockTypeSupervisions XML a block export carries.
                string? xml = null;
                try
                {
                    xml = fb.GetAttribute("Supervisions") as string;
                }
                catch (Exception)
                {
                    // Not readable on this block; Count is still known.
                }

                if (!string.IsNullOrWhiteSpace(xml))
                {
                    info.Supervisions.AddRange(ParseBlockTypeSupervisions(xml!).Take(Math.Max(0, maxSupervisions)));
                }

                return info;
            }
            catch (Exception ex)
            {
                return new SupervisedBlockInfo { Path = path, Error = Operation.DescribeChain(ex) };
            }
        }

        /// <summary>
        /// One entry per BlockTypeSupervision: number, type, supervised operand and status, delay,
        /// the conditions that are set, category numbers and associated values.
        /// </summary>
        private static IEnumerable<Dictionary<string, string?>> ParseBlockTypeSupervisions(string xml)
        {
            var doc = TryParseXml(xml);
            if (doc == null)
            {
                yield break;
            }

            static string? OperandName(XElement? e) => e?.Attribute("Name")?.Value is string v && v.Length > 0 ? v : null;

            foreach (var sup in doc.Descendants().Where(e => e.Name.LocalName == "BlockTypeSupervision"))
            {
                XElement? Child(string name) => sup.Elements().FirstOrDefault(e => e.Name.LocalName == name);

                var entry = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["number"] = sup.Attribute("Number")?.Value,
                    ["type"] = sup.Attribute("Type")?.Value,
                    ["operand"] = OperandName(Child("SupervisedOperand")),
                    ["status"] = Child("SupervisedStatus")?.Value,
                    ["delay"] = OperandName(Child("DelayOperand")),
                    ["category"] = Child("CategoryNumber")?.Value,
                    ["subCategory1"] = Child("SubCategory1Number")?.Value,
                    ["subCategory2"] = Child("SubCategory2Number")?.Value
                };

                var conditions = sup.Descendants().Where(e => e.Name.LocalName == "Condition")
                    .Select(c => (Operand: OperandName(c.Elements().FirstOrDefault(e => e.Name.LocalName == "ConditionOperand")),
                                  Status: c.Elements().FirstOrDefault(e => e.Name.LocalName == "TriggeringStatus")?.Value))
                    .Where(c => c.Operand != null)
                    .Select(c => $"{c.Operand}={c.Status}")
                    .ToList();
                if (conditions.Count > 0)
                {
                    entry["conditions"] = string.Join("; ", conditions);
                }

                var values = sup.Descendants().Where(e => e.Name.LocalName == "AssociatedValueOperand")
                    .Select(OperandName).Where(n => n != null).ToList();
                if (values.Count > 0)
                {
                    entry["associatedValues"] = string.Join("; ", values);
                }

                yield return entry;
            }
        }

        private static XDocument? TryParseXml(string xml)
        {
            try
            {
                return XDocument.Parse(xml);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? ReadAttributeText(IEngineeringObject obj, string name)
        {
            try
            {
                return obj.GetAttribute(name)?.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Exports supervisions as xlsx: the global supervisions of the PLC when blockPath is empty,
        /// otherwise those of one ProDiag FB.
        /// </summary>
        public SupervisionXlsxOutcome ExportProDiagSupervisions(string softwarePath, string blockPath, string exportPath)
        {
            return Operation.Run(_logger, nameof(ExportProDiagSupervisions), PortalErrorCode.ExportFailed,
                () =>
                {
                    Operation.RequireAbsolutePath(exportPath);
                    var (provider, scope) = RequireSupervisionProvider(softwarePath, blockPath);

                    var directory = System.IO.Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var result = provider.ExportSupervisionsToXlsx(new FileInfo(exportPath));
                    return Outcome(scope, exportPath, result);
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("exportPath", exportPath));
        }

        /// <summary>
        /// Imports supervisions (or only their settings) from xlsx. The current supervisions of the
        /// same scope are exported to the backup folder first.
        /// </summary>
        public (SupervisionXlsxOutcome Outcome, BackupResult Backup) ImportProDiagSupervisions(
            string softwarePath, string blockPath, string importPath, bool settingsOnly, bool overwrite)
        {
            return Operation.Run(_logger, nameof(ImportProDiagSupervisions), PortalErrorCode.ImportFailed,
                () =>
                {
                    Operation.RequireAbsolutePath(importPath, "importPath");
                    if (!File.Exists(importPath))
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"Import file not found: '{importPath}'.");
                    }

                    var (provider, scope) = RequireSupervisionProvider(softwarePath, blockPath);
                    var backup = WriteBackup(nameof(ImportProDiagSupervisions), scope, true,
                        file => CheckXlsx(provider.ExportSupervisionsToXlsx(file)), "xlsx");

                    var options = overwrite ? ImportOptions.Override : ImportOptions.None;
                    var importFile = new FileInfo(importPath);
                    var result = settingsOnly
                        ? provider.ImportSupervisionSettingsFromXlsx(importFile, options)
                        : provider.ImportSupervisionsFromXlsx(importFile, options);

                    var outcome = Outcome(scope, importPath, result);
                    if (!outcome.Success)
                    {
                        // Throwing makes the tool transaction roll back whatever the import did.
                        throw new PortalException(PortalErrorCode.ImportFailed,
                            $"TIA Portal reported '{outcome.State}' for the supervision import into {scope}." +
                            (outcome.LogFile != null ? $" Details in '{outcome.LogFile}'." : string.Empty));
                    }

                    return (outcome, backup);
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("importPath", importPath));
        }

        /// <summary>
        /// Sets which ProDiag FB supervises an instance DB, or a UDT member of a global DB when
        /// memberPath is given. An empty proDiagFb removes the assignment.
        /// </summary>
        public (string BlockPath, string? Old, string? New, BackupResult Backup) SetAssignedProDiagFb(
            string softwarePath, string blockPath, string memberPath, string proDiagFb)
        {
            return Operation.Run(_logger, nameof(SetAssignedProDiagFb), PortalErrorCode.InvalidParams,
                () =>
                {
                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'ResolveObjectPath' or 'GetBlocks' to find its path.");

                    var value = (proDiagFb ?? string.Empty).Trim();
                    if (value.Length > 0)
                    {
                        var target = FindBlockByName(softwarePath, value);
                        if (target is not FB targetFb || targetFb.ProgrammingLanguage != ProgrammingLanguage.ProDiag)
                        {
                            throw new PortalException(PortalErrorCode.NotFound,
                                $"'{value}' is not a ProDiag FB in '{softwarePath}'. 'GetProDiagInfo' lists the ProDiag FBs.");
                        }
                    }

                    IEngineeringObject owner;
                    if (string.IsNullOrWhiteSpace(memberPath))
                    {
                        if (block is not InstanceDB)
                        {
                            throw new PortalException(PortalErrorCode.NotSupported,
                                $"'{block.Name}' is a {block.GetType().Name}. Without 'memberPath' only an instance DB takes a ProDiag FB; for a global DB name the UDT member in 'memberPath'.");
                        }

                        owner = block;
                    }
                    else
                    {
                        var (_, members) = RequireDataBlockMembers(softwarePath, blockPath);
                        owner = FindDbMember(members, memberPath)
                            ?? throw new PortalException(PortalErrorCode.NotFound,
                                $"Member '{memberPath}' not found in '{block.Name}'. 'GetDbStartValues' lists the member paths.");
                    }

                    var old = ReadAttributeText(owner, "AssignedProDiagFB");
                    var backup = BackupBlock(softwarePath, blockPath, nameof(SetAssignedProDiagFb));

                    try
                    {
                        owner.SetAttribute("AssignedProDiagFB", value);
                    }
                    catch (Exception ex)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"TIA Portal refused ProDiag FB '{value}' for '{GetBlockPath(block)}{(string.IsNullOrWhiteSpace(memberPath) ? string.Empty : "." + memberPath)}': {Operation.DescribeChain(ex)}" +
                            " Only instance DBs and UDT instances (in a global DB or tag table) take one.", null, ex);
                    }

                    return (GetBlockPath(block), old, ReadAttributeText(owner, "AssignedProDiagFB"), backup);
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("memberPath", memberPath));
        }

        private (SupervisionProvider Provider, string Scope) RequireSupervisionProvider(string softwarePath, string blockPath)
        {
            if (string.IsNullOrWhiteSpace(blockPath))
            {
                var plcSoftware = GetPlcSoftwareOrThrow(softwarePath);
                var global = plcSoftware.GetService<SupervisionProvider>()
                    ?? throw new PortalException(PortalErrorCode.NotSupported,
                        $"'{softwarePath}' offers no ProDiag supervisions (S7-1500 only).");

                return (global, $"global supervisions of {softwarePath}");
            }

            var block = GetBlock(softwarePath, blockPath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block not found at '{blockPath}'. Use 'GetProDiagInfo' to list the ProDiag FBs.");

            if (block is not FB fb || fb.ProgrammingLanguage != ProgrammingLanguage.ProDiag)
            {
                throw new PortalException(PortalErrorCode.NotSupported,
                    $"'{block.Name}' is not a ProDiag FB. Leave 'blockPath' empty for the global supervisions of the PLC.");
            }

            var provider = fb.GetService<SupervisionProvider>()
                ?? throw new PortalException(PortalErrorCode.NotSupported, $"ProDiag FB '{block.Name}' offers no supervision export.");

            return (provider, GetBlockPath(block));
        }

        private static SupervisionXlsxOutcome Outcome(string scope, string file, SupervisionXlsxResult result) => new SupervisionXlsxOutcome
        {
            Scope = scope,
            File = file,
            State = result?.State.ToString(),
            LogFile = result?.LogFilePath?.ToString(),
            Success = result != null && result.State == SupervisionXlsxResultState.Success
        };

        private static void CheckXlsx(SupervisionXlsxResult result)
        {
            if (result == null || result.State != SupervisionXlsxResultState.Success)
            {
                throw new InvalidOperationException($"supervision export reported '{result?.State}'" +
                    (result?.LogFilePath != null ? $", see '{result.LogFilePath}'" : string.Empty));
            }
        }
    }
}
