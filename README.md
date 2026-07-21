# LightDl Desktop

Cross-platform Avalonia applications for [LightDl](https://github.com/greepar/LightDl).

The application uses the published `LightDl` NuGet package for its download engine.

## Projects

- `LightDl.UI`: shared Avalonia UI and application services.
- `LightDl.Desktop`: Windows, Linux, and macOS GUI entry point and browser Native Messaging host.
- `LightDl.Android`: Android application.
- `LightDl.iOS`: iOS application.

## Build

```bash
dotnet build LightDl.Desktop/LightDl.Desktop.csproj -c Release
```

See [BUILDING.md](BUILDING.md) for platform-specific requirements and publishing commands.
