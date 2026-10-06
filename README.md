# Claudex Yourself

Customize Codex Desktop with local JavaScript userscripts, and inspect or control its renderer through a CLI or MCP server.

Claudex launches Codex with a loopback DevTools endpoint. It includes a sidebar usage display, reversible UI tweaks, an in-app userscript settings page, and a Windows update companion for controlled launches.

This is an independent project using internal Codex Desktop interfaces, not an officially supported extension API. Codex updates can break compatibility. Scripts run with renderer authority; read the [trust guide](SECURITY.md) before using third-party scripts.

## Platform support

| Platform                      | Status                                                                                |
| ----------------------------- | ------------------------------------------------------------------------------------- |
| Windows x64                   | Implemented and live-tested                                                           |
| macOS Apple Silicon           | Build, controlled launch, renderer access, and MCP hot reload verified on app build `12947` |
| macOS Intel                   | Build target and launcher implemented; native testing still needed                     |
| Linux x64                     | Build, controlled launch, renderer access, usage display and settings verified on app build `26.930.61225` |
| Linux Arm64                   | Build target implemented; native testing still needed                                 |

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

Keep the published directory together, including `scripts/`, `update/`, DLL, and runtime configuration files. The build does not add the executable to PATH.

For a Mac installation that does not need a separately installed runtime, publish a self-contained build (use `osx-x64` on Intel):

```bash
dotnet publish ClaudexYourself.csproj -c Release -r osx-arm64 --self-contained true -o bin
```

### 2. Launch Codex

Install Codex Desktop and quit it completely before the first controlled launch. If Codex is already running, the launcher first verifies its controlled renderer, then activates it and reapplies autoload scripts. If the renderer is unavailable, launch fails with instructions to quit and reopen Codex; it cannot retrofit the DevTools endpoint into an existing process.

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

On Windows, `.\bin\claudex-yourself.exe install-shortcut` creates a **Codex (controlled)** desktop shortcut.

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

Wait for the initial `reloaded` result, then press Ctrl+C to stop watching. Leaving the command running reloads the script whenever its source changes. On macOS, use `./scripts/sidebar_usage.js`.

Repeat with `userscript_settings.js` to add **User scripts** to Settings. Publish `hide_invite_a_friend.js` and `hide_pets_button.js` the same way if you want those switches to control installed scripts. The settings page catalogs those three selectable scripts; it is not an automatic catalog of arbitrary scripts.

See the [userscript guide](docs/userscripts.md) for metadata, live development, autoload, switches, and MCP publication.

### Windows notification clicks

The Windows launcher preserves Codex's packaged production app identity and default profile and adds only the debugging arguments needed for renderer access. This lets Codex handle its own notification delivery and clicks. A controlled restart is needed when switching from a previously launched process with a different identity or profile; an already running process keeps its original launch arguments.

## MCP setup

The Codex CLI must be available on PATH for configuration:

```powershell
claudex-yourself configure codex
claudex-yourself run reload-mcp
```

The first command registers the current executable as the `claudex-yourself` stdio MCP server. The second reloads Codex's MCP configuration through the controlled renderer and verifies that this server reconnects. Keep the executable at its registered location, or configure it again after moving it.

The separate `reload` shortcut runs `reload-dgspy`, a helper for an existing dgSpy MCP setup. It is not the reload command for this project's own server.

| Purpose                       | Tools                                                                                                       |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Connection                    | `claudex_status`                                                                                            |
| Script files and execution    | `list_userscripts`, `read_userscript`, `write_userscript`, `run_userscript`                                 |
| Compatibility                 | `get_userscript_metadata`, `list_userscript_compatibility`, `mark_userscript_tested`                        |
| Startup and live controllers  | `set_userscript_autoload`, `get_autoload_status`, `get_userscript_runtime_status`                           |
| Renderer inspection and input | `inspect_renderer`, `interact_renderer`, `capture_renderer`, `evaluate_renderer`, `search_renderer_sources` |
| MCP reload                    | `reload_mcp`, `get_reload_status`                                                                           |
| Windows updates               | `check_codex_update`, `install_codex_update`, `get_codex_update_status`                                     |

Script files are read fresh on each call; JavaScript edits do not require an MCP restart. `reload_mcp` replaces the connection through a detached worker. Call `get_reload_status` after reconnecting.

## Windows updates

Controlled launches use Codex's `dev` build flavor, where its native updater is disabled. Claudex adds an **Updates** panel that checks for stable Windows releases on startup and every 15 minutes. Installation quits and restarts Codex and can interrupt active work.

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
| MCP configuration fails           | Check that `codex` resolves on PATH. Use `run reload-mcp` after configuration.                                    |
| Script absent after restart       | Check `list` for its per-user copy and `[autoload]` marker, then inspect `get_autoload_status`.                   |
| Script loads but its UI is hidden | Check its in-app preference. Autoload and in-app enablement are separate.                                         |
| `untested_current_version`        | Verify the script's behavior on the exact Codex build before marking it tested.                                   |
| Update pending or failed          | Run `update status` and see the [update guide](docs/windows-updates.md).                                          |

`list` prints the per-user script directory without requiring a live renderer. On Windows, state normally lives under `%APPDATA%\claudex-yourself`, including scripts, captures, and update status. Review screenshots and diagnostic output for private information before sharing them.

The DevTools endpoint is checked on both IPv4 and IPv6 loopback. On Windows, Chromium can bind `::1:9229` when `127.0.0.1:9229` is unavailable; a connection failure on IPv4 alone does not mean the controlled renderer is down.

## Development

See [AGENTS.md](AGENTS.md) for the repository map, build and validation commands, and coding-agent instructions.

## Claudex and userscript sources

**Settings → Claudex - User scripts** checks Claudex releases and groups each script’s switches, settings, installed version, and update controls in one card. Add custom scripts by GitHub file link or HTTP(S) URL, review their source, then install/update and choose autoload. An agent can use `preview_userscript_url` and `install_userscript_url` for the same flow. Source URLs persist for future checks. Checks run on startup by default, with configurable intervals and per-script overrides; no updates install automatically. Claudex's global check opens a release link rather than replacing the running controller. See [URL sources and agent installation](docs/userscripts.md#in-app-switches) for the manifest format and trust model.
