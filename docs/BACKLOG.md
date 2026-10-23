# Backlog

Open work for this fork, in priority order within each group. The HMI group (C) comes first right now. Before starting an item, open a draft pull
request titled with its id (see "Contributing to this fork" in [AGENTS.md](../AGENTS.md)) so nobody else
picks it up. Remove the item here in the same pull request that finishes it.

Done so far: software checksum (A0), network-level block edit with LAD flow order and SCL text (A1), backup
before delete or replacing import (A2), DB start values read/write (A3), V21 build switch (part of D12), HMI screen item dynamizations read/write (C6), block and UDT member add/change/delete (`EditBlockInterface`, `EditTypeInterface`), change log of write calls (B4, `GetChangeLog`).

## C. HMI (WinCC Unified), alarms, texts - current priority

| Id | Item | Notes |
| -- | ---- | ----- |
| C1 | Write Unified alarms, alarm classes and text lists | Reading exists already. `HmiUnified.HmiAlarm`, `HmiUnified.TextGraphicList`. Next C item to do. |
| C7 | Read global script modules | The shared functions screen events call; item event scripts are already readable. `HmiUnified.Scripts.HmiScriptModule`. |
| C8 | Read alarm classes | Color, acknowledgement. `HmiUnified.HmiAlarm.HmiAlarmClass`. Pairs with C1. |
| C2 | Export/import PLC alarm texts and text lists as xlsx | `SW.Alarm.PlcAlarmTextProvider`, `PlcAlarmTextListProvider`. |
| C3 | HMI connections, tag acquisition cycles, logging tags | `HmiUnified.HmiConnections`. |
| C4 | HMI compile result | Check whether `CompileSoftware` already covers HMI software. |
| C9 | Create and move screen groups | `HmiUnified.UI.ScreenGroup`. |
| C10 | Data logs, alarm logs, logging tags: read and set | `HmiUnified.HmiLogging`, `LoggingTags`. |
| C11 | Read runtime settings | Language and font, max logins, OPC UA server, reports. `HmiUnified.RuntimeSettings`. |
| C12 | Audit trail settings | `HmiLogging.HmiAuditTrail`. |
| C13 | Read system text lists | `TextGraphicList.HmiSystemTextList`. |
| C5 | Project text export/import (multilingual) | `ExportProjectTexts`. |

## A. Most useful next

| Id | Item | Notes |
| -- | ---- | ----- |
| A4 | Read the CPU diagnostic buffer | Direct S7CommPlus connection, next to `GetActivePlcAlarmsDirect`. Shows why a CPU went to STOP or why an axis reports a limit alarm without opening TIA Portal online. Needs a real PLC to test. |
| A5 | Simple value trace | Read a list of tags every N ms, M times, and report only the moments a value changed. Builds on `ReadPlcTagValuesDirect`. |
| A6 | HMI tag usage search (in progress) | PLC tag -> HMI tags -> screens, screen items and event scripts that use them. |
| A7 | I/O map | Address <-> PLC tag <-> hardware module and channel, as one table. Openness "addresses" and "channels of device items". |

## B. Useful, with risk (separate flag)

| Id | Item | Notes |
| -- | ---- | ----- |
| B1 | Download (software, changes only) | Behind its own `--allow-download` flag and a confirmation per call. Show the pre-download check first (does the CPU need STOP?). Openness `DownloadProvider`, `DownloadConfiguration`. |
| B2 | Snapshot online values as start values | Openness upload. |
| B3 | CPU RUN/STOP | Openness has no standalone command; only `StopModules`/`StartModules` inside a download, so it belongs to B1. |
| B5 | Hardware compare (project vs. another PLC or online) | Read-only. `IHardwareCompareTarget`, `CompareTo`. |

## D. Libraries, motion, other

| Id | Item | Notes |
| -- | ---- | ----- |
| D1 | Library types: list, compare, find instances, out-of-date instances | `Library.Types`, `LibraryTypeVersion.FindInstances`. Upstream added `GetOpenLibraries`/`GetReferencedLibraries` after our base; start from those. Also enables a native block copy from a master copy. |
| D2 | Read SINAMICS drive parameters (Startdrive) | `MC.Drives`. Check that Startdrive is installed first. |
| D3 | Create/delete single watch table entries | The entry composition only has a comment-row creator; find the right creation call on the live API. Today: export, edit, `ImportWatchTable`. |
| D4 | Project history and unsaved-change flag | `HistoryEntries`, `IsModified`. Small. |
| D5 | Compare the safety signature before and after a change | Extend `GetSafetyInfo`. |
| D6 | Know-how protection handling | `PlcBlockProtectionProvider`. Passwords make this low priority. |
| D7 | ProDiag supervision export/import | `SW.Supervision`. |
| D8 | Read GRAPH and STL blocks as text | |
| D9 | Read the OPC UA server interface | `SW.OpcUa`. |
| D10 | Update instruction versions in a program | `PlcSoftware.UpdateProgram`. Writes the project; low priority. |
| D11 | Multiuser server support | Upstream has it; only needed if multiuser is used. |
| D12 | V21 | Build switch done (`-p:TiaVersion=21`). Not yet built or tested on a PC with TIA Portal V21; API differences may show up there. |
