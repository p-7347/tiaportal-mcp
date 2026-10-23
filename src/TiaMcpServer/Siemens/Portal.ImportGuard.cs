using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Import guard: an import that would replace a block or type changed in TIA Portal after
        // the import file was last written is refused. That is the pattern of a silently lost
        // change - an older copy of a file imported again after the block was edited in place
        // (EditBlockInterface, EditBlockNetwork, by hand), or a file copy that failed and left the
        // previous version on disk. 'allowOlderFile' imports anyway.
        //
        // TIA Portal's ModifiedDate is UTC; file times are compared in UTC too. Two seconds of
        // slack cover file systems that store whole seconds.

        private static readonly TimeSpan ImportGuardSlack = TimeSpan.FromSeconds(2);

        internal static void EnsureImportNotOlder(string kind, string objectPath, DateTime modified, string importPath, bool allowOlderFile)
        {
            if (allowOlderFile || !File.Exists(importPath))
            {
                return;
            }

            var modifiedUtc = modified.Kind == DateTimeKind.Local ? modified.ToUniversalTime() : DateTime.SpecifyKind(modified, DateTimeKind.Utc);
            var fileUtc = File.GetLastWriteTimeUtc(importPath);

            if (modifiedUtc > fileUtc + ImportGuardSlack)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"Not imported: {kind} '{objectPath}' was changed in TIA Portal at {modifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}, " +
                    $"after '{importPath}' was last written ({fileUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}). Importing it would undo that change, " +
                    "e.g. when an older copy of the file is imported again. Export the current version and edit that, " +
                    "or pass 'allowOlderFile' true to replace it anyway.");
            }
        }

        private PlcBlock? FindBlockByName(string softwarePath, string name)
        {
            var root = GetPlcBlockGroupByPath(softwarePath, string.Empty);
            if (root == null)
            {
                return null;
            }

            var list = new List<PlcBlock>();
            GetBlocksRecursive(root, list, "^" + Regex.Escape(name) + "$");
            return list.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private PlcType? FindTypeByName(string softwarePath, string name)
        {
            var root = GetPlcTypeGroupByPath(softwarePath, string.Empty);
            if (root == null)
            {
                return null;
            }

            var list = new List<PlcType>();
            GetTypesRecursive(root, list, "^" + Regex.Escape(name) + "$");
            return list.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static readonly Regex SourceObjectHeader = new Regex(
            @"^\s*(FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK|TYPE)\s+(?:""([^""]+)""|([A-Za-z_][A-Za-z0-9_]*))",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Names of the blocks or types a source file declares, e.g. FUNCTION_BLOCK "FB_Pitch".</summary>
        internal static List<string> SourceObjectNames(string file, bool isType)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception)
            {
                return new List<string>();
            }

            return SourceObjectHeader.Matches(text)
                .Cast<Match>()
                .Where(m => isType == m.Groups[1].Value.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Before a source file is generated: refuse when it would replace an object changed after
        /// the file, then back up every object it will replace.
        /// </summary>
        private void GuardAndBackUpSourceImport(string softwarePath, string file, bool isType, bool allowOlderFile, string toolName, ImportedSourcesResult result)
        {
            var existing = new List<(string Path, DateTime Modified, bool Consistent, Action<FileInfo> Export)>();

            foreach (var name in SourceObjectNames(file, isType))
            {
                if (isType)
                {
                    var type = FindTypeByName(softwarePath, name);
                    if (type != null)
                    {
                        existing.Add((GetTypePath(type), type.ModifiedDate, type.IsConsistent, f => type.Export(f, ExportOptions.None)));
                    }
                }
                else
                {
                    var block = FindBlockByName(softwarePath, name);
                    if (block != null)
                    {
                        existing.Add((GetBlockPath(block), block.ModifiedDate, block.IsConsistent, f => block.Export(f, ExportOptions.None)));
                    }
                }
            }

            // Check all first, so a refused file leaves no half-written backups behind.
            foreach (var item in existing)
            {
                EnsureImportNotOlder(isType ? "type" : "block", item.Path, item.Modified, file, allowOlderFile);
            }

            foreach (var item in existing)
            {
                var backup = WriteBackup(toolName, item.Path, item.Consistent, item.Export);
                if (backup.Path != null)
                {
                    result.Backups.Add(backup.Path);
                }

                if (backup.Warning != null)
                {
                    result.BackupWarnings.Add(backup.Warning);
                }
            }
        }
    }
}
