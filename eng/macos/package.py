#!/usr/bin/env python3
"""Package the single-file NativeAOT desktop publish as a signed .app and .dmg.

Inspired by greepar/PatchInstaller-Custom's macOS app/DMG packaging. Uses dmgbuild
for Finder layout without driving Finder or requesting Automation permissions.
"""
import argparse
import json
import os
import pathlib
import plistlib
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
IDENTIFIER = "io.lightdl.LightDl"
EXECUTABLE = "LightDl.Desktop"


def run(*arguments, capture=False):
    # Never echo arguments: notary credentials can be supplied through environment.
    result = subprocess.run([str(value) for value in arguments], check=True,
                            text=True, stdout=subprocess.PIPE if capture else None)
    return result.stdout if capture else None


def sign(path, identity, application=False):
    options = ["codesign", "--force", "--sign", identity]
    if identity == "-":
        options.append("--timestamp=none")
    else:
        options.append("--timestamp")
        if application:
            options += ["--options", "runtime"]
    run(*options, path)
    run("codesign", "--verify", "--deep", "--strict", "--verbose=2", path)


def notarize(path, profile, keychain):
    options = ["xcrun", "notarytool", "submit", path, "--keychain-profile", profile,
               "--wait", "--timeout", "20m", "--output-format", "json"]
    if keychain:
        options += ["--keychain", keychain]
    result = json.loads(run(*options, capture=True))
    if result.get("status") != "Accepted":
        raise RuntimeError(f"Apple notarization did not accept the package: {result.get('status')} ({result.get('id')})")
    print(f"Notarization accepted: {result['id']}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("publish_directory", type=pathlib.Path)
    parser.add_argument("--rid", required=True, choices=["osx-x64", "osx-arm64"])
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--version", default=None)
    parser.add_argument("--identity", default=os.environ.get("MACOS_SIGN_IDENTITY") or "-")
    parser.add_argument("--notary-profile", default=os.environ.get("MACOS_NOTARY_PROFILE", ""))
    args = parser.parse_args()
    if sys.platform != "darwin":
        parser.error("Build macOS application bundles on macOS.")
    try:
        import dmgbuild
    except ImportError:
        parser.error("Install eng/macos/requirements.txt into a virtual environment first.")
    if args.notary_profile and args.identity == "-":
        parser.error("Apple notarization requires a Developer ID Application identity; ad-hoc signing cannot be notarized.")
    version = args.version or ET.parse(ROOT / "LightDl.Desktop/LightDl.Desktop.csproj").findtext(".//Version")
    if not version or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        parser.error("--version must contain three numeric components, e.g. 1.0.2")
    publish = args.publish_directory.resolve()
    executable = publish / EXECUTABLE
    if not executable.is_file():
        parser.error(f"NativeAOT publish executable missing: {executable}")
    architecture = "arm64" if args.rid == "osx-arm64" else "x86_64"
    actual = run("lipo", "-archs", executable, capture=True).strip().split()
    if actual != [architecture]:
        parser.error(f"Expected {architecture} executable, got {actual}")
    unexpected = [entry.name for entry in publish.iterdir() if entry.is_file() and
                  entry.name not in (EXECUTABLE, "lightdl.png") and entry.suffix not in (".pdb", ".dbg")]
    if unexpected:
        parser.error(f"Expected a single-file static NativeAOT publish, found extra files: {unexpected}")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    app = output / "LightDl.app"
    if app.exists():
        metadata = app / "Contents/Info.plist"
        if not metadata.is_file() or plistlib.loads(metadata.read_bytes()).get("CFBundleIdentifier") != IDENTIFIER:
            parser.error(f"Refusing to replace an unrelated app bundle: {app}")
        shutil.rmtree(app)
    macos = app / "Contents/MacOS"
    resources = app / "Contents/Resources"
    macos.mkdir(parents=True)
    resources.mkdir()
    shutil.copyfile(executable, macos / EXECUTABLE)
    (macos / EXECUTABLE).chmod(0o755)
    metadata = {
        "CFBundleDevelopmentRegion": "zh_CN",
        "CFBundleDisplayName": "Light Downloader Manager",
        "CFBundleExecutable": EXECUTABLE,
        "CFBundleIconFile": "AppIcon.icns",
        "CFBundleIdentifier": IDENTIFIER,
        "CFBundleInfoDictionaryVersion": "6.0",
        "CFBundleName": "Light Downloader Manager",
        "CFBundlePackageType": "APPL",
        "CFBundleShortVersionString": version,
        "CFBundleVersion": version,
        "LSApplicationCategoryType": "public.app-category.utilities",
        "LSMinimumSystemVersion": "14.0",
        "NSHighResolutionCapable": True,
        "NSPrincipalClass": "NSApplication",
        "CFBundleURLTypes": [{"CFBundleTypeRole": "Viewer", "CFBundleURLName": "io.lightdl.protocol",
                              "CFBundleURLSchemes": ["lightdl"]}],
    }
    (app / "Contents/Info.plist").write_bytes(plistlib.dumps(metadata, sort_keys=False))
    (app / "Contents/PkgInfo").write_bytes(b"APPL????")
    with tempfile.TemporaryDirectory(prefix="lightdl-icon-") as temporary:
        iconset = pathlib.Path(temporary) / "AppIcon.iconset"
        iconset.mkdir()
        for size in (16, 32, 128, 256, 512):
            for scale, suffix in ((1, ""), (2, "@2x")):
                pixels = size * scale
                run("sips", "-z", pixels, pixels, ROOT / "LightDl.UI/Assets/lightdl-light.png", "--out",
                    iconset / f"icon_{size}x{size}{suffix}.png", capture=True)
        run("iconutil", "-c", "icns", iconset, "-o", resources / "AppIcon.icns")
    run("plutil", "-lint", app / "Contents/Info.plist")
    sign(app, args.identity, application=True)
    if args.identity != "-":
        signature = subprocess.run(["codesign", "-d", "--verbose=4", str(app)],
                                   check=True, capture_output=True, text=True).stderr
        if "Authority=Developer ID Application:" not in signature:
            raise RuntimeError("Distribution signing requires a Developer ID Application certificate.")
    stem = f"lightdl-desktop-{args.rid}"
    archive = output / f"{stem}.zip"
    dmg = output / f"{stem}.dmg"
    for path in (archive, dmg):
        path.unlink(missing_ok=True)
    keychain = os.environ.get("MACOS_SIGN_KEYCHAIN", "")
    if args.notary_profile:
        run("ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, archive)
        notarize(archive, args.notary_profile, keychain)
        run("xcrun", "stapler", "staple", app)
        run("xcrun", "stapler", "validate", app)
        archive.unlink()
    dmgbuild.build_dmg(str(dmg), "LightDl", settings={
        "format": "UDZO", "filesystem": "HFS+", "files": [str(app)],
        "symlinks": {"Applications": "/Applications"}, "icon": str(resources / "AppIcon.icns"),
        "background": "#f4f7fb", "window_rect": ((200, 200), (560, 340)),
        "icon_locations": {"LightDl.app": (150, 160), "Applications": (410, 160)},
        "icon_size": 112, "text_size": 14, "default_view": "icon-view",
        "show_status_bar": False, "show_toolbar": False, "show_pathbar": False,
        "show_sidebar": False, "show_tab_view": False,
    })
    sign(dmg, args.identity)
    if args.notary_profile:
        notarize(dmg, args.notary_profile, keychain)
        run("xcrun", "stapler", "staple", dmg)
        run("xcrun", "stapler", "validate", dmg)
        run("spctl", "--assess", "--type", "execute", "--verbose=2", app)
    run("hdiutil", "verify", dmg)
    print(f"Created {app}\nCreated {dmg}\nSigning: {'ad-hoc (not notarized)' if args.identity == '-' else 'Developer ID'}", flush=True)


if __name__ == "__main__":
    main()
