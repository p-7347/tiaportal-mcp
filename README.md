# TIA-Portal MCP-Server

A MCP server which connects to Siemens TIA Portal.

> **Fork notice:** this is a fork of [heilingbrunner/tiaportal-mcp](https://github.com/heilingbrunner/tiaportal-mcp),
> pinned to build/run against **TIA Portal V20** by default (see `TiaMcpServer.csproj`'s
> `Siemens.Collaboration.Net.TiaPortal.Packages.Openness` package version) instead of upstream's V21
> default, plus a few local bug fixes and additions (tag table tools, resilient assembly resolver,
> attribute JSON-serialization fix, block/type path fix). See `CHANGES.md` for the full history of
> what was changed and why.

## Features

- Connect to a TIA Portal instance
- Browse and interact with TIA Portal projects
- Perform basic project operations from within VS Code

## Requirements

- __.net Framework 4.8__ installed
- __Siemens TIA Portal V20__ installed and running on your machine (this fork's default - see the
  fork notice above; pass `--tia-major-version` to target a different installed version instead)
- Check if under `Environment Variables/User variable for user <name>` the variable `TiaPortalLocation` is set to `C:\Program Files\Siemens\Automation\Portal V20`
- User must be in Windows User Group `Siemens TIA Openness`

### Diagnose the environment

Run the server with `--doctor` to check all of the above without starting the MCP server:

```text
> TiaMcpServer.exe --doctor --tia-major-version 20
Diagnose:
├─ Connected = False
├─ Project: No project open
├─ Active Version: V20
├─ Installed TIA Portal versions:
│  └─ V21: C:\Program Files\Siemens\Automation\Portal V21
│     ├─ Engineering: OK
│     └─ Portal:      OK
└─ User in 'Siemens TIA Openness' user group: True
```

  (`Doctor`'s installed-versions scan only reports V21+; a V20 install still works fine for
  connecting, but won't show up in that particular list - see `CHANGES.md`.)

The same report is available to MCP clients through the `Doctor` tool, which additionally returns
the findings as structured content. Both are read-only: they never connect to TIA Portal, open a
project, or change user group membership.

## TIA-Portal Versions

- __V20__ is the default version in this fork (upstream defaults to V21).
- Other installed versions are also supported via the `--tia-major-version` argument.
- Export as documents (.s7dcl/.s7res) via `ExportAsDocuments`/`ExportBlocksAsDocuments` requires TIA Portal V20 or newer.
- Import from documents (.s7dcl/.s7res) via `ImportFromDocuments`/`ImportBlocksFromDocuments` also requires TIA Portal V20 or newer.

## Known Limitations

- As of 2025-09-02: Importing Ladder (LAD) blocks from SIMATIC SD documents requires the companion `.s7res` file to contain en-US tags for all items; otherwise import may fail. This is a known limitation/bug in TIA Portal Openness.
 - `ExportBlock` requires a fully qualified `blockPath` like `Group/Subgroup/Name`. If only a name is provided, the tool fails with an error result that may include suggestions for likely full paths.

## Testing

- See `tests/TiaMcpServer.Test/README.md` for environment prerequisites and test asset setup.
- Standard command: `dotnet test` (run from the repo root).
- Test execution policy: offer to run tests, but only execute after explicit user confirmation. Details in `AGENTS.md`.

## Contributing

- See `agents.md` for guidance on working with agentic assistants and the test execution policy (offer to run tests only with explicit user confirmation).

## Error Handling (ExportBlock)

- The Portal layer throws `PortalException` with a short message and `PortalErrorCode` (e.g., NotFound, ExportFailed), and attaches `softwarePath`, `blockPath`, `exportPath` in `Exception.Data` while preserving `InnerException` on export failures.
- The MCP layer rethrows these as `McpException`. Since SDK 2.x, an `McpException` thrown from a tool is returned to the client as a `CallToolResult` with `isError: true` and the message as text content, rather than as a JSON-RPC error, so the model can read the reason and self-correct. For `ExportFailed` the message includes a concise reason from the underlying error; for `NotFound` it may suggest likely full block paths if a bare name was provided.
- Consistency required: TIA Portal never exports inconsistent blocks/types. Single export returns `InvalidParams` with a message to compile first. Bulk export skips inconsistent items and returns them in an `Inconsistent` list alongside `Items`.
- Standardization: Exception context metadata is attached in a single catch per portal method right before rethrow, not at inline throw sites. See `docs/error-model.md`.
- This standardized pattern currently applies to `ExportBlock` and will expand incrementally.

## MCP Protocol

- Built on the [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) .NET SDK **2.2.0**.
- Protocol revisions negotiated during `initialize`: `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25` (the SDK picks the highest the client also supports).
- Every tool advertises a human-readable `title` and behaviour annotations (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`).
- The 13 read-only `Get*` tools also publish an `outputSchema` and return `structuredContent`.
- Long-running export/import tools report progress via `notifications/progress` when the client supplies a `progressToken`.
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

## Copilot Chat

- Example mcp.json, when using VS Code extension [TIA-Portal MCP-Server](https://marketplace.visualstudio.com/items?itemName=JHeilingbrunner.vscode-tiaportal-mcp) and TIA-Portal V18
  ```json
  {
      "servers": {
          "vscode-tiaportal-mcp": {
          "command": "c:\\Users\\<user>\\.vscode\\extensions\\jheilingbrunner.vscode-tiaportal-mcp-<version>\\srv\\net48\\TiaMcpServer.exe",
          "args": [
              "--tia-major-version",
              "18"
          ],
          "env": {}
          }
      }
  }
  ```

## Claude Desktop

- Create/Edit to add/remove server to `C:\Users\<user>\AppData\Roaming\Claude\claude_desktop_config.json`:

  ```json
  {
    "mcpServers": {
      "tiaportal-mcp": {
        "command": "<path-to>\\TiaMcpServer.exe",
        "args": ["--tia-major-version", "20"],
        "env": {
          "TiaPortalLocation": "C:\\Program Files\\Siemens\\Automation\\Portal V20"
        }
      }
    }
  }
  ```

- __If Claude Desktop is installed from the Microsoft Store (MSIX)__, the config file above is not
  actually the one Claude Desktop reads - MSIX app-data virtualization redirects it to:
  `C:\Users\<user>\AppData\Local\Packages\Claude_<hash>\LocalCache\Roaming\Claude\claude_desktop_config.json`.
  Editing the "normal" path silently has no effect. Confirm which one is live by checking the
  server's "Arguments"/"Environment Variables" in Claude Desktop's own connector settings screen
  after editing - if it still shows the old values, you edited the wrong file. Find the actual
  package folder name via `Get-Process Claude | Select-Object Path` or under
  `C:\Program Files\WindowsApps\Claude_*`.
- If the server fails to start only from Claude Desktop (but a manual stdio test of the same exe
  works fine), it's likely launching the child process with a stripped-down environment missing
  basics like `SystemRoot`/`TEMP`/`USERPROFILE`. Add those explicitly under `env` as a workaround;
  see `CHANGES.md` for the full writeup and a working example.
- After editing the config, fully quit Claude Desktop from its tray icon (closing the window alone
  just refocuses the already-running instance and does not reload the config) before relaunching.
