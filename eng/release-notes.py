#!/usr/bin/env python3
"""Write release download links and reject missing/unexpected release assets."""
import argparse
import subprocess
from pathlib import Path
from urllib.parse import quote

PACKAGES = [
    ("Windows", "x64", "lightdl-desktop-win-x64.zip"),
    ("Windows", "ARM64", "lightdl-desktop-win-arm64.zip"),
    ("Windows", "x86", "lightdl-desktop-win-x86.zip"),
    ("Linux", "x64", "lightdl-desktop-linux-x64.AppImage"),
    ("Linux", "ARM64", "lightdl-desktop-linux-arm64.AppImage"),
    ("macOS 14+", "Apple Silicon", "lightdl-desktop-osx-arm64.dmg"),
    ("macOS 14+", "Intel", "lightdl-desktop-osx-x64.dmg"),
    ("Browser extension", "Chrome / Edge", "lightdl-automatic-chromium.zip"),
    ("Browser extension", "Firefox", "lightdl-automatic-firefox.zip"),
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    expected = {name for _, _, name in PACKAGES}
    actual = {p.name for p in args.artifacts.iterdir() if p.is_file()}
    if actual != expected:
        parser.error(f"Release asset mismatch: missing={sorted(expected - actual)}, unexpected={sorted(actual - expected)}")
    base = f"https://github.com/{args.repository}/releases/download/{quote(args.tag, safe='')}"
    lines = ["<!-- lightdl-downloads:start -->", "## Downloads / 下载", "", "| Platform / 平台 | Architecture / 架构 | Download / 下载 |",
             "| --- | --- | --- |"]
    for platform, architecture, name in PACKAGES:
        label = "AppImage" if name.endswith(".AppImage") else "DMG" if name.endswith(".dmg") else "ZIP"
        lines.append(f"| {platform} | {architecture} | [{label}]({base}/{name}) |")
    lines += ["", "Linux: make the AppImage executable, then run it (`chmod +x lightdl-desktop-linux-*.AppImage`).",
              "", "macOS: open the DMG and drag LightDl into Applications.", "", "<!-- lightdl-downloads:end -->", ""]
    # GitHub auto-generated notes can contain only a compare link for direct commits.
    # Include actual commit subjects so the application has useful version notes to show.
    root = Path(__file__).resolve().parents[1]
    previous = subprocess.run(["git", "describe", "--tags", "--abbrev=0", "--match", "v*", "HEAD^"],
                              cwd=root, capture_output=True, text=True, check=False)
    revision = f"{previous.stdout.strip()}..HEAD" if previous.returncode == 0 else "HEAD"
    commits = subprocess.run(["git", "log", "--no-merges", "--format=%s", revision],
                             cwd=root, capture_output=True, text=True, check=True)
    subjects = [line.strip() for line in commits.stdout.splitlines() if line.strip()]
    lines += ["## 更新内容", ""] + [f"- {subject}" for subject in subjects] + [""]
    args.output.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
