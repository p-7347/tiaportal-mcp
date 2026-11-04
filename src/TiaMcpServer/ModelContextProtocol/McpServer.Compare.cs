using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // REQUESTS.md P7: diff two export folders (e.g. before and after a change, or the project
        // copy against the online copy). Pure file comparison - TIA Portal is not touched, so this
        // tool takes no lock and works without a connection.

        private static readonly HashSet<string> CompareTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xml", ".scl", ".s7dcl", ".s7res", ".db", ".udt", ".awl", ".csv", ".txt", ".json", ".yml", ".yaml", ".aml"
        };

        // SimaticML exports carry the export time and settings in <DocumentInfo>; two exports of
        // an unchanged block differ only there.
        private static readonly Regex DocumentInfoPattern =
            new Regex(@"<DocumentInfo>.*?</DocumentInfo>", RegexOptions.Singleline | RegexOptions.Compiled);

        [McpServerTool(Name = "CompareExports", Title = "Compare export folders", ReadOnly = true, Idempotent = true, OpenWorld = false),
         Description("Compare two export folders file by file (recursively, matched by relative path) and list files only in one folder and files that differ, with the changed line range of each. Ignores the export timestamp in SimaticML <DocumentInfo> and line-ending differences. Reads files only; TIA Portal is not needed")]
        public static ResponseCompareExports CompareExports(
            [Description("pathA: the first export folder, e.g. the export before a change")] string pathA,
            [Description("pathB: the second export folder, e.g. the export after a change")] string pathB,
            [Description("regexName: optional regular expression on the relative file path; empty (default) compares every file")] string regexName = "",
            [Description("maxExcerptLines: lines of each changed region to return per side. Default 5, 0 for none")] int maxExcerptLines = 5,
            [Description("maxFiles: maximum number of changed files to describe in detail; the counts always cover all. Default 200")] int maxFiles = 200)
        {
            try
            {
                foreach (var path in new[] { pathA, pathB })
                {
                    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    {
                        throw new McpException($"Folder '{path}' does not exist.");
                    }
                }

                if (maxExcerptLines < 0 || maxFiles < 0)
                {
                    throw new McpException("maxExcerptLines and maxFiles must not be negative.");
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

                var filesA = RelativeFiles(pathA, filter);
                var filesB = RelativeFiles(pathB, filter);

                var onlyInA = filesA.Keys.Where(k => !filesB.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                var onlyInB = filesB.Keys.Where(k => !filesA.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

                var changed = new List<ResponseFileDiff>();
                var changedCount = 0;
                var unchanged = 0;

                foreach (var relative in filesA.Keys.Where(filesB.ContainsKey).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    var diff = CompareFile(relative, filesA[relative], filesB[relative], maxExcerptLines);

                    if (diff == null)
                    {
                        unchanged++;
                        continue;
                    }

                    changedCount++;

                    if (changed.Count < maxFiles)
                    {
                        changed.Add(diff);
                    }
                }

                var truncated = changedCount > changed.Count;

                return new ResponseCompareExports
                {
                    Message = $"{changedCount} changed, {onlyInA.Count} only in A, {onlyInB.Count} only in B, {unchanged} unchanged" +
                              (truncated ? $" - details for the first {changed.Count} changed files only (maxFiles)" : string.Empty),
                    OnlyInA = onlyInA,
                    OnlyInB = onlyInB,
                    Changed = changed,
                    ChangedCount = changedCount,
                    UnchangedCount = unchanged,
                    Meta = new JsonObject { ["timestamp"] = DateTime.Now, ["success"] = true }
                };
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error comparing '{pathA}' with '{pathB}': {ex.Message}", ex);
            }
        }

        private static Dictionary<string, string> RelativeFiles(string root, Regex? filter)
        {
            var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return Directory
                .GetFiles(full, "*", SearchOption.AllDirectories)
                .Select(f => (Relative: f.Substring(full.Length + 1).Replace('\\', '/'), File: f))
                .Where(x => filter == null || filter.IsMatch(x.Relative))
                .ToDictionary(x => x.Relative, x => x.File, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Null when both files are equal after normalizing; otherwise where they differ.</summary>
        private static ResponseFileDiff? CompareFile(string relative, string fileA, string fileB, int maxExcerptLines)
        {
            if (!CompareTextExtensions.Contains(Path.GetExtension(fileA)))
            {
                return File.ReadAllBytes(fileA).SequenceEqual(File.ReadAllBytes(fileB))
                    ? null
                    : new ResponseFileDiff { Path = relative, Note = "Binary file differs" };
            }

            var linesA = NormalizedLines(fileA);
            var linesB = NormalizedLines(fileB);

            var prefix = 0;
            while (prefix < linesA.Length && prefix < linesB.Length && linesA[prefix] == linesB[prefix])
            {
                prefix++;
            }

            if (prefix == linesA.Length && prefix == linesB.Length)
            {
                return null;
            }

            var suffix = 0;
            while (suffix < linesA.Length - prefix && suffix < linesB.Length - prefix &&
                   linesA[linesA.Length - 1 - suffix] == linesB[linesB.Length - 1 - suffix])
            {
                suffix++;
            }

            var regionA = linesA.Skip(prefix).Take(linesA.Length - prefix - suffix).ToList();
            var regionB = linesB.Skip(prefix).Take(linesB.Length - prefix - suffix).ToList();

            // Lines of the changed region that have no counterpart on the other side; a moved line
            // counts as neither.
            var countsB = regionB.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
            var removed = 0;

            foreach (var line in regionA)
            {
                if (countsB.TryGetValue(line, out var n) && n > 0)
                {
                    countsB[line] = n - 1;
                }
                else
                {
                    removed++;
                }
            }

            var added = countsB.Values.Sum();

            return new ResponseFileDiff
            {
                Path = relative,
                LinesRemoved = removed,
                LinesAdded = added,
                RegionA = regionA.Count == 0 ? $"inserted after line {prefix}" : $"lines {prefix + 1}-{prefix + regionA.Count}",
                RegionB = regionB.Count == 0 ? $"deleted after line {prefix}" : $"lines {prefix + 1}-{prefix + regionB.Count}",
                ExcerptA = maxExcerptLines > 0 ? regionA.Take(maxExcerptLines).ToList() : null,
                ExcerptB = maxExcerptLines > 0 ? regionB.Take(maxExcerptLines).ToList() : null
            };
        }

        private static string[] NormalizedLines(string file)
        {
            var text = File.ReadAllText(file);

            if (Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                text = DocumentInfoPattern.Replace(text, string.Empty);
            }

            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(l => l.TrimEnd())
                .ToArray();
        }
    }
}
