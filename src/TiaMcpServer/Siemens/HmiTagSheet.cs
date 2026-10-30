using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Reads the "Hmi Tags" sheet of the .xlsx file that the TIA Portal HMI tag editor exports
    /// ("Show all tags" > Export). Only the OpenXML package itself is read - no Excel and no
    /// extra library - so the file may also have been saved again by Excel.
    /// </summary>
    public sealed class HmiTagSheet
    {
        public const string SheetName = "Hmi Tags";

        /// <summary>The value TIA Portal writes for an empty cell.</summary>
        public const string NoValue = "<No Value>";

        public List<string> Headers { get; } = new List<string>();

        public List<HmiTagSheetRow> Rows { get; } = new List<HmiTagSheetRow>();

        public static HmiTagSheet Read(string path)
        {
            using var zip = ZipFile.OpenRead(path);

            var workbook = LoadXml(zip, "xl/workbook.xml")
                ?? throw new InvalidDataException($"'{path}' is not an Excel .xlsx file (no xl/workbook.xml).");

            var sheets = workbook.Descendants().Where(e => e.Name.LocalName == "sheet").ToList();
            var sheet = sheets.FirstOrDefault(s => string.Equals((string?)s.Attribute("name"), SheetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(
                    $"'{path}' has no sheet '{SheetName}' (sheets: {string.Join(", ", sheets.Select(s => (string?)s.Attribute("name")))}). " +
                    "Export the tags from TIA Portal: HMI tags > Show all tags > Export.");

            var relationId = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
            var relations = LoadXml(zip, "xl/_rels/workbook.xml.rels");
            var target = relations?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Relationship" && (string?)e.Attribute("Id") == relationId)
                ?.Attribute("Target")?.Value
                ?? throw new InvalidDataException($"'{path}': the sheet '{SheetName}' has no worksheet part.");

            var sheetPath = target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target;
            var worksheet = LoadXml(zip, sheetPath)
                ?? throw new InvalidDataException($"'{path}': worksheet part '{sheetPath}' is missing.");

            var shared = (LoadXml(zip, "xl/sharedStrings.xml")?.Root?.Elements() ?? Enumerable.Empty<XElement>())
                .Where(e => e.Name.LocalName == "si")
                .Select(TextOf)
                .ToList();

            var result = new HmiTagSheet();
            var first = true;

            foreach (var row in worksheet.Descendants().Where(e => e.Name.LocalName == "row"))
            {
                var cells = new SortedDictionary<int, string>();
                var nextColumn = 0;

                foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
                {
                    var column = ColumnIndex((string?)cell.Attribute("r")) ?? nextColumn;
                    nextColumn = column + 1;
                    cells[column] = CellValue(cell, shared);
                }

                if (first)
                {
                    var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
                    for (var i = 0; i < width; i++)
                    {
                        result.Headers.Add(cells.TryGetValue(i, out var header) ? header.Trim() : string.Empty);
                    }

                    if (!result.Headers.Contains("Name", StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"'{path}': the first row of '{SheetName}' has no 'Name' column.");
                    }

                    first = false;
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in cells)
                {
                    if (pair.Key < result.Headers.Count && result.Headers[pair.Key].Length > 0)
                    {
                        values[result.Headers[pair.Key]] = Clean(pair.Value);
                    }
                }

                var line = (int?)row.Attribute("r") ?? result.Rows.Count + 2;
                var name = values.TryGetValue("Name", out var n) ? n : string.Empty;

                if (name.Length == 0 && values.Values.All(v => v.Length == 0))
                {
                    continue;
                }

                result.Rows.Add(new HmiTagSheetRow(line, name, values));
            }

            return result;
        }

        /// <summary>Trimmed cell text, with TIA Portal's "&lt;No Value&gt;" read as empty.</summary>
        private static string Clean(string value)
        {
            var trimmed = value.Trim();
            return trimmed == NoValue ? string.Empty : trimmed;
        }

        private static XDocument? LoadXml(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName);
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        private static string CellValue(XElement cell, List<string> shared)
        {
            var type = (string?)cell.Attribute("t");

            if (type == "inlineStr")
            {
                var inline = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "is");
                return inline == null ? string.Empty : TextOf(inline);
            }

            var raw = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? string.Empty;

            if (type == "s" && int.TryParse(raw, out var index))
            {
                return index >= 0 && index < shared.Count ? shared[index] : string.Empty;
            }

            if (type == "b")
            {
                return raw == "1" ? "True" : "False";
            }

            return raw;
        }

        /// <summary>The text of a shared or inline string: all its &lt;t&gt; runs.</summary>
        private static string TextOf(XElement stringItem) =>
            string.Concat(stringItem.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));

        /// <summary>Zero-based column of a cell reference such as "AB12", or null.</summary>
        private static int? ColumnIndex(string? reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            var column = 0;
            var letters = 0;

            foreach (var ch in reference!)
            {
                if (ch >= 'A' && ch <= 'Z') column = column * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') column = column * 26 + (ch - 'a' + 1);
                else break;
                letters++;
            }

            return letters == 0 ? null : column - 1;
        }
    }

    public sealed class HmiTagSheetRow
    {
        public HmiTagSheetRow(int line, string name, Dictionary<string, string> values)
        {
            Line = line;
            Name = name;
            Values = values;
        }

        /// <summary>Row number in Excel, for messages.</summary>
        public int Line { get; }

        public string Name { get; }

        public Dictionary<string, string> Values { get; }

        public string Get(string header) => Values.TryGetValue(header, out var value) ? value : string.Empty;

        public bool Has(string header) => Values.ContainsKey(header);
    }
}
