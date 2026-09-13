# Claudex Yourself agent guide

## Project purpose

Claudex Yourself is a cross-platform .NET 10 controller and JavaScript userscript runner for Codex Desktop. It launches Codex with a local DevTools endpoint, executes explicit local scripts in the renderer, and exposes userscript and renderer-development operations through MCP.

The project relies on internal Codex Desktop behavior rather than a supported extension API. Treat Codex upgrades as compatibility events: failures should remain visible, and exact-build compatibility must be confirmed behaviorally.

## Repository map

- `Program.cs`, `RendererDevTools.cs`, and `ClaudexMcpServer.cs`: CLI, renderer control, and MCP entry points.
- `UserscriptMetadata.cs`, `UserscriptDevelopment.cs`, and `AutoloadScripts.cs`: userscript metadata, publishing, execution, and autoload behavior.
- `scripts/*.js`: bundled userscript sources. These are the canonical repository copies.
- `CodexUpdates.cs`, `update/WindowsPackage.ps1`: Windows update discovery, validation, and deployment.
- `README.md`, `docs/`, `SECURITY.md`: setup, userscript, updater, and trust documentation.
- `tests/UpdateChecks.cs`, `tests/WindowsPackageChecks.ps1`, `tests/UpdatePanelChecks.js`: updater regression checks.
- `tests/McpProtocolSmoke.ps1`: MCP protocol/tool smoke test, optionally against a live renderer.
- `tests/SearchRendererSource.ps1`: focused live source-search helper.
- `build.ps1` and `build.sh`: platform build entry points.
- `bin/`: generated/published output; do not hand-edit generated copies.

## Working rules

- Update the canonical source under `scripts/` before publishing a userscript. Do not let the per-user deployed copy become the only copy of a change.
- Preserve unrelated working-tree changes. Before committing, inspect `git status --short` and the focused diff, and stage only the intended paths.
- Userscripts run with renderer authority. Keep privileges and `@grant` declarations minimal.
- A userscript is an async-function body, not a standalone JavaScript module. Top-level `return` is valid. For a lightweight syntax check, use:

  ```powershell
  node -e "new Function(require('fs').readFileSync('scripts/<name>.js','utf8'))"
  ```

- Keep selectable userscripts reversible: expose controllers with working `install()` and `uninstall()` methods and register them in the shared userscript registry.
- Bump `@version` for user-visible userscript changes. `@id` must match the filename without `.js`.
- Do not add the installed Codex version to `@codex-tested` merely because parsing, publication, or execution succeeded. Use `mark_userscript_tested` only after the behavior has been observed and confirmed in that exact Codex build.

## Build and tests

Windows build:

```powershell
.\build.ps1
```

Explicit runtime builds:

```powershell
.\build.ps1 win-x64
.\build.ps1 osx-arm64
.\build.ps1 osx-x64
```

macOS build:

```bash
./build.sh
```

MCP smoke test after building:

```powershell
.\tests\McpProtocolSmoke.ps1
```

With an already controlled, reachable Codex renderer:

```powershell
.\tests\McpProtocolSmoke.ps1 -LiveRenderer
```

Use focused validation for small userscript changes; use the live smoke test when MCP or renderer-control behavior changes.

## Userscript deployment: prefer MCP

When the `claudex_yourself` MCP server is available in the current Codex task, deploy through its tools instead of invoking the CLI:

1. Read the installed script with `read_userscript` before overwriting it.
2. Read the complete canonical source from `scripts/<id>.js`.
3. Publish that complete source with `write_userscript` and explicit overwrite.
4. Execute it immediately with `run_userscript`.
5. Enable future controlled launches with `set_userscript_autoload` when the script is intended to autoload.
6. Verify installation with `get_userscript_runtime_status` and verify the deployed metadata/version with `get_userscript_metadata` or `read_userscript`.
7. If relevant, inspect the live renderer or capture it to validate the actual visual/interactive behavior.

Depending on the Codex tool namespace, these may appear with names such as `mcp__claudex_yourself__write_userscript`. Other useful MCP operations include `list_userscripts`, `list_userscript_compatibility`, `get_autoload_status`, `inspect_renderer`, `evaluate_renderer`, `capture_renderer`, and `search_renderer_sources`.

Script files are read fresh on each MCP call; ordinary userscript edits and deployments do not require an MCP restart. Use `reload_mcp` only for changes to the MCP server itself, then check `get_reload_status` after reconnection.

## CLI deployment fallback

The CLI is expected to be available on `PATH` in newly started sessions. Resolve it before use:

```powershell
Get-Command claudex-yourself
```

An already-running shell may retain its old `PATH`. If resolution fails in such a session, use the repository executable directly:

```powershell
.\bin\claudex-yourself.exe <command>
```

Do not conclude that the executable is uninstalled solely because the bare command is absent from an older shell's `PATH`.

For one-time publication plus live reload and autoload, start the development command, wait for the initial successful `reloaded` result, and then stop the watcher with Ctrl+C:

```powershell
claudex-yourself dev .\scripts\<id>.js --autoload
```

Useful diagnostics:

```powershell
claudex-yourself status
claudex-yourself list
```

The per-user script directory is reported by those commands. On Windows it is normally under `%APPDATA%\claudex-yourself\scripts`, but use the CLI-reported path rather than relying on that location.

## Definition of done for userscript changes

- Canonical repository source updated and syntax-checked.
- Intended focused tests pass.
- Deployed copy has the expected `@version` and source behavior.
- Live controller reports installed when a renderer is available.
- Autoload is enabled when appropriate.
- Visual or interactive behavior is actually checked before declaring a new Codex build tested.
- Any failed deployment attempts or compatibility warnings are reported explicitly.
