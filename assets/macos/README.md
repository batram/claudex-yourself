# Claudex Dock icon

`claudex.png` is the modified icon used by the macOS launcher. It was created
with the built-in imagegen tool using the installed app icon as the edit target.
The prompt preserved the interwoven knot, rounded tile and padding, changed the
knot to copper orange and the tile to charcoal, and added a cream `</>` glyph
in the central opening, with transparency outside the tile.

Build the launcher after publishing the executable:

```sh
python3 scripts/create_macos_launcher.py
```

The output is `artifacts/Claudex.app`. Copy it to `~/Applications` and drag it
into the Dock. The launcher embeds the absolute path of this checkout's
`bin/claudex-yourself`; rebuild it if you move the checkout. It reports launch
errors in a dialog and does not quit an existing Codex session automatically.
Its icon is separate from the running Codex application's icon.
