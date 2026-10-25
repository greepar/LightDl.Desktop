# Building LightDl

## Requirements

- .NET 10 SDK. The repository `global.json` selects the latest installed .NET 10 feature band.
- Avalonia `12.1.4` and SukiUI `7.0.1` are restored from NuGet. Mobile projects use Semi.Avalonia `12.1.0.1`.
- Android and iOS require the matching .NET 10 workloads.
- iOS and macOS packaging require macOS with Xcode.
- Desktop NativeAOT requires the platform native toolchain.

Install mobile workloads after any pending system restart:

```powershell
dotnet workload install android ios
```

## Download engine

The download engine uses the official LightDl `0.4.7` package from NuGet.org.
A normal restore fetches the engine, including real active-worker counts, H2/H3
speed comparison, automatic H3 connection scaling and static MsQuic assets.
No local preview feed or engine bootstrap step is required.

## Desktop NativeAOT

```powershell
dotnet publish LightDl.Desktop/LightDl.Desktop.csproj -c Release -r win-x64
```

The default NativeAOT desktop build statically links MsQuic on Windows x64/arm64, Linux x64/arm64 and macOS x64/arm64. Build on the target OS; Windows needs MSVC. These macOS static archives require macOS 14 or newer. Windows x86 does not include MsQuic and cannot use H3 without separately installed QUIC support. The patched runtime is currently verified with .NET runtime 10.0.12/10.0.13; CI uses SDK 10.0.402. The build rejects unsupported runtime versions instead of producing an unverified binary.

Use `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64` on the matching build operating system. NativeAOT publishing is platform-specific.

Desktop window and tray icons follow the application's light/dark theme. Windows executables embed the light icon. Linux publish output includes `lightdl.png`; when installing to `/opt/lightdl`, keep this file beside the executable and install `packaging/linux/io.lightdl.LightDl.desktop` in `~/.local/share/applications/` for the launcher icon. Adjust both paths in the desktop entry if installing elsewhere.

Linux prefers the native Avalonia Wayland backend via `UseWaylandWithFallback()`. If Wayland cannot initialize, Avalonia uses X11; Windows and macOS keep their platform backends. The Wayland window app ID is `io.lightdl.LightDl`, matching the desktop entry filename, while X11 uses `StartupWMClass=LightDl.Desktop`. Install the desktop entry and icon on KDE/GNOME so the taskbar can resolve the application icon. Native Wayland support is experimental; global menus, window icon updates, and blur effects do not yet have the same support as X11. The taskbar launcher uses the static icon from the desktop entry.

StaticLink.Avalonia stays on `3.119.4.1` for SkiaSharp `3.119.4`. StaticLink.Avalonia.Native uses `12.1.4.1`, aligned with Avalonia `12.1.4`; the desktop Actions matrix validates native builds on both macOS architectures.

Linux NativeAOT publishing currently reports upstream trim/AOT analysis warnings (`IL2104`, `IL3053`) from SukiUI's DataGrid dependency, including DataGrid `12.1.2`. These warnings are not suppressed.

Windows x86 uses a local copy of the .NET static Brotli archives with DLL-export directives removed (`eng/WindowsX86Brotli.targets`). This avoids LLD MinGW adding a second underscore to MSVC stdcall export names. Function symbols/code and the NuGet cache are unchanged.

## Browser integration host

The published `LightDl.Desktop` executable also serves as the Native Messaging host. The browser starts the same executable in host mode, while normal launches enter the Avalonia GUI. In LightDl Desktop, open `浏览器集成` and choose `注册 / 修复宿主` after installation.

- Chrome or Edge: load `browser-extensions/automatic/chromium` as an unpacked extension.
- Firefox 142 or newer: load `browser-extensions/automatic/firefox/manifest.json` from `about:debugging`.

The automatic integration uses Native Messaging only. If LightDl is unavailable or the user rejects the confirmation dialog, the browser download continues normally.

## Android NativeAOT

The Android project sets `UseMonoRuntime=false` and `PublishAot=true` to publish with NativeAOT.

```powershell
dotnet publish LightDl.Android/LightDl.Android.csproj -c Release -f net10.0-android36.0 -r android-arm64
```

LightDl uses the default `SocketsHttpHandler`. `AndroidMessageHandler` remains available as a diagnostic fallback by building with `-p:LightDlUseAndroidPlatformHttpHandler=true`.

Android downloads are written to the public `Download` directory. Android 11 and newer require the user to grant the app's "All files access" permission because LightDl uses random-access file writes and sidecar metadata for parallel resume support. Google Play restricts this permission, so a store-distributed build should replace it with a Storage Access Framework or MediaStore-backed destination.

iOS downloads are written to the app's `Documents` directory. File sharing and in-place document access are enabled, so users can find completed files in `Files > On My iPhone/iPad > LightDl`.

## iOS CoreCLR

The iOS project sets `UseMonoRuntime=false`. Physical devices cannot use a JIT, so .NET uses CoreCLR with composite ReadyToRun and interpreter fallback.

```powershell
dotnet publish LightDl.iOS/LightDl.iOS.csproj -c Release -r ios-arm64
```

Publishing and signing an iOS application must be performed on macOS or through a configured Mac build host.

## Mobile runtime note

Android NativeAOT remains experimental. Validate startup, TLS downloads, file access, and resume behavior on physical devices before distribution.

## Managed builds and HTTP/3

Managed builds omit MsQuic by default:

```bash
dotnet publish LightDl.Desktop/LightDl.Desktop.csproj -c Release -r win-x64 -p:PublishAot=false -p:PublishTrimmed=false -p:LightDlStaticQuic=false
```

To explicitly include a shared library, add `-p:LightDlIncludeMsQuic=true`; Windows uses the runtime-pack DLL. On Unix also supply `-p:LightDlMsQuicLibrary=/absolute/path/to/libmsquic.so` (or `.dylib` on macOS). QUIC support still depends on the platform/network. For automatic speed comparison choose “不使用代理” in settings; system/custom proxy settings skip probing. Strict H3 only falls back after the user accepts the per-task connection-failure dialog; HTTP status, certificate and storage errors do not prompt. Task cards show the negotiated HTTP version during transfer.

Run settings/configuration tests with `dotnet test tests/LightDl.UI.Tests/LightDl.UI.Tests.csproj -c Release`.

## macOS .app / DMG packaging and signing

Packaging follows the `.app` layout and automatic ad-hoc signing approach in
[PatchInstaller-Custom](https://github.com/greepar/PatchInstaller-Custom/tree/35d78baa0fc6d6e162061997abf2d65fec36b28f/PatchInstaller/Build),
with LightDl's own icons, identifier `io.lightdl.LightDl`, and macOS 14 minimum.
`dmgbuild` writes Finder's icon layout directly; no Finder Automation permission is required.

Run on macOS with Python 3.12+ after NativeAOT publishing:

```bash
python3 -m venv artifacts/macos-tools
artifacts/macos-tools/bin/python -m pip install -r eng/macos/requirements.txt
artifacts/macos-tools/bin/python eng/macos/package.py publish/osx-arm64 --rid osx-arm64 --output artifacts/macos/osx-arm64
artifacts/macos-tools/bin/python eng/macos/verify.py artifacts/macos/osx-arm64 --rid osx-arm64
```

Use `osx-x64` and an x64 publish for Intel. The packaging script checks the executable architecture.
The script creates `LightDl.app` for staging and `lightdl-desktop-<rid>.dmg` for distribution.
Only the DMG is uploaded to Actions artifacts and Releases; no macOS ZIP or separate checksum file is published.
When notarization is configured, a temporary app ZIP is used for Apple's submission and removed afterward.
Keep the executable inside the installed app bundle; browser native messaging and autostart use its actual `Contents/MacOS` path.

Both app and DMG are signed and verified automatically. With no signing secrets,
this is an **ad-hoc signature**, as in the reference repository: it verifies bundle integrity,
but is **not Apple Developer ID signing or notarization**. Internet-downloaded ad-hoc apps
may still be blocked by Gatekeeper. The build does not remove quarantine attributes or change
system security settings.

For Developer ID distribution, configure these repository Actions secrets:

| Secret | Purpose |
| --- | --- |
| `MACOS_CERTIFICATE_P12_BASE64` | Base64-encoded Developer ID Application certificate **including its private key** |
| `MACOS_CERTIFICATE_PASSWORD` | Password protecting the P12 (may be empty for an unencrypted P12) |
| `MACOS_SIGN_IDENTITY` | Optional certificate SHA-1/name; auto-detected when the P12 has one Developer ID Application identity |
| `APPLE_ID` | Apple developer account used for notarization |
| `APPLE_TEAM_ID` | Apple Developer team identifier |
| `APPLE_APP_SPECIFIC_PASSWORD` | App-specific password for notarization |

The last three secrets enable notarization and must be supplied together. CI imports the P12
into a temporary keychain, signs the app with hardened runtime and a secure timestamp,
notarizes/staples both delivered formats, then removes the certificate/keychain even on failure.
It never exports a local signing key or uploads credentials as build artifacts.
Partially configured credentials fail instead of silently falling back to ad-hoc signing.
Developer ID/notarization cannot be exercised without those credentials.

For local Developer ID signing, select an existing Developer ID Application identity with
`MACOS_SIGN_IDENTITY`, and optionally set `MACOS_NOTARY_PROFILE` to a stored `notarytool`
keychain profile. An Apple Development certificate is not a Developer ID distribution certificate.
The verification script extracts the ZIP and mounts the DMG read-only, validates both app signatures,
checks the architecture/install layout, executes the packaged binary in native-messaging mode
with an invalid frame (no GUI/download), and checks the delivered hashes.

## Linux AppImage and release downloads

CI packages Linux x64/ARM64 publishes into `lightdl-desktop-linux-<arch>.AppImage` using appimagetool `1.9.1`.
The AppDir contains the executable, launcher entry and icon. No loose PNG or Linux ZIP is distributed.
Run `bash eng/linux/package-appimage.sh publish/linux-x64 linux-x64 artifacts /path/to/appimagetool-x86_64.AppImage`
to package locally; `desktop-file-validate` must be installed. Verify with
`python3 eng/linux/verify-appimage.py artifacts/lightdl-desktop-linux-x64.AppImage --rid linux-x64`.
The verification exercises the actual AppImage's native messaging entry point without a GUI or FUSE.

Mark downloaded AppImages executable before launching. Browser native messaging registration and autostart
persist the outer `APPIMAGE` path, not the temporary mount. Register them again after moving the AppImage.
Desktop integration tools can install its bundled launcher/icon; the file manager's thumbnail support depends on the desktop.

Tagged releases publish Windows ZIPs, Linux AppImages, macOS DMGs and browser extension ZIPs.
`eng/release-notes.py` verifies the expected asset set and generates a download table above the automatic changelog.
