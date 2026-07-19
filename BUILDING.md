# Building LightDl

## Requirements

- .NET SDK `11.0.100-preview.6` or newer .NET 11 preview.
- Avalonia `12.1.0` and SukiUI `7.0.1` are restored from NuGet.
- Android and iOS require the matching .NET 11 workloads.
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

## Browser integration host

Publish the Native Messaging host for the same runtime and place its executable beside the desktop executable:

```powershell
dotnet publish LightDl.BrowserHost/LightDl.BrowserHost.csproj -c Release -r win-x64
```

Use the matching Linux or macOS runtime identifier on those platforms. In LightDl Desktop, open `浏览器集成` and choose `注册 / 修复宿主` after both executables are installed.

- Chrome or Edge: load `browser-extensions/automatic/chromium` as an unpacked extension.
- Firefox 128 or newer: load `browser-extensions/automatic/firefox/manifest.json` from `about:debugging`.

The automatic integration uses Native Messaging only. If LightDl is unavailable or the user rejects the confirmation dialog, the browser download continues normally.

## Android CoreCLR

The Android project sets `UseMonoRuntime=false`. Release builds use composite ReadyToRun with CoreCLR; this is not Mono AOT and not NativeAOT.

```powershell
dotnet publish LightDl.Android/LightDl.Android.csproj -c Release -f net11.0-android37.0 -r android-arm64
```

Both CoreCLR and NativeAOT use LightDl's default `SocketsHttpHandler`. .NET 11 Preview 6 drops the AndroidCrypto TrustManager JNI callback from NativeAOT customer app links, so the project explicitly retains and exports that callback. This is the same root cause as `dotnet/runtime#120959`; newer runtime builds register the callback explicitly through `dotnet/runtime#124173`.

`AndroidMessageHandler` remains available as a diagnostic fallback by building with `-p:LightDlUseAndroidPlatformHttpHandler=true`.

Android downloads are written to the public `Download` directory. Android 11 and newer require the user to grant the app's "All files access" permission because LightDl uses random-access file writes and sidecar metadata for parallel resume support. Google Play restricts this permission, so a store-distributed build should replace it with a Storage Access Framework or MediaStore-backed destination.

iOS downloads are written to the app's `Documents` directory. File sharing and in-place document access are enabled, so users can find completed files in `Files > On My iPhone/iPad > LightDl`.

## iOS CoreCLR

The iOS project sets `UseMonoRuntime=false`. Physical devices cannot use a JIT, so .NET uses CoreCLR with composite ReadyToRun and interpreter fallback.

```powershell
dotnet publish LightDl.iOS/LightDl.iOS.csproj -c Release -r ios-arm64
```

Publishing and signing an iOS application must be performed on macOS or through a configured Mac build host.

## Optional NativeAOT mobile experiment

Setting `PublishAot=true` switches to the NativeAOT runtime, not CoreCLR. Android NativeAOT remains experimental and should not be used as the default production profile.
