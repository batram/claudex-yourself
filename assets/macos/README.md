# Claudex Dock icon

`claudex.png` is the modified icon used by the macOS launcher. It was created
with the built-in imagegen tool using the installed app icon as the edit target.
The prompt preserved the interwoven knot, rounded tile and padding, changed the
knot to copper orange and the tile to charcoal, and added a cream `</>` glyph
in the central opening, with transparency outside the tile.

From the repository root, build the launcher after [publishing the executable](../../README.md#1-build):

```sh
python3 scripts/create_macos_launcher.py
```

The output is `artifacts/Claudex.app`. Copy it to `~/Applications` and drag it
into the Dock. The launcher embeds the absolute path of this checkout's
`bin/claudex-yourself`; rebuild it if you move the checkout. It reports launch
errors in a dialog and does not quit an existing Codex session automatically.
Its icon is separate from the running Codex application's icon. The generated app
refuses to overwrite an existing output. Use a fresh output path when rebuilding,
for example:

```sh
python3 scripts/create_macos_launcher.py --output artifacts/Claudex-next.app
```

If you published to an explicit runtime directory, also pass the matching
executable, for example `--executable bin/osx-arm64/claudex-yourself`. The launcher
runs `launch`, which starts the normal Claudex autoload/source workers; it does
not publish or update installed userscript copies. Use
[Claudex - User scripts](../../docs/userscripts.md#in-app-switches) for script
updates and check schedules.

The Dock launcher and the stdio MCP server are separate launch paths. The
launcher environment does not configure the MCP server's environment;
see [MCP setup](../../README.md#mcp-setup) if a Homebrew framework-dependent MCP
process cannot find .NET. Claudex's controller release check also does not replace
the Dock bundle or update Codex itself; see the
[update distinctions](../../README.md#settings-and-updates).
