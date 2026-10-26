# TIA-Portal MCP-Server

A MCP server which connects to Siemens TIA Portal.

This is an independent fork of
[heilingbrunner/tiaportal-mcp](https://github.com/heilingbrunner/tiaportal-mcp) (MIT licensed,
see [LICENSE.txt](LICENSE.txt)). It started by rebasing onto that project's current structure and
has since diverged with its own feature set below; it no longer tracks upstream commit-for-commit.

## Features

- Connect to a TIA Portal instance, including several instances at once (`GetPortals`/`portalId`)
- Browse and interact with TIA Portal projects
- Perform basic project operations from within VS Code
- Read the full PLC software: program blocks, PLC data types, tags and constants, watch and
  force tables, external source files, and cross references
- Optionally create, rename, delete, move and import project objects (see [Write mode](#write-mode))
- Read and write network topology (subnets, IP addresses, PROFINET/PROFIBUS wiring) and compare it
  against a live PRONETA scan
- Read and create hardware devices and device groups from the hardware catalog or master copies
- Export/import the hardware configuration as AutomationML (CAx) for round-tripping with an ECAD
  tool like EPLAN
- Read WinCC Unified HMI tag tables, screens, alarms and text lists, and create/edit screens,
  screen items, faceplates and HMI tags. Classic (non-Unified) WinCC Comfort/Basic panels are not
  supported by this Openness version - see [Tools](#tools)
- Connect directly to a live CPU over S7CommPlus (experimental, bypasses Openness entirely) to
  browse symbols, read/write live tag values and poll active alarms - independent of any open TIA
  Portal session
- "Comfort" tools that reduce multi-step workflows to one call: `GetPlcSummary`,
  `ResolveObjectPath`, `WhereUsed`, `FindInCode` (full-text search across block/type source),
  `PreviewImport`, `GetBlockSource`/`GetTypeSource`/`GetNetworkSource`, `OpenTiaProject`,
  `CompareExports` (diff two export folders)
- `GetState` reports progress for a long-running call (e.g. `FindInCode` on a large PLC) still in
  flight on the same instance, without waiting for it to finish - useful for telling "still
  working" apart from "looks stuck"

## Command Line Arguments

| Argument                  | Description                                                               |
| ------------------------- | ------------------------------------------------------------------------- |
| `--tia-major-version <n>` | TIA Portal major version to bind against. Default: the version the exe was built for (`20`, or `21` with `-p:TiaVersion=21`). |
| `--logging <1\|2\|3>`     | `1` stderr, `2` debug output, `3` Windows event log. Omit for no logging. |
| `--doctor`                | Print the environment report and exit without starting the MCP server.    |
| `--allow-write`           | Register the project-mutating tools. Omitted by default; see below.       |
| `--project-path <file>`   | Preset for the `path` argument of `OpenProject` / `OpenTiaProject`. See below. |
| `--software-path <path>`  | Preset for the `softwarePath` tool argument, e.g. `PLC_1`. See below.     |
| `--export-path <path>`    | Preset for the `exportPath` tool argument. See below.                     |
| `--preserve-path <bool>`  | Preset for the `preservePath` tool argument (`true` or `false`). See below. |
| `--backup-path <path>`    | Folder for the XML backups taken before a delete or replacing import. Default `%TEMP%\tiaportal-mcp-backups`. |
| `--with-dependencies <bool>` | Preset for the `withDependencies` argument of the `ExportSource*` tools (`true` or `false`). See below. |

## Presetting tool arguments

`softwarePath`, `exportPath`, `preservePath` and `withDependencies` (the `ExportSource*` tools) appear in many tools, `path` (the project file) in
`OpenProject` and `OpenTiaProject`. Set them once in the `args`
of the MCP server entry in your client's JSON configuration and the model no longer has to pass them:

```json
"args": ["--project-path", "D:\\Projects\\Plant\\Plant.ap21", "--software-path", "PLC_1", "--export-path", "D:\\Export", "--preserve-path", "true"]
```

- A preset argument becomes optional in `tools/list`; a tool call that omits it (or sends `null`) uses
  the preset. A value passed in the call always overrides the preset.
- Without a preset nothing changes: the argument stays required.
- `GetState` lists the active presets in `Presets`.
- A single `--software-path` only fits a project with one PLC; pass `softwarePath` explicitly otherwise.
- Prompts (slash commands) are not affected.

## Write mode

The server is read-only unless it is started with `--allow-write`.

- Without the flag the 68 project-mutating tools are **not registered at all**, so they never
  appear in `tools/list`. A model cannot call what it cannot see, and the tool list stays small.
- With the flag they are registered and annotated `destructiveHint: true`, so a client can still
  prompt before each call.
- `GetState` and the `--doctor` report both expose `AllowWrite`, so a client can tell whether the
  tools are missing by configuration rather than by version.
- Write operations change the project **in memory only**. Every write response says so and names
  the tool that persists it: `SaveProject`, or `SaveSession` when a multiuser local session is open.

Export tools are intentionally *not* gated. `ExportXmlBlock`, `ExportXmlTagTable`, `ExportXmlWatchTable`,
`ExportCax` and friends only write files on the machine running the server; they never modify the
project. They are annotated `destructiveHint: true` because they can overwrite files on disk.

A few other tools also sit outside `--allow-write` because they don't mutate the open project
either, and gate themselves a different way:

- `CompileSoftware`, `CompileCodeBlock` and `SaveProject`/`SaveAsProject` act on the project but are considered basic
  operations rather than content edits (this matches upstream's own choice, flagged as an open
  question - see `TODO.md`).
- `GoOnline`/`GoOffline` only touch the engineering station's diagnostic connection, never the PLC
  program; their own description asks the model to confirm with the user before use against a
  real (non-PLCSIM) PLC.
- The `*PlcDirect` (S7CommPlus) tools bypass Openness/the project entirely, so `--allow-write`
  doesn't apply to them. `WritePlcTagValueDirect` instead requires `confirm=true` *and*
  `confirmPlcSimOnly=true` on every call, and refuses safety-tagged names - see its tool
  description for the exact rules.

To enable write mode, add the argument to your client configuration, for example
`"args": ["--allow-write"]`.

Before `DeleteBlock`, `DeleteType`, `EditBlockNetwork`, `EditBlockInterface`, `EditTypeInterface`, `SetDbStartValue`, and an `ImportXmlBlock`/`ImportXmlType` or source import (`ImportSourceBlock`, `ImportSources`, ...) that replaces an
object of the same name, the current object is exported as XML to
`<backup path>\<yyyyMMdd>\<time>_<tool>_<object>.xml` and the reply names the file (`backup`).
Re-importing that file restores the old version even after the project was saved. An object TIA
will not export (inconsistent) is changed anyway and the reply says why there is no backup.
WinCC Unified screens cannot be exported, so `SetHmiTagDynamization`, `SetHmiScriptDynamization`,
`SetHmiFlashingDynamization` and `DeleteHmiDynamization` save the dynamization they change as a
`.json` file in the same folder instead (and return it as `previous`).

An import refuses to replace a block or PLC data type that was changed in TIA Portal after the
import file was last written: importing that older file would silently undo the newer change. The
error names both times. Export the object again and edit the new file, or pass `allowOlderFile`
`true` when the older file is really meant to win.

Every write tool call is also logged as one JSON line in `<backup path>\audit\<yyyyMMdd>.jsonl` (time,
tool, arguments, ok or error, reply, backup file). `GetChangeLog` lists a day's entries, e.g. to write
a change summary.

## Tools

185 tools total. Read-only/ungated tools (102) are always available.

| Area                          | Tools                                                                                                                                                                                                  |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Portal and state              | `Connect`, `Disconnect`, `GetPortals`, `GetState`, `Doctor`, `GetChangeLog`, `GetProjectStatus`                                                                                                                                          |
| Project and session           | `GetProject`, `OpenProject`, `SaveProject`, `SaveAsProject`, `ArchiveProject`, `CloseProject`                                                                                                                            |
| Devices                       | `GetProjectTree`, `GetDevices`, `GetDeviceInfo`, `GetDeviceItemInfo`, `GetGsdDependencies`, `GetDeviceMasterCopies`, `FindHardwareCatalogEntries`                                                     |
| Network topology              | `GetSubnets`, `GetNetworkInterfaceInfo`                                                                                                                                                               |
| CAx (AutomationML)            | `ExportCax`                                                                                                                                                                                            |
| PRONETA                       | `CompareNetworkCsv`                                                                                                                                                                                    |
| Engineering-station online    | `GetOnlineState`, `GoOnline`, `GoOffline`                                                                                                                                                             |
| Direct PLC (S7CommPlus, experimental) | `ConnectPlcDirect`, `DisconnectPlcDirect`, `BrowsePlcTagsDirect`, `ReadPlcTagValuesDirect`, `WritePlcTagValueDirect`, `GetActivePlcAlarmsDirect`                                             |
| PLC software                  | `GetSoftwareInfo`, `GetSoftwareTree`, `CompileSoftware`, `CompileCodeBlock`, `CompareSoftware`, `GetBlockFingerprints`, `GetSoftwareChecksum`, `GetSafetyInfo`                                                                                                                                            |
| Technology objects            | `GetTechnologyObjects`, `GetTechnologyObjectParameters`                                                                                                                                              |
| Blocks                        | `GetBlocks`, `GetBlockInfo`, `GetBlocksWithHierarchy`, `GetBlockInterface`, `GetDbStartValues`, `ExportXmlBlock`, `ExportXmlBlocks`                                                                                        |
| Types                         | `GetTypes`, `GetTypeInfo`, `ExportXmlType`, `ExportXmlTypes`                                                                                                                                           |
| Tags and constants            | `GetTagTables`, `GetTagTableInfo`, `GetTags`, `GetTagInfo`, `GetConstants`, `ExportXmlTagTable`                                                                                                        |
| Watch and force tables        | `GetWatchTables`, `GetWatchTableInfo`, `GetForceTables`, `ExportXmlWatchTable`                                                                                                                         |
| External sources              | `GetExternalSources`, `GetExternalSourceInfo`, `ExportSourceBlock`, `ExportSourceType`, `ExportSourceBlocks`, `ExportSourceTypes`, `ExportSources`                                                    |
| Cross references              | `GetCrossReferences`                                                                                                                                                                                   |
| Block documents (V20+)        | `ExportAsDocuments`, `ExportBlocksAsDocuments`                                                                                                                                                         |
| Type documents (V21+)         | `ExportTypeAsDocuments`, `ExportTypesAsDocuments`                                                                                                                                                      |
| HMI, WinCC Unified (read)     | `GetHmiTagTables`, `GetHmiTags`, `GetHmiScreens`, `GetHmiScreenItems`, `GetHmiDynamizations`, `GetHmiDiscreteAlarms`, `GetHmiAnalogAlarms`, `GetHmiAlarmClasses`, `GetHmiTextLists`, `CompareHmiScreens`, `GetHmiTagUsage`, `GetHmiScreenItemProperties`                                      |
| HMI, classic WinCC (read)     | `GetComfortScreens`, `GetComfortScreenItems` (Comfort/Basic panels; items read from an XML export of the screen)                                                                                        |
| Understand and read (comfort) | `GetPlcSummary`, `ResolveObjectPath`, `WhereUsed`, `FindInCode`, `GetBlockSource`, `GetNetworkSource`, `GetTypeSource`, `PreviewImport`, `OpenTiaProject`, `ExportPlcAsDocuments`, `CompareExports`, `SearchExports`    |

Write tools (83) require `--allow-write` (see [Write mode](#write-mode) for the handful that
don't, and why).

| Area                       | Tools                                                                                                                                                                                          |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Block and type groups      | `CreateBlockGroup`, `DeleteBlockGroup`, `CreateTypeGroup`, `DeleteTypeGroup`                                                                                                                    |
| Blocks and types           | `EditBlockNetwork`, `EditBlockInterface`, `EditTypeInterface`, `SetDbStartValue`, `DeleteBlock`, `RenameBlock`, `DeleteType`, `RenameType`, `CreateFB`, `CreateInstanceDB`                                                                                                        |
| Copy and move              | `CopyBlock`, `MoveBlock`, `CopyType`, `MoveType`                                                                                                                                                |
| Tag tables                 | `CreateTagTable`, `DeleteTagTable`, `RenameTagTable`, `CreateTagTableGroup`, `DeleteTagTableGroup`, `ImportXmlTagTable`                                                                         |
| Technology objects         | `SetTechnologyObjectParameter`                                                                                                                                                                   |
| Tags and constants         | `CreateTag`, `UpdateTag`, `DeleteTag`, `CreateUserConstant`, `UpdateUserConstant`, `DeleteUserConstant`                                                                                         |
| Watch tables                | `CreateWatchTable`, `RenameWatchTable`, `DeleteWatchTable`, `CreateWatchTableGroup`, `DeleteWatchTableGroup`, `ImportWatchTable`                                                                |
| External sources           | `CreateExternalSourceFromFile`, `DeleteExternalSource`, `CreateExternalSourceGroup`, `DeleteExternalSourceGroup`, `ImportExternalSource`, `ImportSourceBlock`, `ImportSourceType`, `ImportSourceBlocks`, `ImportSourceTypes`, `ImportSources` |
| Block and type import      | `ImportXmlBlock`, `ImportXmlType`, `ImportFromDocuments`, `ImportBlocksFromDocuments`                                                                                                           |
| Type documents (V21+)      | `ImportTypeFromDocuments`, `ImportTypesFromDocuments`                                                                                                                                           |
| Devices and network        | `CreateDevice`, `CreateDeviceWithItem`, `DeleteDevice`, `CreateDeviceGroup`, `DeleteDeviceGroup`, `SetIpAddress`, `ConnectToSubnet`, `DisconnectFromSubnet`                                      |
| CAx (AutomationML)         | `ImportCax`                                                                                                                                                                                     |
| HMI, WinCC Unified (write) | `CreateHmiScreen`, `DeleteHmiScreen`, `CreateHmiScreenItem`, `UpdateHmiScreenItem`, `SetHmiScreenItemProperties`, `CopyHmiItemStyle`, `SetHmiItemEventScript`, `DeleteHmiItemEventScript`, `DeleteHmiScreenItem`, `BindHmiTag`, `SetHmiTagDynamization`, `SetHmiScriptDynamization`, `SetHmiFlashingDynamization`, `DeleteHmiDynamization`, `CreateHmiFaceplate`, `SetHmiFaceplateInterface`, `CopyHmiScreen`, `CopyHmiScreens`, `CreateHmiTag`, `CreateHmiTags`, `UpdateHmiTags` |

`GetSoftwareTree` accepts a `sections` argument - any comma separated subset of
`blocks,types,tags,watch,sources`, default `all` - to keep the output small on a large PLC.
`GetCrossReferences` accepts `maxDepth` (1-3, default 1) for the same reason. `GetBlocks`/`GetTypes`
default to `includeAttributes=false` and accept `offset`/`limit` paging and (`GetBlocks` only)
`modifiedSince`, for the same reason on a large PLC - reading every object's ~30 Openness
attributes dominates their runtime, so pass `includeAttributes=true` only when you actually need
them (`GetBlockInfo`/`GetTypeInfo` always return them for a single object).

WinCC Unified HMI tools only work against WinCC Unified devices (Unified Comfort/Advanced panels
and Unified Runtime); classic, non-Unified WinCC Comfort/Basic panels are not supported by this
Openness version - see each tool's description. `ConnectPlcDirect` and friends
bypass Openness/TIA Portal entirely over a reverse-engineered protocol; see their descriptions and
[Known Limitations](#known-limitations).

Paths used by these tools are **root-relative**: `1_Tests/FC_Block_1`, not
`Program blocks/1_Tests/FC_Block_1`. Use `GetProjectTree` and `GetSoftwareTree` to discover them.

## Prompts (slash commands)

Every tool has a prompt of the same name that you can start as a slash command, for example
`/mcp__tia-mcp-server__OpenProject D:\Siemens\Test\TestProject1_V21.ap21`. The part after `mcp__` is
the server name from your MCP configuration. Claude Code shows the prompt description and the
argument names, matches the typed values to the arguments by position and requires every argument
without a default. A prompt only sends text to the model, which then calls the tool.

A value with spaces goes in **double quotes**, for example a PC based PLC software or an export
folder:

```
/mcp__tia-mcp-server__ExportXmlBlocks "PC-System_1/Software PLC_1" "D:\My Export"
```

Claude Code itself splits the text at every space and drops the words beyond the last argument. The
server joins the pieces of a quoted value again, so the value reaches the tool unchanged; `""`
passes an empty value. All typed words together must not exceed the number of arguments of the
prompt, so a prompt with few arguments (for example `GetSoftwareInfo`, which only has
`softwarePath`) cannot take a value of several words: write a normal request instead of the slash
command.

One prompt has no tool of its own: `ModifyBlockWorkflow` (`softwarePath`, `blockPath`, `change`) walks
the model through a block change step by step - go offline, read the block, save fingerprints, change it
with `EditBlockNetwork` or `SetDbStartValue` (LAD preview first), compile only that block, compare
fingerprints - and tells it to stop and show the error after two failures of the same step. Quote the
change text, e.g. `/mcp__tia-mcp-server__ModifyBlockWorkflow PLC_1 "009_Servo/FB_PitchHome_Inch" "set the step to 0.5 mm"`.
The server instructions carry the same routine in short form.

## Resources

- [TIA Portal Openness API Documentation](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows)
- [TIA Portal Openness API Overview](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api)
- [TIA Portal Openness API for automation of engineering workflows](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows)
- [TIA Portal Openness API for automation of engineering workflows - Export/Import Documentation](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import)

## Requirements

- __Siemens TIA Portal V20 or V21__ installed and running on your machine (one build per version, see [TIA-Portal Versions](#tia-portal-versions))
- User must be in Windows User Group `Siemens TIA Openness` (log off and on again after adding)
- To build: git, .NET SDK 8 or newer, .NET Framework 4.8 Developer Pack
- If TIA Portal is not installed in the default folder, set the user environment variable `TiaPortalLocation`, e.g. `C:\Program Files\Siemens\Automation\Portal V21`

### Install with the script

`install/install-tiaportal-mcp.ps1` does all of the above on a fresh PC. On a PC without git or the .NET
SDK, run it in a PowerShell opened with "Run as administrator": run without admin rights, it does not
try to install them (the Windows confirmation does not appear inside AI tools or remote shells, so
the install failed silently) and prints the `winget` lines to paste into an admin PowerShell instead.
With the tools present, a normal PowerShell is enough:

```powershell
powershell -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/p-7347/tiaportal-mcp/rebase/upstream-base/install/install-tiaportal-mcp.ps1 -OutFile $env:TEMP\install-tiaportal-mcp.ps1; & $env:TEMP\install-tiaportal-mcp.ps1"
```

It installs missing build tools with winget, detects TIA Portal V20/V21 and builds for it (both
installed: builds both and registers them as `tiaportal-mcp-v20` and `tiaportal-mcp-v21`), checks the
`Siemens TIA Openness` group (`-AddToOpennessGroup` adds you), clones or updates the source in
`C:\Tools\tiaportal-mcp`, runs `--doctor`, registers the server read-only with Claude Desktop,
Claude Code and Antigravity when they are installed, and prints an `mcpServers` example for other
clients. Options: `-TiaVersion 20|21`, `-InstallDir`, `-AllowWrite`, `-NoInstall`, `-NoRegister`.
Run it again to update.

### Diagnose the environment

Run the server with `--doctor` to check all of the above without starting the MCP server:

```text
> TiaMcpServer.exe --doctor
Diagnose:
├─ Connected = False
├─ Project: No project open
├─ Active Version: V21
├─ Installed TIA Portal versions:
│  └─ V21: C:\Program Files\Siemens\Automation\Portal V21
│     ├─ Engineering: OK
│     └─ Portal:      OK
├─ User in 'Siemens TIA Openness' user group: True
└─ Write mode (--allow-write): disabled, read-only tools only
```

The same report is available to MCP clients through the `Doctor` tool, which additionally returns
the findings as structured content. Both are read-only: they never connect to TIA Portal, open a
project, or change user group membership.

## TIA-Portal Versions

- One exe serves one Openness version. V21 split `Siemens.Engineering.dll` into several assemblies with a new identity, so a V20 build cannot attach to V21 and the other way round.
- __V20__ is the default build: `dotnet build -c Release`.
- For __V21__ build on a PC with TIA Portal V21 installed: `dotnet build -c Release -p:TiaVersion=21`. The exe lands in `bin\V21\Release\net48` and binds to V21 without `--tia-major-version`. This build has not been tested against a live V21 installation yet.
- Starting an exe with a newer `--tia-major-version` than it was built for stops with a message saying which build is needed, instead of crashing inside Openness. `--doctor` shows the build version next to the active one.
- Export as documents (.s7dcl/.s7res) via `ExportAsDocuments`/`ExportBlocksAsDocuments` requires TIA Portal V20 or newer.
- Import from documents (.s7dcl/.s7res) via `ImportFromDocuments`/`ImportBlocksFromDocuments` also requires TIA Portal V20 or newer.
- The same for PLC data types - `ExportTypeAsDocuments`, `ExportTypesAsDocuments`,
  `ImportTypeFromDocuments`, `ImportTypesFromDocuments` - requires TIA Portal **V21** or newer:
  Openness only added `PlcType.ExportAsDocuments` and `PlcTypeComposition.ImportFromDocuments`
  in V21.

## SIMATIC Source Documents

A source document is the readable, git-diffable form of an object: `<Name>.s7dcl` holds the
declaration and body as SCL/LAD/STL text, the optional `<Name>.s7res` holds comments and
language resources. Every other export in this server writes SimaticML XML instead, which
diffs poorly.

The file names come from TIA Portal, not from this server: an export response lists the files
that were actually written, and a batch import discovers a document set by base name rather
than assuming one extension. Tag tables and watch tables have no document API in Openness V21
and remain XML-only.

With `preservePath` the export mirrors the project tree below the system folder - `Program
blocks` for blocks, `PLC data types` for types - using the folder name as TIA Portal reports it
in the current interface language. `ImportTypeFromDocuments` and `ImportTypesFromDocuments`
accept that folder name back as a leading segment of `groupPath`, so an export can be fed
straight back in.

A PLC data type name is unique across the whole PLC, not just within its group. Importing a
name that already exists into a *different* group therefore fails with "an object with the name
... already exists in the plc", even with `importOption: Override`; point `groupPath` at the
group the type already lives in to replace it.

## Known Limitations

- As of 2025-09-02: Importing Ladder (LAD) blocks from SIMATIC SD documents requires the companion `.s7res` file to contain en-US tags for all items; otherwise import may fail. This is a known limitation/bug in TIA Portal Openness.
 - `ExportXmlBlock` requires a fully qualified `blockPath` like `Group/Subgroup/Name`. If only a name is provided, the tool fails with an error result that may include suggestions for likely full paths.
- __`GetBlocks`/`GetTypes`/`FindInCode` enumeration cost doesn't shrink with a name filter.__
  Measured on a ~1000-block project: `GetBlocks` with no filter took ~75s, `GetTypes` ~10s. The
  regex filter (`regexName`/`nameFilter`) is applied per object only after its name has already
  been read from Openness, so a narrow filter still pays the cost of walking every object in the
  PLC - it only shrinks the *result*, not the time the call takes. `FindInCode` additionally
  exports and reads the source of every matched object, so it can run for minutes on a large,
  unfiltered PLC. `GetState` reports progress on whichever call is doing this so you can tell it
  apart from a hang (see [MCP Protocol](#mcp-protocol)); there is no automatic timeout/cancel.
- __All Openness calls on this server are serialized behind one process-wide lock__
  (`Operation.Run`), including calls that target *different* TIA Portal instances. A request
  stuck for a long time on one instance (the point above, or a modal dialog in TIA Portal) can
  therefore also delay unrelated requests to another attached instance, not just to its own.
  `GetState`'s busy-timeout message says so when it applies.
- __The `*PlcDirect` (S7CommPlus) tools are experimental__, built on a reverse-engineered,
  community-maintained protocol implementation rather than a Siemens-documented one. Prefer
  pointing them at PLCSIM or a non-critical CPU until proven reliable for your use case; see each
  tool's own description for firmware/version requirements and safeguards.

### Limits imposed by the Openness API itself

These are not gaps in this server - the underlying API offers no operation for them.

- __No move or copy for blocks and types.__ Openness only creates a block or type from a library
  master copy or type version (`CreateFrom`), not from another block. `CopyBlock`, `MoveBlock`,
  `CopyType` and `MoveType` are therefore composed from export, import and (for a move) deleting the source once the import
  succeeds. Two consequences are visible: the object must be consistent, because TIA Portal
  refuses to export an inconsistent one, and its block number travels with it, so importing
  into the same PLC can hit a number collision. Renaming during a copy is not offered - it
  would mean rewriting the exported XML.
- __No generic "create block".__ Openness creates only FBs and instance DBs directly (`CreateFB`,
  `CreateInstanceDB`). Every other block arrives through an import: XML (`ImportXmlBlock`), an
  SCL/DB/STL source that TIA Portal compiles into blocks (`ImportSourceBlock`), or documents
  (`ImportFromDocuments`, V20+). To change an existing block there is no need to rebuild its
  XML by hand: `EditBlockNetwork` replaces, inserts or deletes one network (from XML or SCL
  text), `EditBlockInterface` adds, changes or deletes interface members, several in one import with `moreMembers` and optionally compiled in the same call (`EditTypeInterface` the same for UDT members), and `SetDbStartValue`
  changes one DB start value.
- __Read-only objects.__ System constants cannot be created or changed, the force table cannot
  be created or deleted (the system owns one per PLC), the default tag table cannot be deleted,
  and the system groups (`Program blocks`, `PLC data types`, `PLC tags`, ...) cannot be renamed
  or deleted. These all fail with a `NotSupported` message rather than an opaque Openness error.
- __No cross references__ for watch tables, force tables or external sources; `GetCrossReferences`
  reports `NotSupported` for them.
- __Watch table entries__ cannot be created or deleted one at a time. The Openness composition
  holding them exposes only a comment-row creator and entries have no delete. To change entries,
  export the table (`ExportXmlWatchTable`), edit the XML and import it back (`ImportWatchTable`
  replaces the table of the same name).
- __Safety programs.__ F-blocks and safety tags reject most edits, sometimes requiring the safety
  password. The server does not pre-guess Siemens' rules: the underlying error is passed through
  with its original message.
- __Know-how protected__ blocks and types are rejected before any edit (including
  `EditBlockNetwork` and `SetDbStartValue`), with a message asking you to remove the protection
  in TIA Portal first.

## Testing

- See `tests/TiaMcpServer.Test/README.md` for environment prerequisites and test asset setup.
- Standard command: `dotnet test` (run from the repo root).
- Test execution policy: offer to run tests, but only execute after explicit user confirmation. Details in `AGENTS.md`.

## Contributing

- Rules for this fork (branches, pull requests, adding a tool, testing on a real project) are in `AGENTS.md`; open work is in [docs/BACKLOG.md](docs/BACKLOG.md).
- See `agents.md` for guidance on working with agentic assistants and the test execution policy (offer to run tests only with explicit user confirmation).

## Error Handling

- The Portal layer throws `PortalException` with a short message and `PortalErrorCode`
  (`NotFound`, `InvalidParams`, `InvalidState`, `ExportFailed`, `ImportFailed`, `CreateFailed`,
  `DeleteFailed`, `RenameFailed`, `NotSupported`, `WriteDisabled`), and attaches `softwarePath`, `blockPath`, `exportPath` in `Exception.Data` while preserving `InnerException` on export failures.
- The MCP layer rethrows these as `McpException`. Since SDK 2.x, an `McpException` thrown from a tool is returned to the client as a `CallToolResult` with `isError: true` and the message as text content, rather than as a JSON-RPC error, so the model can read the reason and self-correct. For `ExportFailed` the message includes a concise reason from the underlying error; for `NotFound` it may suggest likely full block paths if a bare name was provided.
- Consistency required: TIA Portal never exports inconsistent blocks/types. Single export returns `InvalidParams` with a message to compile first. Bulk export skips inconsistent items and returns them in an `Inconsistent` list alongside `Items`.
- Standardization: Exception context metadata is attached in a single catch per portal method right before rethrow, not at inline throw sites. See `docs/error-model.md`.
- Since 0.2.0 this is implemented once, in the `Operation.Run` helper, which every portal method
  added for tags, watch tables, external sources, cross references and the write operations
  routes through. The older export/import methods still carry their hand-written equivalent of
  the same block; migrating them is tracked in `TODO.md`.
- `Operation.Run` also serializes all Openness calls behind a lock. Openness objects are not
  thread-safe and the MCP SDK may dispatch tool calls concurrently; with write tools enabled an
  unsynchronized race could corrupt project state rather than merely return stale data.

## MCP Protocol

- Built on the [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) .NET SDK **2.2.0**.
- Protocol revisions negotiated during `initialize`: `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25` (the SDK picks the highest the client also supports).
- Every tool advertises a human-readable `title` and behaviour annotations (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`).
- 165 of the 185 tools publish an `outputSchema` and return `structuredContent` (87 of 102
  read-only, 78 of 83 write).
- Long-running export/import tools report progress via `notifications/progress` when the client supplies a `progressToken`.
- Independently of that, `GetState` can report the progress of another call still running on the
  same TIA Portal instance (tool name, processed/total, current object, time since last progress)
  without waiting for it to finish - see [Known Limitations](#known-limitations) for why a call
  can take much longer than it looks like it should.
- Tool failures are returned as tool results with `isError: true` (not JSON-RPC errors), so the model can read the message and retry.

## Transports

- Supported today: `stdio`
  - Program wires `AddMcpServer().WithStdioServerTransport()`.
  - For stdio, logs must go to stderr to avoid corrupting JSON-RPC.
- Available via SDK: `stream` (custom streams)
  - The SDK exposes `WithStreamServerTransport(Stream input, Stream output)` which can be used to host over TCP sockets or other streams.
  - Not wired in this repo yet.
- HTTP/Streamable HTTP: not implemented yet
  - `ModelContextProtocol` 2.2.0 ships its Streamable HTTP server transport in `ModelContextProtocol.AspNetCore`, which targets .NET 8+.
  - This server targets `net48` (required by TIA Openness), so Streamable HTTP cannot be hosted from this process.
  - A separate .NET 8+ proxy process would be required to expose this server over HTTP.

## Client configuration

The server runs locally over `stdio`, so every client starts `TiaMcpServer.exe` itself. Use the
absolute path of the executable (`<path-to>` below) and double the backslashes in JSON. All
arguments from [Command Line Arguments](#command-line-arguments) go into `args`; the presets
(`--project-path`, `--software-path`, `--export-path`, ...) and `--allow-write` are optional.

### GitHub Copilot (VS Code)

- Workspace file `.vscode/mcp.json` (or `MCP: Open User Configuration` for all workspaces). The
  root key is `servers`. Example with the VS Code extension
  [TIA-Portal MCP-Server](https://marketplace.visualstudio.com/items?itemName=JHeilingbrunner.vscode-tiaportal-mcp)
  and TIA-Portal V21:

  ```json
  {
    "servers": {
      "tia-mcp-server": {
        "type": "stdio",
        "command": "c:\\Users\\<user>\\.vscode\\extensions\\jheilingbrunner.vscode-tiaportal-mcp-<version>\\srv\\net48\\TiaMcpServer.exe",
        "args": [
          "--tia-major-version", "21",
          "--project-path", "D:\\Projects\\Plant\\Plant.ap21",
          "--software-path", "PLC_1",
          "--export-path", "${workspaceFolder}\\export",
          "--preserve-path", "true",
          "--allow-write"
        ],
        "env": {}
      }
    }
  }
  ```

  Then switch Copilot Chat to *Agent* mode and enable the server's tools in the tools picker.

### Claude Code

- Project scope, file `.mcp.json` in the repository root (checked in, shared with the team). The
  root key is `mcpServers`:

  ```json
  {
    "mcpServers": {
      "tia-mcp-server": {
        "type": "stdio",
        "command": "<path-to>\\TiaMcpServer.exe",
        "args": [
          "--tia-major-version", "21",
          "--project-path", "D:\\Projects\\Plant\\Plant.ap21",
          "--software-path", "PLC_1",
          "--export-path", "${workspaceFolder}\\export",
          "--preserve-path", "true",
          "--allow-write"
        ],
        "env": {}
      }
    }
  }
  ```

- The same from the command line (`--scope user` for all projects, `--scope project` writes
  `.mcp.json`, `--scope local` is the default and private). Everything after `--` is the command:

  ```powershell
  claude mcp add tia-mcp-server --scope project -- "<path-to>\TiaMcpServer.exe" --tia-major-version 21 --project-path "D:\Projects\Plant\Plant.ap21" --software-path PLC_1 --export-path "D:\Export"
  ```

  Check it with `claude mcp list` or `/mcp` inside Claude Code.

### Gemini CLI

- `~/.gemini/settings.json` (user) or `.gemini/settings.json` in the project (project scope). The
  root key is `mcpServers`:

  ```json
  {
    "mcpServers": {
      "tia-mcp-server": {
        "command": "<path-to>\\TiaMcpServer.exe",
        "args": [
          "--tia-major-version", "21",
          "--project-path", "D:\\Projects\\Plant\\Plant.ap21",
          "--software-path", "PLC_1",
          "--export-path", "D:\\Export",
          "--preserve-path", "true"
        ],
        "env": {},
        "timeout": 120000
      }
    }
  }
  ```

  `timeout` is in milliseconds; raise it for long exports or compiles. Check it with `/mcp` inside
  Gemini CLI. Do not set `"trust": true` together with `--allow-write`, because that skips the
  confirmation prompt for the project-changing tools.

### Claude Desktop

- Create/Edit to add/remove server to `C:\Users\<user>\AppData\Roaming\Claude\claude_desktop_config.json`:

  ```json
  {
    "mcpServers": {
      "vscode-tiaportal-mcp": {
        "command": "<path-to>\\TiaMcpServer.exe",
        "args": [],
        "env": {}
      }
    }
  }
  ```
