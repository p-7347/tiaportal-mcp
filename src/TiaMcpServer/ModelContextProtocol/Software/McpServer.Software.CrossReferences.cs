using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.CrossReference;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // From the former McpServer.CrossReferences.cs:
        // Read-only MCP tool for PLC cross references.
        //
        // Callers: registered through Program.BuildTools() and invoked directly by the cross
        // reference test class. Affected API: additive only - a new partial of the existing
        // McpServer type. Data: returns ResponseCrossReferences as the JSON text of the reply. No file I/O.
        //
        // Output size is the real constraint here: Sources -> References -> Locations nests three
        // deep and SourceObject.Children recurses, so an unfiltered AllObjects query on a real PLC
        // produces megabytes. maxDepth defaults to 1 and the response reports Truncated.

        #region cross references

        /// <summary>
        /// Mutable counters for the recursive projection. A class rather than 'ref' parameters
        /// because ref locals cannot be captured by the lambdas used below.
        /// </summary>
        private sealed class CrossRefTally
        {
            public bool Truncated;
            public int ReferenceCount;

            // Budget: the device bridge gives a tool call 60 s, and a call that runs past it is
            // dropped and the server restarted - which detached it from TIA Portal on 2026-10-13
            // (maxDepth 3 on an Assy DB). Stopping early with a partial, flagged result beats that.
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public TimeSpan Budget = TimeSpan.FromSeconds(40);
            public int MaxReferences = int.MaxValue;
            public string? StoppedReason;

            public bool OutOfBudget()
            {
                if (StoppedReason != null)
                {
                    return true;
                }

                if (ReferenceCount >= MaxReferences)
                {
                    StoppedReason = $"stopped at maxReferences {MaxReferences}";
                }
                else if (Clock.Elapsed > Budget)
                {
                    StoppedReason = $"stopped after {Budget.TotalSeconds:0} s to stay inside the tool timeout";
                }

                if (StoppedReason != null)
                {
                    Truncated = true;
                }

                return StoppedReason != null;
            }
        }

        [McpServerTool(Name = "GetCrossReferences", Title = "Get cross references", ReadOnly = true, OpenWorld = false),
         Description("Use to see where a tag or block is used, with locations. Get cross references for a PLC software or for one block, type, tag table, tag or block group inside it. Watch tables, force tables and external sources have no cross references")]
        public static ResponseCrossReferences GetCrossReferences(
            [Description("softwarePath: PLC software, e.g. 'PLC_1'")] string softwarePath,
            [Description("objectPath: optional root-relative path of a block, type, tag table, tag or block group; empty targets the whole PLC software")] string objectPath = "",
            [Description("objectKind: 'auto' (default), 'block', 'type', 'tagTable', 'tag' or 'blockGroup'")] string objectKind = "auto",
            [Description("filter: 'AllObjects' (default), 'ObjectsWithReferences', 'ObjectsWithoutReferences' or 'UnusedObjects'")] string filter = "AllObjects",
            [Description("maxDepth: 1 = sources and their references (default), 2 = also source children, 3 = also reference locations. Keeps large results manageable")] int maxDepth = 1,
            [Description("maxReferences: optional cap on the number of references returned; the walk also stops by itself after about 40 s. Either way 'truncated' is true and 'stoppedReason' says why - narrow 'objectPath' (e.g. one tag or block) to get the rest")] int? maxReferences = null)
        {
            try
            {
                if (!Enum.TryParse<CrossReferenceFilter>(filter, ignoreCase: true, out var parsedFilter))
                {
                    throw new McpException(
                        $"Invalid filter '{filter}'. Allowed values are: {string.Join(", ", Enum.GetNames(typeof(CrossReferenceFilter)))}.");
                }

                var depth = Math.Max(1, Math.Min(3, maxDepth));
                var tally = new CrossRefTally();

                if (maxReferences.HasValue && maxReferences.Value > 0)
                {
                    tally.MaxReferences = maxReferences.Value;
                }

                var result = Portal.GetCrossReferences(softwarePath, objectPath, objectKind, parsedFilter);

                // The result's collections are lazy Openness objects; walk them under the same lock
                // as every other Openness call.
                var sources = Operation.Run(null, nameof(GetCrossReferences), PortalErrorCode.ExportFailed, () =>
                {
                    var list = new List<CrossRefSource>();

                    foreach (var source in result.Sources)
                    {
                        if (tally.OutOfBudget())
                        {
                            break;
                        }

                        list.Add(ToSource(source, depth, tally));
                    }

                    return list;
                });

                var target = string.IsNullOrEmpty(objectPath) ? softwarePath : objectPath;

                return new ResponseCrossReferences
                {
                    Message = $"{sources.Count} cross reference source(s) with {tally.ReferenceCount} reference(s) retrieved for '{target}'"
                              + (tally.StoppedReason != null
                                  ? $" - INCOMPLETE: {tally.StoppedReason}; narrow 'objectPath' or lower 'maxDepth' for the rest"
                                  : tally.Truncated ? $" (truncated at maxDepth {depth})" : string.Empty),
                    Sources = sources,
                    SourceCount = sources.Count,
                    ReferenceCount = tally.ReferenceCount,
                    Truncated = tally.Truncated,
                    StoppedReason = tally.StoppedReason,
                    Meta = Ok(new JsonObject
                    {
                        ["filter"] = parsedFilter.ToString(),
                        ["maxDepth"] = depth,
                        ["sourceCount"] = sources.Count,
                        ["referenceCount"] = tally.ReferenceCount
                    })
                };
            }
            catch (TiaMcpServer.Siemens.PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error retrieving cross references from '{softwarePath}': {ex.Message}", ex);
            }
        }

        private static CrossRefSource ToSource(SourceObject source, int maxDepth, CrossRefTally tally)
        {
            var references = new List<CrossRefReference>();

            foreach (var reference in source.References)
            {
                if (tally.OutOfBudget())
                {
                    break;
                }

                tally.ReferenceCount++;

                List<CrossRefLocation>? locations = null;

                if (maxDepth >= 3)
                {
                    locations = reference.Locations.Select(ToLocation).ToList();
                }
                else if (reference.Locations.Count > 0)
                {
                    tally.Truncated = true;
                }

                references.Add(new CrossRefReference
                {
                    Name = reference.Name,
                    Path = reference.Path,
                    Address = reference.Address,
                    Device = reference.Device,
                    TypeName = reference.TypeName,
                    Locations = locations
                });
            }

            List<CrossRefSource>? children = null;

            if (maxDepth >= 2)
            {
                children = new List<CrossRefSource>();

                foreach (var child in source.Children)
                {
                    if (tally.OutOfBudget())
                    {
                        break;
                    }

                    children.Add(ToSource(child, maxDepth, tally));
                }
            }
            else if (source.Children.Count > 0)
            {
                tally.Truncated = true;
            }

            return new CrossRefSource
            {
                Name = source.Name,
                Path = source.Path,
                Address = source.Address,
                Device = source.Device,
                TypeName = source.TypeName,
                References = references,
                Children = children
            };
        }

        private static CrossRefLocation ToLocation(Location location)
        {
            return new CrossRefLocation
            {
                Name = location.Name,
                Address = location.Address,
                TypeName = location.TypeName,
                Access = location.Access.ToString(),
                ReferenceType = location.ReferenceType.ToString(),
                ReferenceLocation = location.ReferenceLocation,
                ReferencedAsName = location.ReferencedAsName
            };
        }

        #endregion
    }
}
