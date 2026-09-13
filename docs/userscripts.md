# Userscripts

Run a bundled script by name or any explicit JavaScript file:

```powershell
claudex-yourself list
claudex-yourself run sidebar_usage
claudex-yourself run C:\tools\my-codex-fix.js
claudex-yourself run-all
claudex-yourself autoload hide_pets_button on
```

The per-user script directory is printed by `status` and `list`. `run-all` executes only that user directory, alphabetically; ordinary bundled scripts are not included in that command. The Windows Updates companion is loaded separately by the launcher.

`autoload <name> on|off` controls whether a per-user script runs after the next controlled launch. The launcher starts a detached worker, waits up to 60 seconds for the renderer, and records each script result without delaying the launcher itself. `list` marks enabled entries with `[autoload]`.

## In-app switches

`userscript_settings.js` adds a **User scripts** page to Codex Settings using only renderer JavaScript. It stores preferences in Codex `localStorage`; it does not read the filesystem or modify `autoload.json`.

The settings page has a fixed catalog. To make a cataloged script's switch control a live controller, publish it and `userscript_settings.js` into the per-user script directory and leave both enabled for autoload. A selectable script still executes at renderer startup, but its controller installs only when its in-app preference is enabled. Switches apply immediately when the script's reversible `install()` and `uninstall()` controller is loaded; otherwise they save a preference for its next execution.

The settings userscript currently catalogs:

- `sidebar_usage`
- `hide_invite_a_friend`
- `hide_pets_button`

## Live development

Develop a workspace userscript with automatic validation, atomic publishing, and renderer reload on every save:

```powershell
claudex-yourself dev .\scripts\userscript_settings.js --autoload
```

The initial run and every subsequent save parse the metadata, require `@id` to match the filename, publish the source into the per-user script directory, and execute it in the controlled renderer. `--autoload` also enables the script for future controlled launches. If execution fails, the previous installed source is restored (or the newly published file is removed). A failure during the initial publication exits the command; failures on subsequent saves leave the watcher running. File rollback does not undo renderer side effects from a partially executed script. Press `Ctrl+C` to stop watching.

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
// @grant         none
// ==/ClaudexUserScript==
```

`@id` must match the filename. `@codex-tested` is repeatable and records exact builds that were behaviourally confirmed. A new Codex version reports `untested_current_version`; autoload and MCP results preserve that warning. Autoload skips unsupported platforms, and the development publisher rejects them. Explicit CLI `run` executes the source directly, so do not rely on metadata to restrict that command. Use `mark_userscript_tested` only after checking the behaviour, not merely because execution returned without an exception.

`reload` is a short alias for the bundled `reload-dgspy.js`. It waits for Codex's real responses and succeeds only after dgSpy exposes a populated tool catalog.


## Sidebar usage

`sidebar_usage` shows remaining limits and reset times above the profile menu. Its **Usage** heading opens **Usage & billing** in Codex. The **User scripts** settings entry can be opened directly from that page, General, or another settings page. In-app switches and the autoload list are separate: leave selectable scripts in autoload so their controllers are available after a restart.

## Publishing through MCP

When the MCP server is connected, read the installed script with `read_userscript` before replacing it. Publish the complete canonical source from `scripts/` using `write_userscript` with `overwrite: true`, then call `run_userscript`. Use `set_userscript_autoload` for future launches and verify the deployed metadata and live behavior. Ordinary script edits do not require an MCP restart.

## Trust and compatibility

Metadata documents intent; `@grant` is not a sandbox or an enforced permission boundary. Even a script declaring `none` executes with renderer authority. Read [the trust guide](../SECURITY.md) before running scripts from another source.

Exact-build testing is per script, not a guarantee for the whole application. Keep the canonical metadata in sync when `mark_userscript_tested` updates the deployed copy.
