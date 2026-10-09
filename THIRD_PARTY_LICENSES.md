# Third-Party Licenses

This project bundles third-party components under their own licenses, separate from this
repository's own license. They are kept as independent assemblies (not merged/ILRepacked into
`TiaMcpServer.exe`), so each can be rebuilt, inspected, or replaced on its own.

## S7CommPlusDriver

- **Path**: `third_party/S7CommPlusDriver` (git submodule)
- **Upstream**: https://github.com/thomas-v2/S7CommPlusDriver
- **Pinned commit**: `60ecafb` (local patches on top of upstream `dbd61e4` - see below)
- **License**: GNU Lesser General Public License v3.0 (LGPL-3.0) - see
  `third_party/S7CommPlusDriver/LICENSE`
- **Copyright**: Thomas Wiens (th.wiens@gmx.de)
- **Used by**: `src/TiaMcpServer/Siemens/S7Diagnostics.cs` and the `*PlcDirect` MCP tools in
  `McpServer.cs` (`ConnectPlcDirect`, `DisconnectPlcDirect`, `BrowsePlcTagsDirect`,
  `ReadPlcTagValuesDirect`, `GetActivePlcAlarmsDirect`)
- **What it does**: a reverse-engineered (not Siemens-documented) implementation of the
  S7CommPlus protocol, used to talk directly to a live S7-1200/1500 CPU over TCP 102 for live tag
  values and active-alarm snapshots - entirely separate from, and unaffected by, this project's
  normal Openness-based connection to TIA Portal. See the tool descriptions in `McpServer.cs` and
  the "Direct PLC (S7CommPlus)" section of `docs/TOOLS.md` for the reliability/maturity caveats
  this carries (undocumented protocol, parts of the driver - e.g. alarm subscriptions - are
  explicitly marked experimental by its own author).
- **Local patches (not upstream)** in `src/S7CommPlusDriver/Net/S7Client.cs`:
  1. `RunThread()` - the driver's background socket-receive thread - had no exception handling at
     all. Confirmed live: connecting to a real safety CPU (1518F-3 PN via PLCSIM Advanced) crashed
     the **entire host process**, not just the connection, because an unhandled exception on a
     background thread always terminates a .NET process regardless of any try/catch at the call
     site (different thread/call stack). Patched to catch and log to stderr, stopping just that
     thread instead.
  2. `SSL_CTX_keylog_cb()` (the TLS session-keylog callback, invoked by native OpenSSL via a
     function pointer) wrote to a relative `key_*.log` path - i.e. resolved against the host
     process's current working directory. Confirmed live this is the **actual root cause** of the
     crash above: Claude Desktop launches this server with its working directory set to
     `C:\WINDOWS\System32`, so the write threw `UnauthorizedAccessException` - and since this
     throw happens inside a callback invoked *by native code*, it crosses the native/managed
     boundary in a way the `RunThread()` try/catch (fix 1) cannot catch either. Fixed two ways:
     wrapped the callback body in try/catch (defense in depth), and - since writing plaintext TLS
     session keys to disk is itself not something this should do unconditionally - **disabled
     registering the callback by default** (`SSL_CTX_set_keylog_callback` call commented out).
     Found via the actual Windows Application Event Log .NET Runtime crash entry
     (`UnauthorizedAccessException` at `S7Client.SSL_CTX_keylog_cb`), not guessed.

  Both are local modifications to the pinned commit, not part of upstream; worth proposing back to
  the maintainer (at minimum, 1 as a general robustness fix; 2's relative path is arguably a
  pre-existing bug independent of this project).
- **Build integration note**: the submodule's own `.csproj` files are legacy-format (not
  SDK-style), target .NET Framework 4.7.2, and only define `x64`/`x86` platform configs (no
  `AnyCPU`). Rather than a `ProjectReference` (which would force `Platform=x64` onto
  `TiaMcpServer.csproj`'s own build and change its output path), `TiaMcpServer.csproj` references
  prebuilt `S7CommPlusDriver.dll`/`zlib.net.dll` directly. After updating the submodule, rebuild
  them with:
  ```
  dotnet build third_party\S7CommPlusDriver\src\S7CommPlusDriver\S7CommPlusDriver.csproj -c Debug -p:Platform=x64 -p:TargetFrameworkVersion=v4.8
  ```
  (`TargetFrameworkVersion=v4.8` is an override for this machine, which has the v4.8 targeting
  pack but not v4.7.2 - the library's own source still targets 4.7.2 in its checked-in `.csproj`.)
- **Build gotcha - stale DLL silently kept on rebuild**: after rebuilding the driver DLL, a plain
  `dotnet build src\TiaMcpServer\TiaMcpServer.csproj` can report "Build succeeded" while still
  copying an **old** cached `S7CommPlusDriver.dll` into `TiaMcpServer`'s own output folder -
  confirmed live (MSBuild's incremental `ResolveAssemblyReference`/copy-to-output-directory state
  in `obj/` doesn't always re-check a `HintPath`-referenced file's content/timestamp when nothing
  in the `.csproj` text changed, and an earlier experiment with `-p:Platform=x64` had also left a
  stray `obj\x64`/`bin\x64` tree with its own stale copy). **After every driver rebuild, verify**
  `(Get-Item src\TiaMcpServer\bin\Debug\net48\S7CommPlusDriver.dll).LastWriteTime` actually matches
  the driver build you just ran; if it doesn't, delete `src\TiaMcpServer\obj\Debug` and
  `src\TiaMcpServer\bin\Debug` (and any stray `obj\x64`/`bin\x64`) and rebuild clean.
