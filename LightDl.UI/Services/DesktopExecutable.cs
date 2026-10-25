namespace LightDl.UI.Services;

public static class DesktopExecutable
{
    // AppImage payloads run from a temporary mount; persisted launch paths must use the outer file.
    public static string? AppImagePath
    {
        get
        {
            if (!OperatingSystem.IsLinux())
                return null;

            var path = Environment.GetEnvironmentVariable("APPIMAGE");
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path)
                ? path
                : null;
        }
    }

    public static string? ProcessPath => AppImagePath ?? Environment.ProcessPath;
}
