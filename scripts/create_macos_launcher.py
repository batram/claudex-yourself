#!/usr/bin/env python3
"""Build a Dock launcher using this checkout's published Claudex executable."""

import argparse
import json
from pathlib import Path
import plistlib
import shlex
import subprocess
import tempfile


def run(*args):
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL)


def main():
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=root / "artifacts/Claudex.app")
    parser.add_argument("--executable", type=Path, default=root / "bin/claudex-yourself")
    args = parser.parse_args()
    executable = args.executable.resolve()
    output = args.output.resolve()
    icon = root / "assets/macos/claudex.png"
    if not executable.is_file() or not icon.is_file():
        parser.error("Build Claudex first and provide assets/macos/claudex.png.")
    if output.exists():
        parser.error(f"Output already exists: {output}; choose a new output path.")
    output.parent.mkdir(parents=True, exist_ok=True)
    command = shlex.quote(str(executable)) + " launch"
    source = f'''use scripting additions
on run
    try
        do shell script {json.dumps(command)}
    on error messageText number errorNumber
        display dialog "Claudex could not start controlled mode." & return & return & messageText & return & return & "If ChatGPT/Codex is already open, quit it completely and click Claudex again." with title "Claudex" buttons {{"OK"}} default button "OK" with icon caution
    end try
end run
'''
    with tempfile.TemporaryDirectory(prefix="claudex-launcher-") as work:
        work = Path(work)
        script = work / "launcher.applescript"
        script.write_text(source)
        run("/usr/bin/osacompile", "-o", str(output), str(script))
        iconset = work / "Claudex.iconset"
        iconset.mkdir()
        for size in (16, 32, 128, 256, 512):
            for scale in (1, 2):
                suffix = "@2x" if scale == 2 else ""
                target = iconset / f"icon_{size}x{size}{suffix}.png"
                run("/usr/bin/sips", "-z", str(size * scale), str(size * scale),
                    str(icon), "--out", str(target))
        run("/usr/bin/iconutil", "-c", "icns", str(iconset), "-o",
            str(output / "Contents/Resources/Claudex.icns"))
    info = output / "Contents/Info.plist"
    with info.open("rb") as stream:
        metadata = plistlib.load(stream)
    metadata.pop("CFBundleIconName", None)
    for key in list(metadata):
        if key.startswith("NS") and key.endswith("UsageDescription"):
            del metadata[key]
    metadata.update(CFBundleIdentifier="local.claudex-yourself.launcher",
                    CFBundleName="Claudex", CFBundleDisplayName="Claudex",
                    CFBundleIconFile="Claudex", CFBundleShortVersionString="1.0",
                    CFBundleVersion="1")
    with info.open("wb") as stream:
        plistlib.dump(metadata, stream)
    run("/usr/bin/codesign", "--force", "--sign", "-", str(output))
    print(output)


if __name__ == "__main__":
    main()
