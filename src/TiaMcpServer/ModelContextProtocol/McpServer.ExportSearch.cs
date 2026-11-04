using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // Search an export folder offline, e.g. an older export when the project is not open.
        // Pure file reading like CompareExports - no lock, no connection.

        [McpServerTool(Name = "SearchExports", Title = "Search export folder", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("Search the files of an export folder (SimaticML XML, SCL, documents, ...) for a tag, block, member or comment text, without TIA Portal. In XML a symbolic access is matched as one path, so '\"G2U1Assy\".PLC_Ins.Pitch' finds every use of that member, and each match names its network. Results reflect the export, not later changes in the project")]
        public static ResponseSearchExports SearchExports(
            [Description("path: the export folder to search (recursively)")] string path,
            [Description("text: what to find, e.g. '\"G2U1Assy\".PLC_Ins.Pitch', 'Step70' or a comment word. Case-insensitive; quotes and '#' are ignored")] string text,
            [Description("useRegex: treat 'text' as a regular expression. Default false")] bool useRegex = false,
            [Description("regexName: optional regular expression on the relative file path, e.g. 'G2U1' to search only those blocks; empty searches every file")] string regexName = "",
            [Description("maxMatches: maximum number of matches to list (default 200); 'matchCount' is always the full count")] int maxMatches = 200)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                throw new McpException($"Folder '{path}' does not exist.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new McpException("'text' must not be empty.");
            }

            Func<string, bool> isMatch;

            if (useRegex)
            {
                Regex pattern;

                try
                {
                    pattern = new Regex(text, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException ex)
                {
                    throw new McpException($"'{text}' is not a valid regular expression: {ex.Message}");
                }

                isMatch = pattern.IsMatch;
            }
            else
            {
                var term = ExportSearch.Normalize(text);
                isMatch = s => s.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            Regex? filter = null;

            if (!string.IsNullOrEmpty(regexName))
            {
                try
                {
                    filter = new Regex(regexName, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException ex)
                {
                    throw new McpException($"regexName '{regexName}' is not a valid regular expression: {ex.Message}");
                }
            }

            var result = ExportSearch.Search(path, relative => filter == null || filter.IsMatch(relative), isMatch, Math.Max(0, maxMatches));

            return new ResponseSearchExports
            {
                Message = $"{result.MatchCount} match(es) for '{text}' in {result.FilesWithMatches} of {result.FilesSearched} file(s) under '{path}'" +
                          (result.MatchCount > result.Matches.Count ? $" (first {result.Matches.Count} listed)" : string.Empty) +
                          (result.Unreadable.Count > 0 ? $"; {result.Unreadable.Count} file(s) could not be read" : string.Empty) + ".",
                MatchCount = result.MatchCount,
                FilesSearched = result.FilesSearched,
                FilesWithMatches = result.FilesWithMatches,
                Matches = result.Matches.Select(m => new ResponseExportMatch { File = m.File, Line = m.Line, Network = m.Network, Kind = m.Kind, Text = m.Text }).ToList(),
                Unreadable = result.Unreadable.Count > 0 ? result.Unreadable : null,
                Meta = OkMeta()
            };
        }
    }
}
