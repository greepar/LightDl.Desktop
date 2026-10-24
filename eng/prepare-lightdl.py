#!/usr/bin/env python3
"""Build the pinned HTTP/3 LightDl preview into a local NuGet feed.

Run once before restore/build. No GitHub Actions artifact or expiring download URL
is required; the package is built from the immutable source commit tested upstream.
"""
import pathlib
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
COMMIT = "5cda174f75c598c07dd923bbd840249a14d97fcb"
VERSION = "0.4.5-h3-desktop.1"
FEED = ROOT / "artifacts" / "nuget"
PACKAGE = FEED / f"LightDl.{VERSION}.nupkg"
SOURCE = ROOT / "artifacts" / "lightdl-source"

if PACKAGE.is_file():
    print(f"Using {PACKAGE}")
    raise SystemExit(0)

FEED.mkdir(parents=True, exist_ok=True)
if not (SOURCE / ".git").is_dir():
    SOURCE.mkdir(parents=True, exist_ok=True)
    subprocess.run(["git", "init", str(SOURCE)], check=True)
subprocess.run(["git", "-C", str(SOURCE), "fetch", "--depth=1",
                "https://github.com/greepar/LightDl.git", COMMIT], check=True)
subprocess.run(["git", "-C", str(SOURCE), "checkout", "--detach", "FETCH_HEAD"], check=True)
actual = subprocess.check_output(["git", "-C", str(SOURCE), "rev-parse", "HEAD"], text=True).strip()
assert actual == COMMIT, actual
subprocess.run(["dotnet", "pack", str(SOURCE / "src/LightDl/LightDl.csproj"),
                "-c", "Release", "-p:TargetFrameworks=net10.0",
                "-p:ManagePackageVersionsCentrally=false", "-p:CentralPackageTransitivePinningEnabled=false",
                f"-p:PackageVersion={VERSION}",
                "-o", str(FEED)], cwd=ROOT, check=True)
assert PACKAGE.is_file(), PACKAGE
print(f"Prepared {PACKAGE}")
