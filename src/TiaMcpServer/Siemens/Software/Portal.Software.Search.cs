using Microsoft.Extensions.Logging;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.WatchAndForceTables;
using Siemens.Engineering.Safety;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // From the former Portal.Search.cs:
        // Searching the program text, not just object names.
        //
        // Callers: the FindInCode tool in McpServer.Software.Search.cs. Affected API: none existing - every
        // member here is new.
        //
        // File I/O: like the source readers, this has to export before it can search, because
        // Openness offers no in-memory access to a block body. Everything is written to a scratch
        // directory under the temp path and removed again.
        //
        // Cost: one export per candidate object the first time; the text is then kept in memory
        // while the object's ModifiedDate is unchanged (see ReadSearchText), so later searches
        // only re-export what changed. Narrow the first search with 'nameFilter' on a large PLC.

        #region search

        /// <summary>
        /// Finds a regular expression in the source text of blocks and PLC data types.
        /// </summary>
        /// <param name="pattern">Regular expression matched against each line, case-insensitive.</param>
        /// <param name="nameFilter">Optional regex on object names, to limit what gets exported.</param>
        /// <param name="maxResults">Stops once this many matches are found.</param>
        public CodeSearchResult FindInCode(
            string softwarePath,
            string pattern,
            string nameFilter = "",
            int maxResults = 200,
            int startAt = 0)
        {
            return Operation.Run(_logger, nameof(FindInCode), PortalErrorCode.ExportFailed,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            "'pattern' is empty. Pass the text or regular expression to search for.");
                    }

                    if (maxResults <= 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "'maxResults' must be greater than zero.");
                    }

                    if (startAt < 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "'startAt' must not be negative.");
                    }

                    var clock = System.Diagnostics.Stopwatch.StartNew();

                    Regex regex;

                    try
                    {
                        regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{pattern}' is not a valid regular expression: {ex.Message}", null, ex);
                    }

                    var result = new CodeSearchResult { Pattern = pattern };

                    // Listing the candidates is itself a long walk on a big PLC (every block name
                    // is read, filtered or not), so report it too - otherwise GetState shows
                    // nothing for the first part of the call.
                    BeginProgress(nameof(FindInCode), null);

                    try
                    {
                        // Blocks and types are read through the same readers the source tools use, so
                        // a hit always corresponds to text the caller can fetch with GetBlockSource.
                        // The objects are kept rather than just their paths: re-resolving each path
                        // walked the device list and group tree again for every single object.
                        // A continued call (startAt > 0) reuses the list the first call made: listing and
                        // checking ~1000 objects took most of the 40 s budget, leaving a few objects per call.
                        var key = $"{CurrentPortalId}|{softwarePath}|{nameFilter}";
                        var cached = _searchTargets;
                        List<(string Path, PlcBlock? Block, PlcType? Type)> readable;

                        if (startAt > 0 && cached != null && cached.Key == key && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(SearchListReuseMinutes))
                        {
                            readable = (List<(string Path, PlcBlock? Block, PlcType? Type)>)cached.Readable;
                        }
                        else
                        {
                            readable = ListSearchTargets(softwarePath, nameFilter, startAt == 0 ? result.Unsearchable : new List<string>());
                            _searchTargets = new SearchTargetCache(key, DateTime.UtcNow, readable);
                        }

                        result.ObjectsTotal = readable.Count;

                        // Each target is exported individually, so a big, unfiltered PLC can take
                        // minutes - 'GetState' reports this while it runs so a caller stuck on a
                        // busy error can tell "still working" from "looks stuck".
                        SetProgressTotal(readable.Count);

                        for (var t = startAt; t < readable.Count; t++)
                        {
                            var target = readable[t];

                            if (result.Items.Count >= maxResults)
                            {
                                result.Truncated = true;

                                break;
                            }

                            // An unfiltered search of a big PLC ran past the client's timeout and the
                            // server was disconnected; stop in time and say where to go on.
                            if (result.ObjectsSearched > 0 && clock.Elapsed > SearchBudget)
                            {
                                result.NextStartAt = t;

                                break;
                            }

                            result.ObjectsSearched++;

                            ReportProgress(t, target.Path);

                            SourceTextResult source;

                            try
                            {
                                source = ReadSearchText(softwarePath, target.Path, target.Block, target.Type);
                            }
                            catch (Exception ex)
                            {
                                // One unreadable object must not end the search; know-how protection
                                // and export refusals are both normal on a real PLC.
                                result.Unsearchable.Add($"{target.Path}: {ex.Message}");
                                _logger?.LogDebug(ex, "Skipping '{Path}' during code search", target.Path);

                                continue;
                            }

                            AddMatches(result, target.Path, source, regex, maxResults);
                        }
                    }
                    finally
                    {
                        EndProgress();
                    }

                    _logger?.LogInformation(
                        "FindInCode '{Pattern}' searched {Count} object(s) in '{Software}': {Hits} hit(s), {Skipped} unsearchable",
                        pattern, result.ObjectsSearched, softwarePath, result.Items.Count, result.Unsearchable.Count);

                    return result;
                },
                ("softwarePath", softwarePath), ("pattern", pattern), ("nameFilter", nameFilter));
        }

        private const int SearchListReuseMinutes = 10;

        // Exported text of each searched object, kept while the object is unchanged: an export took
        // about 5 s per block on a 985-object PLC (live 2026-10-30), so a second search re-exporting
        // everything got through ~7 objects per call. The stamp is ModifiedDate, which TIA moves on
        // any change to the object; the key carries portal and project so another project never hits.
        private readonly Dictionary<string, SearchTextEntry> _searchTexts = new Dictionary<string, SearchTextEntry>(StringComparer.Ordinal);

        private const int SearchTextCacheLimit = 5000;

        private sealed class SearchTextEntry
        {
            public SearchTextEntry(DateTime stamp, SourceTextResult text)
            {
                Stamp = stamp;
                Text = text;
            }

            public DateTime Stamp { get; }
            public SourceTextResult Text { get; }
        }

        private SourceTextResult ReadSearchText(string softwarePath, string path, PlcBlock? block, PlcType? type)
        {
            var key = $"{CurrentPortalId}|{_project?.Path?.FullName}|{softwarePath}|{path}";
            var stamp = block?.ModifiedDate ?? type!.ModifiedDate;

            if (_searchTexts.TryGetValue(key, out var hit) && hit.Stamp == stamp)
            {
                return hit.Text;
            }

            var text = block != null
                ? ReadBlockSource(block, path, "document", int.MaxValue)
                : ReadTypeSource(type!, path, "document", int.MaxValue);

            if (_searchTexts.Count >= SearchTextCacheLimit)
            {
                _searchTexts.Clear();
            }

            _searchTexts[key] = new SearchTextEntry(stamp, text);

            return text;
        }

        // A plain class typed with 'object': a field whose type names PlcBlock (e.g. a tuple) makes the
        // runtime load Siemens.Engineering when Portal is first touched in Main, before the Openness
        // resolver is installed, and the server died at start (live 2026-10-30).
        private SearchTargetCache? _searchTargets;

        private sealed class SearchTargetCache
        {
            public SearchTargetCache(string key, DateTime at, object readable)
            {
                Key = key;
                At = at;
                Readable = readable;
            }

            public string Key { get; }
            public DateTime At { get; }
            public object Readable { get; }
        }

        /// <summary>The blocks and types FindInCode can export, in search order; inconsistent ones go to 'unsearchable'.</summary>
        private List<(string Path, PlcBlock? Block, PlcType? Type)> ListSearchTargets(string softwarePath, string nameFilter, List<string> unsearchable)
        {
            var targets = new List<(string Path, PlcBlock? Block, PlcType? Type)>();

            ReportProgress(0, "(listing blocks)");

            foreach (var block in GetBlocks(softwarePath, nameFilter))
            {
                targets.Add((GetBlockPath(block), block, null));
            }

            ReportProgress(0, "(listing PLC data types)");

            foreach (var type in GetTypes(softwarePath, nameFilter))
            {
                targets.Add((GetTypePath(type), null, type));
            }

            // Inconsistent objects cannot be exported. They used to be dropped without a
            // word, so a search over a block that was just edited and not yet compiled
            // reported "0 objects searched" instead of saying why.
            var readable = new List<(string Path, PlcBlock? Block, PlcType? Type)>();

            foreach (var target in targets)
            {
                var consistent = target.Block?.IsConsistent ?? target.Type!.IsConsistent;

                if (consistent)
                {
                    readable.Add(target);
                }
                else
                {
                    unsearchable.Add($"{target.Path}: inconsistent - compile the software first.");
                }
            }

            return readable;
        }

        private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(40);

        private static void AddMatches(CodeSearchResult result, string objectPath, SourceTextResult source, Regex regex, int maxResults)
        {
            var lines = source.Text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                if (result.Items.Count >= maxResults)
                {
                    result.Truncated = true;

                    return;
                }

                if (!regex.IsMatch(lines[i]))
                {
                    continue;
                }

                result.Items.Add(new CodeMatch
                {
                    ObjectPath = objectPath,
                    Format = source.Format,
                    Line = i + 1,
                    Text = lines[i].Trim('\r').Trim()
                });
            }
        }

        #endregion
    }

    /// <summary>One line of source text that matched the search.</summary>
    public class CodeMatch
    {
        /// <summary>Root-relative path, ready to pass to GetBlockSource or GetTypeSource.</summary>
        public string ObjectPath { get; set; } = string.Empty;

        /// <summary>'document' or 'xml' - which representation was searched.</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>1-based line number within that representation.</summary>
        public int Line { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Outcome of a code search.</summary>
    public class CodeSearchResult
    {
        public string Pattern { get; set; } = string.Empty;

        public List<CodeMatch> Items { get; set; } = new List<CodeMatch>();

        /// <summary>How many objects were exported and scanned by this call.</summary>
        public int ObjectsSearched { get; set; }

        /// <summary>How many objects the filter selected that can be searched.</summary>
        public int ObjectsTotal { get; set; }

        /// <summary>Set when the time budget ran out: pass it as 'startAt' to search the rest.</summary>
        public int? NextStartAt { get; set; }

        /// <summary>Objects that could not be read, with the reason.</summary>
        public List<string> Unsearchable { get; set; } = new List<string>();

        /// <summary>True when maxResults cut the search short, so the list is incomplete.</summary>
        public bool Truncated { get; set; }
    }
}
