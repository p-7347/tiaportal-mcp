# TODO / Enhancements

Centralized list of actionable improvements gathered from initial repo review. Use this to track, prioritize, and reference across PRs. See file paths in backticks.

## ~~Open bug: Attach succeeds but Projects/LocalSessions empty for a specific TIA instance~~ - Resolved, root cause confirmed: missing GSD files

Found while verifying multi-instance `Connect(processId)` (see `CHANGES.md` 2026-09-10, full
writeup). One specific project file (received via KakaoTalk) always showed an empty
`_portal.Projects`/`LocalSessions` after a successful Attach, no matter what - 7 hypotheses ruled
out one by one (elevation, Multiuser/`ProjectServers`, how the file was opened, timing/race, a
blocking modal, another instance's online state, instance launch order - tested standalone too).
Opening a *different* project (unrelated file) attached and enumerated correctly on the first try,
isolating the cause to something specific to that one `.ap20` file - not a bug in `Connect`/
`ListTiaPortalInstances` or this server's attach logic, which was in fact repeatedly confirmed
correct throughout the investigation. **Root cause confirmed**: the project referenced hardware
modules from optional GSD files not installed on this machine - TIA's UI opened the project anyway
(with warnings), but the underlying hardware objects never fully instantiated, so Openness
enumeration came back empty. After installing the missing GSD files, reconnecting to the same file
returned fully populated `GetProject`/`GetDevices`/`GetState` data with no code changes. Closed.

## Multi-instance selection + cross-project compare (2026-09-10 idea, not started)

Not needed right now - noted for later.

1. ~~**Let the user pick which running TIA Portal instance to attach to.**~~ - **Done
   (2026-09-10).** `ListTiaPortalInstances` lists every running process (id, open project path,
   mode) without attaching; `Connect(processId)` attaches to a specific one. With 2+ instances
   running and no `processId`, `Connect` now refuses to guess instead of silently taking
   `TiaPortal.GetProcesses().First()`. Verified live with two real TIA Portal instances (two
   different projects) open at once.
   - Not done: switching which instance you're attached to *without* a fresh `Connect` call
     (i.e. hot-swapping mid-session) - `Connect` again with a different `processId` works fine
     today, this is just about whether a dedicated "switch" affordance is worth adding later.

2. **Cross-project/program compare.** TIA Portal's own compare only works between components
   *within a single open instance* and mostly just says "different", not much more - not
   actually useful for the kind of comparison being asked for here. Rather than chasing whatever
   Openness compare API exists (likely just as shallow), the more promising route is building it
   on top of tools already in this server: export both sides' blocks (`ExportBlock`/
   `ExportBlocksAsDocuments`, or once multi-instance selection above exists, from two different
   open projects) and diff the exported text/XML ourselves, in the same spirit as
   `tools/tia_xml_parser.py`'s existing StructuredText-v4 reconstruction. Bigger scope than item
   1 - treat as a separate effort once there's an actual comparison need in front of us.

## Block interface / cross-reference tools (2026-09-09 roadmap)

Came out of a live debugging session on the Mahindra_CPU01_V20_260909_k1_001 project
(finding duplicate `Main_Tracking_Data` instance declarations, and tracing who calls
`0_Main_CallEnv`). See `CHANGES.md` (2026-09-09 entries) for the bugs found/fixed along
the way (path-prefix stripping, device-name-with-'/' fix, attribute JSON-serialization fix).

1. **`GetBlockInterface(softwarePath, blockPath)`** - read Static/Temp/Input/Output/InOut
   member declarations ({name, dataType, startValue}) as structured JSON, instead of the
   `ExportAsDocuments` → `.s7dcl`/`.s7res` → grep workaround used today.
   - **Status: blocked on the Openness API side.** Verified empirically against the live
     V20 `Siemens.Engineering.dll` (reflection + `GetServiceInfos()`/`GetCompositionInfos()`
     on a real FB instance): a `PlcBlock` exposes exactly 7 services
     (`ICompilable`, `CrossReferenceService`, `LibraryTypeInstanceInfo`,
     `PlcBlockProtectionProvider`, `FingerprintProvider`, `SupervisionProvider`,
     `SafetySignatureProvider`) and only one composition (`Supervisions`) - no interface/member
     accessor among them. `Siemens.Engineering.SW.Blocks.Interface.PlcBlockInterface` is a real
     type ("Interface for all blocks" per the shipped XML docs) but nothing on `PlcBlock` in
     this V20 install actually returns one - not `GetService<T>()`, not `GetComposition("...")`.
     `InterfaceSnapshot` (which *is* a valid service) is for online monitoring snapshot
     *values*, not the interface *definition*. Before sinking more time in: re-check whether a
     newer Openness version (V21+) or a different PLC block subtype exposes this differently.
   - Fallback if it stays blocked: keep the `.s7dcl`/`.s7res` export-and-parse route, but see
     the "snapshot export" idea below to stop paying the round-trip cost per block.

2. **Cross-reference lookup** ("where is this block/type/tag used") - would have made
   today's manual "export 0_Main_CallEnv, not it, export Main_DataSetting, ..." caller search
   a single call.
   - Option A: use `CrossReferenceService` directly (it's a real service on `PlcBlock`, per the
     empirical service list above) - needs its actual API surface investigated (what it
     returns, whether it's queryable by name/type or only enumerable per-object) before
     committing to a tool shape.
   - Option B (safer fallback, no dependency on an unconfirmed API): bulk `ExportBlocksAsDocuments`
     the whole PLC software once, then regex/grep the exported `.s7dcl` files server-side for a
     call/instance pattern and return matching block names. Less precise than a real
     compiler-backed cross-reference (can false-positive on comments/similar names) but only
     needs tools already implemented.

3. **Snapshot export mode** - rather than exporting one candidate block at a time while
   searching, add a mode that bulk-exports a PLC software's blocks to a local folder once
   (already possible today via `ExportBlocksAsDocuments`/`ExportBlocks` with an empty
   `regexName`), so follow-up analysis (call tracing, pattern search) runs against local files
   instead of round-tripping to TIA Portal per candidate. Needs a policy for when the snapshot
   is considered stale (e.g. only refresh after `CompileSoftware`, or require an explicit
   re-export) - not a new tool per se, more a recommended *usage pattern* worth documenting in
   `docs/TOOLS.md` once cross-reference (above) is resolved one way or the other.

4. ~~**Online state / Run-Stop**~~ - **Done (2026-09-10)**, connection state only.
   `GetOnlineState`/`GoOnline`/`GoOffline` shipped, backed by
   `Siemens.Engineering.Online.OnlineProvider` (`GetService<OnlineProvider>()` on a Device or
   DeviceItem - plain public `.State`/`GoOnline()`/`GoOffline()`, verified live against
   `PLC_1`). `docs/TOOLS.md`'s "what these tools can't do" note has been updated accordingly.
   - **Run/Stop control is still deliberately not implemented** - accidentally stopping a live
     PLC is a real safety risk. If ever added, gate it behind an explicit opt-in
     flag/confirmation, separate from these connection-only tools.

5. **Live tag value monitoring** (e.g. reading `icnt`/`icnt2`/`icnt3` without opening a Trace
   view) - **out of Openness's scope entirely**, confirmed against the standard Openness
   feature list (engineering metadata only - name/address/type/comment - never live runtime
   values) and against the separate TIA Portal Test Suite add-on (test-case automation, not
   live tag access either). Would need a completely separate S7 communication stack:
   - Snap7 / S7NetPlus (raw S7 protocol, ISO-on-TCP port 102): simplest, but most blocks in
     this project use `MemoryLayout: Optimized` (confirmed on `Main_Tracking_Data` too), whose
     absolute offsets shift per compile - these libraries generally need symbolic (by-name)
     addressing support to be usable here, which plain Snap7/S7NetPlus don't provide.
   - OPC UA client (`OPCFoundation.NetStandard.Opc.Ua`): this project already has an `OPC UA_1`
     hardware component under `PLC_1`. Enabling the CPU's built-in OPC UA server and subscribing
     by tag name sidesteps the Optimized-offset problem entirely - the more realistic path if
     this is ever built.
   - Recommendation: if pursued, build as a **separate small MCP server**, not folded into
     `tiaportal-mcp` - this project is an engineering-automation tool; live tag monitoring makes
     it a SCADA client, a different concern.

6. **Event subscription** (block-changed/compile-completed notifications) - Openness supports
   registering event handlers, but MCP's request/response model has no clean way to push a
   server-side event to the client in real time. Low priority; skip unless a concrete need
   shows up.

7. **HW/network health-check** (servo/cylinder physical config sanity, network topology
   review) - explicitly a **separate, later effort**. `GetDeviceInfo`/`GetDeviceItemInfo`
   already provide the raw access; what's missing is a checklist of what "correct" looks like
   for this kind of hardware, which needs to be worked out on its own rather than folded into
   the logic-bug-hunting tools above.

## Upstream PR/issue review (2026-09-10) - items 1-4 done, 5-8 not started

Reviewed the upstream repo's open issues/PRs for ideas worth building ourselves (not merging
wholesale - see reasoning per item). Two open PRs looked at: #26 (hemangjoshi37a, "Lots of
improvements", 15 commits incl. two just "ok") and #30 (mortenfj, HTTP transport/Tag tools/
defensive guards, 10 commits, live-tested against V20 Update 5 with numbers in the PR description).

**Verdict on the PRs themselves**: don't pull either wholesale. #26 crams 80+ methods across every
Openness domain (tag/watch tables, block/type CRUD, HW/network config incl. GSD support, HMI
engineering, technology objects/safety) into the same 3 files with no domain split and ends on two
"ok" commits - too large to verify without the live-testing rigor this project has used throughout
(see CHANGES.md 2026-09-10 entries). #30 is much better disciplined and worth borrowing pieces
from directly (see below). User's take (2026-09-10): #26's *scope* is valuable if built and
stabilized properly - treat it as an idea list, not a source to merge from.

Priority order for picking pieces up ourselves, each to go through this project's usual "implement
+ live-verify against a real running TIA instance before calling it done" cycle:

1. ~~**Export path security (issue #18)**~~ - **Done (2026-09-10).** New
   `Siemens/OutputPathPolicy.cs` centralizes every `exportPath` (`ExportBlock`/`ExportBlocks`/
   `ExportType`/`ExportTypes`/`ExportAsDocuments`/`ExportBlocksAsDocuments`/`ExportTagTable`) -
   only a relative subfolder name under a managed root (`TiaMcpExportRoot` env var, default
   `%TEMP%\tiaportal-mcp-exports`) is accepted; absolute paths, drive letters, UNC, and `..`
   traversal are all rejected with a clear message. `Doctor`'s response now includes `exportRoot`
   so an agent can see where exports land. Live-verified against Mahindra
   (`Mahindra_CPU01_V20_260910_k1`, PID 41948): absolute path rejected, relative subfolder
   succeeded and the file was confirmed on disk at
   `%TEMP%\tiaportal-mcp-exports\security-test-ok\DiagnosticErrorInterrupt.xml`, `../../` traversal
   rejected, and confirmed nothing was created outside the managed root for either rejected case.
2. ~~**`GetProject`/`GetProjects` naming split**~~ - **Done (2026-09-10).** The list-returning
   implementation (was misleadingly named `GetProject`) is now `GetProjects`; a new `GetProject`
   returns just the single currently-attached project/session, backed by new
   `Portal.GetActiveProject()`. Live-verified against Mahindra: both return correct, distinct
   data (singular object with attributes vs. `items` array).
3. ~~**Tag table read tools**~~ - **Already done**, turned out to be already implemented
   (`GetTagTables`/`GetTags`/`ExportTagTable` in `Portal.cs`/`McpServer.cs`) before this review -
   no work needed, just confirmed on 2026-09-10.
4. ~~**GSD file visibility**~~ - **Done (2026-09-10).** New `GetGsdDependencies(devicePath?)` walks
   a project's devices/device items (or one device if scoped) and reports any backed by
   `Siemens.Engineering.HW.Features.GsdDevice`/`GsdDeviceItem` (GsdId/GsdName/GsdType/
   Profibus·Profinet), found via reflection against the installed V20 `Siemens.Engineering.dll`.
   Live-verified against Mahindra (all-native-Siemens hardware): correctly reports "No GSD-based
   devices found". Direct verification against the add_on_eqp project's actual GSD device was
   attempted but that TIA instance (PID 22652) stopped responding to `Connect` mid-session
   (30s+ timeout while a sibling instance connected fine at the same moment) - not a code issue,
   see CHANGES.md 2026-09-10 note; re-verify against a GSD device directly if/when convenient.
   **Known limitation, by design**: only sees devices TIA already loaded successfully - if a GSD
   is missing enough that TIA can't instantiate the device at all, it won't appear here either;
   an empty `GetDevices`/`GetProject` right after a successful `Connect` remains the stronger
   signal for that failure mode (see the dedicated writeup above).
5. ~~**Block/type write CRUD**~~ - **Done (2026-09-10).** Scope decision: "create with real
   content" was already covered by `ImportBlock`/`ImportType` (XML) and `GenerateBlocksFromSource`
   (SCL, item 6 below) - skipped `PlcBlockComposition.CreateFB`/`CreateInstanceDB` (empty
   GUI-oriented block creation, low value for a text-driven agent). What was actually missing:
   delete, attribute modification (covers rename via "Name"), and group management. New tools:
   `DeleteBlock`/`DeleteType`, `SetBlockAttribute`/`SetTypeAttribute` (generic - converts the
   given string to match the attribute's current runtime type automatically),
   `CreateBlockGroup`/`DeleteBlockGroup`, `CreateTypeGroup`/`DeleteTypeGroup` (root/System groups
   can't be deleted, rejected with a clear error). Found and fixed a real bug along the way:
   `SetBlockAttribute`/`SetTypeAttribute` were swallowing the actual failure reason behind a
   generic "Set attribute failed" message - fixed to surface `InnerException.Message` like
   `ExportBlock` already did. Safety approach per user's explicit instruction (2026-09-10): build
   create/delete/modify freely, just say what's about to be tested before running it live - user
   set up a disposable clone of a real project ("Tia for Claude") specifically for this. **Live
   end-to-end verified** against it: create group → generate block/type via SCL → modify
   attributes → verify → delete → delete group → final empty-check, for both blocks and types.
   The attribute-set tests also validated the error-surfacing fix: setting a nonexistent attribute
   ("Comment" on an FB) and setting a blocked one (`Number` while `AutoNumber` is on) both came
   back with the real TIA-side reason instead of a generic failure.
6. ~~**External source (SCL) import/export**~~ - **Done (2026-09-10).** New
   `GetExternalSources`/`ImportExternalSource`/`GenerateBlocksFromSource`/`DeleteExternalSource`/
   `ExportSourceFromBlocks`. `PlcExternalSource` itself has no export method (verified via
   reflection) - real export goes through `PlcExternalSourceSystemGroup.GenerateSource(blocks,
   FileInfo[, GenerateOptions])`, which takes existing `PlcBlock`/`PlcType` objects (both
   implement `IGenerateSource`) and writes them out as combined SCL text - so
   `ExportSourceFromBlocks` exports from blocks/types you already have, not from the external
   source object. `GenerateBlocksFromSource` is the real write risk (creates/overwrites project
   blocks); `ImportExternalSource` only adds a source object and doesn't touch blocks by itself.
   Live-verified end to end against a disposable clone of a real project ("Tia for Claude", user
   set it up specifically for testing writes): imported a test FB's `.scl`, generated a real
   `FB_ClaudeTest` block from it, exported that block back to SCL and confirmed on disk the logic
   round-tripped exactly (only whitespace formatting changed), then deleted the source object and
   confirmed it's gone from `GetExternalSources`.
7. ~~**HMI tag table read tools**~~ - **Partially done (2026-09-10).** New `GetHmiTagTables`/
   `GetHmiTags` for **Unified Comfort/Advanced Panels only** (`Siemens.Engineering.HmiUnified.HmiTags`
   - classic WinCC Comfort/Basic panels use a different namespace, not covered). Mirrors the
   PLC `GetTagTables`/`GetTags` shape; `HmiTag` exposes strongly-typed Address/DataType/
   Connection/PlcName/PlcTag/AccessMode/AcquisitionMode/Scope/TagType directly (no `GetAttribute`
   needed). **No export tool** - `HmiTagTable` has no `Export()` method in this Openness version
   (verified via reflection, unlike `PlcTagTable`/classic `Hmi.Tag.TagTable`), so
   `ExportHmiTagTable` isn't possible here. **Path gotcha**: `softwarePath` isn't just the HMI
   device name - the `HmiSoftware` lives on a nested DeviceItem (e.g. `HMI_1/HMI_RT_1`, not just
   `HMI_1`); PLC's `softwarePath` being a single name was a coincidence (DeviceItem happens to
   share the Device's name there). Live-verified against Mahindra's `HMI_1/HMI_RT_1`
   (MTP1200 Unified Comfort): dozens of real tag tables listed, and `HMI_IO List`'s 3 tags
   returned fully correct including PLC linkage (`plcName: PLC_1`, `plcTag:
   HMI_System.InPut_List[1]`). HMI screens/alarms/text lists are also done now, see below;
   technology objects/safety programming remain not pursued - no concrete need yet.
   - ~~**HMI screens/alarms/text lists**~~ - **Done (2026-09-10).** New `GetHmiScreens`/
     `GetHmiDiscreteAlarms`/`GetHmiAnalogAlarms`/`GetHmiTextLists`, all read-only, same
     `softwarePath` rule as HMI tag tables. Text list *entries* (the actual localized values)
     are not reachable - this Openness version has no type shaped like an entry/item for
     `HmiTextList` (confirmed by enumerating every type in the installed DLL), so
     `GetHmiTextLists` only returns names. Live-verified against Mahindra/Tia for Claude's
     `HMI_1/HMI_RT_1`: 4 real screens (1280x600 each), multiple real discrete alarms with
     event text/alarm class/trigger address, 0 analog alarms (correctly empty, not an error),
     and multiple real text list names.
8. **HTTP transport** (PR #30) - not needed today, stdio covers the actual clients in use (Claude
   Desktop, VS Code). See the existing "Transports (HTTP / TCP)" section below for why Streamable
   HTTP specifically isn't reachable from this net48-pinned project anyway.
9. ~~**PLC tag write CRUD**~~ - **Done (2026-09-10).** Natural follow-on to block/type CRUD.
   Actually read PR #26's tag/watch-table commit diff before building this one (unlike the others,
   which were only scoped from commit titles) - its error-handling style matched ours (same
   upstream base, not PR #26 being well-written), but had heavy tag/watch-table duplication and no
   sign of live testing, so only the method-signature/API-shape ideas were kept, not the code
   itself; reflection was used to independently confirm the real API before writing anything.
   New: `CreateTagTable`/`DeleteTagTable` (`PlcTagTableComposition.Create`/`PlcTagTable.Delete`),
   `CreateTag`/`DeleteTag`/`SetTagAttribute` (`PlcTagComposition.Create(name, dataType, address)`,
   `PlcTag.Delete`, generic attribute setter reusing the same `ConvertAttributeValue` helper as
   block/type CRUD). Unlike blocks/types, tags have no SCL-generation creation route, so a
   dedicated `CreateTag` was actually necessary here (not redundant with anything else). Live
   end-to-end verified against "Tia for Claude": create table → create tag → verify → rename via
   `SetTagAttribute` → verify → delete tag → delete table → final empty-check, all passed in one
   run.
10. **Network/hardware topology** (idea, not started, 2026-09-10) - user asked to investigate next.
    Confirmed by reflection that real Openness types exist: `Siemens.Engineering.HW.Subnet`/
    `SubnetComposition`, `Siemens.Engineering.HW.IoSystem`, `Siemens.Engineering.HW.Features.
    NetworkInterface` (read), and `DeviceComposition.Create`/`CreateWithItem`/`CreateFrom` (device
    creation - write). PR #26 also attempted this (`GetSubnets`/`GetNetworkInterfaces`/
    `SetIpAddress`/`ConnectToSubnet`/`CreateDevice`/`DeleteDevice`/`GetModules`/`GetAddresses`/
    `ImportGsdFile`, per its commit message - not yet read the actual diff for this one). Bigger
    scope than tag CRUD: read side (subnet/IO-system topology, IP addressing) is probably safe and
    valuable on its own; write side (creating/deleting devices, rewiring network topology) is a
    different risk shape than block/tag CRUD - could affect how the project reads on real hardware
    in a way that's harder to eyeball-verify than a block/tag diff. Suggest scoping read-only
    topology first, deciding on write scope separately once read is verified.

## Documentation
- [ ] Add a "CLI Options" section to `README.md` documenting `--tia-major-version <int>` and `--logging <1|2|3>` with defaults and effect (1=stderr, 2=Debug, 3=Event Log). Cross-link to samples.
- [ ] Add a "Build and Run" section to `README.md` showing `dotnet build`, `dotnet run --project src/TiaMcpServer/TiaMcpServer.csproj`, and running compiled `TiaMcpServer.exe`.
- [ ] Add a "Testing" section to `README.md` summarizing prerequisites (TIA Portal V20, `.NET Framework 4.8`, env var `TiaPortalLocation`, Windows group membership "Siemens TIA Openness"), how to run `dotnet test`, and expected limitations if environment is not present. Link to `tests/TiaMcpServer.Test/README.md` and mention manual multi-user session creation.
- [ ] Cross-link the `samples/` directory from `README.md`; reference `samples/vscode/mcp.json` and `samples/claude/claude_desktop_config.json`.
- [ ] Reduce duplication between `gemini.md` and `src/TiaMcpServer/README.md`: consolidate content or keep one as an overview and link to the other.
- [ ] Expand Known Limitations: Document that as of 2025-09-02, importing Ladder (LAD) blocks from SIMATIC SD documents requires the `.s7res` to contain en-US tags for all items; otherwise import may fail.

## CLI / Logging
- [ ] Update `src/TiaMcpServer/CliOptions.cs` `Logging` comment to match current numeric modes (1=stderr, 2=Debug, 3=Event Log) or switch to string values (e.g., "stdio", "debug", "eventlog"). Align parsing and docs accordingly.
- [ ] In `src/TiaMcpServer/Program.cs`, remove hard-coded `options.Logging = 1;` override so CLI-provided logging is honored. Instead, set default only when not provided.
- [ ] Document logging behavior (destinations, filters, minimum levels) in `README.md` or a dedicated `docs/logging.md` and link it.

## Consistency
- [ ] Standardize naming to "TIA Portal" (no hyphen) across all docs and headings; ensure consistent section titles (e.g., clarify "Copilot Chat" vs. "VS Code").
- [ ] Ensure requirements are consistently listed across docs: `.NET Framework 4.8`, `TIA Portal V20`, env var `TiaPortalLocation`, and Windows group membership.

## Changelog
- [ ] Fix typo in `CHANGELOG.md`: "Narketplace" → "Marketplace".

## Tests
- [ ] In `tests/TiaMcpServer.Test/README.md`, double-check instructions for creating `TestSession1.als20` and referencing paths in `Settings.cs`; link this from the main `README.md` Testing section.
- [ ] Consider documenting how to selectively run tests or skip environment-dependent ones (e.g., via MSTest categories) when TIA is unavailable.

## Housekeeping
- [x] Add a "Contributing" link in `README.md` pointing to `agents.md`.

## Transports (HTTP / TCP)

Streamable HTTP is **not reachable from this project**: the SDK ships it in
`ModelContextProtocol.AspNetCore`, which requires .NET 8+, while this server is
pinned to `net48` by TIA Openness. The former plan to hand-roll an `HttpListener`
host is retired — it would not be spec-compliant Streamable HTTP.

- [ ] Decide whether remote access is wanted at all (stdio covers the VS Code extension today)
- [ ] If yes, evaluate a separate .NET 8+ proxy that speaks Streamable HTTP to clients and stdio to this server
- [ ] Cheaper interim option: TCP transport via `.WithStreamServerTransport(input, output)` with a TCP listener
  - Not MCP Streamable HTTP; only useful for bespoke clients
- [ ] Documentation
  - Keep the "Transports" and "MCP Protocol" sections in both READMEs in sync with whatever is chosen

## Siemens Wrappers Refactor (Duplication/Exceptions)

- [ ] Centralize exception handling in Siemens wrappers
  Reasoning: `Portal.cs` contains many `try/catch (Exception)` blocks that return `false`/`null` without consistent logging or context. A small helper reduces boilerplate and improves observability.
  Excerpt (today):
  ```csharp
  try
  {
      _project = null;
      _portal?.Dispose();
      return true;
  }
  catch (Exception)
  {
      return false;
  }
  ```
  Example (proposed helper usage):
  ```csharp
  return Operation.Run(_logger, "Disconnecting from TIA Portal", () =>
  {
      _project = null;
      _portal?.Dispose();
  });
  ```

- [ ] Add guard + not-found helpers for Siemens entities
  Reasoning: Repeated null checks (GetDevice/GetType/GetBlock, etc.) and ad-hoc error messages create inconsistencies. A guard establishes consistent messages and reduces lines.
  Excerpt (today):
  ```csharp
  var device = GetDevice(devicePath);
  if (device == null)
  {
      return false; // or throw later in MCP layer
  }
  ```
  Example (proposed):
  ```csharp
  var device = Guard.RequireNotNull(GetDevice(devicePath),
      () => McpErrors.NotFound("Device", devicePath));
  ```

- [ ] Introduce DTO mappers for attributes → response objects
  Reasoning: Mapping attributes and common fields is repeated across blocks/types/devices. Central mappers keep shape changes consistent.
  Excerpt (today):
  ```csharp
  var attrs = Helper.GetAttributeList(block);
  var dto = new ResponseBlockInfo { Name = block.Name, Attributes = attrs, /* ... */ };
  ```
  Example (proposed):
  ```csharp
  var dto = DtoMapper.ToBlockInfo(block);
  ```

- [ ] Roll out PortalException + context enrichment pattern beyond ExportBlock
  Affected: `ImportBlock`, `ExportBlocks`, `ExportType`, `ImportType`, `ExportBlocksAsDocuments`, `ImportFromDocuments`, etc.
  Rules:
  - Short messages + `PortalErrorCode` only (no param echoing in message)
  - Attach context in `Exception.Data` in a single catch per portal method, just before rethrow (see docs/error-model.md)
  - Preserve `InnerException` for operation failures and log once with structured fields

- [ ] Add helpers for path resolution parity
  - `GetTypePath(PlcType)` analogous to `GetBlockPath(PlcBlock)` for building fully-qualified paths.
  - Use these from MCP when building “Did you mean…” suggestions.

- [ ] Create a list mapping helper for collection projections
  Reasoning: Multiple `foreach` loops project Siemens objects into response lists with null filters. A helper simplifies and standardizes this.
  Excerpt (today):
  ```csharp
  var list = new List<ResponseBlockInfo>();
  foreach (var b in blocks)
  {
      if (b != null) list.Add(DtoMapper.ToBlockInfo(b));
  }
  ```
  Example (proposed):
  ```csharp
  var list = ListMapper.Map(blocks, DtoMapper.ToBlockInfo);
  ```

- [ ] Generalize ASCII tree printing (project/software trees)
  Reasoning: Several recursive methods build prefixed tree strings with near-identical logic. A generic tree printer would remove duplication and reduce bugs.
  Excerpt (today):
  ```csharp
  private void GetProjectTreeDevices(StringBuilder sb, DeviceComposition devices, List<bool> ancestorStates) { /*...*/ }
  private void GetProjectTreeGroups(StringBuilder sb, DeviceUserGroupComposition groups, List<bool> ancestorStates) { /*...*/ }
  ```
  Example (proposed):
  ```csharp
  TreePrinter.Write(sb, root,
      children: n => n.Children,
      label:    n => n.DisplayName,
      hasMore:  n => n.HasMore);
  ```

- [ ] Replace boolean returns with lightweight result objects (internals)
  Reasoning: Widespread `return true/false` makes error sources opaque. A `Result` type can carry messages and improves upstream decisions without changing public MCP contracts yet.
  Excerpt (today):
  ```csharp
  if (!Compile()) return false;
  ```
  Example (proposed):
  ```csharp
  var r = Compile();
  if (!r.Success) return r; // r.Message contains context
  ```

- [ ] Consolidate progress reporting for export/import operations
  Reasoning: ExportBlocks/ExportTypes/ExportBlocksAsDocuments share progress calculations and error notifications. A wrapper reduces scattered try/catch and progress-token checks.
  Excerpt (today):
  ```csharp
  // compute totals, send start; for each item send progress; on error send error progress
  ```
  Example (proposed):
  ```csharp
  await ProgressRunner.Run(total, progressToken, onStart, onItem, onComplete, onError);
  ```

- [ ] Address nullable warnings in `Portal.cs` with guards
  Reasoning: Build shows nullability warnings for software tree groups; explicit guards make intent clear and avoid runtime NREs.
  Excerpt (warnings):
  - CS8602: Dereference of a possibly null reference.
  - CS8604: Possible null reference argument for parameter `blockGroup`/`typeGroup`.
  Example (proposed):
  ```csharp
  var group = Guard.RequireNotNull(blockGroup, () => new InvalidOperationException("Block group missing"));
  GetSoftwareTreeBlockGroup(sb, group, ancestorStates, label, isLast);
  ```
- [ ] Verify that all fenced code blocks in Markdown include language hints per `style.md` and wrap lines for readability.

## MCP Tools Docs (Export/Import)
- [ ] Create per-tool docs under `docs/tools/`:
  - `docs/tools/export-blocks.md`
  - `docs/tools/import-blocks.md`
  Each should include: Overview, Preconditions, Parameters (names/types/defaults), Order of operations (numbered), Error model, Examples (request/response for MCP), Troubleshooting, Performance/limits. Include a Mermaid sequence diagram for call flow.
- [ ] Define a shared error mapping in `docs/error-model.md` (validation → `InvalidParams`, not found → `NotFound`, Openness API → `OpennessError` with native code; guidance for partial vs. overall failure).
- [ ] Add a "Tools" section to `README.md` linking to `docs/tools/` and `docs/error-model.md`; reference `samples/` configs.
- [ ] Add XML documentation comments to export/import methods in `ModelContextProtocol/McpServer.cs` and corresponding Siemens wrappers (e.g., `Siemens/Portal.cs`, `Siemens/Openness.cs`). Cover summary, pre/postconditions, ordered steps, params/returns, exceptions, thread-safety/cancellation, and `<seealso>` links to tool docs.
- [ ] Enable XML documentation file generation in `src/TiaMcpServer/TiaMcpServer.csproj` (set `DocumentationFile` for `net48`) so IDE tooltips and doc generation work.
- [ ] Add usage recipes under `docs/recipes/` (e.g., export only FBs matching `FB_Prod.*`, import with overwrite/skip, preservePath false) with minimal and full payloads and expected responses.

- [ ] Document block path rules and suggestions
  - In `docs/tools/export-blocks.md` and server README, state that `blockPath` must be `Group/Subgroup/Name` and that MCP suggests candidates for single-name inputs by regex searching all blocks and formatting paths via `Portal.GetBlockPath`.
- [ ] Cross-link: from tool docs to relevant tests in `tests/TiaMcpServer.Test` and from code via `<seealso>` to markdown docs; from README to samples and tool docs.
- [ ] Optional: Evaluate DocFX (or similar) to generate API docs from XML comments; if adopted, add a short `docs/README.md` and build instructions.
- [ ] Optional CI: add markdown linting and doc build validation to the pipeline (skippable locally if TIA isn’t installed).

### Version Gating (Export as Documents)
- [ ] Document that `ExportAsDocuments` and `ExportBlocksAsDocuments` require TIA Portal V20+; update prompts and README accordingly.

## Import From Documents (V20+)
- [ ] Add tests for `ImportFromDocuments`: single import happy path, version gating (<20), invalid `importPath`, invalid `fileNameWithoutExtension`.
- [ ] Add tests for `ImportBlocksFromDocuments`: regex filtering on `.s7dcl`, progress notifications, partial failures aggregation, empty directory behavior.
- [ ] Validate enum mapping for `importOption` (Override/None; extend if environment exposes more values).
- [ ] Verify placement into `groupPath` (root vs. nested groups) and behavior when group does not exist.
- [ ] Add docs pages under `docs/tools/` for import-from-documents tools; include file discovery rules (.s7dcl/.s7res), name derivation, and option mapping.
