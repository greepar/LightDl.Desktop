<picture>
  <source media="(prefers-color-scheme: dark)" srcset="LightDl.UI/Assets/lightdl-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="LightDl.UI/Assets/lightdl-light.svg" />
  <img src="LightDl.UI/Assets/lightdl-light.svg" alt="LightDl icon" width="128" height="128" />
</picture>

# LightDl Desktop

LightDl Desktop is a high-performance concurrent download manager built with Avalonia and powered by the [LightDl](https://github.com/greepar/LightDl) download engine.

The desktop application supports Windows, Linux, and macOS. This repository also contains Android and iOS projects.

## Build

```bash
dotnet build LightDl.Desktop/LightDl.Desktop.csproj -c Release
```

See [BUILDING.md](BUILDING.md) for platform requirements and publishing commands.

## License

This project is licensed under the [MIT License](LICENSE).
