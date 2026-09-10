# Tool Reference

Every tool listed here is defined in `src/TiaMcpServer/ModelContextProtocol/McpServer.cs` and is
also self-described over MCP (`tools/list`), including full parameter schemas - this file exists
for humans browsing the repo who don't want to spin up the server to see what's available.

Call `Connect` first. Most tools below need a project open via `OpenProject`, and most `Get*`
tools need a `softwarePath` (the path to a PLC software container, e.g. `"PLC_1"` - use
`GetProjectTree` to find it).

**Path format gotcha:** `GetSoftwareTree`/`GetBlocksWithHierarchy` render the block/type roots with
the display labels `"Program blocks"` and `"PLC data types"`. Those are cosmetic, not real
subgroups, but the tools strip them automatically, so paths copied straight out of that tree output
work fine either with or without the leading label (see `CHANGES.md`, 2026-09-09 entry).

Legend: **RO** = read-only, **Destructive** = can overwrite/delete something on disk or in the open
project (never reaches actual PLC hardware - see the "Safety" note at the end).

> **`exportPath` gotcha:** every `Export*` tool writes under a single server-managed export root
> (`TiaMcpExportRoot` env var, default `%TEMP%\tiaportal-mcp-exports` - see `Doctor`'s `exportRoot`
> field for the live value). `exportPath` must be a relative subfolder name under that root (or
> omitted); an absolute path, a different drive, a UNC path, or `..` traversal is rejected with a
> clear error instead of being written anywhere (see `CHANGES.md` 2026-09-10, closes the same risk
> as upstream issue #18).

---

## Connection

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `ListTiaPortalInstances` | RO | - | Lists every running TIA Portal process (id, open project path, mode) without attaching to any of them. Check this first if more than one might be open. |
| `Connect` | idempotent | `processId` (optional) | Connects to a running TIA Portal instance. Call this first. With exactly one TIA Portal process running, `processId` can be omitted. With two or more, `Connect` refuses to guess and asks for a `processId` from `ListTiaPortalInstances` instead - it never silently picks one for you. |
| `Disconnect` | idempotent | - | Disconnects from TIA Portal. |
| `GetState` | RO | - | Returns `isConnected`, open `project`/`session` name. |
| `Doctor` | RO | - | Environment diagnostics: connection state, active/installed TIA versions, Openness user group membership. Never connects or changes anything. Installed-version scan only reports V21+ (a working V20 install won't show up there - see `CHANGES.md`). |

## Project / session

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetProject` | RO | - | The single currently-attached project/session, with its attributes. Errors if none is open. |
| `GetProjects` | RO | - | Lists *every* open local project/session in this TIA Portal instance with their attributes. |
| `OpenProject` | idempotent | `path` - absolute `.apXX` (project) or `.alsXX` (session) path | Closes whatever's open first. `XX` = TIA version, e.g. `.ap20`. |
| `SaveProject` | **Destructive** | - | Saves the open project, or the local session if one is open. |
| `SaveAsProject` | **Destructive** | `newProjectPath` | Only valid for projects, not local sessions. |
| `CloseProject` | **Destructive** | - | Closes the open project or local session. |
| `GetProjectTree` | RO | - | ASCII tree of the whole project structure (devices, PLC software, etc.). Good starting point to find `softwarePath`/`devicePath` values. |

## Devices

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetDevices` | RO | - | Lists all devices in the project with their attributes. |
| `GetDeviceInfo` | RO | `devicePath` | Path from `GetProjectTree`/`GetDevices`. Handles device names that themselves contain `/` (e.g. `S7-1500/ET200MP station_1`, see `CHANGES.md` 2026-09-09). |
| `GetDeviceItemInfo` | RO | `deviceItemPath` | For sub-items of a device (modules, submodules). |
| `GetGsdDependencies` | RO | `devicePath` (optional - omit to scan the whole project) | Lists third-party (GSD-based) devices/device items with their GsdId/GsdName/GsdType/Profibus·Profinet - check this before moving a project to another machine to see what GSD files need installing there. Only sees devices TIA already loaded successfully - if a missing GSD stops TIA from instantiating a device at all, it won't show up here either; an empty `GetDevices`/`GetProject` right after a successful `Connect` is the stronger signal for that (see `CHANGES.md` 2026-09-10, the "Projects/LocalSessions empty" writeup). |
| `GetSubnets` | RO | - | Network topology: every subnet in the project with its type, connected node names, and IO systems. Only works on a full project - returns an empty list (not an error) when attached to a multiuser local session (`.als`), since `Subnets` isn't exposed there. |
| `GetNetworkInterfaceInfo` | RO | `path` (nested DeviceItem path) | IP/mask/connected-subnet info for a device's network interface. The interface is usually nested *inside* a CPU/module, e.g. `S7-1500/ET200MP station_1/PLC_1/PROFINET interface_1` - use `GetProjectTree` to find its exact name; this is not the same shape as `softwarePath`/`devicePath`. |
| `GetOnlineState` | RO | `path` (device or device item) | Reads the engineering station's own online/diagnostic connection state (`Offline`/`Connecting`/`Online`/...) - unrelated to whether the PLC itself is Run/Stop. Tries a Device first, then a DeviceItem (e.g. the CPU module, like `PLC_1`) at the same path. |
| `GoOnline` | idempotent | `path` (device or device item) | Establishes that connection. Fails with TIA's own error if the target isn't reachable/configured (e.g. PLCSIM Advanced instance not running) - a real Openness error, not a bug in this tool. |
| `GoOffline` | idempotent | `path` (device or device item) | Disconnects it; a no-op if already offline. |

> **Before calling `GoOffline` to unblock an export:** this drops the *engineering station's*
> live diagnostic connection, not something private to this MCP server. If a human has TIA
> Portal's own window open, they will see it go offline in real time - their online/monitoring
> view disappears with no warning from their side. If a `ExportBlock`/`ExportType`/`ExportAsDocuments`
> call fails because the project is online, don't reach for `GoOffline` automatically - tell the
> user the export needs the project offline first and let them decide (they may be relying on
> that connection, e.g. watching live values or mid-download). Call `GoOnline` afterward to restore
> it once you're done, since nothing else does that for you.

## PLC software

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetSoftwareInfo` | RO | `softwarePath` | |
| `GetSoftwareTree` | RO | `softwarePath` | ASCII tree of blocks, types, and external sources under a PLC software container. |
| `CompileSoftware` | idempotent | `softwarePath`, `password` (optional) | Compiles the *engineering project*, not a download to hardware. |

## Blocks

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetBlockInfo` | RO | `softwarePath`, `blockPath` | Use a fully qualified `blockPath` like `Group/Subgroup/Name`; a bare name is ambiguous. |
| `GetBlocks` | RO | `softwarePath`, `regexName` (optional, default = all) | Flat list; can be slow on large projects since it reads every block's full attribute set. |
| `GetBlocksWithHierarchy` | RO | `softwarePath` | Same data as `GetBlocks` but nested by group, mirroring `GetSoftwareTree`'s shape. |
| `ExportBlock` | **Destructive** | `softwarePath`, `blockPath`, `exportPath`, `preservePath` (optional) | Exports one block to XML. Fails with "not found" + path suggestions if `blockPath` is a bare, ambiguous name. Requires the project to be **offline** - TIA Portal itself refuses export while online/monitoring. See the offline-mode callout below `GoOffline` before reaching for it to unblock this. |
| `ImportBlock` | **Destructive** | `softwarePath`, `groupPath`, `importPath` (XML file) | Overwrites an existing block of the same name. |
| `ExportBlocks` | **Destructive**, async w/ progress | `softwarePath`, `exportPath`, `regexName` (optional), `preservePath` (optional) | Bulk export. Skips inconsistent blocks and reports them separately in `Inconsistent`; compile first if you need them included. |
| `GetBlockCrossReferences` | RO | `softwarePath`, `blockPath` | Compiler-backed "where is this block used" - e.g. an FB used as an instance type: which blocks declare a Static instance of it (`access: "Multiinstance"`), or an FC/OB and who calls it (`access: "Call"`). Only returns `UsedBy` locations for the block's own entry (its internal `Uses` - what it calls/reads - is filtered out; see `CHANGES.md` 2026-09-09). |
| `DeleteBlock` | **Destructive** | `softwarePath`, `blockPath` | Irreversible except via TIA's own undo/project backup. Confirm with the user before using against a real (non-disposable) project. |
| `SetBlockAttribute` | **Destructive** | `softwarePath`, `blockPath`, `attributeName`, `value` (string) | Generic attribute setter - set `Name` to rename. `value` is auto-converted to match the attribute's current type. Attribute names/writability vary by block type - check `GetBlockInfo` first. Errors surface the real TIA-side reason (e.g. "attribute not supported by this block type", "can't set Number while AutoNumber is on"). |
| `CreateBlockGroup` / `DeleteBlockGroup` | Create: idempotent-ish / Delete: **Destructive** | `softwarePath`, `parentGroupPath`/`groupPath`, `name` (create only) | Deleting a group deletes every block inside it. Root/System group can't be deleted. |

## Types (PLC data types)

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetTypeInfo` | RO | `softwarePath`, `typePath` | |
| `GetTypes` | RO | `softwarePath`, `regexName` (optional) | |
| `ExportType` | **Destructive** | `softwarePath`, `exportPath`, `typePath`, `preservePath` (optional) | Same offline-mode requirement as `ExportBlock`; same `GoOffline` callout applies. |
| `ImportType` | **Destructive** | `softwarePath`, `groupPath`, `importPath` (XML file) | |
| `ExportTypes` | **Destructive**, async w/ progress | `softwarePath`, `exportPath`, `regexName` (optional), `preservePath` (optional) | Bulk export, same inconsistent-item handling as `ExportBlocks`. |
| `GetTypeCrossReferences` | RO | `softwarePath`, `typePath` | Same as `GetBlockCrossReferences` but for a PLC data type (UDT). Note: an FB used as an instance type is a *block*, not a type - use `GetBlockCrossReferences` for those (e.g. `Main_Tracking_Data`). |
| `DeleteType` | **Destructive** | `softwarePath`, `typePath` | Check `GetTypeCrossReferences` first - deleting a type still used by blocks will break them. |
| `SetTypeAttribute` | **Destructive** | `softwarePath`, `typePath`, `attributeName`, `value` (string) | Generic attribute setter, same conversion/error-surfacing behavior as `SetBlockAttribute`. |
| `CreateTypeGroup` / `DeleteTypeGroup` | Create: idempotent-ish / Delete: **Destructive** | `softwarePath`, `parentGroupPath`/`groupPath`, `name` (create only) | Deleting a group deletes every type inside it. Root/System group can't be deleted. |

## External sources (SCL/AWL/GRAPH import, SCL export)

`PlcExternalSource` (the imported source object) has no export method itself - export goes
through existing blocks/types instead, see `ExportSourceFromBlocks`.

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetExternalSources` | RO | `softwarePath`, `regexName` (optional) | |
| `ImportExternalSource` | **Destructive**, writes to project | `softwarePath`, `groupPath`, `importPath` (local file), `sourceName` (optional) | Adds a source object only - does not create/change any blocks by itself. |
| `GenerateBlocksFromSource` | **Destructive**, writes to project | `softwarePath`, `sourcePath`, `keepOnError` (optional) | The real write step - compiles the source into real blocks/types, which can create new ones or **overwrite existing ones of the same name**. Highest-risk tool in this group; confirm with the user before using it against a real (non-disposable) project. |
| `DeleteExternalSource` | **Destructive** | `softwarePath`, `sourcePath` | Removes the source object only - blocks already generated from it are unaffected. |
| `ExportSourceFromBlocks` | **Destructive** (writes to disk only, doesn't touch the project) | `softwarePath`, `exportPath`, `fileName`, `blockPaths` (optional), `typePaths` (optional), `withDependencies` (optional) | Exports existing blocks/types as combined SCL text. Give at least one of `blockPaths`/`typePaths`. |

## Documents (.s7dcl/.s7res) - **requires TIA Portal V20+**

SIMATIC SD document format, mainly useful for round-tripping SCL logic through text-based tools.

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `ExportAsDocuments` | **Destructive** | `softwarePath`, `blockPath`, `exportPath`, `preservePath` (optional) | Single block. |
| `ExportBlocksAsDocuments` | **Destructive**, async w/ progress | `softwarePath`, `exportPath`, `regexName` (optional), `preservePath` (optional) | Bulk. |
| `ImportFromDocuments` | **Destructive** | `softwarePath`, `groupPath` (optional), `importPath` (folder), `fileNameWithoutExtension`, `importOption` (`None`/`Override`/`SkipInactiveCultures`/`ActivateInactiveCultures`, default `Override`) | Warns if the `.s7res` is missing en-US tags for any item - known Openness import bug for LAD blocks in that case, see `README.md` Known Limitations. |
| `ImportBlocksFromDocuments` | **Destructive**, async w/ progress | `softwarePath`, `groupPath` (optional), `importPath` (folder), `regexName` (optional), `importOption` (optional, default `Override`) | Bulk version of the above, matched against `.s7dcl` filenames. |

## Tag tables

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetTagTables` | RO | `softwarePath`, `regexName` (optional) | |
| `GetTags` | RO | `softwarePath`, `tagTablePath`, `regexName` (optional) | |
| `ExportTagTable` | **Destructive** | `softwarePath`, `tagTablePath`, `exportPath`, `preservePath` (optional) | |
| `CreateTagTable` / `DeleteTagTable` | Create: idempotent-ish / Delete: **Destructive** | `softwarePath`, `groupPath`/`tagTablePath`, `name` (create only) | Deleting a table deletes every tag inside it. |
| `CreateTag` | **Destructive**, writes to project | `softwarePath`, `tagTablePath`, `name`, `dataType`, `logicalAddress` (optional - omit to auto-assign) | Tags have no SCL-generation route (unlike blocks/types), so this is the direct way to create one. |
| `DeleteTag` | **Destructive** | `softwarePath`, `tagTablePath`, `tagName` | |
| `SetTagAttribute` | **Destructive** | `softwarePath`, `tagTablePath`, `tagName`, `attributeName`, `value` (string) | Generic attribute setter - set `Name` to rename. Same type-conversion/error-surfacing behavior as `SetBlockAttribute`. |

## HMI tag tables (Unified Comfort/Advanced Panels only)

Classic WinCC Comfort/Basic panels use a different Openness API and aren't covered by these tools.

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetHmiTagTables` | RO | `softwarePath`, `regexName` (optional) | |
| `GetHmiTags` | RO | `softwarePath`, `tagTablePath`, `regexName` (optional) | Returns `plcName`/`plcTag` linkage, `accessMode`, `acquisitionMode`, `scope`, `tagType` alongside the usual name/dataType/address/comment. |

> **`softwarePath` gotcha for HMI:** unlike PLC (`"PLC_1"` alone works because the Device and its
> software-holding DeviceItem happen to share a name there), an HMI device's `HmiSoftware` lives on
> a *nested* DeviceItem - e.g. `"HMI_1/HMI_RT_1"`, not `"HMI_1"`. Use `GetProjectTree` to find the
> exact DeviceItem name under the HMI device (look for `HmiSoftware: ... [HMI Program]` in the
> tree). Also **no export tool** - `HmiTagTable` has no `Export()` method in this Openness version
> (unlike `PlcTagTable`), verified via reflection - not just missing, unsupported here.

## HMI screens/alarms/text lists (Unified Comfort/Advanced Panels only)

Same `softwarePath` rule as HMI tag tables (e.g. `"HMI_1/HMI_RT_1"`). All read-only.

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetHmiScreens` | RO | `softwarePath`, `regexName` (optional) | Name, display name, screen number, width/height. |
| `GetHmiDiscreteAlarms` | RO | `softwarePath`, `regexName` (optional) | Bit-triggered alarms: event/info text, alarm class, area, priority, trigger bit address. |
| `GetHmiAnalogAlarms` | RO | `softwarePath`, `regexName` (optional) | Limit-triggered alarms: same fields plus `condition` (the comparison type) and a plain trigger address. |
| `GetHmiTextLists` | RO | `softwarePath`, `regexName` (optional) | **Names only** - this Openness version has no type for individual text list entries/values (verified by enumerating every type in the installed DLL), so entry contents aren't reachable at all. |

---

## Safety: what these tools *cannot* do

None of the above ever downloads to, starts/stops, or forces I/O on an actual PLC. Every
`Destructive`-flagged tool only reads or writes the **local TIA Portal engineering project**
(the open `.apXX`/`.alsXX` file and the block/type/tag XML files on disk) - there is no "download to
device", "start/stop CPU", or force-write tool implemented anywhere in this server. Reaching a live
PLC from a changed project still requires a human to do that explicitly in TIA Portal itself.

`GoOnline`/`GoOffline` are the one exception worth calling out explicitly: they do establish/drop
the engineering station's own online *connection* (the same thing the "Go online" button in TIA
Portal's toolbar does) - but that connection is for diagnostics/monitoring only. It never commands
the PLC to Run or Stop, and going offline never stops the PLC either; the PLC keeps doing whatever
it was doing regardless of whether this connection exists. Deliberate choice: Run/Stop control is
intentionally not implemented (see `TODO.md`) - accidentally stopping a live PLC is a real safety
risk that a plain MCP tool call shouldn't be able to trigger.
