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

---

## Connection

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `Connect` | idempotent | - | Connects to a running TIA Portal instance. Call this first. |
| `Disconnect` | idempotent | - | Disconnects from TIA Portal. |
| `GetState` | RO | - | Returns `isConnected`, open `project`/`session` name. |
| `Doctor` | RO | - | Environment diagnostics: connection state, active/installed TIA versions, Openness user group membership. Never connects or changes anything. Installed-version scan only reports V21+ (a working V20 install won't show up there - see `CHANGES.md`). |

## Project / session

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetProject` | RO | - | Lists currently open local projects/sessions with their attributes. |
| `OpenProject` | idempotent | `path` - absolute `.apXX` (project) or `.alsXX` (session) path | Closes whatever's open first. `XX` = TIA version, e.g. `.ap20`. |
| `SaveProject` | **Destructive** | - | Saves the open project, or the local session if one is open. |
| `SaveAsProject` | **Destructive** | `newProjectPath` | Only valid for projects, not local sessions. |
| `CloseProject` | **Destructive** | - | Closes the open project or local session. |
| `GetProjectTree` | RO | - | ASCII tree of the whole project structure (devices, PLC software, etc.). Good starting point to find `softwarePath`/`devicePath` values. |

## Devices

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetDevices` | RO | - | Lists all devices in the project with their attributes. |
| `GetDeviceInfo` | RO | `devicePath` | Path from `GetProjectTree`/`GetDevices`. |
| `GetDeviceItemInfo` | RO | `deviceItemPath` | For sub-items of a device (modules, submodules). |

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
| `ExportBlock` | **Destructive** | `softwarePath`, `blockPath`, `exportPath`, `preservePath` (optional) | Exports one block to XML. Fails with "not found" + path suggestions if `blockPath` is a bare, ambiguous name. Requires the project to be **offline** - TIA Portal itself refuses export while online/monitoring. |
| `ImportBlock` | **Destructive** | `softwarePath`, `groupPath`, `importPath` (XML file) | Overwrites an existing block of the same name. |
| `ExportBlocks` | **Destructive**, async w/ progress | `softwarePath`, `exportPath`, `regexName` (optional), `preservePath` (optional) | Bulk export. Skips inconsistent blocks and reports them separately in `Inconsistent`; compile first if you need them included. |

## Types (PLC data types)

| Tool | Flags | Parameters | Notes |
|---|---|---|---|
| `GetTypeInfo` | RO | `softwarePath`, `typePath` | |
| `GetTypes` | RO | `softwarePath`, `regexName` (optional) | |
| `ExportType` | **Destructive** | `softwarePath`, `exportPath`, `typePath`, `preservePath` (optional) | Same offline-mode requirement as `ExportBlock`. |
| `ImportType` | **Destructive** | `softwarePath`, `groupPath`, `importPath` (XML file) | |
| `ExportTypes` | **Destructive**, async w/ progress | `softwarePath`, `exportPath`, `regexName` (optional), `preservePath` (optional) | Bulk export, same inconsistent-item handling as `ExportBlocks`. |

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

---

## Safety: what these tools *cannot* do

None of the above ever downloads to, starts/stops, or forces I/O on an actual PLC. Every
`Destructive`-flagged tool only reads or writes the **local TIA Portal engineering project**
(the open `.apXX`/`.alsXX` file and the block/type/tag XML files on disk) - there is no "download to
device", "go online/offline", "start/stop CPU", or force-write tool implemented anywhere in this
server. Reaching a live PLC from a changed project still requires a human to do that explicitly in
TIA Portal itself.
