# Agents Guide

This repository can be used with agentic coding assistants. Follow these guidelines to collaborate safely and efficiently.

## Test Execution Policy

- Offer to run tests, but only run them after explicit user confirmation.
- Tests may require user‑specific environment conditions (e.g., installed TIA Portal, licenses, PLC project assets), so do not assume they will pass in your environment.
- When offering to run tests, clearly state prerequisites and potential side effects.
- If the user declines or does not respond, provide concise instructions for the user to run tests locally instead of running them yourself.

### Standard Commands

```powershell
dotnet test
```

If tests need to write to temporary locations or access external resources, note these requirements up front.

## How To Ask For Confirmation

Use clear, actionable language. For example:

- "I can run `dotnet test` to validate the changes. Some tests require TIA Portal and project assets on this machine. Do you want me to run them now?"
- If approved: proceed and summarize results. If not approved: provide steps the user can run.

## Environment Considerations

- Respect the user's environment constraints (e.g., offline, restricted permissions, licensed software).
- If a command fails due to environment limitations, do not retry destructively; report the exact failure and suggest alternatives.
- Document any meaningful limitations or deviations in the commit message or relevant README, per the Contributor Guidelines.


## Formatting & Encoding

- Preserve existing indentation style (tabs vs. spaces).
- Do not modify file encodings; keep UTF-8 BOM where present.
- Ensure Windows CRLF line endings are retained when editing files.

## Contributing to this fork

These rules apply to everyone (people and AI agents) working on `p-7347/tiaportal-mcp`. Open work is listed in
[docs/BACKLOG.md](docs/BACKLOG.md).

### Branches and pull requests

- The base branch is `rebase/upstream-base`. Never push to `main` or to the upstream repository
  (heilingbrunner/tiaportal-mcp).
- Pull from this fork only, naming it: `git pull origin rebase/upstream-base`. Never pull from the upstream
  repository; a plain `git pull` on a checkout that tracks upstream brings in its history and conflicts.
- One backlog item per branch and pull request. Name the branch after the item, e.g. `c6-hmi-dynamization`,
  and open a draft pull request against `rebase/upstream-base` early, with the item id first in the title
  (`C6: ...`). The draft claims the item, so two people do not build the same thing.
- Keep the pull request to that item. Unrelated fixes go in their own pull request.
- Say in the description what was tested and on what (TIA version, project, live or compile only).

### Compatibility

- Do not change the name, arguments, defaults or response shape of an existing tool. Add an optional
  argument at the end if needed. Anything else needs the maintainer's OK first.
- One exe serves one Openness version: V20 by default, V21 with `dotnet build -p:TiaVersion=21`. Code that
  only exists in one version goes behind `#if TIA_V21`.

### Sources and licensing

- Write the code yourself against the Openness API. Look up exact names in the Openness XML documentation
  next to the DLLs (`C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\...\Siemens.Engineering.xml`);
  use the Openness manual for explanations and examples.
- Other TIA Portal MCP projects and Siemens application examples (e.g. TIA Scripting Python) are for ideas
  only. Never copy their code. Do not change `LICENSE.txt`.

### Adding a tool

- Put it in `src/TiaMcpServer/ModelContextProtocol/McpServer.<Area>.cs` with the Openness work in
  `src/TiaMcpServer/Siemens/Portal.<Area>.cs`, and a response class in `Responses.<Area>.cs`.
- The tool description starts with one line on when to use it ("Use to ..."). Describe every argument with
  an example value; use the existing wording for `softwarePath`, block and group paths.
- A tool that changes the project gets `[WriteTool]` (hidden unless the server runs with `--allow-write`),
  runs through `Guarded(...)`, and backs up what it changes before changing it (see `BackupBlock`).
- Add a prompt of the same name to `McpPrompts.cs` that lists exactly the tool's arguments; the tests in
  `Test7ToolRegistration.cs` check this.
- File arguments are absolute paths (`Operation.RequireAbsolutePath`); file names made from TIA object names
  go through `FileNames.Safe`.
- A call that can take long on a big project (walking all blocks, cross references) gets a time budget of
  about 40 seconds and reports `stoppedReason` when it stops early; clients give up after about 60 seconds.
- Update the tool count and table in `README.md` and add an entry to `CHANGELOG.md` under `[Unreleased]`.

### Testing on a real TIA project

- Put the device offline before compile or import (`GoOffline`).
- Name every test object `MCP_TEST_...`, and do not change other objects without the project owner's OK.
- Do not save the project unless the owner asks. Never download to a PLC.
- If a change cannot be tested live, say so in the pull request.
