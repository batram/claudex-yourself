# Claudex Windows icon

`claudex.ico` is generated from `../macos/claudex.png` and holds 256, 128, 64,
48, 40, 32, 24, 20 and 16 px frames. The build embeds it as the executable's
application icon and also copies it next to the executable as `claudex.ico`,
for shortcuts or other launchers that want a separate icon file.

The PNG carries macOS-style padding (the tile is ~80% of the canvas, plus a
faint shadow), which looks undersized next to Windows taskbar icons. The ICO is
cropped to the tile with a ~1% margin instead.

Regenerate it with ImageMagick 7 after changing the PNG (re-measure the crop
with `magick claudex.png -alpha extract -threshold 50% -format "%@" info:` if
the tile moved):

```sh
magick assets/macos/claudex.png -crop 1011x1011+121+121 +repage -background none -gravity center -extent 1031x1031 -filter Lanczos -define icon:auto-resize=256,128,64,48,40,32,24,20,16 assets/windows/claudex.ico
```
