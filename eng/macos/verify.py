#!/usr/bin/env python3
"""Verify the delivered DMG, including signatures after mounting."""
import argparse
import json
import pathlib
import plistlib
import struct
import subprocess
import tempfile

from ds_store import DSStore


def run(*arguments, capture=False):
    return subprocess.run([str(value) for value in arguments], check=True, text=True,
                          stdout=subprocess.PIPE if capture else None).stdout


def verify_app(app, architecture):
    run("codesign", "--verify", "--deep", "--strict", "--verbose=2", app)
    metadata = plistlib.loads((app / "Contents/Info.plist").read_bytes())
    assert metadata["CFBundleIdentifier"] == "io.lightdl.LightDl"
    assert metadata["CFBundleName"] == metadata["CFBundleDisplayName"] == "Light Downloader Manager"
    assert metadata["LSMinimumSystemVersion"] == "14.0"
    executable = app / "Contents/MacOS" / metadata["CFBundleExecutable"]
    assert run("lipo", "-archs", executable, capture=True).strip() == architecture
    assert (app / "Contents/Resources/AppIcon.icns").is_file()
    assert not any("msquic" in path.name.lower() for path in app.rglob("*"))
    dependencies = run("otool", "-L", executable, capture=True)
    assert "msquic" not in dependencies.lower(), dependencies
    # Exercise the real packaged executable without opening a GUI or downloading:
    # an invalid native-messaging frame must return a framed rejection and exit.
    result = subprocess.run([str(executable), "-mozilla"], input=struct.pack("<i", 0),
                            capture_output=True, timeout=20, check=True)
    length = struct.unpack("<i", result.stdout[:4])[0]
    response = json.loads(result.stdout[4:4 + length])
    assert response.get("Accepted") is False or response.get("accepted") is False, response


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=pathlib.Path)
    parser.add_argument("--rid", required=True, choices=("osx-x64", "osx-arm64"))
    args = parser.parse_args()
    directory = args.directory.resolve()
    architecture = "arm64" if args.rid == "osx-arm64" else "x86_64"
    stem = f"lightdl-desktop-{args.rid}"
    with tempfile.TemporaryDirectory(prefix="lightdl-archive-verify-") as temporary:
        mount = pathlib.Path(temporary) / "mounted"
        dmg = directory / f"{stem}.dmg"
        run("codesign", "--verify", "--strict", "--verbose=2", dmg)
        run("hdiutil", "attach", "-readonly", "-nobrowse", "-mountpoint", mount, dmg)
        try:
            assert (mount / "Applications").is_symlink()
            assert (mount / "Applications").readlink() == pathlib.Path("/Applications")
            verify_app(mount / "LightDl.app", architecture)
            with DSStore.open(str(mount / ".DS_Store"), "r") as store:
                assert store["LightDl.app"]["Iloc"] == (150, 160)
                assert store["Applications"]["Iloc"] == (410, 160)
        finally:
            run("hdiutil", "detach", mount)
    print("DMG verified: app signatures, executable startup, architectures and layout.")


if __name__ == "__main__":
    main()
