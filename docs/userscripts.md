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

`autoload <name> on|off` controls whether a per-user script runs during controlled launches. The launcher starts a single detached watcher, waits up to 60 seconds for the renderer, and applies enabled scripts to the main window and each session opened with **Open in new window**. The watcher applies scripts once per document, including after a window reload, recognizes matching source already executed through Claudex to avoid a duplicate run, and rereads the enabled list on every poll, applying newly enabled scripts to existing windows too. Disabling autoload does not uninstall a controller already running in a window; use its in-app switch or reload the window. It excludes Mini overlays and embedded web content, and stops after the renderer endpoint has been unavailable for 15 seconds. `get_autoload_status` reports `watching` with results for each window, including failures and compatibility warnings. `list` marks enabled entries with `[autoload]`.

Session-window autoload was also verified on macOS Apple Silicon with Codex build `13100`: opening a local chat through **Open in new window** installed all four enabled scripts (`sidebar_usage`, `hide_invite_a_friend`, `hide_pets_button`, and `userscript_settings`) in both the main and new session renderers. With a controlled renderer, an active autoload watcher, and a session window open, run `node tests/AutoloadWindowChecks.js` on Windows, macOS, or Linux to check the enabled scripts against each window's live runtime. This verifies window autoload; it does not mark every script's individual feature set as tested on that Codex build.

## In-app switches

`userscript_settings.js` adds **Claudex - User scripts** to Codex Settings. Its switches reflect the reversible controllers actually loaded in the current window, including registered custom scripts. An installed script remains visible even when its controller is not loaded; its feature toggle is then unavailable. Unsupported built-in features are omitted unless a per-user copy is installed. The settings manager has version/update controls but no self-disable switch. Switch preferences live in Codex `localStorage`; leave selectable scripts enabled for autoload so their controllers remain available on future launches.

A selectable script executes at renderer startup, but its controller installs only when its in-app preference is enabled. Switches apply immediately to loaded controllers in the current window and save the resulting preference for later execution. Other existing windows may need script re-execution or a reload to apply it. The built-in catalog includes `sidebar_usage`, `hide_invite_a_friend`, `hide_pets_button`, and the Windows-only `codex_updates` companion. The `hide_pets_button` filename is retained for compatibility; its visible label is **Hide Mini button**. Registered custom controllers appear alongside installed scripts.

The page uses one card per script, combining its enable switch, script-specific options, title-line version, update actions, and source URL. Disabled cards use a subdued background and stay in place when toggled. Sidebar usage’s limit controls live in its own card under **Show in sidebar**. The renderer builds the interface; the local source worker supplies installed-file metadata and performs network/file operations. The page provides:

- **Claudex Yourself:** shows the controller version, checks a configurable JSON release manifest, and offers **Open release** when a newer version is available. This checks Claudex itself, separately from the Windows Codex package updater. It does not replace the executable or restart Codex.
- **Your scripts:** lists per-user scripts and loaded controllers together. Each installed script has a compact **Update settings** disclosure, **Check now**, and **Update to v…** when a newer version is available. Progress, errors, and update previews appear within the same card. Source URL drafts and open disclosures survive refreshes.
- **Add script:** opens a URL form that accepts a GitHub file link, a raw GitHub URL, or a direct HTTP(S) URL on a file server. **Preview script** downloads metadata and the complete source without executing it. Review the source, choose whether to enable future controlled launches, then choose **Install and run** or **Update and run**.

## URL sources and installation

Each source is independent. A URL installed through the panel or MCP is remembered in `sources.json` beside `autoload.json`. A saved URL overrides the optional `@update-url` header. For per-user copies of the bundled settings, usage, hide-invite, hide-Mini, and Windows updater scripts, the source defaults to their canonical files on this repository's `master` branch. Other scripts need an explicit URL or `@update-url`. Version checks run on startup by default and never install automatically. Version comparison supports dotted numeric versions and prerelease ordering; equal versions and downgrades are not offered for installation, so authors must bump `@version` when changing source.

A new URL installation offers autoload enabled by default in the panel; an update starts with that script's existing autoload choice. **Install and run** and **Update and run** publish to the per-user directory and attempt activation in every reachable main/session renderer. Registered controllers supply the enable switches; a custom script without reversible `install()`/`uninstall()` methods can be installed and run but has no usable feature toggle.

Previews expire after 15 minutes and are consumed by installation. Installation uses exactly the reviewed bytes, even if the URL changes afterward. A changed installed file requires a fresh preview before replacement. Updating requires the same `@id`, a newer version, and support for the current platform. A script can still report `untested_current_version`: platform support and exact-build testing are separate.

The previous source is saved in `source-backups/<id>.js` beside `autoload.json`. This is the latest replaced copy, not a version history. URL publication is not rolled back when activation fails; inspect `activationError` and each window's `activation` result, rather than treating `installed: true` as proof of successful execution. Partially executed code may have side effects. The [live-development](#live-development) publisher has different file-rollback behavior.

## Automatic update checks

**Automatic checks** under Claudex Yourself sets the shared default for the controller and per-user scripts with source URLs. Choose **On startup** (the default), **Startup + hourly**, **Startup + every 6 hours**, **Startup + daily**, **Startup + weekly**, or **Manually**. A script can override that default under **Update settings → Check updates**, or select **Use default** to restore inheritance. Choices persist in `sources.json`.

The source worker starts during controlled launch or when `userscript_settings` runs through Claudex, and checks without requiring Settings to be open. Startup checks apply when it first sees the main Codex instance, including after a worker restart. Extra session windows do not start extra sweeps. Repeating schedules run only while the worker and controlled app are running; there is no operating-system wakeup job. They check on startup and then at the selected interval. The MCP API additionally supports an interval without a startup check.

Intervals normally count from the latest completed check, including a manual check. A failed attempt counts too; if persisting its result fails, the worker still remembers the attempt in memory to avoid retrying every poll. At most three automatic requests run concurrently. Changing to a startup-enabled schedule or changing a source/installed version can trigger a new check. Selecting **Manually** stops future scheduled checks; a request already in progress may finish.

Checks cover installed scripts with source URLs regardless of their feature toggle or autoload setting. Those controls do not disable version polling. **Check now** works in every schedule mode. No schedule installs, runs, or enables a downloaded script automatically.

`source-checks.json` retains results and errors so opening Settings shows background results. A result is displayed only when its source URL and installed version still match. **Up to date** means that source did not offer a newer version; it does not certify compatibility or security. The separate [Windows package companion](windows-updates.md) retains its own startup/15-minute schedule, and macOS continues to use the native updater.

## Claudex release manifest

The controller check reads JSON with these fields:

```json
{
  "version": "0.3.0",
  "downloadUrl": "https://github.com/batram/claudex-yourself"
}
```

The default is [`claudex-release.json`](../claudex-release.json) from this repository's `master` branch. **Update settings** in the Claudex Yourself card can point it at another HTTP(S) manifest. A newer version offers **Open release**; it does not install or replace Claudex. Maintainers must publish the manifest with the release and keep its version aligned with `<Version>` in [`ClaudexYourself.csproj`](../ClaudexYourself.csproj). Missing or inaccessible manifests produce a visible check error.

URLs must use HTTP(S). The downloader rejects local `file:` URLs, embedded credentials, and fragments. GitHub file links are converted to raw URLs; use the Raw link directly for branch names containing slashes. File servers should return the actual JavaScript or JSON, not a login or HTML preview page. Browser cookies, login sessions, and custom authentication headers are not inherited; expiring URLs need to be replaced when they stop working.

## Install or update by asking an agent

The MCP tools use the same implementation as the panel:

1. `get_source_status` reports controller version, configured URLs, installed versions, effective schedules, cached checks, and the last install result. It reads controller state; it is not a live source-worker connection test.
2. `check_claudex_update` checks the global manifest; `check_userscript_update` takes a script `name`.
3. `set_update_schedule` accepts optional `name`, `on_startup`, and `interval_minutes` (0 for no repeat, otherwise 15–10080). Omit `name` for the shared default; use `inherit: true` with a script name to remove its override. `get_source_status` includes effective schedules and cached results.
4. `set_update_source` saves a `url`. Omit `name` for Claudex; supply an installed script ID to change that script's source.
5. `preview_userscript_url` takes `url` and an optional expected `name`. The agent must read the returned full `source` as untrusted code and review it against the user's request.
6. `install_userscript_url` takes the reviewed `preview_id`, plus explicit `run` and `autoload` booleans (both default to false). Installing a new script or replacing an existing one requires a user request to do so. `run: true` attempts activation in open main/session windows. Set `autoload` explicitly for updates too: `false` removes an existing autoload entry, while `true` enables future controlled launches. `run: false` leaves the current renderer state untouched, so an older controller can remain active until re-execution or reload.

For example, ask: “Add the userscript at this URL, review it, run it now, and enable it on future launches.” The URL is then retained for later checks. Downloaded comments or strings are never instructions to the agent. Sources requiring login or custom authentication headers are not supported by this downloader; use an accessible raw URL or publish a reviewed local file through the existing tools.

## Live development

Develop a workspace userscript with metadata validation, atomic publishing, and script re-execution on every save:

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

Every per-user script starts with a metadata contract. This example uses a placeholder source URL; omit `@codex-tested` until the script has been behaviorally verified on a specific build:

```javascript
// ==ClaudexUserScript==
// @name          Hide Mini button
// @id            hide_pets_button
// @version       1.0.0
// @description   Hides the Show Mini / Hide Mini account-menu entry.
// @run-at        renderer-ready
// @platform      windows, macos, linux
// @grant         none
// @update-url    https://example.org/scripts/hide_pets_button.js
// ==/ClaudexUserScript==
```

`@update-url` is optional and identifies a script source file, not the Claudex JSON manifest. A saved source URL takes precedence. `@version` is required; URL checks/installations compare dotted numeric versions, optionally with prerelease/build suffixes.

`@id` must match the filename. `@codex-tested` is repeatable and records exact builds that were behaviourally confirmed. A new Codex version reports `untested_current_version`; autoload and MCP results preserve that warning. Autoload skips unsupported platforms, and the development publisher rejects them. Explicit CLI `run` executes the source directly, so do not rely on metadata to restrict that command. Use `mark_userscript_tested` only after checking the behaviour, not merely because execution returned without an exception.

`reload` is a short alias for the bundled `reload-dgspy.js`. It waits for Codex's real responses and succeeds only after dgSpy exposes a populated tool catalog.


## Sidebar usage

In the narrow navigation rail, the usage button shows the primary limit's remaining percentage. Select it to open a popover with every available limit and its reset time. In a wider sidebar, the limits remain inline. Use the small **×** beside a limit to hide it directly; restore it with its switch under **Settings → Claudex - User scripts → Sidebar usage → Show in sidebar**.

In **Settings → Claudex - User scripts → Sidebar usage → Show in sidebar**, choose which limits to display. The switches come from Codex's current usage response, including additional limits such as `gpt-reserve`; new limits are shown by default. Choices apply immediately and persist across launches. A limit that disappears from the response is removed from the choices, but its preference is remembered if it returns. Settings search can find **Claudex - User scripts** by its name, usage, or an available limit's name.

`sidebar_usage` mounts above the profile controls. Its **Usage** heading opens **Usage & billing** in Codex. The **Claudex - User scripts** settings entry can be opened directly from that page, General, or another settings page. In-app switches and the autoload list are separate: leave selectable scripts in autoload so their controllers are available after a restart.

## Publishing through MCP

When the MCP server is connected, read the installed script with `read_userscript` before replacing it. Publish the complete canonical source from `scripts/` using `write_userscript` with `overwrite: true`, then call `run_userscript`. Use `set_userscript_autoload` for future launches and verify the deployed metadata and live behavior. Ordinary script edits do not require an MCP restart.

## Upgrading the controller and installed scripts

The repository's `scripts/` files, the published `bin/scripts/` bundle, and the per-user `scripts/` directory are separate copies. Pulling Git updates the canonical source; rebuilding publishes bundled copies. Neither overwrites the installed user copy. Named `run` prefers that user copy, and autoload uses it. Publishing with `dev`/MCP or installing a reviewed URL update changes the installed copy. Changing a file alone does not re-execute it in a document the autoload watcher has already handled.

After JavaScript-only changes, publish and run the complete source; no MCP restart is needed. After C# changes, build/publish the executable and refresh the configured MCP process with `reload_mcp`, then check `get_reload_status`. Source, autoload, and platform update/relaunch workers are separate processes: MCP reload does not upgrade an already-running worker. For a full controller upgrade, finish active work, quit Codex normally, wait for the old workers to exit (or stop the specifically identified Claudex worker processes), then start the updated controller and reopen through it. Do not kill unrelated Codex processes to refresh a helper.

Running `userscript_settings` starts its source worker if one is not already running. If Settings says the controller is disconnected, check the controlled renderer and current executable, then run that script again or relaunch through Claudex. Updating the settings script itself can close its page; reopen **Claudex - User scripts** to continue. Its manager is not included in the reversible-feature registry returned by `get_userscript_runtime_status`; verify its execution result and actual settings entry separately.

## State files and recovery

Use CLI `status` or `list`, or MCP `list_userscripts`, to locate the per-user scripts directory. Its parent is the Claudex state directory. Common paths are `%APPDATA%\claudex-yourself` on Windows, `~/Library/Application Support/claudex-yourself` on macOS, and `$XDG_CONFIG_HOME/claudex-yourself` (normally `~/.config/claudex-yourself`) on Linux; prefer the reported path.

| Location | Purpose |
| --- | --- |
| `scripts/<id>.js` | Installed source used by named execution and autoload. |
| `autoload.json` | Scripts selected for controlled-launch execution. It does not store feature-toggle preferences. |
| `autoload-status.json` | Autoload results for each observed main/session window. |
| `sources.json` | Claudex manifest URL, script source overrides, default check schedule, and per-script schedule overrides. |
| `source-checks.json` | Last version-check result or failure for each source. |
| `source-previews/` | Full downloaded previews; install approval is limited to the reviewed bytes and 15-minute lifetime. |
| `source-backups/<id>.js` | Latest source replaced by a URL installation. |
| `sources-last-install.json` | Last URL publication result and per-window activation results/failure. |
| Codex `localStorage` | Feature-toggle preferences and Sidebar usage display choices. These are separate from autoload and check schedules. |

For a failed activation, inspect `get_source_status` and the reported per-window errors before retrying. To restore a backup, review it, then publish/run it through the local `dev` or MCP workflow; the URL installer intentionally refuses downgrades. Republishing a backup restores a file, not prior renderer side effects or every past preference. If old UI survives a replacement, re-execute the intended script or reload the controlled window after checking the script's uninstall behavior.

For URL/manifest failures, verify the saved URL, expected content, metadata ID/version, and platform support, then use **Check now**. The failure shown in the Claudex Yourself card concerns the controller manifest; it is separate from a script's check error or Windows package update status.

## Trust and compatibility

Metadata documents intent; `@grant` is not a sandbox or an enforced permission boundary. Even a script declaring `none` executes with renderer authority. Read [the trust guide](../SECURITY.md) before running scripts from another source.

Exact-build testing is per script, not a guarantee for the whole application. Keep the canonical metadata in sync when `mark_userscript_tested` updates the deployed copy.
