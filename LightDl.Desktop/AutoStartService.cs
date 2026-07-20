using System.Security;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LightDl.Desktop;

internal static class AutoStartService
{
    private const string AppName = "LightDl";
    private const string SilentArgument = "--silent";

    public static Task ConfigureAsync(bool enabled)
    {
        if (OperatingSystem.IsWindows())
            ConfigureWindows(enabled);
        else if (OperatingSystem.IsMacOS())
            ConfigureMacOS(enabled);
        else if (OperatingSystem.IsLinux())
            ConfigureLinux(enabled);

        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private static void ConfigureWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
            key.SetValue(AppName, BuildCommandLine(GetLaunchArguments()));
        else
            key.DeleteValue(AppName, false);
    }

    private static void ConfigureLinux(bool enabled)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "autostart",
            "lightdl.desktop");
        if (!enabled)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var command = BuildCommandLine(GetLaunchArguments());
        File.WriteAllText(path, $"""
[Desktop Entry]
Type=Application
Name=LightDl
Comment=LightDl Download Manager
Exec={command}
Terminal=false
X-GNOME-Autostart-enabled=true
""");
    }

    private static void ConfigureMacOS(bool enabled)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents",
            "io.lightdl.LightDl.plist");
        if (!enabled)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var arguments = string.Join(
            Environment.NewLine,
            GetLaunchArguments().Select(argument => $"        <string>{SecurityElement.Escape(argument)}</string>"));
        File.WriteAllText(path, $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>io.lightdl.LightDl</string>
    <key>ProgramArguments</key>
    <array>
{arguments}
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>
""");
    }

    private static IReadOnlyList<string> GetLaunchArguments()
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定 LightDl 可执行文件路径");
        var arguments = new List<string> { processPath };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryPath = Environment.GetCommandLineArgs().FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(entryPath))
                arguments.Add(Path.GetFullPath(entryPath));
        }

        arguments.Add(SilentArgument);
        return arguments;
    }

    private static string BuildCommandLine(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        return $"\"{argument.Replace("\"", "\\\"")}\"";
    }
}
