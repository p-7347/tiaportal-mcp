using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.WatchAndForceTables;
using Siemens.Engineering.Safety;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // From the former Portal.CrossReferences.cs:
        // Cross references for PLC software objects.
        //
        // Callers: the GetCrossReferences tool in McpServer.Software.CrossReferences.cs. Affected API: none
        // existing - all members are new. Reads/writes no data files.
        //
        // CrossReferenceService lives in Siemens.Engineering.Base, not Step7, and is offered by
        // software, blocks, types, their groups, tags, tag tables and constants - but NOT by watch
        // tables, force tables or external sources. A null service therefore means "this object
        // kind has no cross references", which is reported as NotSupported rather than NotFound.

        #region cross references

        /// <param name="objectPath">Empty targets the whole PLC software.</param>
        /// <param name="objectKind">
        /// "auto" (default) resolves the path against blocks, then types, then tag tables, then
        /// tags, then block groups. Pass an explicit kind to skip the probing.
        /// </param>
        public CrossReferenceResult GetCrossReferences(
            string softwarePath,
            string objectPath = "",
            string objectKind = "auto",
            CrossReferenceFilter filter = CrossReferenceFilter.AllObjects)
        {
            return Operation.Run(_logger, nameof(GetCrossReferences), PortalErrorCode.NotFound,
                () =>
                {
                    var provider = ResolveCrossReferenceProvider(softwarePath, objectPath, objectKind);

                    var service = provider.GetService<CrossReferenceService>()
                        ?? throw new PortalException(PortalErrorCode.NotSupported,
                            $"'{(string.IsNullOrEmpty(objectPath) ? softwarePath : objectPath)}' does not provide cross references. " +
                            "Watch tables, force tables and external sources have none; blocks, types, groups, tags and tag tables do.");

                    return service.GetCrossReferences(filter);
                },
                ("softwarePath", softwarePath), ("objectPath", objectPath), ("objectKind", objectKind), ("filter", filter));
        }

        /// <summary>
        /// Who calls a block, up to 'levels' levels: one line per chain from the block up to a block
        /// nobody calls (an OB) or the level limit, e.g. "FB920 SM_PitchHome_Inch &lt;- FB118 G2U1_SM118 (Multiinstance, NW6) &lt;- OB1 Main".
        /// A caller is a referencing block whose location is a call, a multi-instance or an instance DB
        /// and is not a 'Uses' reference (those are the blocks this one calls). Instance DBs end a chain:
        /// the block that calls with them is listed as a caller of the FB itself.
        /// </summary>
        public List<string> GetCallerChains(string softwarePath, string objectPath, int levels, TimeSpan budget, int maxLines, out string? stoppedReason)
        {
            string? stopped = null;

            var chains = Operation.Run(_logger, nameof(GetCallerChains), PortalErrorCode.NotFound,
                () =>
                {
                    var block = GetBlock(softwarePath, objectPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"No block found at '{objectPath}' in '{softwarePath}'. 'callers' needs a block; use 'GetSoftwareTree' or 'GetBlocks' to find the path.");

                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var cache = new Dictionary<PlcBlock, List<(PlcBlock Caller, string How)>>();
                    var lines = new List<string>();

                    void Walk(PlcBlock current, string chain, HashSet<PlcBlock> onPath, int level)
                    {
                        if (stopped != null)
                        {
                            return;
                        }

                        if (lines.Count >= maxLines)
                        {
                            stopped = $"stopped at {maxLines} chains";
                            return;
                        }

                        if (clock.Elapsed > budget)
                        {
                            stopped = $"stopped after {budget.TotalSeconds:0} s to stay inside the tool timeout";
                            return;
                        }

                        if (!cache.TryGetValue(current, out var callers))
                        {
                            callers = CallersOf(current);
                            cache[current] = callers;
                        }

                        if (callers.Count == 0)
                        {
                            lines.Add(level == 0 ? $"{BlockLabel(current)}: no callers found" : chain);
                            return;
                        }

                        if (level >= levels)
                        {
                            lines.Add($"{chain} <- ... ({callers.Count} more caller(s); raise 'callers')");
                            return;
                        }

                        foreach (var (caller, how) in callers)
                        {
                            var link = $"{chain} <- {BlockLabel(caller)} ({how})";

                            if (onPath.Contains(caller))
                            {
                                lines.Add(link + " (recursion)");
                                continue;
                            }

                            if (caller is InstanceDB)
                            {
                                lines.Add(link);
                                continue;
                            }

                            onPath.Add(caller);
                            Walk(caller, link, onPath, level + 1);
                            onPath.Remove(caller);
                        }
                    }

                    Walk(block, BlockLabel(block), new HashSet<PlcBlock> { block }, 0);

                    return lines;
                },
                ("softwarePath", softwarePath), ("objectPath", objectPath), ("levels", levels));

            stoppedReason = stopped;

            return chains;
        }

        private static readonly HashSet<Access> CallAccesses = new HashSet<Access>
        {
            Access.Call, Access.Multiinstance, Access.MultiinstanceAndSymbol, Access.InstanceDB, Access.InstanceAndSymbol,
            Access.Parameterinstance, Access.ParameterinstanceAndSymbol
        };

        /// <summary>The blocks that call 'block' (or hold its instance), each with how and where, e.g. "Multiinstance, NW6".</summary>
        private static List<(PlcBlock Caller, string How)> CallersOf(PlcBlock block)
        {
            var callers = new List<(PlcBlock, string)>();
            var service = block.GetService<CrossReferenceService>();

            if (service == null)
            {
                return callers;
            }

            foreach (var source in service.GetCrossReferences(CrossReferenceFilter.AllObjects).Sources)
            {
                foreach (var reference in source.References)
                {
                    if (reference.UnderlyingObject is not PlcBlock caller || caller.Equals(block))
                    {
                        continue;
                    }

                    var calls = reference.Locations
                        .Where(l => CallAccesses.Contains(l.Access) && l.ReferenceType != ReferenceType.Uses)
                        .ToList();

                    if (calls.Count == 0 || callers.Any(c => c.Item1.Equals(caller)))
                    {
                        continue;
                    }

                    var where = calls.Select(l => l.ReferenceLocation).FirstOrDefault(r => !string.IsNullOrEmpty(r));
                    var how = calls[0].Access + (where != null ? $", {where}" : string.Empty) +
                              (calls.Count > 1 ? $" +{calls.Count - 1}" : string.Empty);

                    callers.Add((caller, how));
                }
            }

            return callers;
        }

        private static string BlockLabel(PlcBlock block)
        {
            var kind = block switch
            {
                OB => "OB",
                FB => "FB",
                FC => "FC",
                _ => "DB"
            };

            return $"{kind}{block.Number} {block.Name}";
        }

        /// <summary>
        /// Maps a path plus kind onto the Openness object that offers the cross reference
        /// service. Probing order matters: block names and type names can collide.
        /// </summary>
        private IEngineeringServiceProvider ResolveCrossReferenceProvider(string softwarePath, string objectPath, string objectKind)
        {
            var plcSoftware = GetPlcSoftwareOrThrow(softwarePath);

            if (NormalizeGroupPath(objectPath).Length == 0)
            {
                return plcSoftware;
            }

            var kind = (objectKind ?? "auto").Trim().ToLowerInvariant();

            IEngineeringServiceProvider? provider;

            switch (kind)
            {
                case "block":
                    provider = GetBlock(softwarePath, objectPath);
                    break;
                case "type":
                    provider = GetType(softwarePath, objectPath);
                    break;
                case "tagtable":
                    provider = GetTagTable(softwarePath, objectPath);
                    break;
                case "tag":
                    provider = GetTag(softwarePath, objectPath);
                    break;
                case "blockgroup":
                    provider = GetPlcBlockGroupByPath(softwarePath, objectPath);
                    break;
                case "auto":
                    // Each candidate needs its own cast: the ?? operator has no common type
                    // across PlcBlock, PlcType, PlcTagTable, PlcTag and PlcBlockGroup.
                    provider = (IEngineeringServiceProvider?)GetBlock(softwarePath, objectPath)
                               ?? (IEngineeringServiceProvider?)GetType(softwarePath, objectPath)
                               ?? (IEngineeringServiceProvider?)GetTagTable(softwarePath, objectPath)
                               ?? (IEngineeringServiceProvider?)TryGetTag(softwarePath, objectPath)
                               ?? (IEngineeringServiceProvider?)GetPlcBlockGroupByPath(softwarePath, objectPath);
                    break;
                default:
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        $"Unknown objectKind '{objectKind}'. Allowed values are 'auto', 'block', 'type', 'tagTable', 'tag' and 'blockGroup'.");
            }

            return provider
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"No object found at '{objectPath}' in '{softwarePath}'. Use 'GetSoftwareTree' to discover valid paths.");
        }

        /// <summary>
        /// GetTag throws InvalidParams for a path without a table segment, which is a normal
        /// outcome while probing in "auto" mode rather than a failure.
        /// </summary>
        private PlcTag? TryGetTag(string softwarePath, string objectPath)
        {
            try
            {
                return GetTag(softwarePath, objectPath);
            }
            catch (PortalException)
            {
                return null;
            }
        }

        #endregion
    }
}
