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

### Windows updates in controlled mode

Codex's native updater is disabled for its `dev` build flavor, which the controlled launcher needs for the DevTools endpoint. Claudex supplies a separate **Updates** panel during controlled Windows launches. It checks OpenAI's stable release manifest on startup and every 15 minutes. Select **Update and restart now** to download and stage the signed MSIX, quit Codex normally, install, verify the registered version, and reopen in controlled mode with userscripts restored. Active work can be interrupted by the restart; Codex's normal quit confirmation remains in effect.

The updater runs outside Codex's package/job lifetime. It waits until no processes have the Codex package identity for two continuous seconds before registration. Cancelling quit or leaving a process running aborts installation after 60 seconds. It retries only package-in-use errors, at most twice, and records failures in `%APPDATA%\claudex-yourself\updates\status.json`. It does not force-kill Codex or change Windows package permissions. OpenAI's downloaded package identity, publisher, architecture, and exact manifest version must match before Windows validates its signature during staging. A mismatch between the release feed and stable download fails before Codex closes.

The panel acknowledges clicks immediately, keeps the install action disabled until the worker responds, and shows download progress. Release checks run in the background so they do not block progress updates. The launcher keeps its controlled startup environment active until the main Codex renderer and preload bridge are ready, then restores normal Windows package debugging settings. Update completion is recorded only after that readiness check; activating an ordinary Codex window is insufficient. The first installation attempt shares the existing shutdown quiet period instead of adding a second fixed delay.

If Windows has already staged the exact target version (for example, after a failed native update), the updater validates that protected package's manifest and registers it after shutdown. It uses the normal user's registered package location; it does not require an administrator query of other users' packages. This also handles the case where OpenAI's stable download temporarily trails the Store manifest. Without an exact staged package or a matching download, it reports the mismatch and leaves Codex running.

```powershell
claudex-yourself update check     # Inspect installed and available package versions
claudex-yourself update prepare   # Download, validate, and stage; leave Codex running
claudex-yourself update install   # Download if needed, quit, install, and restart controlled Codex
claudex-yourself update status    # Read persisted progress or failure
```

The MCP equivalents are `check_codex_update`, `install_codex_update`, and `get_codex_update_status`. Installation requires a reachable controlled renderer for graceful quit. Ordinary Codex launches keep their normal behavior. This companion updater currently supports the stable `OpenAI.Codex` Windows package on x64 and Arm64; macOS updating remains unchanged. The update panel is a reversible registered userscript (`codex_updates`), loaded by the launcher's companion worker rather than the per-user autoload list. No new Codex build is marked tested just because installation succeeds.

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

### In-app switches

`userscript_settings.js` adds a **User scripts** page to Codex Settings using only renderer JavaScript. It stores preferences in Codex `localStorage`; it does not read the filesystem or modify `autoload.json`.

For a script to appear and remain switchable, copy it and `userscript_settings.js` into the per-user script directory and leave all of them enabled for autoload. A selectable script still executes at renderer startup, but its controller installs only when its in-app preference is enabled. Switches apply immediately because selectable scripts provide reversible `install()` and `uninstall()` operations.

The settings userscript currently catalogs:

- `sidebar_usage`
- `hide_invite_a_friend`
- `hide_pets_button`

### Live development

Develop a workspace userscript with automatic validation, atomic publishing, and renderer reload on every save:

```powershell
claudex-yourself dev .\scripts\userscript_settings.js --autoload
```

The initial run and every subsequent save parse the metadata, require `@id` to match the filename, publish the source into the per-user script directory, and execute it in the controlled renderer. `--autoload` also enables the script for future controlled launches. If execution fails, the previous installed source is restored and the watcher remains active for the next edit. Press `Ctrl+C` to stop watching.

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

Renderer development tools are also available through MCP:

- `inspect_renderer` returns bounded selector/text matches with visibility, bounds, attributes, HTML, and ancestors.
- `interact_renderer` performs trusted CDP clicks and key presses.
- `capture_renderer` saves a viewport or selected-element PNG under the Claudex state directory.
- `evaluate_renderer` runs diagnostic JavaScript with serialized output capped at 20,000 characters.
- `get_userscript_runtime_status` reports registered controllers and whether they are installed and reversible.
- `search_renderer_sources` searches loaded JavaScript bundles and returns bounded source snippets.

`reload_mcp` starts a detached worker and returns before Codex replaces the MCP transport. After the server reconnects, `get_reload_status` reports the worker's verified result. This lets Codex control the same DevTools userscript host that is controlling Codex.

Scripts execute with the renderer's authority. Detailed trust considerations are [REDACTED]. Scripts run only through explicit `run` or `run-all` commands; there is no automatic startup loading or dependency download.

## Platform status

- Windows x64: implemented and live-tested.
- macOS Apple Silicon and Intel: builds are supported; first live Codex launch still needs verification on a Mac.
- Linux: the runner core is portable, but Codex Desktop controlled launch is not implemented.
