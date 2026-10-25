#!/usr/bin/env python3
"""Build the pinned, unpublished LightDl worker-progress preview into the local feed."""
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
REVISION = "9815027684176ee6e38cc63ede1c08ed24ac80aa"
VERSION = "0.4.7-preview.9815027"


def run(*args, cwd=None):
    subprocess.run(args, cwd=cwd, check=True)


def main():
    reference = ET.parse(ROOT / "Directory.Packages.props").find(".//PackageVersion[@Include='LightDl']")
    if reference is None or reference.get("Version") != VERSION:
        raise RuntimeError("The LightDl bootstrap version must match the central package reference")
    feed = ROOT / "artifacts/local-nuget"
    feed.mkdir(parents=True, exist_ok=True)
    # A fresh checkout builds from an immutable source revision, never a moving branch.
    with tempfile.TemporaryDirectory(prefix="lightdl-engine-") as directory:
        source = Path(directory)
        run("git", "init", "--quiet", directory)
        run("git", "remote", "add", "origin", "https://github.com/greepar/LightDl.git", cwd=source)
        run("git", "fetch", "--depth", "1", "origin", REVISION, cwd=source)
        run("git", "checkout", "--detach", "--quiet", "FETCH_HEAD", cwd=source)
        run("dotnet", "pack", str(source / "src/LightDl/LightDl.csproj"), "-c", "Release",
            "-p:TargetFrameworks=net10.0", f"-p:PackageVersion={VERSION}",
            "--output", str(feed), "--nologo", cwd=ROOT)
    print(f"Prepared LightDl {VERSION} from {REVISION}")


if __name__ == "__main__":
    main()
