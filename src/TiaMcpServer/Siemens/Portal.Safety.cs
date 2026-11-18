using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Safety;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Read-only view of the safety program of an F-CPU (Openness manual V19, 5.27).
        //
        // SafetyAdministration on the PLC device item gives the password and login state, the
        // settings and the runtime groups; reading them needs no Safety license. The only F
        // signature Openness offers is the offline signature of each F-block
        // (SafetySignatureProvider on a block, type BlockOfflineSignature) - there is no
        // collective signature in the V20 API. So the tool lists the per-block signatures and
        // adds a hash over all of them: if that hash is unchanged, no F-block signature changed.
        // It is our own value, not TIA's F-collective signature, and is labelled that way.
        //
        // Nothing here logs on, sets a password or changes the program.

        public sealed class SafetyBlockSignature
        {
            public string Path { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
            public bool Valid { get; set; }
        }

        public sealed class SafetyRuntimeGroupInfo
        {
            public string? Name { get; set; }
            public string? FobName { get; set; }
            public string? FobEventClass { get; set; }
            public string? MainSafetyBlockName { get; set; }
            public string? MainSafetyBlockIDbName { get; set; }
            public string? InfoDbName { get; set; }
            public string? MaximumCycleTime { get; set; }
            public string? WarnCycleTime { get; set; }
        }

        public sealed class SafetyInfo
        {
            public bool IsFailsafeCpu { get; set; }
            public bool? PasswordSet { get; set; }
            public bool? LoggedOn { get; set; }
            public Dictionary<string, string?> Settings { get; } = new Dictionary<string, string?>();
            public List<SafetyRuntimeGroupInfo> RuntimeGroups { get; } = new List<SafetyRuntimeGroupInfo>();
            public List<SafetyBlockSignature> Blocks { get; } = new List<SafetyBlockSignature>();
            public int BlocksChecked { get; set; }
            public string? SignatureHash { get; set; }
            public string? StoppedReason { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        public SafetyInfo GetSafetyInfo(string softwarePath, bool includeSignatures, string groupPath, string regexName, TimeSpan budget)
        {
            return Operation.Run(_logger, nameof(GetSafetyInfo), PortalErrorCode.NotFound,
                () =>
                {
                    var softwareContainer = RequireSoftwareContainer(softwarePath);
                    var info = new SafetyInfo();

                    var admin = (softwareContainer.Parent as DeviceItem)?.GetService<SafetyAdministration>();
                    if (admin == null)
                    {
                        return info;
                    }

                    info.IsFailsafeCpu = true;
                    info.PasswordSet = ReadPart<bool?>(() => admin.IsSafetyOfflineProgramPasswordSet, "IsSafetyOfflineProgramPasswordSet", info.Warnings);
                    info.LoggedOn = ReadPart<bool?>(() => admin.IsLoggedOnToSafetyOfflineProgram, "IsLoggedOnToSafetyOfflineProgram", info.Warnings);

                    var settings = ReadPart(() => admin.Settings, "Settings", info.Warnings);
                    if (settings != null)
                    {
                        info.Settings["SafetySystemVersion"] = ReadPart(() => settings.SafetySystemVersion?.Value, "SafetySystemVersion", info.Warnings);
                        info.Settings["ActivationOfFChangeHistory"] = ReadPart(() => Convert.ToString(settings.ActivationOfFChangeHistory), "ActivationOfFChangeHistory", info.Warnings);
                        info.Settings["SafetyModeCanBeDisabled"] = ReadPart(() => Convert.ToString(settings.SafetyModeCanBeDisabled), "SafetyModeCanBeDisabled", info.Warnings);
                        info.Settings["CreateDriverInstanceDataBlocksWithoutPrefix"] = ReadPart(() => Convert.ToString(settings.CreateDriverInstanceDataBlocksWithoutPrefix), "CreateDriverInstanceDataBlocksWithoutPrefix", info.Warnings);
                    }

                    var groups = ReadPart(() => admin.RuntimeGroups, "RuntimeGroups", info.Warnings);
                    if (groups != null)
                    {
                        foreach (var group in groups)
                        {
                            info.RuntimeGroups.Add(new SafetyRuntimeGroupInfo
                            {
                                Name = ReadPart(() => group.Name, "RuntimeGroup.Name", info.Warnings),
                                FobName = ReadPart(() => group.FOBName, "FOBName", info.Warnings),
                                FobEventClass = ReadPart(() => Convert.ToString(group.FOBEventClass), "FOBEventClass", info.Warnings),
                                MainSafetyBlockName = ReadPart(() => group.MainSafetyBlockName, "MainSafetyBlockName", info.Warnings),
                                MainSafetyBlockIDbName = ReadPart(() => group.MainSafetyBlockIDbName, "MainSafetyBlockIDbName", info.Warnings),
                                InfoDbName = ReadPart(() => group.InfoDbName, "InfoDbName", info.Warnings),
                                MaximumCycleTime = ReadPart(() => Convert.ToString(group.MaximumCycleTime), "MaximumCycleTime", info.Warnings),
                                WarnCycleTime = ReadPart(() => Convert.ToString(group.WarnCycleTime), "WarnCycleTime", info.Warnings)
                            });
                        }
                    }

                    if (includeSignatures)
                    {
                        ReadSafetySignatures(softwarePath, groupPath, regexName, budget, info);
                    }

                    return info;
                },
                ("softwarePath", softwarePath));
        }

        private void ReadSafetySignatures(string softwarePath, string groupPath, string regexName, TimeSpan budget, SafetyInfo info)
        {
            var clock = Stopwatch.StartNew();
            var blocks = CollectBlocks(softwarePath, groupPath, regexName);

            BeginProgress(nameof(GetSafetyInfo), blocks.Count);
            try
            {
                foreach (var block in blocks)
                {
                    if (clock.Elapsed > budget)
                    {
                        info.StoppedReason = $"stopped after {budget.TotalSeconds:0} s at block {info.BlocksChecked + 1} of {blocks.Count}; narrow 'groupPath' (e.g. the safety folder) for the rest";
                        break;
                    }

                    info.BlocksChecked++;
                    ReportProgress(info.BlocksChecked, block.Name);

                    SafetySignatureProvider? provider;
                    try
                    {
                        provider = block.GetService<SafetySignatureProvider>();
                    }
                    catch (Exception)
                    {
                        // Not an F-block service host; same as null.
                        continue;
                    }

                    if (provider == null)
                    {
                        continue;
                    }

                    ulong value = 0;
                    try
                    {
                        value = provider.Signatures.Find(SafetySignatureType.BlockOfflineSignature)?.Value ?? 0;
                    }
                    catch (Exception ex)
                    {
                        info.Warnings.Add($"{GetBlockPath(block)}: signature not readable ({ex.Message})");
                    }

                    info.Blocks.Add(new SafetyBlockSignature
                    {
                        Path = GetBlockPath(block),
                        // TIA shows F-signatures as 16#XXXXXXXX; 0 means none or no longer valid.
                        Signature = $"16#{value:X8}",
                        Valid = value != 0
                    });
                }
            }
            finally
            {
                EndProgress();
            }

            if (info.StoppedReason == null && info.Blocks.Count > 0)
            {
                var text = string.Join("\n", info.Blocks
                    .OrderBy(b => b.Path, StringComparer.Ordinal)
                    .Select(b => $"{b.Path}={b.Signature}"));

                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                    info.SignatureHash = string.Concat(hash.Take(8).Select(b => b.ToString("X2")));
                }
            }
        }

        /// <summary>
        /// Blocks below a group ('' = all of Program blocks), optionally filtered by a name regex.
        /// Shared by the tools that walk many blocks (safety signatures, fingerprints).
        /// </summary>
        private List<PlcBlock> CollectBlocks(string softwarePath, string groupPath, string regexName)
        {
            var group = GetPlcBlockGroupByPath(softwarePath, groupPath ?? string.Empty)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Block group '{groupPath}' not found in '{softwarePath}'. 'GetSoftwareTree' lists the groups.");

            var list = new List<PlcBlock>();
            GetBlocksRecursive(group, list, regexName ?? string.Empty);

            return list;
        }

        private SoftwareContainer RequireSoftwareContainer(string softwarePath)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal.");
            }

            return GetSoftwareContainer(softwarePath)
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"No software found at '{softwarePath}'. 'GetProjectTree' lists the valid software paths.");
        }

        /// <summary>One attribute read that may fail on its own without failing the whole report.</summary>
        private static T? ReadPart<T>(Func<T> read, string what, List<string> warnings)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                warnings.Add($"{what}: not readable ({ex.Message})");
                return default;
            }
        }
    }
}
