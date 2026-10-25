<picture>
  <source media="(prefers-color-scheme: dark)" srcset="LightDl.UI/Assets/lightdl-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="LightDl.UI/Assets/lightdl-light.svg" />
  <img src="LightDl.UI/Assets/lightdl-light.svg" alt="LightDl icon" width="128" height="128" />
</picture>

# LightDl Desktop

LightDl Desktop is a high-performance concurrent download manager built with Avalonia and powered by the [LightDl](https://github.com/greepar/LightDl) download engine.

The desktop application supports Windows, Linux, and macOS. This repository also contains Android and iOS projects.

## Download

Download the package for your platform from [Releases](https://github.com/greepar/LightDl.Desktop/releases).

macOS builds include a signed `LightDl.app` ZIP and a DMG with an Applications shortcut. Choose `osx-arm64` for Apple Silicon or `osx-x64` for Intel (macOS 14+). Open the DMG and drag LightDl into Applications. CI uses ad-hoc signing by default; Developer ID signing and Apple notarization are enabled when the signing secrets documented in [BUILDING.md](BUILDING.md) are configured.

## Build

```bash
dotnet build LightDl.Desktop/LightDl.Desktop.csproj -c Release
```

See [BUILDING.md](BUILDING.md) for platform requirements and publishing commands.

## HTTP/3

The current main branch uses a pinned LightDl worker-progress preview. Run `python3 eng/prepare-lightdl.py` before building; see [BUILDING.md](BUILDING.md). NativeAOT publishing enables `LightDlStaticQuic` by default on supported desktop architectures.

Settings → Network → HTTP/3 mode offers automatic H2/H3 speed comparison (default), strict HTTP/3, and HTTP/3 disabled. Save settings before creating a new task. Existing tasks keep their settings. Automatic comparison requires the direct connection proxy setting; it skips small files and servers without range support. Strict mode requires HTTPS and fails when HTTP/3 is unavailable. The task card and details show the negotiated protocol while downloading. If strict H3 encounters a connection/negotiation failure, a dialog offers H2/H1.1 for that task only. Declining keeps strict H3; accepting preserves resumable ranges and remembers the task choice across pause/restart. Global settings and other tasks remain unchanged.

Desktop NativeAOT builds link MsQuic statically on Windows x64/arm64, Linux x64/arm64 and macOS x64/arm64. Windows x86 has no bundled QUIC support. Managed builds omit the MsQuic shared library by default; see [BUILDING.md](BUILDING.md) to opt in.

## License

This project is licensed under the [MIT License](LICENSE).
