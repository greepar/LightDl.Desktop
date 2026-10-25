#!/usr/bin/env python3
"""Check the AppImage layout and exercise the packaged native messaging entry point."""
import argparse
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("appimage", type=Path)
    parser.add_argument("--rid", choices=("linux-x64", "linux-arm64"), required=True)
    args = parser.parse_args()
    appimage = args.appimage.resolve()
    assert os.access(appimage, os.X_OK), "AppImage must be executable"
    with tempfile.TemporaryDirectory(prefix="lightdl-appimage-verify-") as temporary:
        subprocess.run([str(appimage), "--appimage-extract"], cwd=temporary,
                       stdout=subprocess.DEVNULL, check=True, timeout=60)
        appdir = Path(temporary) / "squashfs-root"
        executable = appdir / "usr/bin/LightDl.Desktop"
        header = executable.read_bytes()[:20]
        assert header[:4] == b"\x7fELF" and header[4:6] == b"\x02\x01"
        assert struct.unpack_from("<H", header, 18)[0] == (62 if args.rid == "linux-x64" else 183)
        assert (appdir / ".DirIcon").resolve() == appdir / "lightdl.png"
        subprocess.run(["desktop-file-validate", str(appdir / "io.lightdl.LightDl.desktop")], check=True)
        # Exercise AppRun and argument/stdin/stdout forwarding without requiring a GUI or FUSE.
        result = subprocess.run([str(appimage), "--appimage-extract-and-run", "-mozilla"],
                                input=struct.pack("<i", 0), capture_output=True, timeout=60, check=True)
        length = struct.unpack("<i", result.stdout[:4])[0]
        response = json.loads(result.stdout[4:4 + length])
        assert response.get("Accepted") is False or response.get("accepted") is False, response
    print("AppImage verified: executable architecture, icon, desktop entry and native messaging startup.")


if __name__ == "__main__":
    main()
