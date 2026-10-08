# Third-Party Licenses

This project bundles third-party components under their own licenses, separate from this
repository's own license. They are kept as independent assemblies (not merged/ILRepacked into
`TiaMcpServer.exe`), so each can be rebuilt, inspected, or replaced on its own.

## S7CommPlusDriver

- **Path**: `third_party/S7CommPlusDriver` (git submodule)
- **Upstream**: https://github.com/thomas-v2/S7CommPlusDriver
- **Pinned commit**: `dbd61e4`
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
