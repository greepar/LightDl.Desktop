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

## Desktop NativeAOT

```powershell
dotnet publish LightDl.Desktop/LightDl.Desktop.csproj -c Release -r win-x64
```

Use `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64` on the matching build operating system. NativeAOT publishing is platform-specific.

Desktop window and tray icons follow the application's light/dark theme. Windows executables embed the light icon. Linux publish output includes `lightdl.png`; when installing to `/opt/lightdl`, keep this file beside the executable and install `packaging/linux/io.lightdl.LightDl.desktop` in `~/.local/share/applications/` for the launcher icon. Adjust both paths in the desktop entry if installing elsewhere.

Linux prefers the native Avalonia Wayland backend via `UseWaylandWithFallback()`. If Wayland cannot initialize, Avalonia uses X11; Windows and macOS keep their platform backends. The Wayland window app ID is `io.lightdl.LightDl`, matching the desktop entry filename, while X11 uses `StartupWMClass=LightDl.Desktop`. Install the desktop entry and icon on KDE/GNOME so the taskbar can resolve the application icon. Native Wayland support is experimental; global menus, window icon updates, and blur effects do not yet have the same support as X11. The taskbar launcher uses the static icon from the desktop entry.

StaticLink.Avalonia stays on `3.119.4.1` for SkiaSharp `3.119.4`. StaticLink.Avalonia.Native uses the latest available package, `12.1.3.1`; macOS NativeAOT publishing against Avalonia `12.1.4` must be validated on macOS.

Linux NativeAOT publishing currently reports upstream trim/AOT analysis warnings (`IL2104`, `IL3053`) from SukiUI's DataGrid dependency, including DataGrid `12.1.2`. These warnings are not suppressed.

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
