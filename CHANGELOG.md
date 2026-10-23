# Change Log

## [Unreleased]

### Changed

- `Doctor` compares the source with the branch the exe was built from on GitHub (e.g. `rebase/upstream-base`) instead of always `main`.
- `EditBlockNetwork`: a replaced network keeps its title and comment when the new network has none. A network written with `sclFile` never has one, so replacing a network that way used to drop its title. New optional argument `title` (at the end) sets the title of a replaced or inserted network. Existing arguments are unchanged.
- The server instructions (sent when the server starts) and `Connect` mention when the running server build is more than 7 days old (by the exe's file time) and says how to update (close the MCP client, run `install\install-tiaportal-mcp.ps1` again or `git pull` and rebuild). It only informs; nothing is blocked. The server instructions ask the client to pass this on to the user once. The response shape is unchanged.
- `install/install-tiaportal-mcp.ps1`: without admin rights it no longer tries to install git, the .NET SDK or the 4.8 Developer Pack (the Windows confirmation never appeared in AI tools or remote shells, so the install failed silently); it prints the `winget` lines for an admin PowerShell instead. winget installs use `--source winget` and report their exit code, and the default Git and .NET install folders are searched even before PATH picks them up.
- `EditBlockNetwork` rejects a know-how protected block up front with the same message as the other edit tools. README "Limits imposed by the Openness API itself" updated: other blocks than FBs and instance DBs also arrive through `ImportSourceBlock` and `ImportFromDocuments`, `EditBlockNetwork`/`SetDbStartValue` change existing blocks in place, and watch table entries are changed by exporting, editing and re-importing the table.
- TIA Portal V21: the exe can now be built for V21 with `dotnet build -c Release -p:TiaVersion=21` (Openness package 21, output in `bin\V21`); the default build stays V20. `--tia-major-version` now defaults to the version the exe was built for, and asking a build for a newer version stops with a clear message instead of an Openness crash ("Exception.ToString() failed"). `--doctor` lists V20 installations and shows the build version. The V21 build is not tested against a live V21 installation yet.
- Tool descriptions say what the path arguments look like: `softwarePath` as `GetProjectTree` names it (e.g. `PLC_1`), block and group paths relative to their system folder with `""` for the root, and `exportPath`/`importPath` as absolute paths on the server PC. The server instructions add the same plus when to use XML, documents or external source. `GetBlocks`, `GetBlocksWithHierarchy` and `GetSoftwareTree` say how they differ, and `GetNetworkSource` says where to read LAD networks. Tool names and arguments are unchanged.
- `ExportXmlBlock`, `ExportXmlType`, `ImportXmlBlock` and `ImportXmlType` now reject a relative path with a clear error, like the other export tools already did, instead of resolving it against the server's working directory.
- Server instructions now carry a short task guide (start, reading code, changing code with `EditBlockNetwork` instead of hand-edited block XML, going offline before compile/import), and the most used tools' descriptions start with one line on when to use them, so clients that see many tools pick the right one. Tool names and arguments are unchanged.
- `ImportXmlBlock`: when TIA rejects a file because its elements are not "sorted according to the current flow", the error now adds a hint to put each new `<Part>` directly after the part that feeds it. This, not the size of the network, was why a large LAD network failed to import.
- Server instructions in read-only mode now say that the write tools are hidden and that `--allow-write` enables them, so a client can tell the user what to change.
- `ImportXmlBlock`, `ImportXmlType`, `ImportFromDocuments` and `ImportBlocksFromDocuments` now need `--allow-write` like every other import tool. They change the project but were registered without it.

### Added

- `GetProjectStatus` (read): whether the open project has unsaved changes, its last modified time and user, and the newest project history entries (D4).
- `GetSafetyInfo`: optional `saveFile` and `compareFile` keep the F-block signatures in a JSON file and later list which F-block signatures changed, were added or removed, e.g. before and after a change and compile (D5). Without them the reply is unchanged.
- `EditTypeInterface` (write): add, change or delete one member of a PLC data type (UDT), the same way `EditBlockInterface` does for blocks. Blocks using the type need a compile afterwards.
- Change log: every write tool call is logged as one JSON line in `<backup path>\audit\<yyyyMMdd>.jsonl` (time, tool, arguments, ok or error, reply, backup file). `GetChangeLog` (read) lists a day's entries, optionally for one tool. A log line that cannot be written never fails the call. The log keeps the reply's message text, not the whole JSON reply.
- `EditBlockInterface` reports a start value given for a Temp member or an FC parameter before it checks that the block is compiled.
- `EditBlockInterface` (write): add, change (data type, start value, comment) or delete one member of an FB, FC, OB or global DB interface (Input, Output, InOut, Static, Temp, Constant; Structs by '.'-path) without hand-editing the block XML. The block is exported, the member changed, backed up and imported again; `previewFile` only writes the edited XML. Instance DBs are refused (change the FB and compile). Start values for Temp members and FC parameters are refused, since TIA keeps none.
- `GetHmiDynamizations` (read): what drives each property of the items on a WinCC Unified screen, in full - the tag with its mapping table (range, single bit or bit mask entries -> property value, colors as `#RRGGBB`, flashing per entry) or formula, script code with its trigger, flashing condition/rate/colors, resource list, expression and tag parameter dynamizations. `GetHmiScreenItems` only showed property, type and tag. Filter by item name and property.
- `SetHmiTagDynamization`, `SetHmiScriptDynamization`, `SetHmiFlashingDynamization`, `DeleteHmiDynamization` (write): bind a tag and set its range mapping (e.g. BackColor green at 0, red at 1) or formula, set a script with its trigger, set flashing, or remove a dynamization. A property with another kind of dynamization is refused until it is deleted. The previous dynamization is returned and saved as `.json` in the backup folder, since Unified screens cannot be exported. Mapping writes take range entries with both bounds only (Openness keeps `RangeType` read-only, so open ranges cannot be written); single bit and bit mask tables are read but not written. A Unified Comfort panel refuses value converter formulas; the tool then says so.
- `ModifyBlockWorkflow` prompt and a matching block-change routine in the server instructions: go offline, read the block, save fingerprints, change it with the smallest tool (`EditBlockNetwork` with a LAD preview first or `sclFile`, `SetDbStartValue`), compile only that block, fix an error once, compare fingerprints and report. It also lists what not to do (re-import a hand-written whole-block XML, compile the whole PLC repeatedly, repeat a failing call, work around the server with scripts) and says to stop and show the error after two failures of the same step. Tool names and arguments are unchanged.
- `install/install-tiaportal-mcp.ps1`: sets up a fresh PC in one run. Installs missing build tools (git, .NET SDK 8, .NET Framework 4.8 Developer Pack) with winget, detects TIA Portal V20/V21 and builds for it (both installed: builds both and registers `tiaportal-mcp-v20` and `tiaportal-mcp-v21`; `-TiaVersion` builds one), checks the `Siemens TIA Openness` group (optionally adds the user), clones or updates the source, runs `--doctor`, registers the server with Claude Desktop, Claude Code and Antigravity, and prints an `mcpServers` example. README "Requirements" points to it.
- `GetDbStartValues` (read) and `SetDbStartValue` (write): list the start values of a data block's members, including members inside structs, UDTs and arrays (e.g. `S[3].OriginOffset`), and change one of them in place without exporting or re-importing the DB. The DB is backed up before a change.
- `EditBlockNetwork` (write): replace, insert or delete one network of a program block. The new network comes from an XML file (e.g. another block's export); LAD/FBD parts are put into the flow order TIA requires on import (rebuilt from the wires, matched TIA's own order on all 110 exported networks tried), IDs are renumbered, and the block is backed up first. `previewFile` writes the edited XML without importing. With `sclFile` the new network is written as SCL text instead: TIA compiles it in a scratch FB with the block's interface, and that FB is deleted again.

- `GetSoftwareChecksum`: the checksum of the compiled program (and of the text lists) of a PLC software, as TIA Portal shows it; equal checksums mean the same compiled program.

- Backup before destructive changes: `DeleteBlock`, `DeleteType`, and `ImportXmlBlock`/`ImportXmlType` when they replace an object of the same name, first export the current object as XML and return its path as `backup`. Folder: `--backup-path`, default `%TEMP%\tiaportal-mcp-backups`.

- `GetTechnologyObjects`, `GetTechnologyObjectParameters`: list the technology objects (axes, encoders, cams, PID, ...) of a PLC and read one object's configuration parameters as name/value pairs, filterable by name (e.g. `Homing|PositionLimits`). Technology objects are not program blocks, so no other tool showed them.

- `SetTechnologyObjectParameter` (write): change one technology object parameter in the offline project. The text value is converted to the parameter's type; TIA checks the range and refuses read-only parameters. Old and new value are returned.

- `GetSafetyInfo`: read-only status of the safety program of an F-CPU: password set and logged on, safety settings, runtime groups and the offline signature of every F-block, plus a hash over all of them to see whether any F-block changed. V20 Openness has no F-collective signature; the hash is our own and labelled as such.

- `GetBlockFingerprints`: read block fingerprints (interface, program code, comments, properties, ...) without exporting; `saveFile` writes them, `compareFile` lists the changed, added and removed blocks and which aspect changed.

- `CompareSoftware`: TIA Portal's own compare of a PLC software with the online PLC or another PLC software; lists the objects that differ or exist on one side only.

- `ArchiveProject`: write the saved project as a .zapXX archive (backup before an import) without moving the open project. Refuses while there are unsaved changes.

- `CompileCodeBlock`: compile one code block (OB, FC, FB or DB) instead of the whole PLC, for example right after importing it. Same report as `CompileSoftware` (every compiler message, `meta.success` false on errors). Idea taken from upstream's tool of the same name; `CompileSoftware` keeps its name.

- `SearchExports`: search an export folder offline for a tag, block, member or comment. In SimaticML a symbolic access split over `<Component>` elements is matched as one path (`"G2U1Assy".PLC_Ins.Pitch`), and each match names its network.

- `CompareHmiScreens`: compare two WinCC Unified screens item by item (items on one side only, differing properties, texts, dynamizations, event scripts, faceplate interface values), with optional `replace` applied to the first screen so a numbered copy can be checked against its pattern.

- `CopyHmiScreens`: make a numbered series of WinCC Unified screens from one screen (`%n%`/`%n2%`/`%n3%` placeholders in name and replace rules, optional `screenNumberStart`). One transaction per screen, existing names are skipped, stops after ~40 s and returns `nextNumber` to continue. A continued call reuses the source snapshot for 10 minutes, and copies after the first skip re-reading the attribute values a fresh copy starts with; the reply reports `snapshotSeconds`/`secondsPerScreen`.
- __HMI event scripts readable__: `GetHmiScreenItems` gets `includeScripts` (default true) and returns the
  event handler scripts of each item (`events`: eventType, scriptCode, ...), the screen's own event scripts
  (`screenEvents`) and the code of script dynamizations (`dynamizations[].scriptCode`). Added fields only;
  the write tools' read-back does not read scripts. Answers "what does this button/popup actually do",
  which no tool could show before.
- __GetHmiScreens displayText__: the screen display name as plain text (`displayName` keeps the raw
  `<body><p>..</p></body>` value).

- __CopyHmiScreen__ (write): copies a WinCC Unified screen to a new screen in the same folder with
  optional `replace` ('Servo01=>Servo02;...') applied to every copied string - tag names, texts, scripts,
  faceplate interface values. V20 Openness has no Unified screen copy/export, so the copy is rebuilt item
  by item from the generic attribute API: every read-write attribute, multilingual texts, part objects,
  dynamizations of any type, event handler scripts and faceplate interfaces. Reads happen before the
  write transaction (unreadable parts are reported under `skipped`); a write TIA Portal refuses aborts the
  whole copy and names the item and attribute. Written independently from the Openness DLL.

- __CompareExports__ (REQUESTS.md P7): compares two export folders by relative path and lists files only
  in one folder and changed files with the changed line range, lines added/removed and a short excerpt
  of each side. Ignores the SimaticML `<DocumentInfo>` export timestamp, line endings and trailing
  blanks. Reads files only - no TIA Portal connection or lock needed.
- __GetBlocks paging__ (P5): optional `offset` and `limit`; the reply gets `totalCount` (added field), the
  number of matches before paging. Attributes are read for the returned page only.
- __GetBlocks modifiedSince__ (P6): optional ISO date/time; only blocks changed after it are returned,
  e.g. to find what was edited directly in TIA Portal since the last export. Without an offset the value
  is local time of the server PC; `modifiedDate` in the reply is UTC (Openness), `meta.timestamp` local.
- __Prompts for the HMI tools__: the ten WinCC Unified tools had no prompt of the same name, which
  `Test_708_EveryTool_HasAPromptOfTheSameName` requires.

- __PreviewImport checks the files themselves__ (REQUESTS.md P12-2): duplicate `ID`s in a SimaticML file,
  duplicate `UId`s within one network, and a `ConstantType` on an SCL Bool literal - the three causes of
  silent import rejections found on 2026-10-12. Reported per item as `problems` and counted in
  `problemFileCount` (added fields).
- __Imports refuse library type instances up front__ (P11-4): `ImportXmlBlock`/`ImportXmlType` over a
  block or type linked to a project library type now fail with an explanation instead of letting TIA
  Portal open a modal "test environment of the library cannot be deleted or replaced" dialog.

- __WinCC Unified faceplates__: `CreateHmiFaceplate` places a faceplate container of a library faceplate
  type on a screen; `SetHmiFaceplateInterface` sets one interface property to a static value or binds it
  to an HMI tag. `GetHmiScreenItems` now also returns `faceplateType` and `faceplateInterface` for
  faceplate containers, and position/size for window-type items (faceplate containers, screen windows),
  which were empty before (added fields only).
  Live-tested on CPU01 (`V0.0.7\Servo_Faceplate_2`): tag-typed interface properties refuse a tag
  dynamization and hold the tag name as their value, so `tag` falls back to that; text interface values
  are returned as their `<body>` part instead of the whole ProjectText XML.

- __WinCC Unified screen tools__ (REQUESTS.md P9-2): `GetHmiScreenItems` lists the items of one screen
  with kind, position, size, text, IO field process value and tag bindings. With `--allow-write`:
  `CreateHmiScreen`, `DeleteHmiScreen`, `CreateHmiScreenItem` (Button, IOField, Label, Text, Rectangle;
  an IOField can be bound to a tag on create), `UpdateHmiScreenItem`, `DeleteHmiScreenItem`, `BindHmiTag`
  (tag dynamization of any item property) and `CreateHmiTag` (internal or PLC-linked). Every write returns
  the object as read back after the change. Written against the V20 Openness API; not yet live-tested.
- __Preset tool arguments__: the new command line arguments `--project-path`, `--software-path`,
  `--export-path`, `--preserve-path` and `--with-dependencies` preset the `path`, `softwarePath`, `exportPath`, `preservePath` and `withDependencies` arguments of every tool
  that declares them. A preset argument is no longer `required` in the tool schema and is filled in when
  a call omits it; a value in the call still wins. Without the arguments nothing changes. `GetState`
  reports the active presets. Prompts are unchanged.

### Fixed

- __PreviewImport with '/' in a block name__: a block named like `GTS_EQ_I/FLower` was reported as
  `overwrite-elsewhere` although it is in the target group, because its group was taken as the text
  before the last '/'. The group is now the resolved path minus the known name.

- __Live-test fixes (f490ded exe)__: `PreviewImport` flagged every clean LAD export because wire
  `NameCon`/`IdentCon` repeat part UIds by design - they no longer count; items are now named by the
  object name inside the XML, not the (possibly sanitised) file name. `SetHmiFaceplateInterface` with
  `tag` failed with "Commit of a Transaction is not allowed after an exception": a refused dynamization
  attempt poisons the tool transaction, so the tag is now written as the interface value directly, and
  a static `value` is converted to the current value's type and written once.

- __Exports of objects whose names are not valid file names__: TIA Portal allows names such as
  `T_Data->HMI` or `GTS_EQ_I/FLower`; every export, `GetBlockSource`/`GetTypeSource`, `FindInCode` and
  `ExportXmlBlocks` with a broad regex failed with "Illegal characters in path" on them (REQUESTS.md
  P12-3). File and folder names now replace such characters with `_`; lookups keep the real name.
- __ExportXmlBlocks/ExportXmlTypes reply size__ (P12-3): new `includeAttributes` (default true, unchanged)
  to leave out the per-object attribute lists that made a 226-block reply exceed the token limit. The
  `regexName` description now says the pattern matches anywhere in the name ('G3U' also matches G3U10).

- __GetCrossReferences no longer outlives the tool timeout__: the walk stops after about 40 s (or at the
  new optional `maxReferences`) and returns what it has with `truncated: true` and a new `stoppedReason`.
  Before, `maxDepth 3` on a large DB ran past the 60 s device timeout, the call was dropped and the
  restarted server was no longer attached to TIA Portal. The lazy result is now also walked under the
  Openness lock.

- __HMI screen item text__: `CreateHmiScreenItem`/`UpdateHmiScreenItem` with `text` failed for every
  Button, Label and Text, because WinCC Unified stores screen texts as rich text
  (`<body><p>USE</p></body>`) and rejects a plain string. Plain text is now wrapped; text that already
  starts with `<body` is written as given. Found in the first live test on the CPU01 project.
- __Errors no longer end at "<Tool> failed"__: an unexpected Openness exception inside any operation
  now carries its exception chain in the message (`BindHmiTag failed: EngineeringTargetInvocationException: ...`).
  `BindHmiTag` also says which property/tag it could not bind and what to check.
- __HMI Label__: `CreateHmiScreenItem` kind `Label` failed with "Not supported" (HmiLabel cannot be
  created); it now creates TIA Portal's "Text field" (HmiTextBox, a subclass of HmiLabel).
- __Binding to a missing HMI tag__: TIA Portal accepts it silently; `BindHmiTag` and `CreateHmiScreenItem`
  with `tag` now start their message with a WARNING when no tag table holds that name.

- __Compile and block import say when TIA Portal is online__: both fail while the engineering station is
  online with the PLC (live, 2026-10-13), and TIA's exception does not say so. The error now ends with "go
  offline first" when the PLC's online state is Online or Connecting.
- __Source reads fall back to XML on any document export failure__: `GetBlockSource` / `FindInCode` fell
  back to SimaticML only for "not supported" errors. LAD blocks in a project last edited before V20
  Update 4 fail with a SimaticSdEnabler error instead, so they were unreadable and `FindInCode` skipped
  them. Any document export failure (or an export that produced no file) now falls back to XML;
  `format` in the result says which was read. `FindInCode` also opens its message with a warning when
  objects were skipped, so "0 matches" is not mistaken for "not used".
- __`CompileSoftware` says why it could not compile__: every failure before the compile result (no
  project, unknown software path, safety login, Openness refusing the compile) came back as a bare
  "Failed compiling software 'PLC_1'". Live, this happened three times in a row with no hint. Each case now
  returns its own reason, including TIA's exception text; a compile that runs and finds errors still
  returns the full message list.
- __`ImportXmlBlock` / `ImportXmlType` pass on TIA's rejection reason__: import failures were swallowed
  into "Failed importing block". Live, a servo FB whose network had 900+ elements was rejected and the
  reason was never shown. The error now carries the whole exception chain from the import, and names a
  missing file, software path or group separately.
- __Block and type names with `[ ]` resolve without escaping__: `GetBlock`/`GetType` (and with them
  `GetBlockSource`, `GetTypeSource`, exports and `FindInCode`) treated any name containing a regex
  character as a regex, so `5_G2U1_Home[0]` looked for `5_G2U1_Home0` and was "not found" unless written
  `5_G2U1_Home\[0\]`. An exact name is now tried first (`Blocks.Find` / `Types.Find`, one Openness call),
  the regex reading only after that.
- __`FindInCode` reports inconsistent objects__: blocks and types that are not compiled were dropped
  silently, so a search over a just-edited block returned "0 objects searched". They are now listed in
  `unsearchable` as "inconsistent - compile the software first".
- __`GetTypeSource` falls back to XML before V21__: type source documents need V21, and the version check
  raised an error `ReadSource` does not fall back on, so on V20 every type failed - and `FindInCode` listed
  every PLC data type as unsearchable. It now returns the SimaticML text with `format: "xml"`, like blocks
  without a source document.

### Performance

- __`FindInCode` no longer re-resolves every object__: it kept only the path of each candidate and then
  resolved it up to three times (source read, document export, XML fallback), each time walking the device
  list and the group tree again. It now reads the objects `GetBlocks`/`GetTypes` returned directly.
  `GetState` progress also covers the listing phase, which was invisible before.
- __`GetBlocks` / `GetTypes` can skip the attribute list__: reading all ~30 Openness attributes per
  object is most of their run time (measured on the Mahindra CPU01 project: 70 blocks in ~14 s with
  attributes, the name walk of the whole PLC in ~9 s). The new `includeAttributes` argument defaults to
  `true`, so existing calls get the same response; pass `false` on a large PLC to list names and paths only.

- __`OpenProject` / `OpenTiaProject` reuse an already open project__: the tool used to close the current
  project before looking for the requested one, so an open project was always closed and reopened. The
  lookup now runs first and matches by file path (not by name), so a project already open in the TIA Portal
  instance, also one opened in the UI, is used as it is; the response says so. The same applies to local
  sessions (`.alsXX`). Another project is still closed only when a different one has to be opened.

### Changed

- __`Portal` partial classes consolidated__: the 18 `Portal.*.cs` files are now `Portal.cs`,
  `Portal.Blocks.cs`, `Portal.Types.cs`, `Portal.Tags.cs` and `Portal.Devices.cs`, plus two folders:
  `Siemens/Documents/` (`Portal.Documents.*.cs`, six files) and `Siemens/Software/`
  (`Portal.Software.*.cs`, seven files). Members were moved verbatim; no signature, visibility or
  behaviour changes. The class-level notes of the retired files are kept as comments above their
  former sections.

- __`McpServer` partial classes follow the same layout__: the ten `McpServer.*.cs` partials are now
  `McpServer.cs`, `McpServer.Devices.cs`, `McpServer.Blocks.cs`, `McpServer.Types.cs` and
  `McpServer.Tags.cs`, plus `ModelContextProtocol/Documents/` (`McpServer.Documents.*.cs`) and
  `ModelContextProtocol/Software/` (`McpServer.Software.*.cs`). Tool names, attributes and behaviour are
  unchanged.
- __Document and source methods regrouped__: `Portal` and `McpServer` now have `Portal.Documents.cs` /
  `McpServer.Documents.cs` (the `...AsDocuments` exports and the `...FromDocuments` imports) and
  `Portal.Sources.cs` / `McpServer.Sources.cs` (every source file method, including `GetBlockSource` and
  `GetTypeSource`, which moved out of the Blocks/Types partials). The `Documents/` subfolders are gone.
  Members were moved verbatim.
- __`McpServerWrite` merged into `McpServer`__: the project-mutating tools now live in the matching
  `McpServer` partials (`McpServer.Blocks.cs`, `.Types.cs`, `.Tags.cs`, `Documents/McpServer.Documents.*.cs`),
  and the shared write plumbing (`Guarded`, `SaveHint`, response builders) in `McpServer.cs`. Tool
  names, attributes and behaviour are unchanged. The `--allow-write` gate is preserved: the 40 write tools
  carry the new `[WriteTool]` attribute and `Program.BuildTools` registers them only under
  `--allow-write`, so they stay out of `tools/list` as before. `WritePolicy.EnsureEnabled` still runs
  first in every write tool. The write-side `Join` helper is now `JoinPath` (it does not trim a
  trailing slash, unlike the preview `Join`). New tests: `Test7ToolRegistration`.
- __Breaking: XML tools renamed__. The tools and the `Portal` methods that read and write TIA Portal
  XML files now say so in their name: `ExportBlock` to `ExportXmlBlock`, `ExportBlocks` to
  `ExportXmlBlocks`, `ImportBlock` to `ImportXmlBlock`, `ExportType` to `ExportXmlType`, `ExportTypes` to
  `ExportXmlTypes` and `ImportType` to `ImportXmlType`. Parameters and results are unchanged. MCP clients,
  prompts and permission allow-lists that name the old tools must be updated. The prompts
  `ExportBlocks` and `ExportTypes` are renamed to `ExportXmlBlocks` and `ExportXmlTypes` as well.
  Likewise the source file exports follow the Export naming: `GenerateBlockSource` to
  `ExportSourceBlock`, `GenerateTypeSource` to `ExportSourceType` and `GenerateSources` to
  `ExportSources` (tools, prompts and `Portal` methods; `ImportSources` keeps its name; the
  tool title is now "Export sources"). `GenerateBlocksFromSource` is now
  `ImportExternalSource` (tool, prompt and `Portal` method; the Siemens API method of the same name is
  unaffected). It compiles a source that is already registered in the project; the name `ImportSourceBlocks`
  now belongs to the new folder import described under Added.
  The tag and watch table exports follow suit: `ExportTagTable` is now `ExportXmlTagTable` and
  `ExportWatchTable` is now `ExportXmlWatchTable` (tool, response type, `Portal` method and
  prompt).

### Added

- __Source file imports that mirror the exports__: `ImportSourceBlock`, `ImportSourceType`,
  `ImportSourceBlocks` and `ImportSourceTypes` (tools, prompts and `Portal` methods) complete the set next to
  the existing `ImportSources`, one to one with `ExportSourceBlock`, `ExportSourceType`,
  `ExportSourceBlocks`, `ExportSourceTypes` and `ExportSources`. The single-file tools take a target group and the
  file path, like `ImportXmlBlock` and `ImportXmlType`; the folder tools take a group, a folder, an optional
  `regexName` and `preservePath`, which reads the subfolders into the matching subgroups. All of them take
  `keepOnError`, return the same result as `ImportSources` and are `[WriteTool]`s. `ImportSources` now shares
  its walk with them; its behaviour is unchanged. New tests: `Test_495` to `Test_499` in `Test4Software`.
- __`ExportXmlTagTable` prompt__: a prompt template for exporting a PLC tag table to XML through the
  `ExportXmlTagTable` tool, next to the existing block and type export prompts. Likewise prompts for the source
  file tools: `ExportSourceBlock`, `ExportSourceType`, `ImportSources` and `ImportExternalSource`, plus the
  convenience prompts `ImportAllSources` and `ImportAllSourcesKeepOnError`. Every other tool now has a prompt
  of the same name as well (80 prompts, generated from the tool descriptions and parameter descriptions), so
  all 99 tools are reachable as prompts.

## [0.3.0] - 2026-09-11

Generate TIA Portal external source files - the format the compiler reads back - from blocks and
PLC data types. The read tool surface grows from 56 to 59.

### Added

- __`GenerateBlockSource`__: writes one program block as an external source file. The extension
  is dictated by the object, not chosen by the caller: `.db` for data blocks, `.awl` for STL
  blocks, `.scl` for SCL blocks. Openness throws on a mismatch, so the mapping is derived rather
  than passed in. Data blocks are recognised by type instead of by `ProgrammingLanguage`, because
  the DB family spans several language values (`DB`, `CPU_DB`, `F_DB`, `Motion_DB`) that all
  write `.db`.
- __`GenerateTypeSource`__: writes one PLC data type as a `*.udt` external source file. Unlike
  `GetTypeSource` and `ExportTypeAsDocuments` this needs no TIA Portal V21, and the result can be
  imported again.
- __`GenerateSources`__: the comfort function - writes every block and PLC data type of one PLC
  software into a folder tree that mirrors the project groups, `<exportPath>/Program blocks/...`
  and `<exportPath>/PLC data types/...`, one file per object. Accepts `regexName` to narrow the
  set. This is the compilable counterpart to `ExportPlcAsSourceTree`, which snapshots the same
  tree as source documents and XML.
- All three take `withDependencies`, which maps to `GenerateOptions.WithDependencies` and pulls
  every object the subject uses - called blocks, instance DBs, UDTs - into the same file, so it
  compiles on its own. Off by default, which keeps one object per file and therefore a tree that
  diffs cleanly.

### Notes

- Openness generates sources only from data blocks and STL or SCL blocks. LAD, FBD, GRAPH and the
  rest have no textual form: the single-object tools reject them with the reason and a pointer to
  `ExportBlock` or `ExportAsDocuments`, and `GenerateSources` reports them in `Skipped` rather
  than failing the whole run. Inconsistent and know-how protected objects are handled the same
  way.
- Openness treats an existing file at the target path as an error, not an overwrite. Every other
  exporter in this server overwrites, so the target file is deleted first and a second run
  succeeds instead of failing.
- Filesystem only: generating a source does not modify the project, so these tools are not gated
  behind `--allow-write`. They are marked destructive, like the `Export*` tools, because they
  overwrite files.
- New files: `Siemens/Portal.GenerateSource.cs` and
  `ModelContextProtocol/McpServer.GenerateSource.cs`, kept apart from the `*.Source.cs` pair,
  which deals in source documents and SimaticML rather than in compilable sources.

### Added (write side)

- __`ImportSources`__: the bulk-import counterpart to `GenerateSources`. Walks a folder tree of
  `.db`/`.awl`/`.scl`/`.udt` files and compiles each back into a block or PLC data type, placed
  into the group its folder path implies - the same layout `GenerateSources` writes. Requires
  `--allow-write`, since unlike the `Generate*` tools this mutates the project.
- Each file goes through the two Openness steps there is no shortcut around: register it as a
  scratch `PlcExternalSource` (reusing the existing `CreateExternalSourceFromFile`), call
  `PlcExternalSource.GenerateBlocksFromSource` - which returns a mix of `PlcBlock` and `PlcType`
  objects from a single file, since the source's own content decides what comes out - then
  delete the scratch source again (reusing `DeleteExternalSource`), regardless of outcome.
- `keepOnError` maps to `GenerateBlockOption.KeepOnError`: successfully generated objects from a
  file are kept even when others in the same file fail. Openness gives up per-object
  success/failure reporting in that mode, which is a limitation of the underlying API, not of
  this wrapper.
- A folder whose implied group does not exist in the project fails that one file (reported in
  `Failures`) rather than being created automatically; the group structure is expected to
  already exist, since these files were themselves generated from objects that lived in it.
- New files: `Siemens/Portal.ImportSources.cs` and
  `ModelContextProtocol/McpServerWrite.ImportSources.cs`. Reuses
  `GetPlcBlockGroupByPath`/`GetPlcTypeGroupByPath` and the private `StripSystemRootSegment`
  helper (`Portal.Documents.cs`) rather than re-deriving the same "does this folder name match
  the localized system root" logic a third time.

## [0.2.0] - 2026-09-10

Complete the PLC software area of the Openness API: tags, constants, watch and force tables,
external sources, cross references, and create/rename/delete/move for blocks and types. The tool
surface grows from 31 tools to 44 read tools plus 37 project-mutating tools.

### Added

- __Write mode__, opt-in through the new `--allow-write` command line argument. The 37
  project-mutating tools live in a separate `McpServerWrite` tool type that is only registered
  when the flag is present, so without it they are absent from `tools/list` rather than merely
  refused when called. `WritePolicy.EnsureEnabled` additionally guards every write tool at
  runtime, because these are `public static` methods that the test suite invokes directly and
  that path bypasses tool registration. Filesystem-only exports are deliberately not gated: they
  never modify the project. Both `GetState` and the `--doctor` report now show `AllowWrite`.
- __PLC tags and constants__: `GetTagTables`, `GetTagTableInfo`, `GetTags`, `GetTagInfo`,
  `GetConstants` (user and/or system) and `ExportTagTable`. Write side: `CreateTagTable`,
  `DeleteTagTable`, `RenameTagTable`, `CreateTagTableGroup`, `DeleteTagTableGroup`,
  `ImportTagTable`, `CreateTag`, `UpdateTag`, `DeleteTag`, `CreateUserConstant`,
  `UpdateUserConstant`, `DeleteUserConstant`.
- __Watch and force tables__: `GetWatchTables`, `GetWatchTableInfo` (including entries),
  `GetForceTables` and `ExportWatchTable`. Write side: `CreateWatchTable`, `RenameWatchTable`,
  `DeleteWatchTable`, `CreateWatchTableGroup`, `DeleteWatchTableGroup`, `ImportWatchTable`.
- __External source files__: `GetExternalSources`, `GetExternalSourceInfo`,
  `CreateExternalSourceFromFile`, `DeleteExternalSource`, `CreateExternalSourceGroup`,
  `DeleteExternalSourceGroup` and `GenerateBlocksFromSource`.
- __Cross references__: `GetCrossReferences` for a whole PLC software or for one block, type,
  tag table, tag or block group. Because `Sources -> References -> Locations` nests three deep
  and source children recurse, the tool takes `maxDepth` (default 1, maximum 3) and reports a
  `Truncated` flag with counts instead of returning megabytes.
- __Blocks and types__: `CreateBlockGroup`, `DeleteBlockGroup`, `CreateTypeGroup`,
  `DeleteTypeGroup`, `DeleteBlock`, `RenameBlock`, `DeleteType`, `RenameType`, `CreateFB`,
  `CreateInstanceDB`, plus `CopyBlock`, `MoveBlock`, `CopyType` and `MoveType`.
- `Portal.GetTypePath(PlcType)`, the counterpart to `GetBlockPath(PlcBlock)`.
- `Operation.Run`, the single exception-decoration point that `docs/error-model.md` prescribes.
  It wraps a non-`PortalException` into one, stamps context into `Exception.Data`, logs once
  (nested calls do not re-log) and rethrows. It also serializes all Openness traffic behind a
  `Monitor`; a `SemaphoreSlim` would self-deadlock, because portal methods call one another.
- `PortalErrorCode` gains `ImportFailed`, `CreateFailed`, `DeleteFailed`, `RenameFailed`,
  `NotSupported` and `WriteDisabled`. Existing members keep their order and values.

- __PLC data types as SIMATIC Source Documents__ (TIA Portal V21+): `ExportTypeAsDocuments` and
  `ExportTypesAsDocuments`, plus the write-gated `ImportTypeFromDocuments` and
  `ImportTypesFromDocuments`. Until now only program blocks could be written as documents - the
  readable, git-diffable form where `<Name>.s7dcl` holds the SCL/LAD source text and the
  optional `<Name>.s7res` the comments; every other export produced SimaticML XML. Openness only
  added `PlcType.ExportAsDocuments` and `PlcTypeComposition.ImportFromDocuments` in V21, hence
  the higher version gate than the V20 block tools. Tag tables and watch tables have no document
  API in V21 and remain XML-only.
  File names come from TIA Portal rather than from a hardcoded extension: an export reports the
  files it actually wrote (`DocumentExportResult.ExportedDocuments`, unioned with what is on
  disk) and a batch import discovers a document set by base name across a known extension set.
  Deleting a previous export stays restricted to `.s7dcl`/`.s7res`, so a hand-written `.scl` or
  `.udt` in the same directory is never removed.
  Unlike `ExportBlocksAsDocuments`, which only logs its failures, the bulk type export returns
  its inconsistent and failed types so the response can name them. The two type import tools sit
  in the `--allow-write` surface, where project-mutating tools belong; the older
  `ImportFromDocuments` and `ImportBlocksFromDocuments` remain ungated, which is a known
  inconsistency in those block tools rather than a pattern the new tools follow.

- __Ten comfort tools__ that shorten the path between a question and an answer. All read-only
  except where noted, and none change an existing tool's contract.
  - `GetBlockSource` / `GetTypeSource` return an object's source text inline instead of making
    the caller export a file and open it. Openness has no in-memory block body, so the readers
    export into a temp scratch directory and remove it again. STL and mixed-language blocks
    have no SIMATIC Source Document at all, so those fall back to XML and say so in `Format`.
  - `GetBlockInterface` lists a data block's members from `DataBlock.Interface` without any
    export, and works on inconsistent blocks. Data blocks only - V21 Openness offers no
    interface accessor for FB, FC or OB.
  - `FindInCode` searches the program text with a regular expression. Every other filter in the
    server matches object names only.
  - `CompileSoftware` now returns the whole `CompilerResult.Messages` tree flattened to
    `{path, state, description}` with error and warning counts, instead of one stringified
    sentence. Warnings are a successful compile with detail; only errors fail the call.
  - `GetPlcSummary` replaces six discovery calls: counts per area, a programming-language
    histogram, and the inconsistent and know-how-protected objects.
  - `WhereUsed` answers "what uses this?" from a bare name, flattening the cross-reference tree.
  - `ResolveObjectPath` turns a bare or partial name into the root-relative path the other tools
    need, across all six object areas. The `Path` property is also no longer commented out on
    `ResponseBlockInfo` and `ResponseTypeInfo`, so `GetBlocks` and `GetTypes` finally return
    round-trippable paths - exporting one type is now one call instead of three.
  - `OpenTiaProject` connects, opens and returns the device and PLC software paths in one call.
  - `ExportPlcAsSourceTree` snapshots a whole PLC to a git-ready folder tree in one call.
  - `PreviewImport` reports what an import would create, overwrite or collide with, without
    touching the project - including the PLC-global data type name rule.
- __Atomic, undoable writes.__ Every one of the write tools now runs inside
  `ExclusiveAccess.Transaction`, so a tool call commits as a unit and appears in the TIA Portal
  undo stack as one named entry. A body that throws rolls back instead of leaving the project
  half-edited (verified against a live V21). If TIA Portal refuses exclusive access the write
  still runs unwrapped, so this can never turn a working write into a failure.

### Changed

- `GetSoftwareTree` renders three further sections - PLC tags, watch and force tables, and
  external source files - and takes a `sections` argument accepting any comma separated subset
  of `blocks,types,tags,watch,sources` (default `all`) so the output stays manageable on a large
  PLC. Existing single-argument callers are unaffected.
- `Portal` and `McpServer` are now `partial` and split by area (`Portal.Tags.cs`,
  `Portal.WatchTables.cs`, `Portal.MoveCopy.cs`, `McpServer.Tags.cs`, ...). The generic path
  helpers in `Portal.Resolve.cs` (`WalkGroups`, `BuildGroupPath`, `WalkRecursive`) now back the
  existing block and type resolvers as well, so all five group hierarchies share one traversal.
- `Diagnostics.Run` takes an optional `bool allowWrite`. The flag is passed in rather than read
  from `WritePolicy`, so the Siemens layer keeps no dependency on the MCP layer.
- 27 read tools and all 37 write tools publish an `outputSchema` and return `structuredContent`
  (previously 13).

### Fixed

- The software tree omitted external source files even though the `GetSoftwareTree` description
  had always promised them.
- `GetBlockPath` returned paths prefixed with the `Program blocks` system group, which
  `GetBlock` then rejected - so the "Did you mean ...?" suggestions on a failed `ExportBlock`
  named paths that could not be used. Path building now takes an `includeSystemRoot` flag:
  suggestions are root-relative and round-trip, while `preservePath` exports keep the existing
  on-disk layout unchanged.
- `ExportTagTable` with `preservePath` wrote the group structure directly below `exportPath`,
  while block and type exports place theirs below the system folder (`Program blocks`,
  `PLC data types`). Tag tables now land in `<exportPath>/PLC tags/...`, using the system group
  name as TIA Portal reports it in the current interface language. `ImportTagTable` accepts a
  leading `PLC tags` segment in `groupPath` so the exported layout can be fed straight back.
- `--doctor --allow-write` reported write mode as disabled. `WritePolicy.AllowWrite` was only
  assigned inside `RunStdioHost`, which `--doctor` returns before reaching; it is now set in
  `Main`.

### Known gaps

- `CreateWatchTableEntry` and `DeleteWatchTableEntry` are not implemented. `PlcWatchTable.Entries`
  is a `PlcTableCommentEntryComposition` whose only typed creator produces a comment row; a real
  entry requires the untyped `IEngineeringComposition.Create(typeof(PlcWatchTableEntry), ...)`
  path, whose required attribute names must first be read from `GetCreationInfos()` against a
  live project rather than guessed.
- Openness offers no move or copy operation for blocks and types, so `CopyBlock`, `MoveBlock`,
  `CopyType` and `MoveType` are composed from export, import and - for a move - deleting the
  source after the import succeeds. Two consequences are visible to callers: the object must be
  consistent, and its block number travels with it, so importing into the same PLC can collide.

## [0.1.0] - 2026-09-07

Upgrade to the current MCP .NET SDK and adopt the newer protocol surface.

### Breaking

- Tool failures are no longer JSON-RPC errors. `ModelContextProtocol` 2.x turns an
  `McpException` thrown from a tool into a `CallToolResult` with `isError: true`, carrying
  the message as text content. Clients that inspected JSON-RPC `error.code` (`InvalidParams`,
  `InternalError`, ...) must read the tool result's `isError` flag and message instead.
  `McpErrorCode` was removed from all 75 throw sites; error codes now live only on the
  derived `McpProtocolException`, which is reserved for protocol-level faults.

### Added

- Environment diagnostics ('doctor'), available two ways: the `--doctor` command line argument
  prints a report and exits without starting the MCP server, and the new `Doctor` tool returns the
  same report plus structured content to MCP clients. Both report the connection state, the open
  project, the active TIA major version, every installed TIA Portal version >= V21 (with a check
  that its Openness assemblies and the Portal executable are present), and membership in the
  `Siemens TIA Openness` user group. Read-only throughout: unlike `Openness.IsUserInGroup`, the
  new `Openness.CheckUserInGroup` never adds the user to the group.
- `Engineering.GetTiaPortalInstallPath(int)` resolves the install path of any TIA major version and
  no longer depends on the `TIAP{version}\TIA_Opns` registry sub key alone - installations that do
  not write that key are now found via their other product sub keys, which carry the same path.

### Changed

- Siemens TIA Portal Openness updated to V21. `Siemens.Collaboration.Net.TiaPortal.Packages.Openness` 20.0.1744190253 -> 21.0.1765349347 and
  `Siemens.Collaboration.Net.TiaPortal.Openness.Resolver` 1.1.1725480302 -> 2.0.1765367256. The default
  TIA major version is now 21 (`Openness.Initialize`, `Program.Main`); older versions remain reachable via
  `--tia-major-version`. `Engineering.Resolver` now also excludes `V21` directories when targeting another
  version, and the test `App.config` probing path points at `Portal V21`. A machine with TIA Portal V21
  Openness installed is now required to build.
- `ModelContextProtocol` 0.3.0-preview.4 -> 2.2.0; `Microsoft.Extensions.Hosting`
  10.0.0-preview.4 -> 10.0.10. Negotiated protocol revisions are now `2024-11-05`,
  `2025-03-26`, `2025-06-18` and `2025-11-25`.
- `initialize` now reports `serverInfo` (name, title, version) and `instructions` telling the
  client to call `Connect` and `OpenProject` first and how to discover path arguments.
- Progress reporting for `ExportBlocks`, `ExportTypes`, `ExportBlocksAsDocuments` and
  `ImportBlocksFromDocuments` moved from hand-rolled `notifications/progress` calls to the
  SDK-injected `IProgress<ProgressNotificationValue>`. The removed `IMcpServer` and
  `RequestContext<CallToolRequestParams>` parameters were SDK-injected and never part of the
  tool input schemas, so the wire-visible schemas are unchanged. Progress notifications are
  emitted only when the client supplies a `progressToken`, as before; the non-standard
  `Error` field previously sent on failure notifications is gone.
- Tools and prompts are registered explicitly (`WithTools`/`WithPrompts`) instead of by
  assembly scanning, giving a stable `tools/list` order.

### Added

- All 30 tools carry a `title` and behaviour annotations: 13 read-only `Get*` tools are
  `readOnlyHint: true`; project mutations (`SaveProject`, `SaveAsProject`, `CloseProject`),
  all export tools (they overwrite files already present at the target path) and all import tools
  are `destructiveHint: true`. Everything is `openWorldHint: false`.
- The 13 read-only tools publish an `outputSchema` and return `structuredContent`.

### Removed

- `src/TiaMcpServer/packages.config` — a stale packages.config-era leftover pinning
  `ModelContextProtocol` 0.2.0-preview.1. The project has used SDK-style `PackageReference`
  for some time and the build ignored this file.

## [0.0.16] - 2025-09-02

- New: ImportFromDocuments and ImportBlocksFromDocuments (V20+)
- Guard: Version checks for export/import as documents (V20+)
- UX: Pre-check .s7res for missing en-US tags; warnings surfaced in responses
- Docs: README updates, prompts note V20+ and known LAD en-US limitation
- Refactor: Updated all McpException throws to SDK signature with McpErrorCode
- Chore: Added TODOs for tests/docs

## [0.0.15] - 2025-08-30

- prompts improved
- long running tasks as async tasks

## [0.0.14] - 2025-08-18

- better structure/tree format
- new GetSoftwareTree()
- bugfixes

## [0.0.13] - 2025-08-14

- logging integrated
- prompts added

## [0.0.12] - 2025-08-07

- export path fixed

## [0.0.11] - 2025-08-07

- project structure formatted as markdown code

## [0.0.10] - 2025-08-07

- tool responses improved

## [0.0.9] - 2025-08-04

- export of blocks and types with 'preservePath' option
- new tools
- some infos with attributes

## [0.0.8] - 2025-08-01

- improved jsonrpc responses
- updated dependencies

## [0.0.7] - 2025-07-18

- new GetState()
- return values fixed

## [0.0.6] - 2025-07-16

- refactored code to use new TIA Portal API
- only blocks (OB/FB/FC/DB) and types (UDT) are now retrieved from the PLC software
- use regex to filter blocks and types
- import of blocks and types to PLC software

## [0.0.5] - 2025-07-11

- locating of plc software by softwarePath. This makes it possible to access plc software in groups/subgroups
- new tool: retrieving of project structure as text
- new tool: compile plc software

## [0.0.4] - 2025-06-30

- opens local session or projects, depending on project file extension

## [0.0.3] - 2025-06-23

- Release on Visual Studio Code Narketplace

