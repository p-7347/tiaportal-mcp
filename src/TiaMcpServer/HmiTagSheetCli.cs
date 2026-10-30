using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TiaMcpServer.Siemens;

namespace TiaMcpServer
{
    /// <summary>
    /// 'TiaMcpServer.exe hmitags &lt;file.xlsx&gt; [--hmi HMI_1/HMI_RT_1] [--portal &lt;id&gt;] [--apply] [--yes]':
    /// the HMI tag sheet tool for use without an MCP client (see Portal.HmiTagSheet.cs). Without
    /// --apply it only shows what would change; with --apply it shows the same and asks before writing.
    /// Every run writes a preview .csv next to the sheet; --apply also writes a .json backup there.
    /// </summary>
    public static class HmiTagSheetCli
    {
        public const string Command = "hmitags";

        private const int ListedChanges = 60;

        public static int Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string? file = null, hmiArg = null;
            int? portalId = null;
            bool apply = false, yes = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--hmi": hmiArg = i + 1 < args.Length ? args[++i] : null; break;
                    case "--portal": portalId = i + 1 < args.Length && int.TryParse(args[i + 1], out var p) ? p : (int?)null; i++; break;
                    case "--apply": apply = true; break;
                    case "--yes": yes = true; break;
                    case "--help": case "-h": case "/?": PrintUsage(); return 0;
                    default:
                        if (file == null && !args[i].StartsWith("--", StringComparison.Ordinal)) file = args[i];
                        else { Console.Error.WriteLine($"Unknown argument '{args[i]}'."); PrintUsage(); return 2; }
                        break;
                }
            }

            if (file == null)
            {
                PrintUsage();
                return 2;
            }

            file = Path.GetFullPath(file.Trim('"'));
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"File not found: {file}");
                return 2;
            }

            var portal = new Portal();

            try
            {
                Console.WriteLine($"Reading {file} ...");
                var sheet = HmiTagSheet.Read(file);
                Console.WriteLine($"  {sheet.Rows.Count} tag row(s).");

                Console.WriteLine("Connecting to TIA Portal (accept the Openness access prompt in TIA Portal if it appears) ...");
                var attached = portal.AttachPortal(portalId);
                var state = portal.GetState(attached);
                Console.WriteLine($"  TIA Portal {attached}, project '{state.Project}'.");

                var hmiPath = ChooseHmi(portal.ListUnifiedHmiPaths(), hmiArg);
                if (hmiPath == null)
                {
                    return 2;
                }

                Console.WriteLine($"Comparing with {hmiPath} ...");
                var plan = portal.PlanHmiTagSheet(hmiPath, sheet, (done, total) =>
                {
                    if (done % 100 == 0 || done == total) Console.Write($"\r  {done}/{total}");
                });
                Console.WriteLine();

                PrintPlan(plan);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var preview = SiblingPath(file, $"preview_{stamp}.csv");
                WritePreviewCsv(plan, preview);
                Console.WriteLine($"Full list: {preview}");

                if (plan.Problems.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Fix the problems above in the sheet and run again. Nothing was changed.");
                    return 1;
                }

                if (plan.Items.Count == 0)
                {
                    Console.WriteLine("Nothing to change.");
                    return 0;
                }

                if (!apply)
                {
                    Console.WriteLine("Preview only. Run again with --apply to write these changes.");
                    return 0;
                }

                if (!yes && !Confirm($"Write {plan.Items.Count} tag(s) to {hmiPath}? Type Y and Enter: "))
                {
                    Console.WriteLine("Cancelled. Nothing was changed.");
                    return 0;
                }

                var result = portal.ApplyHmiTagSheet(plan, SiblingPath(file, $"backup_{stamp}.json"));
                Console.WriteLine();
                Console.WriteLine($"Done: {result.Created} created, {result.Changed} changed.");
                Console.WriteLine($"Previous values: {result.Backup}");
                Console.WriteLine("The changes are not saved yet: save the project in TIA Portal (Ctrl+S). Edit > Undo there takes them all back.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Failed: {Operation.DescribeChain(ex)}");
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Edit WinCC Unified HMI tags with Excel, without deleting them:");
            Console.WriteLine("  1. TIA Portal: HMI_x > HMI tags > Show all tags > Export (.xlsx).");
            Console.WriteLine("  2. Edit or add rows in the 'Hmi Tags' sheet and save.");
            Console.WriteLine("  3. TiaMcpServer.exe hmitags <file.xlsx> [--hmi HMI_1/HMI_RT_1] [--portal <id>] [--apply] [--yes]");
            Console.WriteLine();
            Console.WriteLine("Without --apply it only shows the changes. Existing tags are changed in place (bindings stay),");
            Console.WriteLine("new names are created, nothing is deleted or renamed. Written columns:");
            Console.WriteLine("  " + string.Join(", ", Portal.HmiTagSheetColumns));
            Console.WriteLine("An empty cell keeps the value, except comments (empty clears).");
        }

        private static string? ChooseHmi(List<string> hmis, string? wanted)
        {
            if (hmis.Count == 0)
            {
                Console.Error.WriteLine("The project has no WinCC Unified HMI.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(wanted))
            {
                var match = hmis.Where(h => h.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                        || h.Split('/').Any(part => part.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (match.Count == 1)
                {
                    return match[0];
                }

                Console.Error.WriteLine($"No single HMI matches '{wanted}'. HMIs: {string.Join(", ", hmis)}");
                return null;
            }

            if (hmis.Count == 1)
            {
                return hmis[0];
            }

            Console.WriteLine("Which HMI?");
            for (var i = 0; i < hmis.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {hmis[i]}");
            }

            Console.Write("Number: ");
            return int.TryParse(Console.ReadLine(), out var n) && n >= 1 && n <= hmis.Count ? hmis[n - 1] : null;
        }

        private static void PrintPlan(Portal.HmiTagSheetPlan plan)
        {
            var created = plan.Items.Where(i => i.Kind == "new").ToList();
            var changed = plan.Items.Where(i => i.Kind == "change").ToList();

            Console.WriteLine();
            Console.WriteLine($"New: {created.Count}   Changed: {changed.Count}   Unchanged: {plan.Unchanged}   Not in the sheet (left as they are): {plan.NotInSheet}");
            Console.WriteLine($"Columns compared: {string.Join(", ", plan.UsedColumns)}");

            var listed = 0;
            foreach (var item in plan.Items)
            {
                if (listed++ >= ListedChanges)
                {
                    Console.WriteLine($"  ... and {plan.Items.Count - ListedChanges} more (see the .csv)");
                    break;
                }

                if (item.Kind == "new")
                {
                    Console.WriteLine($"  + {item.Name}  [{item.TagTable}]  " + string.Join(", ", item.Changes.Select(c => $"{c.Column}={c.After}")));
                }
                else
                {
                    Console.WriteLine($"  ~ {item.Name}  " + string.Join(", ", item.Changes.Select(c => $"{c.Column}: {Show(c.Before)} -> {Show(c.After)}")));
                }
            }

            foreach (var note in plan.Notes.Take(20))
            {
                Console.WriteLine($"  note: {note}");
            }

            if (plan.Notes.Count > 20)
            {
                Console.WriteLine($"  ... {plan.Notes.Count - 20} more notes");
            }

            if (plan.Problems.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"PROBLEMS ({plan.Problems.Count}):");
                foreach (var problem in plan.Problems.Take(50))
                {
                    Console.WriteLine($"  ! {problem}");
                }
            }
        }

        private static string Show(string value) => value.Length == 0 ? "(empty)" : value;

        private static void WritePreviewCsv(Portal.HmiTagSheetPlan plan, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Row;Name;Action;Column;Before;After");

            foreach (var item in plan.Items)
            {
                foreach (var change in item.Changes.DefaultIfEmpty(new Portal.HmiTagSheetChange()))
                {
                    sb.AppendLine(string.Join(";", item.Line, Csv(item.Name), item.Kind, Csv(change.Column), Csv(change.Before), Csv(change.After)));
                }
            }

            foreach (var note in plan.Notes)
            {
                sb.AppendLine($";;note;;;{Csv(note)}");
            }

            foreach (var problem in plan.Problems)
            {
                sb.AppendLine($";;PROBLEM;;;{Csv(problem)}");
            }

            // UTF-8 with BOM so Excel shows Korean comments correctly.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string value) =>
            value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

        private static string SiblingPath(string file, string suffix) =>
            Path.Combine(Path.GetDirectoryName(file) ?? ".", $"{Path.GetFileNameWithoutExtension(file)}_{suffix}");

        private static bool Confirm(string question)
        {
            Console.Write(question);
            return string.Equals(Console.ReadLine()?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);
        }
    }
}
