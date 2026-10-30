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
        // McpServer type. Data: returns ResponseCrossReferences as MCP structuredContent. No file I/O.
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

        [McpServerTool(Name = "GetCrossReferences", Title = "Get cross references", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use to see where a tag or block is used, with locations. Get cross references for a PLC software or for one block, type, tag table, tag or block group inside it. Watch tables, force tables and external sources have no cross references")]
        public static ResponseCrossReferences GetCrossReferences(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
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

        #region insight

        [McpServerTool(Name = "WhereUsed", Title = "Where used", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("Use for a quick 'who uses X' by name. Answer 'what uses this?' for a tag, block, PLC data type or tag table by name. Resolves the name, picks the right object kind and flattens the cross-reference tree to a plain list of users, including the uses of a DB's or data type's members ('member'). Use 'GetCrossReferences' instead when the full nested result or a specific filter is needed")]
        public static ResponseWhereUsed WhereUsed(
            [Description("softwarePath: the PLC software as 'GetProjectTree' names it, e.g. 'PLC_1'; a PLC inside a PC station or device group takes its parent path, e.g. 'PC-System_1/Software PLC_1'")] string softwarePath,
            [Description("name: the object to look up, by bare name or by full root-relative path")] string name,
            [Description("kind: restrict resolution to 'block', 'type', 'tag' or 'tagTable'. Default 'any' picks the single match, and reports the candidates when the name is ambiguous")] string kind = "any")
        {
            try
            {
                var matches = Portal.ResolveObjectPath(softwarePath, name, kind)
                    .Where(m => CrossReferenceKinds.Contains(m.Kind))
                    .ToList();

                if (matches.Count == 0)
                {
                    throw new McpException(
                        $"No block, PLC data type, tag or tag table named '{name}' in '{softwarePath}'. " +
                        "Watch tables and external source files have no cross references.");
                }

                if (matches.Count > 1)
                {
                    throw new McpException(
                        $"'{name}' is ambiguous in '{softwarePath}': " +
                        string.Join(", ", matches.Select(m => $"{m.Path} ({m.Kind})")) +
                        ". Pass the full path, or narrow it with 'kind'.");
                }

                var target = matches[0];
                var result = Portal.GetCrossReferences(softwarePath, target.Path, target.Kind, CrossReferenceFilter.AllObjects);

                // Members of a DB, UDT or struct are child sources with references of their own;
                // reading only the top level missed every use of a DB member
                var found = new List<ResponseUsage>();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var complete = true;

                foreach (var source in result.Sources)
                {
                    complete &= CollectUsages(source, null, 0, found, clock);
                }

                var users = found
                    .GroupBy(u => $"{u.Path}|{u.UsedBy}|{u.Address}|{u.Member}")
                    .Select(g => g.First())
                    .OrderBy(u => u.Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(u => u.Member, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ResponseWhereUsed
                {
                    Message = (users.Count == 0
                        ? $"'{target.Path}' ({target.Kind}) is not used anywhere in '{softwarePath}'"
                        : $"'{target.Path}' ({target.Kind}) has {users.Count} use(s)" +
                          (users.Any(u => u.Member != null) ? ", including uses of its members ('member')" : string.Empty)) +
                              (complete ? string.Empty : ". WARNING: stopped after about 40 s, so the list is incomplete - use 'GetCrossReferences' on a single member or tag for the rest"),
                    Path = target.Path,
                    Kind = target.Kind,
                    Items = users,
                    Meta = Ok(new JsonObject { ["userCount"] = users.Count, ["kind"] = target.Kind })
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error resolving usages of '{name}': {ex.Message}", ex);
            }
        }

        private static readonly TimeSpan WhereUsedBudget = TimeSpan.FromSeconds(40);

        /// <summary>Adds the references of 'source' and of its members; false when the time budget ran out.</summary>
        private static bool CollectUsages(SourceObject source, string? member, int depth, List<ResponseUsage> sink, System.Diagnostics.Stopwatch clock)
        {
            if (clock.Elapsed > WhereUsedBudget)
            {
                return false;
            }

            foreach (var reference in source.References)
            {
                sink.Add(new ResponseUsage
                {
                    UsedBy = reference.Name,
                    Path = reference.Path,
                    Address = reference.Address,
                    TypeName = reference.TypeName,
                    Member = member
                });
            }

            if (depth >= 8)
            {
                return true;
            }

            foreach (var child in source.Children)
            {
                if (!CollectUsages(child, member == null ? child.Name : $"{member}.{child.Name}", depth + 1, sink, clock))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Object kinds Openness can produce cross references for.</summary>
        private static readonly HashSet<string> CrossReferenceKinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "block", "type", "tag", "tagTable" };

        #endregion
    }
}
