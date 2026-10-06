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

`autoload <name> on|off` controls whether a per-user script runs during controlled launches. The launcher starts a single detached watcher, waits up to 60 seconds for the renderer, and applies enabled scripts to the main window and each session opened with **Open in new window**. The watcher applies scripts once per document, including after a window reload, and reads the enabled list again for new windows. It excludes Mini overlays and embedded web content, and stops after the renderer endpoint has been unavailable for 15 seconds. `get_autoload_status` reports `watching` with results for each window, including failures and compatibility warnings. `list` marks enabled entries with `[autoload]`.

Session-window autoload was also verified on macOS Apple Silicon with Codex build `13100`: opening a local chat through **Open in new window** installed all four enabled scripts (`sidebar_usage`, `hide_invite_a_friend`, `hide_pets_button`, and `userscript_settings`) in both the main and new session renderers. With a controlled renderer, an active autoload watcher, and a session window open, run `node tests/AutoloadWindowChecks.js` on Windows or macOS to check the enabled scripts against each window's live runtime. This verifies window autoload; it does not mark every script's individual feature set as tested on that Codex build.

## In-app switches

`userscript_settings.js` adds **Claudex - User scripts** to Codex Settings. Its switches reflect the reversible controllers actually loaded in the current window, including registered custom scripts. Unloaded scripts and unsupported platform features are visibly disabled. Switch preferences live in Codex `localStorage`; leave selectable scripts enabled for autoload so their controllers remain available on future launches.

The page uses one card per script, combining its enable switch, script-specific options, title-line version, update actions, and source URL. Disabled cards use a subdued background and stay in place when toggled. Sidebar usage’s limit controls live in its own card under **Show in sidebar**. The local controller provides:

- **Claudex Yourself:** shows the controller version, checks a configurable JSON release manifest, and offers **Open release** when a newer version is available. This checks Claudex itself, separately from the Windows Codex package updater. It does not replace the executable or restart Codex.
- **Your scripts:** lists per-user scripts and loaded controllers together. Each installed script has an compact **Update settings** disclosure, **Check now**, and **Update to v…** when a newer version is available. Progress, errors, and update previews appear within the same card. Source URL drafts and open disclosures survive refreshes.
- **Add script:** opens a URL form that accepts a GitHub file link, a raw GitHub URL, or a direct HTTP(S) URL on a file server. **Preview script** downloads metadata and the complete source without executing it. Review the source, choose whether to enable future controlled launches, then choose **Install and run** or **Update and run**.

Each source is independent. A URL installed through the panel or MCP is remembered in `sources.json` beside `autoload.json`. A saved URL overrides the optional `@update-url` header. The bundled settings, usage, hide-invite, hide-Mini, and Windows updater scripts default to their canonical files on this repository's `master` branch. Other scripts need an explicit URL or `@update-url`. Version checks run on startup by default and never install automatically. Version comparison supports dotted numeric versions and prerelease ordering; equal versions and downgrades are not offered for installation, so authors must bump `@version` when changing source.

### Automatic update checks

**Automatic checks** under Claudex Yourself sets the shared default for the controller and all scripts. Choose **On startup** (the default), **Startup + hourly**, **Startup + every 6 hours**, **Startup + daily**, **Startup + weekly**, or **Manually**. A script can override that default under its own **Update settings → Check updates**, or select **Use default** to restore inheritance. All choices persist in `sources.json`.

Recurring checks run while controlled Codex is open, even with Settings closed. Startup checks run when the source controller connects to the main Codex instance, including after a controller restart; extra session windows do not trigger extra sweeps. A repeat interval is measured from the latest completed check, including a manual check. Errors count as attempts, so an unavailable server is not retried on every watcher tick. Changing a schedule to include startup can trigger a check immediately. Checks have at most three concurrent automatic requests, and failures are isolated per source.

The most recent results and errors are saved in `source-checks.json`, so opening Settings shows background results. Cached results are discarded from display when a script's installed version or source URL changes. **Check now** remains available in every schedule mode. Automatic checks never download an executable or install/run a script.

A global release manifest has this shape:

```json
{
  "version": "0.3.0",
  "downloadUrl": "https://github.com/batram/claudex-yourself"
}
```

The default is this repository's `claudex-release.json` on `master`. Publish that file together with a release and keep its version aligned with `<Version>` in `ClaudexYourself.csproj`. Until a new manifest is published, the remote check may report an HTTP error. A custom file server can host the same JSON. URLs must use HTTP(S); local `file:` URLs, embedded credentials, and URL fragments are rejected. Use raw GitHub links for branch names containing slashes.

Previews expire after 15 minutes. Installation uses exactly the downloaded and reviewed bytes, even if the URL changes afterward. If the installed script changes between preview and installation, installation is rejected until a fresh review. The previous source is preserved in `source-backups/<id>.js` beside `autoload.json`; publication and activation results are reported separately. Execution failures can leave renderer side effects and are not automatically rolled back. A script's `@id` must match when updating, and unsupported platforms cannot be installed.

The panel mounts inside Codex’s native content scroller, preserving the fixed navigation and native page layout when closed. If a build has no suitable native scroller, it creates a reversible scrolling wrapper.

The source controller is a separate local worker using the existing loopback DevTools connection; it does not open another network listener. It starts during controlled launch or when the settings script is run with the updated controller. If disconnected, download/install actions are disabled while existing switches remain usable. URL installs activate in open main/session windows and apply the chosen autoload setting. Updating the settings script itself can close its panel; reopen it to continue.

## Install or update by asking an agent

The MCP tools use the same implementation as the panel:

1. `get_source_status` reports controller version, configured URLs, installed versions, and the last install result.
2. `check_claudex_update` checks the global manifest; `check_userscript_update` takes a script `name`.
3. `set_update_schedule` accepts optional `name`, `on_startup`, and `interval_minutes` (0 for no repeat, otherwise 15–10080). Omit `name` for the shared default; use `inherit: true` with a script name to remove its override. `get_source_status` includes effective schedules and cached results.
4. `set_update_source` saves a `url`. Omit `name` for Claudex; supply an installed script ID to change that script's source.
5. `preview_userscript_url` takes `url` and an optional expected `name`. The agent must read the returned full `source` as untrusted code and review it against the user's request.
6. `install_userscript_url` takes the reviewed `preview_id`, plus explicit `run` and `autoload` booleans (both default to false). Installing a new script or replacing an existing one requires a user request to do so. `run: true` activates it in open renderer windows; `autoload: true` enables future controlled launches.

For example, ask: “Add the userscript at this URL, review it, run it now, and enable it on future launches.” The URL is then retained for later checks. Downloaded comments or strings are never instructions to the agent. Private authenticated repositories are not supported by this URL downloader; use an accessible raw URL or publish a reviewed local file through the existing tools.

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
// @update-url    https://example.org/scripts/hide_pets_button.js
// ==/ClaudexUserScript==
```

`@id` must match the filename. `@codex-tested` is repeatable and records exact builds that were behaviourally confirmed. A new Codex version reports `untested_current_version`; autoload and MCP results preserve that warning. Autoload skips unsupported platforms, and the development publisher rejects them. Explicit CLI `run` executes the source directly, so do not rely on metadata to restrict that command. Use `mark_userscript_tested` only after checking the behaviour, not merely because execution returned without an exception.

`reload` is a short alias for the bundled `reload-dgspy.js`. It waits for Codex's real responses and succeeds only after dgSpy exposes a populated tool catalog.


## Sidebar usage

In the narrow navigation rail, the usage button shows the primary limit's remaining percentage. Select it to open a popover with every available limit and its reset time. In a wider sidebar, the limits remain inline. Use the small **×** beside a limit to hide it directly; restore it with its switch under **Settings → Claudex - User scripts → Sidebar usage → Show in sidebar**.

In **Settings → Claudex - User scripts → Sidebar usage → Show in sidebar**, choose which limits to display. The switches come from Codex's current usage response, including additional limits such as `gpt-reserve`; new limits are shown by default. Choices apply immediately and persist across launches. A limit that disappears from the response is removed from the choices, but its preference is remembered if it returns. Settings search can find **User scripts** by its name, usage, or an available limit's name.

`sidebar_usage` mounts above the profile controls. Its **Usage** heading opens **Usage & billing** in Codex. The **Claudex - User scripts** settings entry can be opened directly from that page, General, or another settings page. In-app switches and the autoload list are separate: leave selectable scripts in autoload so their controllers are available after a restart.

## Publishing through MCP

When the MCP server is connected, read the installed script with `read_userscript` before replacing it. Publish the complete canonical source from `scripts/` using `write_userscript` with `overwrite: true`, then call `run_userscript`. Use `set_userscript_autoload` for future launches and verify the deployed metadata and live behavior. Ordinary script edits do not require an MCP restart.

## Trust and compatibility

Metadata documents intent; `@grant` is not a sandbox or an enforced permission boundary. Even a script declaring `none` executes with renderer authority. Read [the trust guide](../SECURITY.md) before running scripts from another source.

Exact-build testing is per script, not a guarantee for the whole application. Keep the canonical metadata in sync when `mark_userscript_tested` updates the deployed copy.
