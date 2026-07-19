using System.Text.Json;
using System.Runtime.Versioning;
using LightDl.UI.Models;
using Microsoft.Win32;

namespace LightDl.UI.Services;

public sealed class BrowserHostRegistrationService
{
    private const string HostName = "com.lightdl.browser";
    private const string ChromiumExtensionId = "nafkhlacfpmamhmhdfbnfnkainjdaohi";
    private const string FirefoxExtensionId = "automatic-integration@lightdl.io";

    public bool IsRegistered()
    {
        if (OperatingSystem.IsWindows())
            return IsWindowsRegistered();

        return GetManifestDestinations().Any(File.Exists);
    }

    public async Task RegisterAsync()
    {
        var hostPath = FindHostExecutable()
                       ?? throw new FileNotFoundException("找不到 LightDl.BrowserHost，请先构建或安装浏览器宿主程序。");
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LightDl",
            "browser-host");
        Directory.CreateDirectory(dataDirectory);

        var chromiumManifestPath = Path.Combine(dataDirectory, "chromium.json");
        var firefoxManifestPath = Path.Combine(dataDirectory, "firefox.json");
        await WriteManifestAsync(chromiumManifestPath, new NativeMessagingManifest
        {
            Path = hostPath,
            AllowedOrigins = [$"chrome-extension://{ChromiumExtensionId}/"]
        });
        await WriteManifestAsync(firefoxManifestPath, new NativeMessagingManifest
        {
            Path = hostPath,
            AllowedExtensions = [FirefoxExtensionId]
        });

        if (OperatingSystem.IsWindows())
        {
            RegisterWindows(chromiumManifestPath, firefoxManifestPath);
            return;
        }

        foreach (var destination in GetChromiumManifestDestinations())
            CopyManifest(chromiumManifestPath, destination);
        foreach (var destination in GetFirefoxManifestDestinations())
            CopyManifest(firefoxManifestPath, destination);
    }

    public void Unregister()
    {
        if (OperatingSystem.IsWindows())
        {
            UnregisterWindows();
            return;
        }

        foreach (var destination in GetManifestDestinations())
        {
            if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    private static async Task WriteManifestAsync(string path, NativeMessagingManifest manifest)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            NativeMessagingManifestJsonContext.Default.NativeMessagingManifest);
    }

    private static void CopyManifest(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static IEnumerable<string> GetManifestDestinations()
    {
        return GetChromiumManifestDestinations().Concat(GetFirefoxManifestDestinations());
    }

    private static IEnumerable<string> GetChromiumManifestDestinations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "Google", "Chrome", "NativeMessagingHosts", HostName + ".json");
            yield return Path.Combine(home, "Library", "Application Support", "Microsoft Edge", "NativeMessagingHosts", HostName + ".json");
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return Path.Combine(home, ".config", "google-chrome", "NativeMessagingHosts", HostName + ".json");
            yield return Path.Combine(home, ".config", "chromium", "NativeMessagingHosts", HostName + ".json");
            yield return Path.Combine(home, ".config", "microsoft-edge", "NativeMessagingHosts", HostName + ".json");
        }
    }

    private static IEnumerable<string> GetFirefoxManifestDestinations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            yield return Path.Combine(home, "Library", "Application Support", "Mozilla", "NativeMessagingHosts", HostName + ".json");
        else if (OperatingSystem.IsLinux())
            yield return Path.Combine(home, ".mozilla", "native-messaging-hosts", HostName + ".json");
    }

    private static string? FindHostExecutable()
    {
        var executableName = OperatingSystem.IsWindows() ? "LightDl.BrowserHost.exe" : "LightDl.BrowserHost";
        var installedPath = Path.Combine(AppContext.BaseDirectory, executableName);
        if (File.Exists(installedPath))
            return Path.GetFullPath(installedPath);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDirectory = Path.Combine(directory.FullName, "LightDl.BrowserHost");
            if (Directory.Exists(projectDirectory))
            {
                return Directory.EnumerateFiles(projectDirectory, executableName, SearchOption.AllDirectories)
                    .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            }

            directory = directory.Parent;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsRegistered()
    {
        using var chrome = Registry.CurrentUser.OpenSubKey($"Software\\Google\\Chrome\\NativeMessagingHosts\\{HostName}");
        using var edge = Registry.CurrentUser.OpenSubKey($"Software\\Microsoft\\Edge\\NativeMessagingHosts\\{HostName}");
        using var firefox = Registry.CurrentUser.OpenSubKey($"Software\\Mozilla\\NativeMessagingHosts\\{HostName}");
        return chrome?.GetValue(null) is string && edge?.GetValue(null) is string && firefox?.GetValue(null) is string;
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string chromiumManifestPath, string firefoxManifestPath)
    {
        SetRegistryValue($"Software\\Google\\Chrome\\NativeMessagingHosts\\{HostName}", chromiumManifestPath);
        SetRegistryValue($"Software\\Microsoft\\Edge\\NativeMessagingHosts\\{HostName}", chromiumManifestPath);
        SetRegistryValue($"Software\\Mozilla\\NativeMessagingHosts\\{HostName}", firefoxManifestPath);
    }

    [SupportedOSPlatform("windows")]
    private static void SetRegistryValue(string keyPath, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(null, value);
    }

    [SupportedOSPlatform("windows")]
    private static void UnregisterWindows()
    {
        Registry.CurrentUser.DeleteSubKeyTree($"Software\\Google\\Chrome\\NativeMessagingHosts\\{HostName}", false);
        Registry.CurrentUser.DeleteSubKeyTree($"Software\\Microsoft\\Edge\\NativeMessagingHosts\\{HostName}", false);
        Registry.CurrentUser.DeleteSubKeyTree($"Software\\Mozilla\\NativeMessagingHosts\\{HostName}", false);
    }
}
