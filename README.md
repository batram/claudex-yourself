# Claudex Yourself

Customize Codex Desktop with local JavaScript userscripts, and inspect or control its renderer through a CLI or MCP server.

Claudex launches Codex with a loopback DevTools endpoint. It includes a sidebar usage display, reversible UI tweaks, an in-app userscript settings page, and a Windows update companion for controlled launches.

This is an independent project using internal Codex Desktop interfaces, not an officially supported extension API. Codex updates can break compatibility. Scripts run with renderer authority; read the [trust guide](SECURITY.md) before using third-party scripts.

## Platform support

| Platform                      | Status                                                                                |
| ----------------------------- | ------------------------------------------------------------------------------------- |
| Windows x64                   | Implemented and live-tested                                                           |
| macOS Apple Silicon and Intel | Build targets and launcher implemented; live launch still needs verification on a Mac |
| Linux                         | Controlled Codex Desktop launch is not implemented                                    |

The Windows updater handles stable `OpenAI.Codex` packages on x64 and Arm64. That package support does not establish end-to-end Windows Arm64 launcher testing.

## Quick start

Run these commands from the repository root.

### 1. Build

Building requires the .NET 10 SDK. Published output is framework-dependent: the destination needs the .NET 10 runtime for the selected architecture. An SDK installation includes a runtime.

Windows:

```powershell
.\build.ps1
```

macOS:

```bash
sh ./build.sh
```

The default output is `bin/`. Windows defaults to `win-x64`; the macOS shell script selects the host architecture. Explicit runtime builds write to `bin/<runtime>/`:

```powershell
.\build.ps1 win-x64
.\build.ps1 osx-arm64
.\build.ps1 osx-x64
```

```bash
sh ./build.sh osx-arm64
sh ./build.sh osx-x64
```

Keep the published directory together, including `scripts/`, `update/`, DLL, and runtime configuration files. The build does not add the executable to PATH.

### 2. Launch Codex

Install Codex Desktop and quit it completely before the first controlled launch. Launching while an ordinary Codex process is running activates that process; it does not retrofit the DevTools endpoint.

Windows:

```powershell
.\bin\claudex-yourself.exe launch
.\bin\claudex-yourself.exe status
```

macOS:

```bash
./bin/claudex-yourself launch
./bin/claudex-yourself status
```

Windows uses the installed Codex package. macOS looks for `Codex.app` in `/Applications` and `~/Applications`. The launcher requests a DevTools endpoint at `127.0.0.1:9229` and uses the Codex profile under the platform's application-data directory.

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

## Development

See [AGENTS.md](AGENTS.md) for the repository map, build and validation commands, and coding-agent instructions.
