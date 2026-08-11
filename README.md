# Claudex Yourself

A small cross-platform Codex Desktop controller and JavaScript userscript runner. It launches Codex with a local DevTools endpoint, then runs explicit local scripts inside the Codex renderer.

This uses an internal Codex Desktop interface rather than an officially supported extension API. Commands fail visibly if a Codex update changes that interface.

## Build

Windows:

```powershell
.\build.ps1
```

macOS:

```bash
./build.sh
```

Explicit runtime builds are also supported:

```powershell
.\build.ps1 win-x64
.\build.ps1 osx-arm64
.\build.ps1 osx-x64
```

The build requires the matching .NET 10 runtime on the destination machine.

## Launch Codex

Quit Codex completely before the first controlled launch:

```powershell
.\bin\claudex-yourself.exe launch
```

```bash
./bin/claudex-yourself launch
```

Windows activation uses the installed Codex package. macOS launch looks for `Codex.app` in `/Applications` and `~/Applications` and starts its executable with the same isolated control configuration.

## Userscripts

Run a bundled script by name or any explicit JavaScript file:

```powershell
claudex-yourself list
claudex-yourself run reload-dgspy
claudex-yourself run C:\tools\my-codex-fix.js
claudex-yourself run-all
claudex-yourself autoload hide_pets_button on
```

The per-user script directory is printed by `status` and `list`. `run-all` executes only that user directory, alphabetically; bundled scripts never run automatically.

`autoload <name> on|off` controls whether a per-user script runs after the next controlled launch. The launcher starts a detached worker, waits up to 60 seconds for the renderer, and records each script result without delaying the launcher itself. `list` marks enabled entries with `[autoload]`.

Scripts are async function bodies with a small `claudex` API:

```javascript
claudex.log("starting");
const status = await claudex.request("mcpServerStatus/list", {
  detail: "toolsAndAuthOnly"
});
await claudex.sleep(250);
return { serverCount: status.data.length };
```

Available values:

- `claudex.request(method, params?, timeoutMs?)`
- `claudex.sleep(milliseconds)`
- `claudex.log(...values)`
- `claudex.scriptName`

Every per-user script starts with a metadata contract:

```javascript
// ==ClaudexUserScript==
// @name          Hide pets button
// @id            hide_pets_button
// @version       1.0.0
// @description   Hides the Show pet / Hide pet account-menu entry.
// @run-at        renderer-ready
// @platform      windows, macos
// @codex-tested  26.803.10989.0
// @grant         codex-request
// ==/ClaudexUserScript==
```

`@id` must match the filename. `@codex-tested` is repeatable and records exact builds that were behaviourally confirmed. A new Codex version reports `untested_current_version`; the script still runs, but autoload and MCP results preserve that warning. Unsupported platforms are skipped. Use `mark_userscript_tested` only after checking the behaviour, not merely because execution returned without an exception.

`reload` is a short alias for the bundled `reload-dgspy.js`. It waits for Codex's real responses and succeeds only after dgSpy exposes a populated tool catalog.

## MCP ouroboros

Register this executable as a Codex MCP server:

```powershell
claudex-yourself configure codex
claudex-yourself reload
```

It exposes tools to inspect, read, write, run, mark userscripts for autoload, compare compatibility, and record an explicitly verified Codex build. Script files are read fresh on every call, so changing JavaScript requires no MCP restart. `get_autoload_status` reports the previous launch worker result.

`reload_mcp` starts a detached worker and returns before Codex replaces the MCP transport. After the server reconnects, `get_reload_status` reports the worker's verified result. This lets Codex control the same DevTools userscript host that is controlling Codex.

Scripts execute with the renderer's authority. Detailed trust considerations are [REDACTED]. Scripts run only through explicit `run` or `run-all` commands; there is no automatic startup loading or dependency download.

## Platform status

- Windows x64: implemented and live-tested.
- macOS Apple Silicon and Intel: builds are supported; first live Codex launch still needs verification on a Mac.
- Linux: the runner core is portable, but Codex Desktop controlled launch is not implemented.
