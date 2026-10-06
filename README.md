# Claudex Yourself

Customize Codex Desktop with local JavaScript userscripts, and inspect or control its renderer through a CLI or MCP server.

Claudex launches Codex with a loopback DevTools endpoint. It includes a sidebar usage display, reversible UI tweaks, compact per-script settings, URL-based script installation, scheduled version checks, and a Windows update companion for controlled launches.

This is an independent project using internal Codex Desktop interfaces, not an officially supported extension API. Codex updates can break compatibility. Scripts run with renderer authority; read the [trust guide](SECURITY.md) before using third-party scripts.

## Platform support

| Platform                      | Status                                                                                |
| ----------------------------- | ------------------------------------------------------------------------------------- |
| Windows x64                   | Implemented and live-tested                                                           |
| macOS Apple Silicon           | Build, controlled launch, renderer access, and MCP hot reload verified on app build `12947` |
| macOS Intel                   | Build target and launcher implemented; native testing still needed                     |
| Linux x64                     | Build, controlled launch, renderer access, usage display and settings verified on app build `26.930.61225` |
| Linux Arm64                   | Build target implemented; native testing still needed                                 |

The newer compact settings, URL version checks, startup scheduling, and schedule persistence were checked on macOS Apple Silicon with Codex build `13232`. That validation does not extend these features to every platform/build in the table or add `@codex-tested` entries automatically; native Windows/Linux validation of the new settings flow remains pending.

The Windows updater handles stable `OpenAI.Codex` packages on x64 and Arm64. That package support does not establish end-to-end Windows Arm64 launcher testing.

## Quick start

Run these commands from the repository root.

### 1. Build

Building requires the .NET 10 SDK. Published output is framework-dependent: the destination needs the .NET 10 runtime for the selected architecture. An SDK installation includes a runtime.

Windows:

```powershell
.\build.ps1
```

macOS and Linux:

```bash
sh ./build.sh
```

The default output is `bin/`. Windows defaults to `win-x64`; the shell script selects the host OS and architecture. Explicit runtime builds write to `bin/<runtime>/`:

```powershell
.\build.ps1 win-x64
.\build.ps1 osx-arm64
.\build.ps1 osx-x64
```

```bash
sh ./build.sh osx-arm64
sh ./build.sh osx-x64
sh ./build.sh linux-x64
sh ./build.sh linux-arm64
```

Keep the published directory together, including `scripts/`, `update/`, `claudex.ico`, DLL, and runtime configuration files. The build does not add the executable to PATH. If a running Claudex MCP server or background worker locks published files, stop that process before rebuilding its directory, or publish to a separate directory for validation.

For a Mac installation that does not need a separately installed runtime, publish a self-contained build (use `osx-x64` on Intel):

```bash
dotnet publish ClaudexYourself.csproj -c Release -r osx-arm64 --self-contained true -o bin
```

### 2. Launch Codex

Install Codex Desktop and quit it completely before the first controlled launch. If Codex is already running, the launcher first verifies its controlled renderer, then activates it and ensures the autoload and source-update watchers are running. An existing watcher keeps its per-document execution state; activation does not re-execute scripts that it already applied. If the renderer is unavailable, launch fails with instructions to quit and reopen Codex; it cannot retrofit the DevTools endpoint into an existing process.

Windows:

```powershell
.\bin\claudex-yourself.exe launch
.\bin\claudex-yourself.exe status
```

macOS and Linux:

```bash
./bin/claudex-yourself launch
./bin/claudex-yourself status
```

Windows uses the installed Codex package. macOS looks for `Codex.app` or `ChatGPT.app` in `/Applications` and `~/Applications`, verifies the `com.openai.codex` bundle identity, and reads the executable name from `Info.plist`. The classic ChatGPT app is not a matching installation. The launcher requests a DevTools endpoint at `127.0.0.1:9229` and preserves the app's normal build flavor and profile. On macOS this also preserves the native updater. Launch succeeds only after the main renderer and its preload bridge are ready.

On macOS, Sparkle's native update relaunch does not preserve Claudex's DevTools arguments. Controlled launches start a companion watcher that waits for the app to exit and reopen with a newer bundle build. If that updated app lacks a ready DevTools renderer, the watcher requests a normal quit and reopens the updated bundle through Claudex, restoring userscript autoload. This adds one extra restart after the native update; a cancelled quit or failed restart is reported by `claudex-yourself status` and in `mac-relaunch-status.json` under the Claudex state directory. Ordinary quits leave Codex closed, and same-build or older uncontrolled launches are not restarted. The watcher waits at most two minutes for the updater to reopen the app and never force-quits it. Exact-build update/relaunch compatibility still needs behavioral verification after Codex upgrades.

When setting up Claudex from inside Codex on macOS, finish active work before quitting the app. Then run `./bin/claudex-yourself launch` from Terminal and reopen the chat. The debugging endpoint cannot be added to the already-running process. An organization-settings startup dialog must be resolved through the app's normal sign-in and network setup before renderer verification can succeed.

On Linux, Claudex recognizes the Codex Electron installation at `/usr/lib/chatgpt/ChatGPT` and common `/usr/lib/codex` and `/opt` locations. For another installation, set `CLAUDEX_CODEX_EXECUTABLE` to the Electron binary, not the bundled `resources/codex` CLI. It checks `resources/app.asar` for Codex's package identity and version. Quit the current app before the first controlled launch, then run `./bin/claudex-yourself launch` and `./bin/claudex-yourself status`. Linux uses the app's normal profile and requires a graphical desktop session.

For Linux without a system .NET runtime, publish a self-contained build:

```bash
dotnet publish ClaudexYourself.csproj -c Release -r linux-x64 --self-contained true -o bin
```

For a macOS Dock launcher, see [Claudex Dock icon and launcher](assets/macos/README.md).

On Windows, `.\bin\claudex-yourself.exe install-shortcut` creates a **Codex (controlled)** desktop shortcut with the Claudex icon and the installed Codex package's AppUserModelID. Run the command from the published executable you intend to keep: the shortcut stores its absolute path. Unpin the old launcher and pin this shortcut, then verify that running Codex windows group with it. Changing a shortcut's icon alone does not fix a mismatched app identity, and Explorer can retain old pin information until you repin. The shortcut continues to start Codex through Claudex, enabling renderer control and autoload scripts. Recreate and repin it after moving the published directory.

The remaining examples use `claudex-yourself` for readability. Add the published directory to PATH or substitute the executable path above.

### 3. Try a userscript

```powershell
claudex-yourself list
claudex-yourself run sidebar_usage
```

The sidebar script shows remaining usage and reset times. Click its **Usage** title to open **Usage & billing**.

To publish a script into your per-user directory, run it immediately, and enable future controlled launches:

```powershell
claudex-yourself dev .\scripts\sidebar_usage.js --autoload
```

Wait for the initial `reloaded` result, then press Ctrl+C to stop watching. Leaving the command running reloads the script whenever its source changes. On macOS and Linux, use `./scripts/sidebar_usage.js`.

Repeat with `userscript_settings.js` to add **Claudex - User scripts** to Settings. Publish `hide_invite_a_friend.js` and `hide_pets_button.js` the same way if you want those switches available. The page combines the local controller's per-user file inventory with registered runtime controllers. Each installed script has its version and update controls in the same card as its toggle and options; unloaded or unsupported controllers have disabled toggles. The settings manager itself has no enable toggle.

Pulling the repository or rebuilding `bin/` does not replace per-user script copies. Named execution prefers the per-user copy, and autoload reads that directory. Publish the canonical source with `dev`/MCP or use the panel's reviewed URL update flow to update an installed script. **Check now** only checks; installation is a separate action.

See the [userscript guide](docs/userscripts.md) for metadata, live development, autoload, switches, and MCP publication.

### Windows notification clicks

The Windows launcher preserves Codex's packaged production app identity and default profile and adds only the debugging arguments needed for renderer access. This lets Codex handle its own notification delivery and clicks. A controlled restart is needed when switching from a previously launched process with a different identity or profile; an already running process keeps its original launch arguments.

## Settings and updates

Open **Settings → Claudex - User scripts**. **Add script** accepts a GitHub file link or a direct HTTP(S) script URL, shows the complete source for review, and offers **Install and run** with an autoload choice. **Update settings** within each script card holds its source URL and check schedule. Scripts need Claudex metadata and a newer `@version` for URL updates.

**Automatic checks** sets a shared default for Claudex and installed scripts: **On startup** (default), startup plus hourly/six-hourly/daily/weekly repeats, or **Manually**. Each script can inherit or override that default. The source worker checks while controlled Codex is open, including with Settings closed; it never installs automatically. A feature's enable toggle, its autoload setting, and its check schedule are independent.

| What is updated | Check and action | Effect |
| --- | --- | --- |
| Claudex controller | **Claudex Yourself → Check now** / `check_claudex_update` | Reads a JSON manifest and offers **Open release**. Updating the executable is manual. |
| Userscript source | Script card → **Check now → Update to v…** / `check_userscript_update` | Review the downloaded source, then explicitly install/run it and choose autoload. |
| Codex Desktop on Windows | Separate **Updates** panel / `check_codex_update` | Downloads and validates a signed package; installation quits and restarts Codex. |

Claudex/script schedules do not change the Windows package companion's startup/15-minute cadence or the macOS native updater. See [userscripts and URL sources](docs/userscripts.md#url-sources-and-installation), [automatic check schedules](docs/userscripts.md#automatic-update-checks), and [Windows package updates](docs/windows-updates.md).

## MCP setup

The Codex CLI must be available on PATH for configuration:

```powershell
claudex-yourself configure codex
claudex-yourself run reload-mcp
```

The first command registers the current executable as the `claudex-yourself` stdio MCP server. The second reloads Codex's MCP configuration through the controlled renderer and verifies that this server reconnects. Keep the executable at its registered location, or configure it again after moving it.

For a framework-dependent Homebrew build on macOS, the GUI-launched MCP process may not inherit your terminal's `DOTNET_ROOT`. If it exits during initialization, register the runtime explicitly instead of using `configure codex`. For an Apple Silicon Homebrew install, run this from the repository root (adjust the runtime path for your installation):

```bash
codex mcp add claudex-yourself --env DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec -- "$PWD/bin/claudex-yourself" mcp
./bin/claudex-yourself run reload-mcp
```

A self-contained build does not need this runtime setting. The Dock launcher's environment discovery and the MCP server's environment are separate.

The separate `reload` shortcut runs `reload-dgspy`, a helper for an existing dgSpy MCP setup. It is not the reload command for this project's own server.

| Purpose                       | Tools                                                                                                       |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Connection                    | `claudex_status`                                                                                            |
| Script files and execution    | `list_userscripts`, `read_userscript`, `write_userscript`, `run_userscript`                                 |
| Sources and version checks    | `get_source_status`, `check_claudex_update`, `check_userscript_update`, `set_update_source`, `set_update_schedule` |
| Reviewed URL installation     | `preview_userscript_url`, `install_userscript_url` |
| Compatibility                 | `get_userscript_metadata`, `list_userscript_compatibility`, `mark_userscript_tested`                        |
| Startup and live controllers  | `set_userscript_autoload`, `get_autoload_status`, `get_userscript_runtime_status`                           |
| Renderer inspection and input | `inspect_renderer`, `interact_renderer`, `capture_renderer`, `evaluate_renderer`, `search_renderer_sources` |
| MCP reload                    | `reload_mcp`, `get_reload_status`                                                                           |
| Windows updates               | `check_codex_update`, `install_codex_update`, `get_codex_update_status`                                     |

Script files are read fresh on each call; JavaScript edits do not require an MCP restart. `reload_mcp` replaces the connection through a detached worker. Call `get_reload_status` after reconnecting. Rebuilding a compiled worker does not replace an already-running worker process; see [upgrading the controller](docs/userscripts.md#upgrading-the-controller-and-installed-scripts).

## Windows updates

Controlled launches preserve Codex's packaged production identity and add the local debugging endpoint. Claudex adds an **Updates** panel that checks for stable Windows releases on startup and every 15 minutes. Installation quits and restarts Codex and can interrupt active work.

```powershell
claudex-yourself update check
claudex-yourself update prepare
claudex-yourself update install
claudex-yourself update status
```

`prepare` downloads, validates, and stages without closing Codex. `install` requests a normal quit, installs, verifies registration, and restarts controlled Codex. The updater chooses a verified obtainable version newer than the installed version; Store announcements and direct downloads can arrive at different times.

Read the [Windows update guide](docs/windows-updates.md) for candidate selection, shutdown recovery, package checks, and limitations.

## Troubleshooting

| Symptom                           | What to check                                                                                                     |
| --------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| DevTools connection refused       | Quit Codex fully, launch through Claudex, and run `status`. An already-running ordinary window is not controlled. |
| Command not found                 | Use the published executable's path. The build does not install a PATH entry.                                     |
| Runtime missing                   | Install the .NET 10 runtime matching the published runtime target.                                                |
| MCP configuration or initialization fails | Check `codex` on PATH and the registered executable/runtime. For Homebrew on macOS, set the MCP entry's `DOTNET_ROOT` as shown above. Reload and check `get_reload_status`. |
| Settings still show an old version or layout | Compare the per-user version with the canonical source. Rebuilds do not publish installed script copies; use `dev`, MCP publication, or a reviewed URL update. |
| Update controls say disconnected | The source worker is separate from MCP. Run `userscript_settings` with the current controller or relaunch through Claudex; restart stale compiled workers after upgrades. |
| Claudex/script check fails | Use `get_source_status` to inspect cached source errors and URLs. Verify raw content/metadata or the release JSON, then use **Check now**. |
| No background checks | Check **Automatic checks** and the script's override. Repeats run only while the controlled app/source worker is running; **Manually** disables automatic checks. |
| Script absent after restart       | Check `list` for its per-user copy and `[autoload]` marker, then inspect `get_autoload_status`.                   |
| Script loads but its UI is hidden | Check its in-app preference. Autoload and in-app enablement are separate.                                         |
| `untested_current_version`        | Verify the script's behavior on the exact Codex build before marking it tested.                                   |
| Windows package update pending or failed | Run `update status` and see the [update guide](docs/windows-updates.md).                                          |

`list` prints the per-user script directory without requiring a live renderer. On Windows, state normally lives under `%APPDATA%\claudex-yourself`, including scripts, captures, sources, schedules, and update status. The [state-file guide](docs/userscripts.md#state-files-and-recovery) distinguishes runtime preferences from controller files. Review screenshots and diagnostic output for private information before sharing them.

The DevTools endpoint is checked on both IPv4 and IPv6 loopback. On Windows, Chromium can bind `::1:9229` when `127.0.0.1:9229` is unavailable; a connection failure on IPv4 alone does not mean the controlled renderer is down.

## Development

See [AGENTS.md](AGENTS.md) for the repository map, build and validation commands, and coding-agent instructions.
