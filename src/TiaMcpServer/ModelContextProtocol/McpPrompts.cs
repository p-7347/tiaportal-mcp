using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    [McpServerPromptType]
    public static class McpPrompts
    {
        #region Basic Connection Templates

        [McpServerPrompt(Name = "Doctor"), Description("Diagnose the TIA Portal environment (connection, versions, user group)")]
        public static string Doctor()
        {
            return @"Diagnose the TIA Portal environment.

The report shows:
- Connected: whether this server currently holds a TIA Portal connection
- Project: name and path of the open project, or 'No project open'
- Active Version: the TIA major version this server was started with (see the --tia-major-version argument)
- Installed TIA Portal versions: every installed version >= V21 with its installation path, plus a check
  that the Openness assemblies ('Engineering') and the Portal executable ('Portal') are present
- User in 'Siemens TIA Openness' user group: required for any Openness access

Start here when a connection fails: a missing user group membership, a missing installation or an
active version that is not installed are the usual causes.

Use the Doctor tool to run the diagnosis. It is read-only: it does not connect to TIA Portal,
does not open a project and does not change user group membership.";
        }

        [McpServerPrompt(Name = "Connect"), Description("Connect to a running TIA Portal instance, or start one")]
        public static string Connect(string portalId = "")
        {
            var usage = string.IsNullOrWhiteSpace(portalId)
                ? "Use the Connect tool to initiate the connection."
                : "Use the Connect tool with this parameter:" + OptionalParameter("portalId", portalId);

            return $@"Connect to TIA Portal.

This attaches to the only running TIA Portal instance, or starts a new one when none is running. With several instances running it fails and lists them; call the GetPortals tool and pass the chosen portalId.

Common parameter values:
- portalId: process id of the TIA Portal instance (see GetPortals); leave empty when at most one instance is running

{usage}";
        }

        [McpServerPrompt(Name = "GetPortals"), Description("List the running TIA Portal instances and their portalId")]
        public static string GetPortals()
        {
            return @"List the running TIA Portal instances.

The result shows, for every instance, its portalId (process id), the open project path, the mode and whether this server is attached. The top-level portalId is the instance the other tools use when no portalId is passed.

Use the GetPortals tool to choose the portalId when more than one instance is running.";
        }

        [McpServerPrompt(Name = "OpenProject"), Description("Open a local project (.apXX) or session (.alsXX) in TIA Portal")]
        public static string OpenProject(string projectPath)
        {
            return $@"Open the TIA Portal project.

Common parameter values:
- projectPath: the full path to the project file (.ap19, .ap20, .ap21, etc.) or local session file (.als19, .als20, .als21, etc.).

Use the OpenProject tool with this parameter:
- path: {projectPath}";
        }

        [McpServerPrompt(Name = "CloseProject"), Description("Close the current project or session")]
        public static string CloseProject()
        {
            return @"Close the currently open TIA Portal project.

This will close the active project and return TIA Portal to the main screen.

Use the CloseProject tool to close the current project.";
        }

        [McpServerPrompt(Name = "Disconnect"), Description("Disconnect from the attached TIA Portal instance")]
        public static string Disconnect()
        {
            return @"Disconnect from TIA Portal.

Use the Disconnect tool to remove the connection.";
        }

        #endregion

        #region Project Information Templates

        [McpServerPrompt(Name = "GetProjectTree"), Description("Show the project structure: devices, groups and PLC/HMI software")]
        public static string GetProjectTree()
        {
            return @"Retrieve the complete structure of the current TIA Portal project.

The hierarchical tree will display:
- All devices
- Device items
- Groups
- PLC/HMI software

Use the GetProjectTree tool to display the project organization and locate software paths for other operations.";
        }

        [McpServerPrompt(Name = "GetSoftwareTree"), Description("Show the tree of a PLC software: blocks, types, tags, tables, sources")]
        public static string GetSoftwareTree(string softwarePath, string sections = "")
        {
            return $@"Retrieve the complete structure of PLC software.

The hierarchical tree will display:
- Function (OB, FB, FC) and data (ArrayDB, GlobalDB, InstanceDB) blocks (organized by groups and subgroups)
- User-defined data types (organized by groups and subgroups)
- Hierarchical organization with proper tree formatting

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- sections: optional comma separated subset of 'blocks,types,tags,watch,sources' to keep the output small; leave empty for all

Use the GetSoftwareTree tool with these parameters:
- softwarePath: {softwarePath}{OptionalParameter("sections", sections)}";
        }

        #endregion

        #region Export Templates

        [McpServerPrompt(Name = "ExportXmlBlocks"), Description("Export the blocks of a PLC software as XML files")]
        public static string ExportXmlBlocks(string softwarePath, string exportPath, string regexName = "", string preservePath = "false")
        {
            return $@"Export blocks from PLC software.

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- exportPath: '${{workspacefolder}}/export/Program blocks' is a good default
- regexName: Use empty string """" for all blocks, or patterns like ""FB_.*"" for function blocks
- preservePath: Use false for flat export, true to maintain folder structure

Use the ExportXmlBlocks tool with these parameters:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ExportXmlTypes"), Description("Export the PLC data types of a PLC software as XML files")]
        public static string ExportXmlTypes(string softwarePath, string exportPath, string regexName = "", string preservePath = "false")
        {
            return $@"Export user-defined types from PLC software.

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- exportPath: '${{workspacefolder}}/export/Plc data types' is a good default
- regexName: Use empty string """" for all types, or patterns like ""Typ_.*""
- preservePath: Use false for flat export, true to maintain folder structure

Use the ExportXmlTypes tool with these parameters:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ExportBlocksAsDocuments"), Description("Export the blocks of a PLC software as documents (.s7dcl/.s7res)")]
        public static string ExportBlocksAsDocuments(string softwarePath, string exportPath, string regexName = "", string preservePath = "false")
        {
            return $@"Export blocks as SIMATIC SD documents (.s7dcl/.s7res format) from PLC software.
Requires TIA Portal V20 or newer.

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- exportPath: '${{workspacefolder}}/export/Plc' is a good default
- regexName: Use empty string """" for all blocks, or patterns like ""FB_.*""
- preservePath: Use false for flat export, true to maintain folder structure

Use the ExportBlocksAsDocuments tool with these parameters:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ExportXmlTagTable"), Description("Export one PLC tag table as XML")]
        public static string ExportXmlTagTable(string softwarePath, string tagTablePath, string exportPath, string preservePath = "false")
        {
            return $@"Export a PLC tag table from PLC software to an XML file.

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- tagTablePath: root-relative path of the tag table, e.g. 'TagGroup1/Table1'. Use the GetTagTables tool to list the tag tables of a PLC software
- exportPath: '${{workspacefolder}}/export/PLC tags' is a good default
- preservePath: Use false for a flat export, true to maintain the tag table group structure

Use the ExportXmlTagTable tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- exportPath: {exportPath}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ExportSourceBlock"), Description("Export one block as a source file (.db, .scl or .awl)")]
        public static string ExportSourceBlock(string softwarePath, string blockPath, string exportPath, string withDependencies = "false", string preservePath = "false")
        {
            return $@"Export one program block from PLC software as a TIA Portal external source file, in the format the compiler reads back.

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- blockPath: root-relative path of the block, e.g. '0_OBs/Main'. Use the ResolveObjectPath tool if you only know the name
- exportPath: '${{workspacefolder}}/export/Sources' is a good default
- withDependencies: Use true to also write called blocks, instance DBs and UDTs into the same file so it compiles on its own, false for one file per object
- preservePath: Use false to write straight into exportPath, true to mirror the project groups below 'Program blocks'

The extension follows the block: '.db' for data blocks, '.scl' for SCL blocks, '.awl' for STL blocks. LAD, FBD and GRAPH blocks have no source form; use the ExportXmlBlock tool for those.

Use the ExportSourceBlock tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- exportPath: {exportPath}
- withDependencies: {NormalizeBool(withDependencies)}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ExportSourceType"), Description("Export one PLC data type as a source file (.udt)")]
        public static string ExportSourceType(string softwarePath, string typePath, string exportPath, string withDependencies = "false", string preservePath = "false")
        {
            return $@"Export one PLC data type from PLC software as a '.udt' TIA Portal external source file, in the format the compiler reads back.

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- typePath: root-relative path of the PLC data type, e.g. 'Common/BtnTyp_X'. Use the ResolveObjectPath tool if you only know the name
- exportPath: '${{workspacefolder}}/export/Sources' is a good default
- withDependencies: Use true to also write every data type this one uses into the same file, false for one file per type
- preservePath: Use false to write straight into exportPath, true to mirror the project groups below 'PLC data types'

Use the ExportSourceType tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}
- exportPath: {exportPath}
- withDependencies: {NormalizeBool(withDependencies)}
- preservePath: {NormalizeBool(preservePath)}";
        }

        #endregion

        #region Convenience Export Templates

        [McpServerPrompt(Name = "ExportAllBlocksFlattened"), Description("Export all blocks of a PLC software as XML into one folder")]
        public static string ExportAllBlocksFlattened(string softwarePath, string exportPath)
        {
            return ExportXmlBlocks(softwarePath, exportPath, "", "false");
        }

        [McpServerPrompt(Name = "ExportAllBlocksStructured"), Description("Export all blocks of a PLC software as XML, keeping the group folders")]
        public static string ExportAllBlocksStructured(string softwarePath, string exportPath)
        {
            return ExportXmlBlocks(softwarePath, exportPath, "", "true");
        }

        [McpServerPrompt(Name = "ExportAllTypesFlattened"), Description("Export all PLC data types as XML into one folder")]
        public static string ExportAllTypesFlattened(string softwarePath, string exportPath)
        {
            return ExportXmlTypes(softwarePath, exportPath, "", "false");
        }

        [McpServerPrompt(Name = "ExportAllTypesStructured"), Description("Export all PLC data types as XML, keeping the group folders")]
        public static string ExportAllTypesStructured(string softwarePath, string exportPath)
        {
            return ExportXmlTypes(softwarePath, exportPath, "", "true");
        }

        [McpServerPrompt(Name = "ExportAllBlocksAsDocumentsFlattened"), Description("Export all blocks as documents (.s7dcl/.s7res) into one folder")]
        public static string ExportAllBlocksAsDocumentsFlattened(string softwarePath, string exportPath)
        {
            return ExportBlocksAsDocuments(softwarePath, exportPath, "", "false");
        }

        [McpServerPrompt(Name = "ExportAllBlocksAsDocumentsStructured"), Description("Export all blocks as documents (.s7dcl/.s7res), keeping the group folders")]
        public static string ExportAllBlocksAsDocumentsStructured(string softwarePath, string exportPath)
        {
            return ExportBlocksAsDocuments(softwarePath, exportPath, "", "true");
        }

        [McpServerPrompt(Name = "ExportTypesAsDocuments"), Description("Export PLC data types as documents (.s7dcl/.s7res, V21+)")]
        public static string ExportTypesAsDocuments(string softwarePath, string exportPath, string regexName = "", string preservePath = "false")
        {
            return $@"Export PLC data types (UDTs) as SIMATIC Source Documents instead of XML.
The documents are readable text that git can diff. Requires TIA Portal V21 or newer.

Common parameter values:
- softwarePath: normally something like 'PLC_1' for hardware PLC, 'PC-System_1/Software PLC_1' for PC based PLC; in a slash command write a value with spaces in double quotes, e.g. ""PC-System_1/Software PLC_1""
- exportPath: '${{workspacefolder}}/export/Plc' is a good default
- regexName: Use empty string """" for all types, or patterns like ""UDT_.*""
- preservePath: Use false for flat export, true to mirror the project tree below the 'PLC data types' folder

The response lists the files TIA Portal actually wrote, so do not assume an extension.

Use the ExportTypesAsDocuments tool with these parameters:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ExportAllTypesAsDocumentsStructured"), Description("Export all PLC data types as documents, keeping the group folders")]
        public static string ExportAllTypesAsDocumentsStructured(string softwarePath, string exportPath)
        {
            return ExportTypesAsDocuments(softwarePath, exportPath, "", "true");
        }

        #endregion

        #region Import From Documents Templates

        [McpServerPrompt(Name = "ImportFromDocuments"), Description("Import one block from documents (.s7dcl/.s7res, V20+)")]
        public static string ImportFromDocuments(string softwarePath, string importPath, string fileNameWithoutExtension, string groupPath = "", string importOption = "Override")
        {
            return $@"Import a program block from documents into PLC software (requires TIA Portal V20 or newer).

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- groupPath: optional, e.g. 'Program blocks/FBs'
- importPath: folder containing .s7dcl/.s7res files
- fileNameWithoutExtension: e.g. 'FC_DateTime'
- importOption: 'Override' (default), 'None', 'SkipInactiveCultures', 'ActivateInactiveCultures'

Note: As of 2025-09-02, importing Ladder (LAD) blocks requires the companion .s7res to contain en-US tags for all items; otherwise import may fail.

Use the ImportFromDocuments tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- fileNameWithoutExtension: {fileNameWithoutExtension}
- importOption: {importOption}";
        }

        [McpServerPrompt(Name = "ImportTypesFromDocuments"), Description("Import PLC data types from documents (.s7dcl/.s7res, V21+)")]
        public static string ImportTypesFromDocuments(string softwarePath, string importPath, string groupPath = "", string regexName = "", string importOption = "Override")
        {
            return $@"Import types (UDTs) from documents into PLC software (requires TIA Portal V21 or newer and the server started with '--allow-write').

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- groupPath: optional target group, empty for the 'PLC data types' root. A leading 'PLC data types' segment from a preservePath export is accepted
- importPath: folder holding the document files
- regexName: empty for all, or e.g. 'UDT_.*'
- importOption: 'Override' (default), 'None', 'SkipInactiveCultures', 'ActivateInactiveCultures'

Use ImportTypeFromDocuments instead when you want a single named document set.

Use the ImportTypesFromDocuments tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- regexName: {regexName}
- importOption: {importOption}";
        }

        [McpServerPrompt(Name = "ImportBlocksFromDocuments"), Description("Import blocks from documents (.s7dcl/.s7res, V20+)")]
        public static string ImportBlocksFromDocuments(string softwarePath, string importPath, string groupPath = "", string regexName = "", string importOption = "Override")
        {
            return $@"Import multiple program blocks from SIMATIC SD documents into PLC software (requires TIA Portal V20 or newer).

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- groupPath: optional target group path, empty for root
- importPath: folder containing .s7dcl/.s7res files
- regexName: empty for all, or e.g. 'FB_.*'
- importOption: 'Override' (default), 'None', 'SkipInactiveCultures', 'ActivateInactiveCultures'

Note: As of 2025-09-02, importing Ladder (LAD) blocks requires the companion .s7res to contain en-US tags for all items; otherwise import may fail.

Use the ImportBlocksFromDocuments tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- regexName: {regexName}
- importOption: {importOption}";
        }

        #endregion

        #region Import From Sources Templates

        [McpServerPrompt(Name = "ImportSources"), Description("Import a folder tree of source files (.db, .scl, .awl, .udt)")]
        public static string ImportSources(string softwarePath, string importPath, string regexName = "", string keepOnError = "false", string allowOlderFile = "false")
        {
            return $@"Import every block and PLC data type source file (.db, .awl, .scl, .udt) under a folder tree into PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- importPath: folder to walk recursively, typically the tree the ExportSources tool wrote. Each file goes into the block or PLC data type group its folder path implies; the group must already exist
- regexName: empty for all, or e.g. 'FB_.*' to select files by base name (use one file name to import a single object)
- keepOnError: Use false (default) to roll back a whole file on any error, true to keep the objects of a file that were generated successfully
- allowOlderFile: Use false (default) to refuse the import when an object it would replace was changed in TIA Portal after the file was written (that import would undo the change), true to import anyway

Use the ImportSources tool with these parameters:
- softwarePath: {softwarePath}
- importPath: {importPath}
- regexName: {regexName}
- keepOnError: {NormalizeBool(keepOnError)}
- allowOlderFile: {NormalizeBool(allowOlderFile)}";
        }

        [McpServerPrompt(Name = "ImportSourceBlock"), Description("Import one block from a source file (.db, .scl or .awl)")]
        public static string ImportSourceBlock(string softwarePath, string groupPath, string importPath, string keepOnError = "false", string allowOlderFile = "false")
        {
            return $@"Compile one block source file into a block group of PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- groupPath: block user group that receives the blocks, e.g. '0_OBs'. Empty for the source's default location; the 'Program blocks' root itself is not allowed
- importPath: full path of the .db, .scl or .awl file, typically one the ExportSourceBlock tool wrote
- keepOnError: Use false (default) to roll back the whole file on any error, true to keep the objects that were generated successfully
- allowOlderFile: Use false (default) to refuse the import when an object it would replace was changed in TIA Portal after the file was written (that import would undo the change), true to import anyway

Use the ImportSourceBlock tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- keepOnError: {NormalizeBool(keepOnError)}
- allowOlderFile: {NormalizeBool(allowOlderFile)}";
        }

        [McpServerPrompt(Name = "ImportSourceType"), Description("Import one PLC data type from a .udt source file")]
        public static string ImportSourceType(string softwarePath, string groupPath, string importPath, string keepOnError = "false", string allowOlderFile = "false")
        {
            return $@"Compile one '*.udt' source file into a PLC data type group of PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- groupPath: PLC data type user group that receives the types, e.g. 'Common'. Empty for the source's default location; the 'PLC data types' root itself is not allowed
- importPath: full path of the .udt file, typically one the ExportSourceType tool wrote
- keepOnError: Use false (default) to roll back the whole file on any error, true to keep the objects that were generated successfully
- allowOlderFile: Use false (default) to refuse the import when an object it would replace was changed in TIA Portal after the file was written (that import would undo the change), true to import anyway

Use the ImportSourceType tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- keepOnError: {NormalizeBool(keepOnError)}
- allowOlderFile: {NormalizeBool(allowOlderFile)}";
        }

        [McpServerPrompt(Name = "ImportSourceBlocks"), Description("Import all block source files of a folder into a group")]
        public static string ImportSourceBlocks(string softwarePath, string groupPath, string importPath, string regexName = "", string preservePath = "false", string keepOnError = "false", string allowOlderFile = "false")
        {
            return $@"Compile every block source file (.db, .scl, .awl) of a folder into a block group of PLC software (requires the server started with '--allow-write'). A file that fails is reported and the rest are still imported.

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- groupPath: block user group that receives the blocks, e.g. '0_OBs'. Empty for each source's default location; the 'Program blocks' root itself is not allowed
- importPath: folder with the source files, typically the one the ExportSourceBlocks tool wrote
- regexName: empty for all, or e.g. 'FB_.*' to select files by base name
- preservePath: Use false (default) to read only the files directly in importPath, true to also read its subfolders into the matching subgroups of groupPath (the groups must already exist)
- keepOnError: Use false (default) to roll back a whole file on any error, true to keep the objects of a file that were generated successfully
- allowOlderFile: Use false (default) to refuse the import when an object it would replace was changed in TIA Portal after the file was written (that import would undo the change), true to import anyway

Use the ImportSources tool instead to import blocks and PLC data types in one go.

Use the ImportSourceBlocks tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- regexName: {regexName}
- preservePath: {NormalizeBool(preservePath)}
- keepOnError: {NormalizeBool(keepOnError)}
- allowOlderFile: {NormalizeBool(allowOlderFile)}";
        }

        [McpServerPrompt(Name = "ImportSourceTypes"), Description("Import all .udt source files of a folder into a group")]
        public static string ImportSourceTypes(string softwarePath, string groupPath, string importPath, string regexName = "", string preservePath = "false", string keepOnError = "false", string allowOlderFile = "false")
        {
            return $@"Compile every '*.udt' source file of a folder into a PLC data type group of PLC software (requires the server started with '--allow-write'). A file that fails is reported and the rest are still imported.

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- groupPath: PLC data type user group that receives the types, e.g. 'Common'. Empty for each source's default location; the 'PLC data types' root itself is not allowed
- importPath: folder with the .udt files, typically the one the ExportSourceTypes tool wrote
- regexName: empty for all, or e.g. 'UDT_.*' to select files by base name
- preservePath: Use false (default) to read only the files directly in importPath, true to also read its subfolders into the matching subgroups of groupPath (the groups must already exist)
- keepOnError: Use false (default) to roll back a whole file on any error, true to keep the objects of a file that were generated successfully
- allowOlderFile: Use false (default) to refuse the import when an object it would replace was changed in TIA Portal after the file was written (that import would undo the change), true to import anyway

Use the ImportSources tool instead to import blocks and PLC data types in one go.

Use the ImportSourceTypes tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- regexName: {regexName}
- preservePath: {NormalizeBool(preservePath)}
- keepOnError: {NormalizeBool(keepOnError)}
- allowOlderFile: {NormalizeBool(allowOlderFile)}";
        }

        [McpServerPrompt(Name = "ImportExternalSource"), Description("Compile an external source already in the project into blocks")]
        public static string ImportExternalSource(string softwarePath, string sourcePath, string targetGroupPath = "", string keepOnError = "false")
        {
            return $@"Compile/import an external source that is already registered in PLC software into program blocks and PLC data types (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: e.g. 'PLC_1' for hardware PLC
- sourcePath: root-relative path of the external source, e.g. 'SourceGroup1/Source_1'. Use the GetExternalSources tool to list them
- targetGroupPath: optional block user group that receives the blocks, empty for the source's default location. The 'Program blocks' root itself is not allowed
- keepOnError: Use false (default) to generate nothing when any object fails, true to keep the objects that were generated successfully

Use the ImportSourceBlock or ImportSourceType tool instead to import a source file from disk, or ImportSources for a whole folder tree.

Use the ImportExternalSource tool with these parameters:
- softwarePath: {softwarePath}
- sourcePath: {sourcePath}
- targetGroupPath: {targetGroupPath}
- keepOnError: {NormalizeBool(keepOnError)}";
        }

        #endregion

        #region Convenience Import Templates

        [McpServerPrompt(Name = "ImportAllSources"), Description("Import all source files of a folder tree")]
        public static string ImportAllSources(string softwarePath, string importPath)
        {
            return ImportSources(softwarePath, importPath, "", "false");
        }

        [McpServerPrompt(Name = "ImportAllSourcesKeepOnError"), Description("Import all sources; keep the objects generated successfully on error")]
        public static string ImportAllSourcesKeepOnError(string softwarePath, string importPath)
        {
            return ImportSources(softwarePath, importPath, "", "true");
        }

        #endregion

        #region Project and Session Templates

        [McpServerPrompt(Name = "GetState"), Description("Show connection, open project, write mode and any in-progress call")]
        public static string GetState(string portalId = "")
        {
            var usage = string.IsNullOrWhiteSpace(portalId)
                ? "Use the GetState tool (it takes no required parameters)."
                : "Use the GetState tool with this parameter:" + OptionalParameter("portalId", portalId);

            return $@"Get the state of the TIA Portal MCP server.

Also reports 'progress' while a long-running call (e.g. FindInCode) is in flight on this instance - call this from a second request to check whether such a call looks stuck or just slow, without waiting for it to finish. Unlike other tools, GetState never waits for a busy instance.

Common parameter values:
- portalId: process id of the TIA Portal instance to report on (see GetPortals); leave empty when at most one instance is attached

{usage}";
        }

        [McpServerPrompt(Name = "GetProject"), Description("Show the open project or session")]
        public static string GetProject()
        {
            return $@"Get open local project/session.

Use the GetProject tool (it takes no parameters).";
        }

        [McpServerPrompt(Name = "SaveProject"), Description("Save the open project or session")]
        public static string SaveProject()
        {
            return $@"Save the current TIA Portal local project/session.

Use the SaveProject tool (it takes no parameters).";
        }

        [McpServerPrompt(Name = "SaveAsProject"), Description("Save the open project under a new name")]
        public static string SaveAsProject(string newProjectPath)
        {
            return $@"Save current TIA Portal project/session with a new name.

Common parameter values:
- newProjectPath: defines the new path where to save the project

Use the SaveAsProject tool with these parameters:
- newProjectPath: {newProjectPath}";
        }

        [McpServerPrompt(Name = "OpenTiaProject"), Description("Connect and open a project in one step; returns the software paths")]
        public static string OpenTiaProject(string projectPath, string portalId = "")
        {
            return $@"Connect to TIA Portal if not already connected, open the given project or session, and return the device and PLC software paths the other tools need. Replaces the Connect then OpenProject then GetProjectTree sequence.

Common parameter values:
- projectPath: full path of the .apXX project or .alsXX session file on the machine running this server
- portalId: process id of the TIA Portal instance to open it in (see GetPortals); leave empty when at most one instance is running

Use the OpenTiaProject tool with these parameters:
- path: {projectPath}{OptionalParameter("portalId", portalId)}";
        }

        [McpServerPrompt(Name = "PreviewImport"), Description("Preview what importing a folder would create or overwrite")]
        public static string PreviewImport(string softwarePath, string importPath, string kind, string groupPath = "")
        {
            return $@"Report what importing a directory would create, overwrite or collide with, without touching the project. Checks each file against the objects already in the PLC, including the rule that a PLC data type name must be unique across the whole PLC - importing an existing type name into a different group fails even with importOption 'Override'.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- importPath: directory holding the files to import (.s7dcl source documents or .xml)
- kind: what the files contain - 'type' for PLC data types, 'block' for program blocks
- groupPath: the group the import would target; empty means the root of that area. A leading system folder segment is accepted

Use the PreviewImport tool with these parameters:
- softwarePath: {softwarePath}
- importPath: {importPath}
- kind: {kind}
- groupPath: {groupPath}";
        }

        #endregion

        #region Device Templates

        [McpServerPrompt(Name = "GetDeviceInfo"), Description("Show details of one device")]
        public static string GetDeviceInfo(string devicePath)
        {
            return $@"Get info from a device from the current project/session.

Common parameter values:
- devicePath: defines the path in the project structure to the device

Use the GetDeviceInfo tool with these parameters:
- devicePath: {devicePath}";
        }

        [McpServerPrompt(Name = "GetDeviceItemInfo"), Description("Show details of one device item")]
        public static string GetDeviceItemInfo(string deviceItemPath)
        {
            return $@"Get info from a device item from the current project/session.

Common parameter values:
- deviceItemPath: defines the path in the project structure to the device item

Use the GetDeviceItemInfo tool with these parameters:
- deviceItemPath: {deviceItemPath}";
        }

        [McpServerPrompt(Name = "GetDevices"), Description("List the devices of the project or session")]
        public static string GetDevices()
        {
            return $@"Get a list of all devices in the project/session.

Use the GetDevices tool (it takes no parameters).";
        }

        #endregion

        #region Software Templates

        [McpServerPrompt(Name = "GetSoftwareInfo"), Description("Show details of one PLC software")]
        public static string GetSoftwareInfo(string softwarePath)
        {
            return $@"Get PLC software info.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software

Use the GetSoftwareInfo tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "CompileSoftware"), Description("Compile a PLC software and list every compiler message")]
        public static string CompileSoftware(string softwarePath, string password = "")
        {
            return $@"Compile the software and report every compiler message with the object it belongs to, so errors can be fixed without re-reading the whole PLC. Warnings are reported as a successful compile with detail; only errors fail the call.

Common parameter values:
|- softwarePath: defines the path in the project structure to the software
- password: the password to access administration, default: no password

Use the CompileSoftware tool with these parameters:
- softwarePath: {softwarePath}
- password: {password}";
        }

        [McpServerPrompt(Name = "CompileCodeBlock"), Description("Compile one code block and list its compiler messages")]
        public static string CompileCodeBlock(string softwarePath, string blockPath, string password = "")
        {
            return $@"Compile one code block (OB, FC, FB or DB) instead of the whole PLC, for example right after importing it, and report its compiler messages with the object each belongs to. Warnings are reported as a successful compile with detail; only errors fail the call.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: path of the block below 'Program blocks', e.g. '009_Servo/FB_PitchHome_Inch'
- password: the password to access administration, default: no password

Use the CompileCodeBlock tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}{OptionalParameter("password", password)}";
        }

        [McpServerPrompt(Name = "ResolveObjectPath"), Description("Turn a partial object name into the full path other tools need")]
        public static string ResolveObjectPath(string softwarePath, string name, string kind = "any")
        {
            return $@"Turn a bare or partial object name into the root-relative path the other tools need, searching program blocks, data types, tags, tag tables, watch tables and external sources. Exact matches win; substring matches are only reported when nothing matches exactly.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- name: the object name to look for, e.g. 'FC_Block_1'. A full path may be passed; only its last segment is matched
- kind: restrict the search to 'block', 'type', 'tag', 'tagTable', 'watchTable' or 'source'. Default 'any' searches all of them

Use the ResolveObjectPath tool with these parameters:
- softwarePath: {softwarePath}
- name: {name}
- kind: {kind}";
        }

        [McpServerPrompt(Name = "FindInCode"), Description("Search block and type source text with a regular expression")]
        public static string FindInCode(string softwarePath, string pattern, string nameFilter = "", string maxResults = "200")
        {
            return $@"Search the actual source text of program blocks and PLC data types with a regular expression - every other filter in this server matches object names only. Returns the object path, line number and the matching line. Each call exports the candidate objects behind the scenes, so narrow a large PLC with 'nameFilter'.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- pattern: regular expression matched against each line, case-insensitive. Plain text works too
- nameFilter: optional regular expression on object names, to limit which objects are searched. Empty (default) searches all of them
- maxResults: stop after this many matching lines (default 200)

Use the FindInCode tool with these parameters:
- softwarePath: {softwarePath}
- pattern: {pattern}
- nameFilter: {nameFilter}
- maxResults: {maxResults}";
        }

        [McpServerPrompt(Name = "GetPlcSummary"), Description("Summarize one PLC software: counts, languages and health")]
        public static string GetPlcSummary(string softwarePath)
        {
            return $@"Counts, programming languages and health of one PLC software in a single call - replaces listing blocks, types, tags, tag tables and watch tables separately just to see what is there. Also names the inconsistent objects (which refuse to export until compiled) and the know-how protected ones (whose content cannot be read).

Common parameter values:
- softwarePath: defines the path in the project structure to the software

Use the GetPlcSummary tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "GetCrossReferences"), Description("List cross references of a PLC software or one object")]
        public static string GetCrossReferences(string softwarePath, string objectPath = "", string objectKind = "auto", string filter = "AllObjects", string maxDepth = "1")
        {
            return $@"Get cross references for a PLC software or for one block, type, tag table, tag or block group inside it. Watch tables, force tables and external sources have no cross references.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- objectPath: optional root-relative path of a block, type, tag table, tag or block group; empty targets the whole PLC software
- objectKind: 'auto' (default), 'block', 'type', 'tagTable', 'tag' or 'blockGroup'
- filter: 'AllObjects' (default), 'ObjectsWithReferences', 'ObjectsWithoutReferences' or 'UnusedObjects'
- maxDepth: 1 = sources and their references (default), 2 = also source children, 3 = also reference locations. Keeps large results manageable

Use the GetCrossReferences tool with these parameters:
- softwarePath: {softwarePath}
- objectPath: {objectPath}
- objectKind: {objectKind}
- filter: {filter}
- maxDepth: {maxDepth}";
        }

        [McpServerPrompt(Name = "WhereUsed"), Description("Find where a tag, block, type or tag table is used")]
        public static string WhereUsed(string softwarePath, string name, string kind = "any")
        {
            return $@"Answer 'what uses this?' for a tag, block, PLC data type or tag table by name. Resolves the name, picks the right object kind and flattens the cross-reference tree to a plain list of users. Use 'GetCrossReferences' instead when the full nested result or a specific filter is needed.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- name: the object to look up, by bare name or by full root-relative path
- kind: restrict resolution to 'block', 'type', 'tag' or 'tagTable'. Default 'any' picks the single match, and reports the candidates when the name is ambiguous

Use the WhereUsed tool with these parameters:
- softwarePath: {softwarePath}
- name: {name}
- kind: {kind}";
        }

        #endregion

        #region Block Templates

        [McpServerPrompt(Name = "GetBlockInfo"), Description("Show details of one block")]
        public static string GetBlockInfo(string softwarePath, string blockPath)
        {
            return $@"Get a block info, which is located in the software.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: defines the path in the project structure to the block

Use the GetBlockInfo tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}";
        }

        [McpServerPrompt(Name = "GetBlocks"), Description("List the blocks of a PLC software")]
        public static string GetBlocks(string softwarePath, string regexName = "")
        {
            return $@"Get a list of blocks, which are located in the software.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- regexName: defines the name or regular expression to find the block. Use empty string (default) to find all

Use the GetBlocks tool with these parameters:
- softwarePath: {softwarePath}
- regexName: {regexName}";
        }

        [McpServerPrompt(Name = "GetBlocksWithHierarchy"), Description("List all blocks of a PLC software with their group hierarchy")]
        public static string GetBlocksWithHierarchy(string softwarePath)
        {
            return $@"Get a list of all blocks with their group hierarchy from the software.

Common parameter values:
- softwarePath: defines the path in the project structure to the software

Use the GetBlocksWithHierarchy tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "ExportXmlBlock"), Description("Export one block as XML")]
        public static string ExportXmlBlock(string softwarePath, string blockPath, string exportPath, string preservePath = "false")
        {
            return $@"Export a block from the software to file.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: full path to the block in the project structure, e.g. 'Group/Subgroup/Name' (single names are ambiguous)
- exportPath: defines the path where to export the block
- preservePath: preserves the path/structure of the PLC software

Use the ExportXmlBlock tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- exportPath: {exportPath}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ImportXmlBlock"), Description("Import one block from an XML file")]
        public static string ImportXmlBlock(string softwarePath, string groupPath, string importPath, string allowOlderFile = "false")
        {
            return $@"Import a block file to the software.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- groupPath: defines the path in the project structure to the group, where to import the block
- importPath: defines the path of the xml file from where to import the block
- allowOlderFile: Use false (default) to refuse the import when an object it would replace was changed in TIA Portal after the file was written (that import would undo the change), true to import anyway

Use the ImportXmlBlock tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- allowOlderFile: {NormalizeBool(allowOlderFile)}";
        }

        [McpServerPrompt(Name = "GetBlockInterface"), Description("List the members of a data block with type and attributes")]
        public static string GetBlockInterface(string softwarePath, string blockPath)
        {
            return $@"List the members of a data block with their data type and every attribute TIA Portal reports. Needs no export and works on inconsistent blocks. Data blocks only: Openness offers no interface accessor for FB, FC or OB, whose declarations come from 'GetBlockSource' instead.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: root-relative path of the data block, e.g. '1_Tests/DB_Block_1'

Use the GetBlockInterface tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}";
        }

        [McpServerPrompt(Name = "CreateBlockGroup"), Description("Create a block group")]
        public static string CreateBlockGroup(string softwarePath, string parentGroupPath, string name)
        {
            return $@"Create a group below the Program blocks root of the PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- parentGroupPath: root-relative path of the parent group; empty creates directly below Program blocks
- name: name of the new group, without a slash

Use the CreateBlockGroup tool with these parameters:
- softwarePath: {softwarePath}
- parentGroupPath: {parentGroupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "DeleteBlockGroup"), Description("Delete a block group and everything inside it")]
        public static string DeleteBlockGroup(string softwarePath, string groupPath)
        {
            return $@"Delete a block group and everything inside it. The Program blocks system group itself cannot be deleted (requires the server started with '--allow-write').
- softwarePath: defines the path in the project structure to the software

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group to delete, e.g. Common/CarrierRegister

Use the DeleteBlockGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}";
        }

        [McpServerPrompt(Name = "EditBlockNetwork"), Description("Replace, insert or delete one network of a program block")]
        public static string EditBlockNetwork(string softwarePath, string blockPath, string action, string networkIndex, string networkFile = "", string sourceNetworkIndex = "", string previewFile = "", string sclFile = "", string title = "")
        {
            return $@"Change one network of a program block: replace or insert a network from an XML file or from SCL text (sclFile), or delete one. LAD/FBD parts are put into flow order and the block is backed up first. Compile the block afterwards.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: root-relative path of the block
- action: 'replace', 'insert' or 'delete'
- networkIndex: 0-based (network 1 in TIA is 0)

Use the EditBlockNetwork tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- action: {action}
- networkIndex: {networkIndex}{OptionalParameter("networkFile", networkFile)}{OptionalParameter("sourceNetworkIndex", sourceNetworkIndex)}{OptionalParameter("previewFile", previewFile)}{OptionalParameter("sclFile", sclFile)}{OptionalParameter("title", title)}";
        }

        [McpServerPrompt(Name = "EditBlockInterface"), Description("Add, change or delete one member of a block interface")]
        public static string EditBlockInterface(string softwarePath, string blockPath, string action, string section, string memberPath, string dataType = "", string startValue = "", string comment = "", string afterMember = "", string previewFile = "", string moreMembers = "", string compile = "false")
        {
            return $@"Change one variable in the interface of a program block (FB, FC, OB or global DB) without editing the whole block XML. The block is backed up first. Compile the block afterwards.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: root-relative path of the block
- action: 'add', 'update' or 'delete'
- section: Input, Output, InOut, Static, Temp or Constant
- memberPath: '.'-separated into Structs, e.g. 'aux.bNew'
- moreMembers: optional JSON array of further member edits done in the same import, one object each with action, section, memberPath, dataType, startValue, comment, afterMember; an item without action or section uses the call's
- compile: true to compile the block right after the change and return the compiler result (default false)

Use the EditBlockInterface tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- action: {action}
- section: {section}
- memberPath: {memberPath}{OptionalParameter("dataType", dataType)}{OptionalParameter("startValue", startValue)}{OptionalParameter("comment", comment)}{OptionalParameter("afterMember", afterMember)}{OptionalParameter("previewFile", previewFile)}{OptionalParameter("moreMembers", moreMembers)}
- compile: {NormalizeBool(compile)}";
        }

        [McpServerPrompt(Name = "EditTypeInterface"), Description("Add, change or delete one member of a PLC data type (UDT)")]
        public static string EditTypeInterface(string softwarePath, string typePath, string action, string memberPath, string dataType = "", string startValue = "", string comment = "", string afterMember = "", string previewFile = "", string moreMembers = "", string compile = "false")
        {
            return $@"Change one member of a PLC data type (UDT) without editing the whole type XML. The type is backed up first. Compile the PLC software afterwards so the blocks using the type are updated.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- typePath: root-relative path of the type in 'PLC data types'
- action: 'add', 'update' or 'delete'
- memberPath: '.'-separated into Structs, e.g. 'Speed.Creep'
- moreMembers: optional JSON array of further member edits done in the same import, one object each with action, memberPath, dataType, startValue, comment, afterMember; an item without action uses the call's
- compile: true to compile the type right after the change and return the compiler result (default false)

Use the EditTypeInterface tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}
- action: {action}
- memberPath: {memberPath}{OptionalParameter("dataType", dataType)}{OptionalParameter("startValue", startValue)}{OptionalParameter("comment", comment)}{OptionalParameter("afterMember", afterMember)}{OptionalParameter("previewFile", previewFile)}{OptionalParameter("moreMembers", moreMembers)}
- compile: {NormalizeBool(compile)}";
        }

        [McpServerPrompt(Name = "GetHmiTagUsage"), Description("Find where a PLC or HMI tag is used on a WinCC Unified HMI")]
        public static string GetHmiTagUsage(string softwarePath, string tag, string useRegex = "", string screenRegex = "", string includeScripts = "", string includeAlarms = "", string refreshIndex = "")
        {
            return $@"Find the HMI tags connected to a PLC tag (or an HMI tag by name) and every screen item, faceplate binding, script and alarm that uses them.

Common parameter values:
- softwarePath: the HMI device item, e.g. 'HMI_1/HMI_RT_1'
- tag: e.g. 'SM_HMIData.SwLamp_S[10].Origin_Sw', or a DB name to find all its members
- If the reply says the time budget was used up, call again with the same arguments; each call continues where the last stopped

Use the GetHmiTagUsage tool with these parameters:
- softwarePath: {softwarePath}
- tag: {tag}{OptionalParameter("useRegex", useRegex)}{OptionalParameter("screenRegex", screenRegex)}{OptionalParameter("includeScripts", includeScripts)}{OptionalParameter("includeAlarms", includeAlarms)}{OptionalParameter("refreshIndex", refreshIndex)}";
        }

        [McpServerPrompt(Name = "GetProjectStatus"), Description("Check for unsaved changes and read the project history")]
        public static string GetProjectStatus(string historyCount = "")
        {
            return $@"Check whether the open project has unsaved changes and list its most recent history entries, last modified time and user.

Use the GetProjectStatus tool with these parameters:{OptionalParameter("historyCount", historyCount)}";
        }

        [McpServerPrompt(Name = "GetChangeLog"), Description("List what this server changed in the project on a day")]
        public static string GetChangeLog(string day = "", string toolRegex = "")
        {
            return $@"List the write tool calls this server made on one day (time, tool, arguments, ok or error, reply, backup file), e.g. to write a change summary.

Use the GetChangeLog tool with these parameters:{OptionalParameter("day", day)}{OptionalParameter("toolRegex", toolRegex)}";
        }

        // Not a tool prompt: the routine that worked for changing blocks live (offline, fingerprint,
        // smallest edit tool, compile one block, compare). Clients that only read tool lists
        // get the short form from the server instructions in Program.cs.
        [McpServerPrompt(Name = "ModifyBlockWorkflow"), Description("Step-by-step routine for changing a PLC block safely")]
        public static string ModifyBlockWorkflow(string softwarePath, string blockPath, string change)
        {
            return $@"Change the PLC block '{blockPath}' in '{softwarePath}': {change}

Follow this routine, one block at a time:
1. Offline: 'GetOnlineState' with path '{softwarePath}'; if it is online, 'GoOffline'. Compile and import fail while online.
2. Read: 'GetBlockSource' for the block, 'GetNetworkSource' for single networks. Network numbers are 0-based (network 1 in TIA is 0). For a LAD/FBD network you will change, take its XML from 'ExportXmlBlock' (absolute exportPath).
3. Fingerprint before: 'GetBlockFingerprints' with regexName '^<block name>$' and saveFile set to an absolute .json path.
4. Change with the smallest tool that fits:
   - one LAD/FBD network: copy the network's <SW.Blocks.CompileUnit> from the export into its own file, edit it, then 'EditBlockNetwork' (action 'replace' or 'insert', networkFile) with previewFile first; check the preview, then run the same call without previewFile.
   - SCL statements as one network: 'EditBlockNetwork' with sclFile (plain SCL statements; '#name' refers to the block's own variables).
   - one DB start value: 'SetDbStartValue' with the member path from 'GetDbStartValues'.
   - a new, changed or removed variable: 'EditBlockInterface' (add it before the network that uses it); a UDT member: 'EditTypeInterface'.
   - a whole new SCL block: 'ImportSourceBlock' from a .scl file.
   The edit tools back the block up first and return the backup path.
5. Compile: 'CompileCodeBlock' for this block only.
6. On an import or compile error: fix once, guided by the error message, and retry.
7. Fingerprint after: 'GetBlockFingerprints' with the same regexName and compareFile set to the file from step 3. Report what changed (code, interface), the backup path, and that the change is in memory until 'SaveProject'. Save only if the user asks.

Do not:
- rewrite a whole block's XML by hand and re-import it with 'ImportXmlBlock' when only a network or a value changes
- run 'CompileSoftware' for the whole PLC again and again; compile the changed block
- repeat a failing call unchanged
- work around this server with your own scripts, Openness code or edits to project files

Stop rule: if the same step fails twice, stop and show the user the full error text, the call you made and what you would try next.";
        }

        [McpServerPrompt(Name = "DeleteBlock"), Description("Delete a block")]
        public static string DeleteBlock(string softwarePath, string blockPath)
        {
            return $@"Delete a program block. Know-how protected blocks are rejected: remove the protection in TIA Portal first (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block, e.g. 1_Tests/FC_Block_1

Use the DeleteBlock tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}";
        }

        [McpServerPrompt(Name = "RenameBlock"), Description("Rename a block")]
        public static string RenameBlock(string softwarePath, string blockPath, string newName)
        {
            return $@"Rename a program block. Know-how protected blocks are rejected (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block, e.g. 1_Tests/FC_Block_1
- newName: the new block name, without a slash

Use the RenameBlock tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "CreateFB"), Description("Create an empty function block")]
        public static string CreateFB(string softwarePath, string groupPath, string name, string language = "LAD", string autoNumber = "true", string number = "0")
        {
            return $@"Create an empty function block. Openness has no generic create-block operation: FB and instance DB are the only kinds creatable without importing XML (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative block group that receives the FB; empty uses the Program blocks root
- name: name of the new function block, without a slash
- language: programming language such as LAD (default), FBD, STL, SCL or GRAPH
- autoNumber: let TIA Portal assign the block number (default true)
- number: explicit block number, used only when autoNumber is false

Use the CreateFB tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- name: {name}
- language: {language}
- autoNumber: {NormalizeBool(autoNumber)}
- number: {number}";
        }

        [McpServerPrompt(Name = "CreateInstanceDB"), Description("Create an instance data block for a function block")]
        public static string CreateInstanceDB(string softwarePath, string groupPath, string name, string instanceOfName, string autoNumber = "true", string number = "0")
        {
            return $@"Create an instance data block for an existing function block (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative block group that receives the DB; empty uses the Program blocks root
- name: name of the new instance data block, without a slash
- instanceOfName: name of the function block this instance DB belongs to
- autoNumber: let TIA Portal assign the block number (default true)
- number: explicit block number, used only when autoNumber is false

Use the CreateInstanceDB tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- name: {name}
- instanceOfName: {instanceOfName}
- autoNumber: {NormalizeBool(autoNumber)}
- number: {number}";
        }

        [McpServerPrompt(Name = "CopyBlock"), Description("Copy a block into another block group")]
        public static string CopyBlock(string softwarePath, string blockPath, string targetGroupPath, string overwrite = "false")
        {
            return $@"Copy a program block into another block group of the same PLC software. Implemented as export plus import because Openness has no copy operation, so the block must be consistent and keeps its block number (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block to copy, e.g. 1_Tests/FC_Block_1
- targetGroupPath: root-relative block group that receives the copy; empty means the Program blocks root
- overwrite: replace a block of the same name already in the target group (default false)

Use the CopyBlock tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- targetGroupPath: {targetGroupPath}
- overwrite: {NormalizeBool(overwrite)}";
        }

        [McpServerPrompt(Name = "MoveBlock"), Description("Move a block into another block group")]
        public static string MoveBlock(string softwarePath, string blockPath, string targetGroupPath, string overwrite = "false")
        {
            return $@"Move a program block into another block group of the same PLC software. Implemented as export, import and deleting the original; the original is only removed after the import succeeds (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block to move, e.g. 1_Tests/FC_Block_1
- targetGroupPath: root-relative block group that receives the block; empty means the Program blocks root
- overwrite: replace a block of the same name already in the target group (default false)

Use the MoveBlock tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- targetGroupPath: {targetGroupPath}
- overwrite: {NormalizeBool(overwrite)}";
        }

        #endregion

        #region Type Templates

        [McpServerPrompt(Name = "GetTypeInfo"), Description("Show details of one PLC data type")]
        public static string GetTypeInfo(string softwarePath, string typePath)
        {
            return $@"Get a type info from the PLC software.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- typePath: defines the path in the project structure to the type

Use the GetTypeInfo tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}";
        }

        [McpServerPrompt(Name = "GetTypes"), Description("List the PLC data types of a PLC software")]
        public static string GetTypes(string softwarePath, string regexName = "")
        {
            return $@"Get a list of types from the PLC software.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- regexName: defines the name or regular expression to find the block. Use empty string (default) to find all

Use the GetTypes tool with these parameters:
- softwarePath: {softwarePath}
- regexName: {regexName}";
        }

        [McpServerPrompt(Name = "ExportXmlType"), Description("Export one PLC data type as XML")]
        public static string ExportXmlType(string softwarePath, string exportPath, string typePath, string preservePath = "false")
        {
            return $@"Export a type from the PLC software.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- exportPath: defines the path where export the type
- typePath: defines the path in the project structure to the type
- preservePath: preserves the path/structure of the PLC software

Use the ExportXmlType tool with these parameters:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- typePath: {typePath}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ImportXmlType"), Description("Import one PLC data type from an XML file")]
        public static string ImportXmlType(string softwarePath, string groupPath, string importPath, string allowOlderFile = "false")
        {
            return $@"Import a type from file into the PLC software.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: defines the path in the project structure to the group, where to import the type
- importPath: defines the path of the xml file from where to import the type
- allowOlderFile: Use false (default) to refuse the import when an object it would replace was changed in TIA Portal after the file was written (that import would undo the change), true to import anyway

Use the ImportXmlType tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- allowOlderFile: {NormalizeBool(allowOlderFile)}";
        }

        [McpServerPrompt(Name = "CreateTypeGroup"), Description("Create a PLC data type group")]
        public static string CreateTypeGroup(string softwarePath, string parentGroupPath, string name)
        {
            return $@"Create a group below the PLC data types root of the PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- parentGroupPath: root-relative path of the parent group; empty creates directly below PLC data types
- name: name of the new group, without a slash

Use the CreateTypeGroup tool with these parameters:
- softwarePath: {softwarePath}
- parentGroupPath: {parentGroupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "DeleteTypeGroup"), Description("Delete a PLC data type group and everything inside it")]
        public static string DeleteTypeGroup(string softwarePath, string groupPath)
        {
            return $@"Delete a PLC data type group and everything inside it. The PLC data types system group itself cannot be deleted (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group to delete

Use the DeleteTypeGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}";
        }

        [McpServerPrompt(Name = "DeleteType"), Description("Delete a PLC data type")]
        public static string DeleteType(string softwarePath, string typePath)
        {
            return $@"Delete a PLC data type (UDT). Know-how protected types are rejected (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- typePath: root-relative path of the type, e.g. Common/CarrierRegister/ML_SubstratState

Use the DeleteType tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}";
        }

        [McpServerPrompt(Name = "RenameType"), Description("Rename a PLC data type")]
        public static string RenameType(string softwarePath, string typePath, string newName)
        {
            return $@"Rename a PLC data type (UDT). Know-how protected types are rejected (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- typePath: root-relative path of the type
- newName: the new type name, without a slash

Use the RenameType tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "CopyType"), Description("Copy a PLC data type into another type group")]
        public static string CopyType(string softwarePath, string typePath, string targetGroupPath, string overwrite = "false")
        {
            return $@"Copy a PLC data type (UDT) into another type group of the same PLC software. Implemented as export plus import because Openness has no copy operation, so the type must be consistent (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- typePath: root-relative path of the type to copy, e.g. Common/CarrierRegister/ML_SubstratState
- targetGroupPath: root-relative type group that receives the copy; empty means the PLC data types root
- overwrite: replace a type of the same name already in the target group (default false)

Use the CopyType tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}
- targetGroupPath: {targetGroupPath}
- overwrite: {NormalizeBool(overwrite)}";
        }

        [McpServerPrompt(Name = "MoveType"), Description("Move a PLC data type into another type group")]
        public static string MoveType(string softwarePath, string typePath, string targetGroupPath, string overwrite = "false")
        {
            return $@"Move a PLC data type (UDT) into another type group of the same PLC software. Implemented as export, import and deleting the original; the original is only removed after the import succeeds (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- typePath: root-relative path of the type to move
- targetGroupPath: root-relative type group that receives the type; empty means the PLC data types root
- overwrite: replace a type of the same name already in the target group (default false)

Use the MoveType tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}
- targetGroupPath: {targetGroupPath}
- overwrite: {NormalizeBool(overwrite)}";
        }

        #endregion

        #region Tag and Table Templates

        [McpServerPrompt(Name = "GetTagTables"), Description("List the PLC tag tables of a PLC software")]
        public static string GetTagTables(string softwarePath, string regexName = "")
        {
            return $@"List the PLC tag tables of a PLC software, optionally filtered by a regular expression on the table name.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- regexName: optional regular expression to filter the tag table names

Use the GetTagTables tool with these parameters:
- softwarePath: {softwarePath}
- regexName: {regexName}";
        }

        [McpServerPrompt(Name = "GetTagTableInfo"), Description("Show details of one PLC tag table")]
        public static string GetTagTableInfo(string softwarePath, string tagTablePath)
        {
            return $@"Get the details of a single tag table, including its tag and constant counts.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: root-relative path of the tag table, e.g. 'TagGroup1/Table1'

Use the GetTagTableInfo tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}";
        }

        [McpServerPrompt(Name = "GetTags"), Description("List PLC tags of one tag table or of all tag tables")]
        public static string GetTags(string softwarePath, string tagTablePath = "", string regexName = "")
        {
            return $@"List PLC tags, either of one tag table or of every tag table of the PLC software, optionally filtered by a regular expression on the tag name.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: optional root-relative tag table path; empty searches every tag table
- regexName: optional regular expression to filter the tag names

Use the GetTags tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- regexName: {regexName}";
        }

        [McpServerPrompt(Name = "GetTagInfo"), Description("Show details of one PLC tag")]
        public static string GetTagInfo(string softwarePath, string tagPath)
        {
            return $@"Get the details of a single PLC tag, including data type, logical address and external access flags.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagPath: path of the tag including its table, e.g. 'TagGroup1/Table1/Tag_1'

Use the GetTagInfo tool with these parameters:
- softwarePath: {softwarePath}
- tagPath: {tagPath}";
        }

        [McpServerPrompt(Name = "GetConstants"), Description("List PLC user and system constants")]
        public static string GetConstants(string softwarePath, string tagTablePath = "", string kind = "all", string regexName = "")
        {
            return $@"List PLC user and/or system constants, either of one tag table or of every tag table of the PLC software.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: optional root-relative tag table path; empty searches every tag table
- kind: which constants to return - 'all' (default), 'user' or 'system'
- regexName: optional regular expression to filter the constant names

Use the GetConstants tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- kind: {kind}
- regexName: {regexName}";
        }

        [McpServerPrompt(Name = "GetWatchTables"), Description("List the PLC watch tables of a PLC software")]
        public static string GetWatchTables(string softwarePath, string regexName = "")
        {
            return $@"List the PLC watch tables of a PLC software, optionally filtered by a regular expression on the table name. Entries are omitted; use GetWatchTableInfo for one table's rows.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- regexName: optional regular expression to filter the watch table names

Use the GetWatchTables tool with these parameters:
- softwarePath: {softwarePath}
- regexName: {regexName}";
        }

        [McpServerPrompt(Name = "GetWatchTableInfo"), Description("Show one PLC watch table with its entries")]
        public static string GetWatchTableInfo(string softwarePath, string watchTablePath)
        {
            return $@"Get a single PLC watch table including all of its entries (address, display format, monitor and modify settings).

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- watchTablePath: root-relative path of the watch table, e.g. 'WatchGroup1/WatchTable_1'

Use the GetWatchTableInfo tool with these parameters:
- softwarePath: {softwarePath}
- watchTablePath: {watchTablePath}";
        }

        [McpServerPrompt(Name = "GetForceTables"), Description("List the PLC force tables with their entries")]
        public static string GetForceTables(string softwarePath)
        {
            return $@"List the PLC force tables of a PLC software including their entries. A force table is created and owned by the system: it cannot be created or deleted through Openness.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software

Use the GetForceTables tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "ExportXmlWatchTable"), Description("Export one PLC watch table as XML")]
        public static string ExportXmlWatchTable(string softwarePath, string watchTablePath, string exportPath, string preservePath = "false")
        {
            return $@"Export a PLC watch table to an XML file on the file system of the machine running this server. Does not modify the project.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- watchTablePath: root-relative path of the watch table, e.g. 'WatchGroup1/WatchTable_1'
- exportPath: directory on this machine that receives the XML file
- preservePath: recreate the watch table group structure below exportPath

Use the ExportXmlWatchTable tool with these parameters:
- softwarePath: {softwarePath}
- watchTablePath: {watchTablePath}
- exportPath: {exportPath}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "CreateTagTable"), Description("Create a PLC tag table")]
        public static string CreateTagTable(string softwarePath, string groupPath, string name)
        {
            return $@"Create a PLC tag table in a tag table group (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative tag table group; empty creates directly below the PLC tags root
- name: name of the new tag table, without a slash

Use the CreateTagTable tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "DeleteTagTable"), Description("Delete a PLC tag table with its tags and constants")]
        public static string DeleteTagTable(string softwarePath, string tagTablePath)
        {
            return $@"Delete a PLC tag table with all of its tags and user constants. The default tag table cannot be deleted (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: root-relative path of the tag table, e.g. TagGroup1/Table1

Use the DeleteTagTable tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}";
        }

        [McpServerPrompt(Name = "RenameTagTable"), Description("Rename a PLC tag table")]
        public static string RenameTagTable(string softwarePath, string tagTablePath, string newName)
        {
            return $@"Rename a PLC tag table (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: root-relative path of the tag table
- newName: the new tag table name, without a slash

Use the RenameTagTable tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "CreateTagTableGroup"), Description("Create a tag table group")]
        public static string CreateTagTableGroup(string softwarePath, string parentGroupPath, string name)
        {
            return $@"Create a group below the PLC tags root of the PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- parentGroupPath: root-relative path of the parent group; empty creates directly below PLC tags
- name: name of the new group, without a slash

Use the CreateTagTableGroup tool with these parameters:
- softwarePath: {softwarePath}
- parentGroupPath: {parentGroupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "DeleteTagTableGroup"), Description("Delete a tag table group and everything inside it")]
        public static string DeleteTagTableGroup(string softwarePath, string groupPath)
        {
            return $@"Delete a tag table group and everything inside it. The PLC tags system group itself cannot be deleted (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group to delete

Use the DeleteTagTableGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}";
        }

        [McpServerPrompt(Name = "ImportXmlTagTable"), Description("Import a PLC tag table from an XML file")]
        public static string ImportXmlTagTable(string softwarePath, string groupPath, string importPath, string overwrite = "true")
        {
            return $@"Import a PLC tag table from an XML file on the file system of the machine running this server (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative tag table group that receives the table; empty uses the PLC tags root. A leading 'PLC tags' segment, as written by preservePath exports, is accepted and ignored
- importPath: full path of the XML file to import
- overwrite: replace an existing tag table of the same name (default true)

Use the ImportXmlTagTable tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- overwrite: {NormalizeBool(overwrite)}";
        }

        [McpServerPrompt(Name = "CreateTag"), Description("Create a PLC tag in a tag table")]
        public static string CreateTag(string softwarePath, string tagTablePath, string name, string dataTypeName = "", string logicalAddress = "")
        {
            return $@"Create a PLC tag in a tag table (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: root-relative path of the tag table, e.g. TagGroup1/Table1
- name: name of the new tag, without a slash
- dataTypeName: PLC data type such as Bool, Int or Word; empty creates the tag with its default type
- logicalAddress: absolute address such as %I0.0, %QW4 or %M10.1

Use the CreateTag tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- name: {name}
- dataTypeName: {dataTypeName}
- logicalAddress: {logicalAddress}";
        }

        [McpServerPrompt(Name = "UpdateTag"), Description("Change properties of an existing PLC tag")]
        public static string UpdateTag(string softwarePath, string tagPath, string newName = "", string dataTypeName = "", string logicalAddress = "", string externalAccessible = "", string externalVisible = "", string externalWritable = "")
        {
            return $@"Change one or more properties of an existing PLC tag. Every argument left empty or null keeps the current value (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagPath: path of the tag including its table, e.g. TagGroup1/Table1/Tag_1
- newName: optional new tag name, without a slash
- dataTypeName: optional new PLC data type
- logicalAddress: optional new absolute address
- externalAccessible: optional new value for accessibility from HMI/OPC UA
- externalVisible: optional new value for visibility in HMI/OPC UA
- externalWritable: optional new value for writability from HMI/OPC UA

Use the UpdateTag tool with these parameters:
- softwarePath: {softwarePath}
- tagPath: {tagPath}
- newName: {newName}
- dataTypeName: {dataTypeName}
- logicalAddress: {logicalAddress}
- externalAccessible: {externalAccessible}
- externalVisible: {externalVisible}
- externalWritable: {externalWritable}";
        }

        [McpServerPrompt(Name = "DeleteTag"), Description("Delete a PLC tag")]
        public static string DeleteTag(string softwarePath, string tagPath)
        {
            return $@"Delete a PLC tag from its tag table (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagPath: path of the tag including its table, e.g. TagGroup1/Table1/Tag_1

Use the DeleteTag tool with these parameters:
- softwarePath: {softwarePath}
- tagPath: {tagPath}";
        }

        [McpServerPrompt(Name = "CreateUserConstant"), Description("Create a user constant in a tag table")]
        public static string CreateUserConstant(string softwarePath, string tagTablePath, string name, string dataTypeName = "", string value = "")
        {
            return $@"Create a user constant in a tag table. System constants are read-only and cannot be created (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: root-relative path of the tag table
- name: name of the new constant, without a slash
- dataTypeName: PLC data type such as Int or Real; empty creates the constant with its default type
- value: the constant value as text, e.g. 42 or 3.14

Use the CreateUserConstant tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- name: {name}
- dataTypeName: {dataTypeName}
- value: {value}";
        }

        [McpServerPrompt(Name = "UpdateUserConstant"), Description("Change an existing user constant")]
        public static string UpdateUserConstant(string softwarePath, string tagTablePath, string name, string newName = "", string dataTypeName = "", string value = "")
        {
            return $@"Change the name, data type or value of an existing user constant. Every argument left null keeps the current value (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: root-relative path of the tag table
- name: current name of the constant
- newName: optional new constant name, without a slash
- dataTypeName: optional new PLC data type
- value: optional new value as text

Use the UpdateUserConstant tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- name: {name}
- newName: {newName}
- dataTypeName: {dataTypeName}
- value: {value}";
        }

        [McpServerPrompt(Name = "DeleteUserConstant"), Description("Delete a user constant")]
        public static string DeleteUserConstant(string softwarePath, string tagTablePath, string name)
        {
            return $@"Delete a user constant from a tag table. System constants cannot be deleted (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagTablePath: root-relative path of the tag table
- name: name of the constant to delete

Use the DeleteUserConstant tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- name: {name}";
        }

        [McpServerPrompt(Name = "CreateWatchTable"), Description("Create a PLC watch table")]
        public static string CreateWatchTable(string softwarePath, string groupPath, string name)
        {
            return $@"Create watch table. Force tables cannot be created: the system owns the single force table per PLC (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative watch table group; empty creates directly below the Watch and force tables root
- name: name of the new watch table, without a slash

Use the CreateWatchTable tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "RenameWatchTable"), Description("Rename a PLC watch table")]
        public static string RenameWatchTable(string softwarePath, string watchTablePath, string newName)
        {
            return $@"Rename watch table (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- watchTablePath: root-relative path of the watch table
- newName: the new watch table name, without a slash

Use the RenameWatchTable tool with these parameters:
- softwarePath: {softwarePath}
- watchTablePath: {watchTablePath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "DeleteWatchTable"), Description("Delete a PLC watch table with its entries")]
        public static string DeleteWatchTable(string softwarePath, string watchTablePath)
        {
            return $@"Delete watch table with all of its entries. Force tables cannot be deleted (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- watchTablePath: root-relative path of the watch table

Use the DeleteWatchTable tool with these parameters:
- softwarePath: {softwarePath}
- watchTablePath: {watchTablePath}";
        }

        [McpServerPrompt(Name = "CreateWatchTableGroup"), Description("Create a watch table group")]
        public static string CreateWatchTableGroup(string softwarePath, string parentGroupPath, string name)
        {
            return $@"Create watch table group below the Watch and force tables root of the PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- parentGroupPath: root-relative path of the parent group; empty creates directly below the root
- name: name of the new group, without a slash

Use the CreateWatchTableGroup tool with these parameters:
- softwarePath: {softwarePath}
- parentGroupPath: {parentGroupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "DeleteWatchTableGroup"), Description("Delete a watch table group and everything inside it")]
        public static string DeleteWatchTableGroup(string softwarePath, string groupPath)
        {
            return $@"Delete watch table group and everything inside it. The Watch and force tables system group itself cannot be deleted (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group to delete

Use the DeleteWatchTableGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}";
        }

        [McpServerPrompt(Name = "ImportWatchTable"), Description("Import a PLC watch table from an XML file")]
        public static string ImportWatchTable(string softwarePath, string groupPath, string importPath, string overwrite = "true")
        {
            return $@"Import a PLC watch table from an XML file on the file system of the machine running this server (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative watch table group that receives the table; empty uses the root
- importPath: full path of the XML file to import
- overwrite: replace an existing watch table of the same name (default true)

Use the ImportWatchTable tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- overwrite: {NormalizeBool(overwrite)}";
        }

        #endregion

        #region Document Templates

        [McpServerPrompt(Name = "ExportTypeAsDocuments"), Description("Export one PLC data type as documents (.s7dcl/.s7res)")]
        public static string ExportTypeAsDocuments(string softwarePath, string typePath, string exportPath, string preservePath = "false")
        {
            return $@"Export type as a SIMATIC Source Document set (.s7dcl plus an optional .s7res) instead of XML. The response lists the files TIA Portal actually wrote. Requires TIA Portal V21 or newer.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- typePath: root-relative path of the PLC data type, e.g. 'Common/BtnTyp_X'. Use 'GetTypes' to list them
- exportPath: directory on this machine that receives the document files
- preservePath: recreate the group structure below exportPath, inside the 'PLC data types' system folder as TIA Portal names it in the current interface language

Use the ExportTypeAsDocuments tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}
- exportPath: {exportPath}
- preservePath: {NormalizeBool(preservePath)}";
        }

        [McpServerPrompt(Name = "ImportTypeFromDocuments"), Description("Import one PLC data type from documents (.s7dcl/.s7res)")]
        public static string ImportTypeFromDocuments(string softwarePath, string groupPath, string importPath, string fileNameWithoutExtension, string importOption = "Override")
        {
            return $@"Import one type from a SIMATIC Source Document set (.s7dcl plus an optional .s7res) on the file system of the machine running this server. A type name is unique across the whole PLC, so importing an existing name into a different group fails even with importOption 'Override' - target the group the type already lives in. Requires TIA Portal V21 or newer (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative type group that receives the type; empty uses the PLC data types root. A leading 'PLC data types' segment, as written by preservePath exports, is accepted and ignored
- importPath: directory containing the document files
- fileNameWithoutExtension: base name of the document set, e.g. 'BtnTyp_X'
- importOption: ImportDocumentOptions value (None, Override, SkipInactiveCultures, ActivateInactiveCultures)

Use the ImportTypeFromDocuments tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- importPath: {importPath}
- fileNameWithoutExtension: {fileNameWithoutExtension}
- importOption: {importOption}";
        }

        [McpServerPrompt(Name = "ExportAsDocuments"), Description("Export one block as documents (.s7dcl/.s7res)")]
        public static string ExportAsDocuments(string softwarePath, string blockPath, string exportPath, string preservePath = "false")
        {
            return $@"Export as documents (.s7dcl/.s7res) from a block in the PLC software to path.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: defines the path in the project structure to the block
- exportPath: defines the path where to export the documents
- preservePath: preserves the path/structure of the PLC software

Use the ExportAsDocuments tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- exportPath: {exportPath}
- preservePath: {NormalizeBool(preservePath)}";
        }

        #endregion

        #region Source File Templates

        [McpServerPrompt(Name = "GetBlockSource"), Description("Show the source text of one block without exporting a file")]
        public static string GetBlockSource(string softwarePath, string blockPath, string format = "document", string maxChars = "40000")
        {
            return $@"Return the source text of one program block directly, instead of exporting a file and reading it back. 'document' gives readable SCL/LAD/STL (SIMATIC Source Document, V20+); objects TIA Portal cannot represent that way - STL and mixed-language blocks - fall back to XML automatically, and the response says which format was produced.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block, e.g. '0_OBs/Main'. Use 'ResolveObjectPath' if you only know the name
- format: 'document' (default) for readable source text, or 'xml' for the SimaticML export
- maxChars: truncate the text at this many characters, on a line boundary (default 40000)

Use the GetBlockSource tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- format: {format}
- maxChars: {maxChars}";
        }

        [McpServerPrompt(Name = "GetTypeSource"), Description("Show the source text of one PLC data type without exporting a file")]
        public static string GetTypeSource(string softwarePath, string typePath, string format = "document", string maxChars = "40000")
        {
            return $@"Return the source text of one PLC data type directly, instead of exporting a file and reading it back. 'document' gives the readable TYPE ... END_TYPE declaration (SIMATIC Source Document, requires TIA Portal V21); 'xml' gives the SimaticML export.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- typePath: root-relative path of the PLC data type, e.g. 'Common/BtnTyp_X'. Use 'ResolveObjectPath' if you only know the name
- format: 'document' (default) for readable source text, or 'xml' for the SimaticML export
- maxChars: truncate the text at this many characters, on a line boundary (default 40000)

Use the GetTypeSource tool with these parameters:
- softwarePath: {softwarePath}
- typePath: {typePath}
- format: {format}
- maxChars: {maxChars}";
        }

        [McpServerPrompt(Name = "ExportPlcAsDocuments"), Description("Export a whole PLC software as one folder tree of documents")]
        public static string ExportPlcAsDocuments(string softwarePath, string exportPath)
        {
            return $@"Write a whole PLC software to one folder tree that mirrors the project, ready to commit: program blocks and PLC data types as readable SIMATIC Source Documents where TIA Portal supports them, tag tables and watch tables as XML, each below its localised system folder. Replaces running the four bulk exports separately. Objects that cannot be exported are reported instead of failing the snapshot.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- exportPath: directory on this machine that receives the tree; existing files of the same name are overwritten

Use the ExportPlcAsDocuments tool with these parameters:
- softwarePath: {softwarePath}
- exportPath: {exportPath}";
        }

        [McpServerPrompt(Name = "ExportSourceBlocks"), Description("Export all blocks below a group as source files")]
        public static string ExportSourceBlocks(string softwarePath, string groupPath, string exportPath, string withDependencies = "false", string preservePath = "false")
        {
            return $@"Write every program block below a block group, including all subgroups, as TIA Portal external source files. Blocks with no source form (LAD, FBD, GRAPH), inconsistent blocks and know-how protected ones are reported in 'Skipped' instead of failing the run.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the block group to export recursively, e.g. '0_OBs'. Empty means all blocks below 'Program blocks'
- exportPath: directory on this machine that receives the files; existing files of the same name are overwritten
- withDependencies: also write every object each block uses into its file. Default false
- preservePath: mirror the project groups below '<exportPath>/Program blocks'. Default false, which writes straight into exportPath

Use the ExportSourceBlocks tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- exportPath: {exportPath}
- withDependencies: {withDependencies}
- preservePath: {preservePath}";
        }

        [McpServerPrompt(Name = "ExportSourceTypes"), Description("Export all PLC data types below a group as .udt source files")]
        public static string ExportSourceTypes(string softwarePath, string groupPath, string exportPath, string withDependencies = "false", string preservePath = "false")
        {
            return $@"Write every PLC data type below a type group, including all subgroups, as '*.udt' external source files. Inconsistent and know-how protected types are reported in 'Skipped' instead of failing the run.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the type group to export recursively, e.g. 'Common'. Empty means all types below 'PLC data types'
- exportPath: directory on this machine that receives the files; existing files of the same name are overwritten
- withDependencies: also write every data type each one uses into its file. Default false
- preservePath: mirror the project groups below '<exportPath>/PLC data types'. Default false, which writes straight into exportPath

Use the ExportSourceTypes tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- exportPath: {exportPath}
- withDependencies: {withDependencies}
- preservePath: {preservePath}";
        }

        [McpServerPrompt(Name = "ExportSources"), Description("Export all blocks and PLC data types as source files")]
        public static string ExportSources(string softwarePath, string exportPath, string regexName = "", string withDependencies = "false")
        {
            return $@"Write every block and PLC data type of one PLC software as external source files into a folder tree that mirrors the project groups: '<exportPath>/Program blocks/...' and '<exportPath>/PLC data types/...', one file per object. The whole-PLC counterpart to 'ExportSourceBlocks' and 'ExportSourceTypes' and the compilable counterpart to 'ExportPlcAsDocuments'; 'ImportSources' reads such a tree back. Objects with no source form (LAD, FBD, GRAPH), inconsistent objects and know-how protected ones are reported in 'Skipped' instead of failing the run.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- exportPath: directory on this machine that receives the tree; existing files of the same name are overwritten
- regexName: optional regular expression, exports only objects whose name matches. Empty means all
- withDependencies: also write every object each one uses into its file. Default false, which keeps one object per file

Use the ExportSources tool with these parameters:
- softwarePath: {softwarePath}
- exportPath: {exportPath}
- regexName: {regexName}
- withDependencies: {NormalizeBool(withDependencies)}";
        }

        [McpServerPrompt(Name = "GetExternalSources"), Description("List the external source files of a PLC software")]
        public static string GetExternalSources(string softwarePath, string regexName = "")
        {
            return $@"List the external source files of a PLC software, optionally filtered by a regular expression on the source name.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- regexName: optional regular expression to filter the external source names

Use the GetExternalSources tool with these parameters:
- softwarePath: {softwarePath}
- regexName: {regexName}";
        }

        [McpServerPrompt(Name = "GetExternalSourceInfo"), Description("Show details of one external source file")]
        public static string GetExternalSourceInfo(string softwarePath, string sourcePath)
        {
            return $@"Get a single external source file. Beyond its name, all metadata is returned in the generic Attributes list.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- sourcePath: root-relative path of the external source, e.g. 'SourceGroup1/Source_1'

Use the GetExternalSourceInfo tool with these parameters:
- softwarePath: {softwarePath}
- sourcePath: {sourcePath}";
        }

        [McpServerPrompt(Name = "CreateExternalSourceFromFile"), Description("Add a source file to the external source files of a PLC software")]
        public static string CreateExternalSourceFromFile(string softwarePath, string groupPath, string name, string filePath)
        {
            return $@"Add a source file (for example an SCL file) from the file system into the external source files of the PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative external source group; empty uses the External source files root
- name: name the source gets in the project, without a slash
- filePath: full path of the source file on the machine running this server

Use the CreateExternalSourceFromFile tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- name: {name}
- filePath: {filePath}";
        }

        [McpServerPrompt(Name = "DeleteExternalSource"), Description("Delete an external source file")]
        public static string DeleteExternalSource(string softwarePath, string sourcePath)
        {
            return $@"Remove an external source file from the PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- sourcePath: root-relative path of the external source, e.g. SourceGroup1/Source_1

Use the DeleteExternalSource tool with these parameters:
- softwarePath: {softwarePath}
- sourcePath: {sourcePath}";
        }

        [McpServerPrompt(Name = "CreateExternalSourceGroup"), Description("Create an external source group")]
        public static string CreateExternalSourceGroup(string softwarePath, string parentGroupPath, string name)
        {
            return $@"Create a group below the External source files root of the PLC software (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- parentGroupPath: root-relative path of the parent group; empty creates directly below the root
- name: name of the new group, without a slash

Use the CreateExternalSourceGroup tool with these parameters:
- softwarePath: {softwarePath}
- parentGroupPath: {parentGroupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "DeleteExternalSourceGroup"), Description("Delete an external source group and everything inside it")]
        public static string DeleteExternalSourceGroup(string softwarePath, string groupPath)
        {
            return $@"Delete an external source group and everything inside it. The External source files system group itself cannot be deleted (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group to delete

Use the DeleteExternalSourceGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}";
        }

        #endregion

        #region Direct PLC (S7CommPlus) Templates

        [McpServerPrompt(Name = "ConnectPlcDirect"), Description("Open a direct S7CommPlus connection to a PLC, bypassing Openness")]
        public static string ConnectPlcDirect(string ipAddress, string username = "", string password = "")
        {
            return $@"Open a direct network connection (S7CommPlus) straight to a live CPU, bypassing TIA Portal/Openness entirely.

Common parameter values:
- ipAddress: the PLC's PROFINET interface IP, or the PLCSIM virtual adapter IP{OptionalParameter("username", username)}{OptionalParameter("password", password)}

Use the ConnectPlcDirect tool with these parameters:
- ipAddress: {ipAddress}";
        }

        [McpServerPrompt(Name = "DisconnectPlcDirect"), Description("Close the direct S7CommPlus PLC connection")]
        public static string DisconnectPlcDirect()
        {
            return @"Close the direct S7CommPlus connection opened by ConnectPlcDirect.

Use the DisconnectPlcDirect tool (it takes no parameters).";
        }

        [McpServerPrompt(Name = "BrowsePlcTagsDirect"), Description("List symbolic tags the directly-connected CPU exposes")]
        public static string BrowsePlcTagsDirect(string regexName = "")
        {
            return $@"Browse the symbolic tags exposed by the directly-connected CPU (requires ConnectPlcDirect first).

Common parameter values:
- regexName: a case-insensitive regex to filter tag names, e.g. 'MainAssy' - pass one rather than browsing unfiltered, a real project can expose hundreds of thousands of tags

Use the BrowsePlcTagsDirect tool with these parameters:{OptionalParameter("regexName", regexName)}";
        }

        [McpServerPrompt(Name = "ReadPlcTagValuesDirect"), Description("Read live tag values off the directly-connected CPU")]
        public static string ReadPlcTagValuesDirect(string tagNames)
        {
            return $@"Read current live values straight off the directly-connected CPU (requires ConnectPlcDirect first).

Common parameter values:
- tagNames: a JSON array of symbolic tag names, e.g. [""MainAssy.Mode.Dry_Run"", ""MyGlobalDb.Counter""]

Use the ReadPlcTagValuesDirect tool with these parameters:
- tagNames: {tagNames}";
        }

        [McpServerPrompt(Name = "WritePlcTagValueDirect"), Description("Write live tag values onto the directly-connected CPU, PLCSIM only")]
        public static string WritePlcTagValueDirect(string tagNames, string values, string confirm = "false", string confirmPlcSimOnly = "false")
        {
            return $@"Write values straight onto the directly-connected CPU (requires ConnectPlcDirect first). PLCSIM ONLY - never pass confirm=true against anything that might be a real/production CPU.

Common parameter values:
- tagNames: a JSON array of symbolic tag names, same format as ReadPlcTagValuesDirect, e.g. [""MainAssy.Mode.Dry_Run""]
- values: a JSON array of new values, same order/length as tagNames, e.g. [""true""]
- confirm: must be true to actually write - false (default) only previews what would be written
- confirmPlcSimOnly: must also be true - you are asserting the connected target is PLCSIM, never a real line

Use the WritePlcTagValueDirect tool with these parameters:
- tagNames: {tagNames}
- values: {values}
- confirm: {NormalizeBool(confirm)}
- confirmPlcSimOnly: {NormalizeBool(confirmPlcSimOnly)}";
        }

        [McpServerPrompt(Name = "GetActivePlcAlarmsDirect"), Description("Snapshot active alarms off the directly-connected CPU")]
        public static string GetActivePlcAlarmsDirect(string languageId = "1033")
        {
            return $@"Snapshot currently active and not-yet-cleared alarms straight off the directly-connected CPU (requires ConnectPlcDirect first). One-time poll, not a live subscription.

Common parameter values:
- languageId: a Windows LCID for the alarm text language, default 1033 (en-US), e.g. 1042 for ko-KR

Use the GetActivePlcAlarmsDirect tool with these parameters:{OptionalParameter("languageId", languageId)}";
        }

        #endregion

        #region Network Topology Templates

        [McpServerPrompt(Name = "GetSubnets"), Description("List every subnet in the project")]
        public static string GetSubnets()
        {
            return @"Get a list of every subnet in the project, with its type and connected devices/IO systems.

Use the GetSubnets tool (it takes no parameters).";
        }

        [McpServerPrompt(Name = "GetNetworkInterfaceInfo"), Description("Get topology info for a device's network interface")]
        public static string GetNetworkInterfaceInfo(string path)
        {
            return $@"Get network topology info for a device's network interface - nodes, connected subnet, IO role, per-port wiring.

Common parameter values:
- path: a device or device item path, e.g. 'PLC_1' - doesn't need to be the exact interface DeviceItem, it auto-searches nested DeviceItems

Use the GetNetworkInterfaceInfo tool with these parameters:
- path: {path}";
        }

        [McpServerPrompt(Name = "SetIpAddress"), Description("Set IP address/subnet mask on a device's network interface")]
        public static string SetIpAddress(string path, string ipAddress, string subnetMask, string routerAddress = "")
        {
            return $@"Set IP address/subnet mask/router on a device's network interface. Project metadata only - nothing reaches real hardware until compiled and downloaded.

Common parameter values:
- path: same auto-resolving rules as GetNetworkInterfaceInfo
- ipAddress: e.g. '192.168.0.1'
- subnetMask: e.g. '255.255.255.0'{OptionalParameter("routerAddress", routerAddress)}

Use the SetIpAddress tool with these parameters:
- path: {path}
- ipAddress: {ipAddress}
- subnetMask: {subnetMask}";
        }

        [McpServerPrompt(Name = "ConnectToSubnet"), Description("Connect a device's network interface to a named subnet")]
        public static string ConnectToSubnet(string path, string subnetName)
        {
            return $@"Connect a device's network interface to a named subnet (must already exist - see GetSubnets).

Common parameter values:
- path: same auto-resolving rules as GetNetworkInterfaceInfo
- subnetName: an existing subnet name, from GetSubnets

Use the ConnectToSubnet tool with these parameters:
- path: {path}
- subnetName: {subnetName}";
        }

        [McpServerPrompt(Name = "DisconnectFromSubnet"), Description("Disconnect a device's network interface from its subnet")]
        public static string DisconnectFromSubnet(string path)
        {
            return $@"Disconnect a device's network interface from its subnet.

Common parameter values:
- path: same auto-resolving rules as GetNetworkInterfaceInfo

Use the DisconnectFromSubnet tool with these parameters:
- path: {path}";
        }

        #endregion

        #region HMI Templates

        [McpServerPrompt(Name = "GetHmiScreenItems"), Description("List the items of one WinCC Unified screen")]
        public static string GetHmiScreenItems(string softwarePath, string screenName, string regexName = "")
        {
            return $@"List the items of one WinCC Unified screen with kind, position, size, text, tag bindings and, for faceplates, the faceplate type and interface values.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen, as GetHmiScreens lists it
- regexName: optional name or regular expression to filter the items; empty lists all

Use the GetHmiScreenItems tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}{OptionalParameter("regexName", regexName)}";
        }

        [McpServerPrompt(Name = "CreateHmiScreen"), Description("Create a WinCC Unified screen")]
        public static string CreateHmiScreen(string softwarePath, string name, string width = "", string height = "", string screenNumber = "")
        {
            return $@"Create a new screen in a WinCC Unified HMI software.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- name: name of the new screen
- width, height: optional size in pixels; empty keeps the device default
- screenNumber: optional screen number

Use the CreateHmiScreen tool with these parameters:
- softwarePath: {softwarePath}
- name: {name}{OptionalParameter("width", width)}{OptionalParameter("height", height)}{OptionalParameter("screenNumber", screenNumber)}";
        }

        [McpServerPrompt(Name = "DeleteHmiScreen"), Description("Delete a WinCC Unified screen")]
        public static string DeleteHmiScreen(string softwarePath, string screenName)
        {
            return $@"Delete one screen of a WinCC Unified HMI software.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen to delete

Use the DeleteHmiScreen tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}";
        }

        [McpServerPrompt(Name = "CreateHmiScreenItem"), Description("Create an item on a WinCC Unified screen")]
        public static string CreateHmiScreenItem(string softwarePath, string screenName, string kind, string name, string left = "", string top = "", string width = "", string height = "", string text = "", string tag = "")
        {
            return $@"Create a button, IO field, text, label, rectangle or other item on a WinCC Unified screen, optionally with a text and a process tag.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen
- kind: item kind, e.g. 'Button', 'IOField', 'Text', 'Label', 'Rectangle'
- name: name of the new item
- left, top, width, height: optional position and size in pixels
- text: optional text; plain text is accepted
- tag: optional HMI tag for the process value

Use the CreateHmiScreenItem tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- kind: {kind}
- name: {name}{OptionalParameter("left", left)}{OptionalParameter("top", top)}{OptionalParameter("width", width)}{OptionalParameter("height", height)}{OptionalParameter("text", text)}{OptionalParameter("tag", tag)}";
        }

        [McpServerPrompt(Name = "UpdateHmiScreenItem"), Description("Change position, size, text or visibility of a screen item")]
        public static string UpdateHmiScreenItem(string softwarePath, string screenName, string itemName, string left = "", string top = "", string width = "", string height = "", string text = "", string visible = "")
        {
            return $@"Change the position, size, text or visibility of an item on a WinCC Unified screen. Only the values given are changed.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen
- itemName: name of the item
- left, top, width, height: optional new position and size in pixels
- text: optional new text
- visible: optional 'true' or 'false'

Use the UpdateHmiScreenItem tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}{OptionalParameter("left", left)}{OptionalParameter("top", top)}{OptionalParameter("width", width)}{OptionalParameter("height", height)}{OptionalParameter("text", text)}{OptionalParameter("visible", visible)}";
        }

        [McpServerPrompt(Name = "DeleteHmiScreenItem"), Description("Delete an item from a WinCC Unified screen")]
        public static string DeleteHmiScreenItem(string softwarePath, string screenName, string itemName)
        {
            return $@"Delete one item from a WinCC Unified screen.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen
- itemName: name of the item to delete

Use the DeleteHmiScreenItem tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}";
        }

        [McpServerPrompt(Name = "BindHmiTag"), Description("Bind an HMI tag to a property of a screen item")]
        public static string BindHmiTag(string softwarePath, string screenName, string itemName, string propertyName, string tagName)
        {
            return $@"Bind an HMI tag to a property of a WinCC Unified screen item as a tag dynamization.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen
- itemName: name of the item
- propertyName: property to dynamize, e.g. 'ProcessValue' or 'Visible'
- tagName: name of the HMI tag

Use the BindHmiTag tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- propertyName: {propertyName}
- tagName: {tagName}";
        }

        [McpServerPrompt(Name = "GetHmiDynamizations"), Description("Show what drives the properties of WinCC Unified screen items")]
        public static string GetHmiDynamizations(string softwarePath, string screenName, string regexName = "", string propertyName = "")
        {
            return $@"List the dynamizations of the items on a WinCC Unified screen in full: tag with mapping table or formula, script with trigger, flashing, resource list. Reads only.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen, as GetHmiScreens lists it
- regexName: optional filter on the item names
- propertyName: optional property, e.g. 'BackColor'

Use the GetHmiDynamizations tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}{OptionalParameter("regexName", regexName)}{OptionalParameter("propertyName", propertyName)}";
        }

        [McpServerPrompt(Name = "SetHmiTagDynamization"), Description("Make a screen item property follow an HMI tag through a value mapping")]
        public static string SetHmiTagDynamization(string softwarePath, string screenName, string itemName, string propertyName, string tagName, string mapping = "", string formula = "")
        {
            return $@"Bind an HMI tag to a property of a WinCC Unified screen item and set how tag values map to property values (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- propertyName: e.g. 'BackColor' or 'Visible'
- mapping: JSON array of range entries, e.g. [{{""from"":0,""to"":0,""value"":""#00FF00""}},{{""from"":1,""to"":1,""value"":""#FF0000""}}]

Use the SetHmiTagDynamization tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- propertyName: {propertyName}
- tagName: {tagName}{OptionalParameter("mapping", mapping)}{OptionalParameter("formula", formula)}";
        }

        [McpServerPrompt(Name = "SetHmiScriptDynamization"), Description("Compute a screen item property with a script")]
        public static string SetHmiScriptDynamization(string softwarePath, string screenName, string itemName, string propertyName, string scriptCode, string triggerType = "", string triggerTags = "", string triggerCycle = "", string globalDefinitionCode = "")
        {
            return $@"Create or change the script dynamization of a WinCC Unified screen item property (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- triggerType: Disabled, T100ms ... T10s, CustomCycle, Tags or AutomaticTags
- triggerTags: comma separated HMI tags for triggerType 'Tags'

Use the SetHmiScriptDynamization tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- propertyName: {propertyName}
- scriptCode: {scriptCode}{OptionalParameter("triggerType", triggerType)}{OptionalParameter("triggerTags", triggerTags)}{OptionalParameter("triggerCycle", triggerCycle)}{OptionalParameter("globalDefinitionCode", globalDefinitionCode)}";
        }

        [McpServerPrompt(Name = "SetHmiFlashingDynamization"), Description("Make a screen item color flash")]
        public static string SetHmiFlashingDynamization(string softwarePath, string screenName, string itemName, string propertyName, string flashingCondition = "", string flashingRate = "", string color = "", string alternateColor = "")
        {
            return $@"Create or change the flashing of a color property of a WinCC Unified screen item (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- flashingCondition: Never, Always or RangeViolation
- flashingRate: Slow, Medium or Fast
- color, alternateColor: '#RRGGBB'

Use the SetHmiFlashingDynamization tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- propertyName: {propertyName}{OptionalParameter("flashingCondition", flashingCondition)}{OptionalParameter("flashingRate", flashingRate)}{OptionalParameter("color", color)}{OptionalParameter("alternateColor", alternateColor)}";
        }

        [McpServerPrompt(Name = "DeleteHmiDynamization"), Description("Remove the dynamization of a screen item property")]
        public static string DeleteHmiDynamization(string softwarePath, string screenName, string itemName, string propertyName)
        {
            return $@"Remove the dynamization of one property of a WinCC Unified screen item; it is backed up as JSON first (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- propertyName: the dynamized property, as GetHmiDynamizations lists it

Use the DeleteHmiDynamization tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- propertyName: {propertyName}";
        }

        [McpServerPrompt(Name = "CreateHmiFaceplate"), Description("Place a faceplate on a WinCC Unified screen")]
        public static string CreateHmiFaceplate(string softwarePath, string screenName, string name, string faceplateType, string left = "", string top = "", string width = "", string height = "")
        {
            return $@"Place an instance of a faceplate type on a WinCC Unified screen.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen
- name: name of the new faceplate container
- faceplateType: faceplate type with version, e.g. 'V0.0.7\Servo_Faceplate_2'
- left, top, width, height: optional position and size in pixels

Use the CreateHmiFaceplate tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- name: {name}
- faceplateType: {faceplateType}{OptionalParameter("left", left)}{OptionalParameter("top", top)}{OptionalParameter("width", width)}{OptionalParameter("height", height)}";
        }

        [McpServerPrompt(Name = "SetHmiFaceplateInterface"), Description("Set a faceplate interface property to a value or tag")]
        public static string SetHmiFaceplateInterface(string softwarePath, string screenName, string itemName, string propertyName, string value = "", string tag = "")
        {
            return $@"Set one interface property of a placed faceplate to a constant value or to an HMI tag. Give exactly one of value and tag.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: name of the screen
- itemName: name of the faceplate container
- propertyName: interface property name, as GetHmiScreenItems lists it
- value: constant value
- tag: name of the HMI tag

Use the SetHmiFaceplateInterface tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- propertyName: {propertyName}{OptionalParameter("value", value)}{OptionalParameter("tag", tag)}";
        }

        [McpServerPrompt(Name = "CreateHmiTag"), Description("Create a WinCC Unified HMI tag")]
        public static string CreateHmiTag(string softwarePath, string tagTablePath, string name, string dataType = "", string connection = "", string plcTag = "")
        {
            return $@"Create an HMI tag in a WinCC Unified tag table, either internal or connected to a PLC tag.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- tagTablePath: name or path of the HMI tag table, e.g. 'Internal Tag'
- name: name of the new tag
- dataType: optional data type; empty means Int
- connection: optional HMI connection for an external tag
- plcTag: optional PLC tag the external tag reads

Use the CreateHmiTag tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}
- name: {name}{OptionalParameter("dataType", dataType)}{OptionalParameter("connection", connection)}{OptionalParameter("plcTag", plcTag)}";
        }

        [McpServerPrompt(Name = "CreateHmiTags"), Description("Create many WinCC Unified HMI tags at once")]
        public static string CreateHmiTags(string softwarePath, string tagTablePath, string tags = "", string tagsFile = "", string connection = "", string skipExisting = "false")
        {
            return $@"Create many HMI tags in one WinCC Unified tag table in one call. Every entry is checked first; nothing is created when one is wrong.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- tagTablePath: name or path of the HMI tag table, e.g. 'Default tag table'
- tags: JSON array of tags, each with 'name' and optionally 'dataType', 'connection' and 'plcTag'; empty when using tagsFile
- tagsFile: optional absolute path of a .json file with that array, or a .csv/.txt file with a header line 'name;dataType;connection;plcTag'
- connection: optional HMI connection for entries with a plcTag but no connection, e.g. 'HMI_Connection_1'
- skipExisting: true to skip names that already exist instead of creating nothing (default false)

Use the CreateHmiTags tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}{OptionalParameter("tags", tags)}{OptionalParameter("tagsFile", tagsFile)}{OptionalParameter("connection", connection)}
- skipExisting: {NormalizeBool(skipExisting)}";
        }

        [McpServerPrompt(Name = "UpdateHmiTags"), Description("Relink existing WinCC Unified HMI tags to other PLC tags")]
        public static string UpdateHmiTags(string softwarePath, string tags = "", string tagsFile = "")
        {
            return $@"Change the PLC tag, connection or data type of existing HMI tags without deleting them, so everything bound to the tag names keeps working. The previous values are backed up.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- tags: JSON array of changes, each with 'name' and the new 'plcTag', 'connection' and/or 'dataType'; empty fields keep their value; empty when using tagsFile
- tagsFile: optional absolute path of a .json file with that array, or a .csv/.txt file with a header line 'name;dataType;connection;plcTag'

Use the UpdateHmiTags tool with these parameters:
- softwarePath: {softwarePath}{OptionalParameter("tags", tags)}{OptionalParameter("tagsFile", tagsFile)}";
        }

        [McpServerPrompt(Name = "CopyHmiScreen"), Description("Copy a WinCC Unified screen with text replacement")]
        public static string CopyHmiScreen(string softwarePath, string screenName, string newScreenName, string replace = "", string screenNumber = "")
        {
            return $@"Copy a WinCC Unified screen to a new screen in the same folder, item by item with properties, texts, tag and script dynamizations, event scripts and faceplate interfaces. 'replace' rewrites text in every copied string, e.g. to make the screen of the next servo from the first one.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: the screen to copy
- newScreenName: name of the new screen
- replace: optional 'from=>to' pairs separated by ';', e.g. 'Servo01=>Servo02;SM118=>SM119'
- screenNumber: optional screen number of the new screen

Use the CopyHmiScreen tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- newScreenName: {newScreenName}{OptionalParameter("replace", replace)}{OptionalParameter("screenNumber", screenNumber)}";
        }

        [McpServerPrompt(Name = "CopyHmiScreens"), Description("Make a numbered series of WinCC Unified screens from one screen")]
        public static string CopyHmiScreens(string softwarePath, string screenName, string newScreenName, string from, string to, string replace = "", string screenNumberStart = "")
        {
            return $@"Make a numbered series of WinCC Unified screens from one screen, e.g. the screens of servo 2..60 from the screen of servo 1. %n% is the number, %n2%/%n3% the number with leading zeros. If the reply has 'nextNumber', call again with from=nextNumber.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: the screen to copy
- newScreenName: name pattern, e.g. '31%n2% SV1%n2%'
- from, to: first and last number
- replace: optional 'from=>to' pairs separated by ';', e.g. 'S{{1}}=>S{{%n%}};Servo_1=>Servo_%n%'
- screenNumberStart: optional screen number of the first new screen

Use the CopyHmiScreens tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- newScreenName: {newScreenName}
- from: {from}
- to: {to}{OptionalParameter("replace", replace)}{OptionalParameter("screenNumberStart", screenNumberStart)}";
        }

        [McpServerPrompt(Name = "CompareHmiScreens"), Description("Compare two WinCC Unified screens item by item")]
        public static string CompareHmiScreens(string softwarePath, string screenName, string otherScreenName, string replace = "", string otherSoftwarePath = "", string maxDifferences = "")
        {
            return $@"Compare two WinCC Unified screens item by item. 'replace' is applied to the first screen first, so a numbered copy can be checked against its pattern.

Common parameter values:
- softwarePath: path of the WinCC Unified HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: the reference screen
- otherScreenName: the screen to compare with it
- replace: optional 'from=>to' pairs separated by ';', e.g. 'S{{1}}=>S{{2}};Servo_1=>Servo_2'

Use the CompareHmiScreens tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- otherScreenName: {otherScreenName}{OptionalParameter("replace", replace)}{OptionalParameter("otherSoftwarePath", otherSoftwarePath)}{OptionalParameter("maxDifferences", maxDifferences)}";
        }

        #endregion

        #region Compare Templates

        [McpServerPrompt(Name = "CompareExports"), Description("Compare two export folders file by file")]
        public static string CompareExports(string pathA, string pathB, string regexName = "")
        {
            return $@"Compare two export folders and list files only in one of them and files that differ, with the changed line range. Ignores the SimaticML export timestamp and line endings. TIA Portal is not needed.

Common parameter values:
- pathA: the first export folder, e.g. before a change
- pathB: the second export folder, e.g. after a change
- regexName: optional regular expression on the relative file path; empty compares every file

Use the CompareExports tool with these parameters:
- pathA: {pathA}
- pathB: {pathB}{OptionalParameter("regexName", regexName)}";
        }

        [McpServerPrompt(Name = "SearchExports"), Description("Search an export folder for a tag, block or comment without TIA Portal")]
        public static string SearchExports(string path, string text, string useRegex = "", string regexName = "", string maxMatches = "")
        {
            return $@"Search the files of an export folder for a tag, block, member or comment text, without TIA Portal. In XML block exports a symbolic access is matched as one path and each match names its network. Results reflect the export date.

Common parameter values:
- path: the export folder
- text: e.g. '""G2U1Assy"".PLC_Ins.Pitch', 'Step70' or a comment word
- regexName: optional regular expression on the file path, e.g. 'G2U1'

Use the SearchExports tool with these parameters:
- path: {path}
- text: {text}{OptionalParameter("useRegex", useRegex)}{OptionalParameter("regexName", regexName)}{OptionalParameter("maxMatches", maxMatches)}";
        }

        [McpServerPrompt(Name = "GetSafetyInfo"), Description("Read the safety program status and the F-block signatures")]
        public static string GetSafetyInfo(string softwarePath, string includeSignatures = "", string groupPath = "", string regexName = "", string saveFile = "", string compareFile = "")
        {
            return $@"Read the safety program status of an F-CPU without changing anything: password and login state, safety settings, runtime groups and the offline signature of every F-block. Keep 'signatureHash' to see later whether any F-block changed (it is not TIA's F-collective signature).

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- groupPath: optional block group, e.g. '005_Safety', to check only the safety folder
- saveFile / compareFile: save the signatures before a change, compare after it (and a compile) to list the changed F-blocks

Use the GetSafetyInfo tool with these parameters:
- softwarePath: {softwarePath}{OptionalParameter("includeSignatures", includeSignatures)}{OptionalParameter("groupPath", groupPath)}{OptionalParameter("regexName", regexName)}{OptionalParameter("saveFile", saveFile)}{OptionalParameter("compareFile", compareFile)}";
        }

        [McpServerPrompt(Name = "GetBlockFingerprints"), Description("Save block fingerprints and later list which blocks changed")]
        public static string GetBlockFingerprints(string softwarePath, string groupPath = "", string regexName = "", string saveFile = "", string compareFile = "", string maxBlocks = "")
        {
            return $@"Read the fingerprints of the program blocks in a group without exporting anything. Save them with 'saveFile'; a later call with that file as 'compareFile' lists the changed, added and removed blocks and which aspect changed (ProgramCode, Interface, Comments, ...).

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- groupPath: optional block group, e.g. '009_Servo'
- saveFile / compareFile: absolute paths of a JSON file

Use the GetBlockFingerprints tool with these parameters:
- softwarePath: {softwarePath}{OptionalParameter("groupPath", groupPath)}{OptionalParameter("regexName", regexName)}{OptionalParameter("saveFile", saveFile)}{OptionalParameter("compareFile", compareFile)}{OptionalParameter("maxBlocks", maxBlocks)}";
        }

        [McpServerPrompt(Name = "GetSoftwareChecksum"), Description("Read the checksum of the compiled program of a PLC")]
        public static string GetSoftwareChecksum(string softwarePath)
        {
            return $@"Read the checksum of the compiled program of a PLC software. Equal checksums mean the same compiled program; it is empty until the program is compiled.

Common parameter values:
- softwarePath: defines the path in the project structure to the software

Use the GetSoftwareChecksum tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "CompareSoftware"), Description("Compare a PLC software with the online PLC or another PLC")]
        public static string CompareSoftware(string softwarePath, string otherSoftwarePath = "", string maxItems = "")
        {
            return $@"Compare a PLC software with the connected PLC (default) or with another PLC software in the project using TIA Portal's own compare, and list what differs. Reads only.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- otherSoftwarePath: empty compares with the online PLC

Use the CompareSoftware tool with these parameters:
- softwarePath: {softwarePath}{OptionalParameter("otherSoftwarePath", otherSoftwarePath)}{OptionalParameter("maxItems", maxItems)}";
        }

        [McpServerPrompt(Name = "ArchiveProject"), Description("Archive the saved project as a .zap file, e.g. before an import")]
        public static string ArchiveProject(string targetDirectory, string archiveName = "", string mode = "")
        {
            return $@"Write the saved project as a TIA Portal archive into a folder as a backup. The open project stays where it is; unsaved changes must be saved first.

Common parameter values:
- targetDirectory: absolute folder path
- mode: 'Compressed' (default) or 'DiscardRestorableDataAndCompressed'

Use the ArchiveProject tool with these parameters:
- targetDirectory: {targetDirectory}{OptionalParameter("archiveName", archiveName)}{OptionalParameter("mode", mode)}";
        }

        [McpServerPrompt(Name = "GetDbStartValues"), Description("List the members of a data block with their start values")]
        public static string GetDbStartValues(string softwarePath, string blockPath, string regexName = "", string maxMembers = "")
        {
            return $@"List the members of a data block (global or instance DB) with data type and start value, including members inside structs, UDTs and arrays. Reads only.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: root-relative path of the data block

Use the GetDbStartValues tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}{OptionalParameter("regexName", regexName)}{OptionalParameter("maxMembers", maxMembers)}";
        }

        [McpServerPrompt(Name = "SetDbStartValue"), Description("Change the start value of one data block member")]
        public static string SetDbStartValue(string softwarePath, string blockPath, string memberPath, string value)
        {
            return $@"Change the start value of one data block member, e.g. 'S[3].OriginOffset', without touching the rest of the DB. The DB is backed up first; compile and download with reinitialization afterwards (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- blockPath: root-relative path of the data block
- memberPath: as listed by GetDbStartValues

Use the SetDbStartValue tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- memberPath: {memberPath}
- value: {value}";
        }

        [McpServerPrompt(Name = "GetTechnologyObjects"), Description("List the technology objects (axes, encoders, PID, ...) of a PLC")]
        public static string GetTechnologyObjects(string softwarePath, string regexName = "")
        {
            return $@"List the technology objects of a PLC software with their type and version. They are not program blocks, so the block tools do not show them.

Common parameter values:
- softwarePath: defines the path in the project structure to the software

Use the GetTechnologyObjects tool with these parameters:
- softwarePath: {softwarePath}{OptionalParameter("regexName", regexName)}";
        }

        [McpServerPrompt(Name = "GetTechnologyObjectParameters"), Description("Read the parameters of a technology object, e.g. axis homing")]
        public static string GetTechnologyObjectParameters(string softwarePath, string objectPath, string regexName = "", string maxParameters = "")
        {
            return $@"Read the configuration parameters of one technology object, e.g. the homing, limit switch, encoder or mechanics settings of an axis. Reads only.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- objectPath: name of the technology object from GetTechnologyObjects
- regexName: e.g. 'Homing|PositionLimits'

Use the GetTechnologyObjectParameters tool with these parameters:
- softwarePath: {softwarePath}
- objectPath: {objectPath}{OptionalParameter("regexName", regexName)}{OptionalParameter("maxParameters", maxParameters)}";
        }

        [McpServerPrompt(Name = "SetTechnologyObjectParameter"), Description("Change one parameter of a technology object, e.g. axis homing")]
        public static string SetTechnologyObjectParameter(string softwarePath, string objectPath, string parameterName, string value)
        {
            return $@"Change one configuration parameter of a technology object in the offline project. Read the current value first with GetTechnologyObjectParameters; the change reaches the PLC only after compile and download.

Common parameter values:
- softwarePath: defines the path in the project structure to the software
- objectPath: name of the technology object from GetTechnologyObjects
- parameterName: exact name, e.g. 'Homing.Mode'

Use the SetTechnologyObjectParameter tool with these parameters:
- softwarePath: {softwarePath}
- objectPath: {objectPath}
- parameterName: {parameterName}
- value: {value}";
        }

        #endregion

        #region Device Write Templates

        [McpServerPrompt(Name = "CreateDevice"), Description("Create a new single-item hardware device")]
        public static string CreateDevice(string typeIdentifier, string name, string groupPath = "")
        {
            return $@"Create a new single-item hardware device in the project.

Common parameter values:
- typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6SL3210-1KE11-8AF2/4.7.14'
- name: name for the new device{OptionalParameter("groupPath", groupPath)}

Use the CreateDevice tool with these parameters:
- typeIdentifier: {typeIdentifier}
- name: {name}";
        }

        [McpServerPrompt(Name = "CreateDeviceWithItem"), Description("Create a hardware device with a station and first sub-item")]
        public static string CreateDeviceWithItem(string typeIdentifier, string name, string deviceItemName, string groupPath = "")
        {
            return $@"Create a new hardware device that has both a station-level name and a first sub-item name in one call - e.g. a CPU station or a drive.

Common parameter values:
- typeIdentifier: a creation-ready catalog TypeIdentifier from FindHardwareCatalogEntries, e.g. 'OrderNumber:6ES7 516-3AN01-0AB0/V2.1'
- name: name for the new device station - this does not become the queryable Device.Name
- deviceItemName: name for the first sub-item - this is what the device is actually found under{OptionalParameter("groupPath", groupPath)}

Use the CreateDeviceWithItem tool with these parameters:
- typeIdentifier: {typeIdentifier}
- name: {name}
- deviceItemName: {deviceItemName}";
        }

        [McpServerPrompt(Name = "DeleteDevice"), Description("Delete a hardware device from the project, dry-run by default")]
        public static string DeleteDevice(string devicePath, string confirm = "false")
        {
            return $@"Delete a hardware device from the project. Dry-run by default - call once to preview, then again with confirm=true to actually delete.

Common parameter values:
- devicePath: full path to the device
- confirm: must be true to actually delete - false (default) only previews

Use the DeleteDevice tool with these parameters:
- devicePath: {devicePath}
- confirm: {NormalizeBool(confirm)}";
        }

        [McpServerPrompt(Name = "CreateDeviceGroup"), Description("Create a new device group (folder) in the project")]
        public static string CreateDeviceGroup(string parentGroupPath, string name)
        {
            return $@"Create a new device group (folder) in the project, for organizing devices.

Common parameter values:
- parentGroupPath: path to the parent group, e.g. 'Group/Subgroup' (empty for the project root)
- name: name for the new group

Use the CreateDeviceGroup tool with these parameters:
- parentGroupPath: {parentGroupPath}
- name: {name}";
        }

        [McpServerPrompt(Name = "DeleteDeviceGroup"), Description("Delete a device group and every device inside it")]
        public static string DeleteDeviceGroup(string groupPath)
        {
            return $@"Delete a device group (folder) from the project - also deletes every device inside it.

Common parameter values:
- groupPath: full path to the group, e.g. 'Group/Subgroup'

Use the DeleteDeviceGroup tool with these parameters:
- groupPath: {groupPath}";
        }

        #endregion

        #region Device Catalog Templates

        [McpServerPrompt(Name = "GetGsdDependencies"), Description("List third-party GSD-based devices in the project")]
        public static string GetGsdDependencies(string devicePath = "")
        {
            return $@"List every third-party (GSD-based) device/device item in the project's hardware config, with its GSD identity.

Common parameter values:{OptionalParameter("devicePath", devicePath)}

Use the GetGsdDependencies tool with these parameters:{OptionalParameter("devicePath", devicePath)}";
        }

        [McpServerPrompt(Name = "GetDeviceMasterCopies"), Description("List master copies in the project's own library")]
        public static string GetDeviceMasterCopies()
        {
            return @"List master copies (saved device/module templates) in the project's own library.

Use the GetDeviceMasterCopies tool (it takes no parameters).";
        }

        [McpServerPrompt(Name = "FindHardwareCatalogEntries"), Description("Search the TIA Portal hardware catalog by text")]
        public static string FindHardwareCatalogEntries(string filter)
        {
            return $@"Search TIA Portal's hardware catalog by a text filter, to find a creation-ready TypeIdentifier for CreateDevice/CreateDeviceWithItem.

Common parameter values:
- filter: an article number or product name fragment, e.g. 'G120C' or '6ES7 510'

Use the FindHardwareCatalogEntries tool with these parameters:
- filter: {filter}";
        }

        #endregion

        #region CAx Templates

        [McpServerPrompt(Name = "ExportCax"), Description("Export the project's hardware config as AutomationML")]
        public static string ExportCax(string exportPath, string devicePath = "")
        {
            return $@"Export the project's hardware config as an AutomationML (.aml) file, for round-tripping with an ECAD tool like EPLAN.

Common parameter values:
- exportPath: directory to write the .aml file into{OptionalParameter("devicePath", devicePath)}

Use the ExportCax tool with these parameters:
- exportPath: {exportPath}";
        }

        [McpServerPrompt(Name = "ImportCax"), Description("Import an AutomationML file back into the project")]
        public static string ImportCax(string importPath, string confirm = "false", string mergeOption = "MoveToParkingLot")
        {
            return $@"Import an AutomationML (.aml) file back into the project - can restructure hardware config. No preview available, so test against a disposable project first.

Common parameter values:
- importPath: full path to the .aml file to import
- confirm: must be true to actually import
- mergeOption: 'MoveToParkingLot' (default, safest), 'OverwriteTiaDevice', or 'RetainTiaDevice'

Use the ImportCax tool with these parameters:
- importPath: {importPath}
- confirm: {NormalizeBool(confirm)}";
        }

        #endregion

        #region Online Connection Templates

        [McpServerPrompt(Name = "GetOnlineState"), Description("Get the engineering station's online connection state")]
        public static string GetOnlineState(string path)
        {
            return $@"Get the engineering station's online connection state for a device or device item. Independent of PLC Run/Stop state.

Common parameter values:
- path: path in the project structure to the device or device item

Use the GetOnlineState tool with these parameters:
- path: {path}";
        }

        [McpServerPrompt(Name = "GoOnline"), Description("Establish the engineering station's online connection")]
        public static string GoOnline(string path)
        {
            return $@"Establish the engineering station's online connection to a device or device item for diagnostics/monitoring. Does not start, stop, or otherwise command the PLC.

Common parameter values:
- path: path in the project structure to the device or device item

Use the GoOnline tool with these parameters:
- path: {path}";
        }

        [McpServerPrompt(Name = "GoOffline"), Description("Disconnect the engineering station's online connection")]
        public static string GoOffline(string path)
        {
            return $@"Disconnect the engineering station's online connection to a device or device item. Does not stop the PLC.

Common parameter values:
- path: path in the project structure to the device or device item

Use the GoOffline tool with these parameters:
- path: {path}";
        }

        #endregion

        #region PRONETA Templates

        [McpServerPrompt(Name = "CompareNetworkCsv"), Description("Compare a PRONETA device CSV against the open project")]
        public static string CompareNetworkCsv(string csvPath)
        {
            return $@"Compare a PRONETA 'Device Table (CSV)' export against the currently open project's devices. Read-only - never writes anything.

Common parameter values:
- csvPath: full path to the PRONETA CSV export file (with 'Include device details' checked)

Use the CompareNetworkCsv tool with these parameters:
- csvPath: {csvPath}";
        }

        #endregion

        #region HMI Templates

        [McpServerPrompt(Name = "GetHmiTagTables"), Description("List HMI tag tables of a Unified Comfort/Advanced panel")]
        public static string GetHmiTagTables(string softwarePath, string regexName = "")
        {
            return $@"Get a list of HMI tag tables from a device's HMI software (Unified Comfort/Advanced Panels only).

Common parameter values:
- softwarePath: path to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'{OptionalParameter("regexName", regexName)}

Use the GetHmiTagTables tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "GetHmiTags"), Description("List tags from a specific HMI tag table")]
        public static string GetHmiTags(string softwarePath, string tagTablePath, string regexName = "")
        {
            return $@"Get a list of tags from a specific HMI tag table (Unified Comfort/Advanced Panels only), including address/PLC-link info.

Common parameter values:
- softwarePath: path to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'
- tagTablePath: full path to the HMI tag table{OptionalParameter("regexName", regexName)}

Use the GetHmiTags tool with these parameters:
- softwarePath: {softwarePath}
- tagTablePath: {tagTablePath}";
        }

        [McpServerPrompt(Name = "GetHmiScreens"), Description("List screens in a Unified Comfort/Advanced panel")]
        public static string GetHmiScreens(string softwarePath, string regexName = "")
        {
            return $@"Get a list of screens in a device's HMI software (Unified Comfort/Advanced Panels only).

Common parameter values:
- softwarePath: path to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'{OptionalParameter("regexName", regexName)}

Use the GetHmiScreens tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "GetComfortScreens"), Description("List screens, templates and pop-ups of a classic WinCC Comfort/Basic panel")]
        public static string GetComfortScreens(string softwarePath, string regexName = "")
        {
            return $@"List the screens, screen templates and pop-up screens of a classic WinCC HMI (Comfort/Basic panels), including subfolders.

Common parameter values:
- softwarePath: the classic HMI software, e.g. 'HMI_1/HMI_RT_1' ('GetProjectTree' shows it as 'HmiTarget'){OptionalParameter("regexName", regexName)}

Use the GetComfortScreens tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "GetComfortScreenItems"), Description("List the items of a classic WinCC Comfort/Basic screen")]
        public static string GetComfortScreenItems(string softwarePath, string screenName, string kind = "", string regexName = "", string includeAttributes = "", string keepExportPath = "")
        {
            return $@"List the items of one classic WinCC screen, template or pop-up with position, size, texts, linked tags and event functions, e.g. to rebuild it in WinCC Unified.

Common parameter values:
- softwarePath: the classic HMI software, e.g. 'HMI_1/HMI_RT_1'
- screenName: as 'GetComfortScreens' lists it; 'Folder/Name' when the name is not unique
- kind: Screen, Template or Popup (optional){OptionalParameter("kind", kind)}{OptionalParameter("regexName", regexName)}{OptionalParameter("includeAttributes", includeAttributes)}{OptionalParameter("keepExportPath", keepExportPath)}

Use the GetComfortScreenItems tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}";
        }

        [McpServerPrompt(Name = "GetHmiDiscreteAlarms"), Description("List discrete alarms of a Unified Comfort/Advanced panel")]
        public static string GetHmiDiscreteAlarms(string softwarePath, string regexName = "")
        {
            return $@"Get a list of discrete (bit-triggered) alarms from a device's HMI software (Unified Comfort/Advanced Panels only).

Common parameter values:
- softwarePath: path to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'{OptionalParameter("regexName", regexName)}

Use the GetHmiDiscreteAlarms tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "GetHmiAnalogAlarms"), Description("List analog alarms of a Unified Comfort/Advanced panel")]
        public static string GetHmiAnalogAlarms(string softwarePath, string regexName = "")
        {
            return $@"Get a list of analog (limit-triggered) alarms from a device's HMI software (Unified Comfort/Advanced Panels only).

Common parameter values:
- softwarePath: path to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'{OptionalParameter("regexName", regexName)}

Use the GetHmiAnalogAlarms tool with these parameters:
- softwarePath: {softwarePath}";
        }

        [McpServerPrompt(Name = "GetHmiTextLists"), Description("List text list names of a Unified Comfort/Advanced panel")]
        public static string GetHmiTextLists(string softwarePath, string regexName = "")
        {
            return $@"Get a list of text list names from a device's HMI software (Unified Comfort/Advanced Panels only). Names only, no entry values.

Common parameter values:
- softwarePath: path to the HMI device/device item, e.g. 'HMI_1/HMI_RT_1'{OptionalParameter("regexName", regexName)}

Use the GetHmiTextLists tool with these parameters:
- softwarePath: {softwarePath}";
        }

        #endregion

        #region Network Source Templates

        [McpServerPrompt(Name = "GetNetworkSource"), Description("Read one block network as SCL text")]
        public static string GetNetworkSource(string softwarePath, string blockPath, string networkIndex = "-1")
        {
            return $@"Read a block's networks as SCL text - the only way to get readable source out of a mixed LAD+SCL block.

Common parameter values:
- softwarePath: path to the PLC software
- blockPath: root-relative path of the block, e.g. '0_OBs/Main'
- networkIndex: 0-based network index to read; -1 (default) reads all networks

Use the GetNetworkSource tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}{OptionalParameter("networkIndex", networkIndex == "-1" ? "" : networkIndex)}";
        }

        #endregion

        // Optional prompt arguments arrive as empty text when the user leaves them out; only real
        // values are passed on to the tool, so the tool's own default applies otherwise.
        private static string OptionalParameter(string name, string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : $"\n- {name}: {value!.Trim()}";
        }

        // MCP prompt arguments are always strings, so boolean flags arrive as text.
        private static string NormalizeBool(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return "false";
            }

            return text.Equals("true", System.StringComparison.OrdinalIgnoreCase)
                || text.Equals("1", System.StringComparison.Ordinal)
                || text.Equals("yes", System.StringComparison.OrdinalIgnoreCase)
                    ? "true"
                    : "false";
        }
    }
}

