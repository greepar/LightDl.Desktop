#!/usr/bin/env bash
set -euo pipefail

publish_dir="$(realpath "${1:?publish directory required}")"
rid="${2:?linux-x64 or linux-arm64 required}"
output_dir="${3:?output directory required}"
appimagetool="$(realpath "${4:?appimagetool path required}")"
case "$rid" in
  linux-x64) export ARCH=x86_64 ;;
  linux-arm64) export ARCH=aarch64 ;;
  *) echo "Unsupported runtime: $rid" >&2; exit 1 ;;
esac
mkdir -p "$output_dir"
output_dir="$(realpath "$output_dir")"
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT
app_dir="$work_dir/LightDl.AppDir"
mkdir -p "$app_dir/usr/bin"
install -m 755 "$publish_dir/LightDl.Desktop" "$app_dir/usr/bin/LightDl.Desktop"
install -m 644 "$repo_dir/LightDl.UI/Assets/lightdl-light.png" "$app_dir/lightdl.png"
ln -s lightdl.png "$app_dir/.DirIcon"
sed -e 's|Exec=/opt/lightdl/LightDl.Desktop|Exec=LightDl.Desktop|' \
    -e 's|Icon=/opt/lightdl/lightdl.png|Icon=lightdl|' \
    "$repo_dir/packaging/linux/io.lightdl.LightDl.desktop" > "$app_dir/io.lightdl.LightDl.desktop"
cat > "$app_dir/AppRun" <<'APPRUN'
#!/bin/sh
set -eu
app_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec "$app_dir/usr/bin/LightDl.Desktop" "$@"
APPRUN
chmod 755 "$app_dir/AppRun"
desktop-file-validate "$app_dir/io.lightdl.LightDl.desktop"
# The tool itself can run on CI hosts without FUSE.
APPIMAGE_EXTRACT_AND_RUN=1 "$appimagetool" "$app_dir" "$output_dir/lightdl-desktop-$rid.AppImage"
chmod 755 "$output_dir/lightdl-desktop-$rid.AppImage"
