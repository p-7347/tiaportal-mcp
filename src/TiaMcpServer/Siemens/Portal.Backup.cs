using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // Backup before a destructive change. Before DeleteBlock/DeleteType, and before
        // ImportXmlBlock/ImportXmlType replace an object of the same name, the current object is
        // exported as SimaticML into
        //   <backup root>/<yyyyMMdd>/<HHmmss_fff>_<tool>_<path>.xml
        // so the old state can be re-imported with the import tools after the project was saved
        // (the per-call transaction only helps until then). The default root is
        // %TEMP%\tiaportal-mcp-backups; '--backup-path' changes it.
        //
        // WinCC Unified screens cannot be exported in this Openness version, so the HMI dynamization
        // writes (Portal.HmiDynamization.cs) store the dynamization they change as a .json file in the
        // same place instead.
        //
        // A backup that cannot be written (e.g. TIA does not export inconsistent blocks) does not
        // stop the change: the caller gets a warning instead, so it can decide to compile first.

        public static string? BackupRoot { get; set; }

        public sealed class BackupResult
        {
            public string? Path { get; set; }
            public string? Warning { get; set; }
        }

        internal static string ResolveBackupRoot() =>
            string.IsNullOrWhiteSpace(BackupRoot)
                ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tiaportal-mcp-backups")
                : BackupRoot!;

        public BackupResult BackupBlock(string softwarePath, string blockPath, string toolName)
        {
            return Operation.Run(_logger, "Backup", PortalErrorCode.ExportFailed,
                () =>
                {
                    var block = GetBlock(softwarePath, blockPath);
                    return block == null
                        ? new BackupResult()
                        : WriteBackup(toolName, GetBlockPath(block), block.IsConsistent, file => block.Export(file, ExportOptions.None));
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath));
        }

        public BackupResult BackupType(string softwarePath, string typePath, string toolName)
        {
            return Operation.Run(_logger, "Backup", PortalErrorCode.ExportFailed,
                () =>
                {
                    var type = GetType(softwarePath, typePath);
                    return type == null
                        ? new BackupResult()
                        : WriteBackup(toolName, GetTypePath(type), type.IsConsistent, file => type.Export(file, ExportOptions.None));
                },
                ("softwarePath", softwarePath), ("typePath", typePath));
        }

        /// <summary>Backs up the block an import file would replace; nothing when no block of that name exists.</summary>
        public BackupResult BackupBlockReplacedByImport(string softwarePath, string importPath, string toolName, bool allowOlderFile = true)
        {
            var name = ReadSimaticMlName(importPath, "SW.Blocks.");
            if (name == null)
            {
                return new BackupResult();
            }

            return Operation.Run(_logger, "Backup", PortalErrorCode.ExportFailed,
                () =>
                {
                    var block = FindBlockByName(softwarePath, name);
                    if (block == null)
                    {
                        return new BackupResult();
                    }

                    EnsureImportNotOlder("block", GetBlockPath(block), block.ModifiedDate, importPath, allowOlderFile);

                    return WriteBackup(toolName, GetBlockPath(block), block.IsConsistent, file => block.Export(file, ExportOptions.None));
                },
                ("softwarePath", softwarePath), ("importPath", importPath));
        }

        /// <summary>Backs up the type an import file would replace; nothing when no type of that name exists.</summary>
        public BackupResult BackupTypeReplacedByImport(string softwarePath, string importPath, string toolName, bool allowOlderFile = true)
        {
            var name = ReadSimaticMlName(importPath, "SW.Types.");
            if (name == null)
            {
                return new BackupResult();
            }

            return Operation.Run(_logger, "Backup", PortalErrorCode.ExportFailed,
                () =>
                {
                    var type = FindTypeByName(softwarePath, name);
                    if (type == null)
                    {
                        return new BackupResult();
                    }

                    EnsureImportNotOlder("type", GetTypePath(type), type.ModifiedDate, importPath, allowOlderFile);

                    return WriteBackup(toolName, GetTypePath(type), type.IsConsistent, file => type.Export(file, ExportOptions.None));
                },
                ("softwarePath", softwarePath), ("importPath", importPath));
        }

        private static BackupResult WriteBackup(string toolName, string objectPath, bool consistent, Action<FileInfo> export, string extension = "xml")
        {
            if (!consistent)
            {
                return new BackupResult
                {
                    Warning = $"No backup of '{objectPath}': it is inconsistent and TIA Portal does not export inconsistent objects. Compile it first if the old state matters."
                };
            }

            try
            {
                var now = DateTime.Now;
                var directory = System.IO.Path.Combine(ResolveBackupRoot(), now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(directory);

                var file = System.IO.Path.Combine(directory,
                    $"{now:HHmmss_fff}_{toolName}_{FileNames.Safe(objectPath.Replace('/', '_'))}.{extension}");

                export(new FileInfo(file));

                return new BackupResult { Path = file };
            }
            catch (Exception ex)
            {
                return new BackupResult { Warning = $"No backup of '{objectPath}': {Operation.DescribeChain(ex)}" };
            }
        }

        /// <summary>
        /// Name of the first block or type in a SimaticML file: the AttributeList/Name of the first
        /// element whose name starts with the given prefix (e.g. "SW.Blocks."). Null if not found.
        /// </summary>
        private static string? ReadSimaticMlName(string importPath, string elementPrefix)
        {
            try
            {
                if (!File.Exists(importPath))
                {
                    return null;
                }

                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true };
                using (var reader = XmlReader.Create(importPath, settings))
                {
                    var depth = -1;
                    var inAttributeList = false;

                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }

                        if (depth < 0)
                        {
                            if (reader.LocalName.StartsWith(elementPrefix, StringComparison.Ordinal))
                            {
                                depth = reader.Depth;
                            }

                            continue;
                        }

                        if (reader.LocalName == "AttributeList" && reader.Depth == depth + 1)
                        {
                            inAttributeList = true;
                        }
                        else if (inAttributeList && reader.LocalName == "Name" && reader.Depth == depth + 2)
                        {
                            var name = reader.ReadElementContentAsString().Trim();
                            return name.Length > 0 ? name : null;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Not readable as SimaticML: the import itself will report why.
            }

            return null;
        }
    }
}
