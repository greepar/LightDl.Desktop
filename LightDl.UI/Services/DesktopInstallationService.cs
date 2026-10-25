using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Platform;

namespace LightDl.UI.Services;

public sealed class DesktopInstallationService
{
    public static DesktopInstallationService? Current { get; set; }
    public string SourcePath { get; }
    public string InstallPath { get; private set; }
    public string? RestartPath { get; private set; }
    private readonly string _choicePath;
    private readonly string _dataDirectory;

    public DesktopInstallationService(string sourcePath, string? dataDirectory = null)
    {
        SourcePath = Path.GetFullPath(sourcePath);
        _dataDirectory = dataDirectory ?? GetDataDirectory();
        InstallPath = Path.Combine(_dataDirectory, OperatingSystem.IsWindows() ? "LightDl.Desktop.exe" : "LightDl.AppImage");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SourcePath)));
        _choicePath = Path.Combine(_dataDirectory, "first-run", hash + ".choice");
        var choice = ReadChoice();
        if (choice.StartsWith("installed\n", StringComparison.Ordinal))
        {
            var remembered = choice[10..];
            if (Path.IsPathFullyQualified(remembered) && remembered.IndexOfAny(['\r', '\n']) < 0)
                InstallPath = remembered;
        }
    }

    public bool NeedsChoice => !File.Exists(_choicePath);
    public bool IsInstalledCopy => ReadChoice().StartsWith("installed", StringComparison.Ordinal) && string.Equals(SourcePath, InstallPath,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private string ReadChoice()
    {
        try { return File.Exists(_choicePath) ? File.ReadAllText(_choicePath) : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    public static string ResolveInstallPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || directory.IndexOfAny(['\r', '\n', '\t']) >= 0)
            throw new IOException(L.T("请选择有效的绝对安装目录。"));
        return Path.Combine(Path.GetFullPath(directory), OperatingSystem.IsWindows() ? "LightDl.Desktop.exe" : "LightDl.AppImage");
    }

    public void ChoosePortable() => SaveChoice("portable");

    private void SaveChoice(string choice)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_choicePath)!);
        File.WriteAllText(_choicePath, choice);
    }

    public async Task InstallAsync(string? installationDirectory = null, bool rememberChoice = true,
        bool createDesktopShortcut = true, bool createStartMenuShortcut = true)
    {
        if (installationDirectory is not null) InstallPath = ResolveInstallPath(installationDirectory);
        RestartPath = null;
        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(InstallPath)!);
        var samePath = string.Equals(SourcePath, InstallPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var copied = false;
        try
        {
            if (!samePath)
            {
                if (File.Exists(InstallPath))
                    throw new IOException(L.T("安装位置已有 LightDl。请先运行已安装版本进行更新，或移走旧文件后重试。"));
                var temporary = InstallPath + "." + Guid.NewGuid().ToString("N") + ".install";
                try
                {
                    await using (var input = File.OpenRead(SourcePath))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await input.CopyToAsync(output);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    File.Move(temporary, InstallPath);
                    copied = true;
                }
                finally { File.Delete(temporary); }
            }

            if (OperatingSystem.IsLinux())
            {
                using var icon = AssetLoader.Open(new Uri("avares://LightDl.UI/Assets/lightdl-light.png"));
                await IntegrateLinuxAsync(InstallPath, _dataDirectory, icon);
            }
            else if (OperatingSystem.IsWindows()) await IntegrateWindowsAsync(InstallPath,
                createDesktopShortcut: createDesktopShortcut, createStartMenuShortcut: createStartMenuShortcut);
            else throw new PlatformNotSupportedException();

            // Mark both paths: neither the old download nor the installed app should prompt repeatedly.
            var installed = new DesktopInstallationService(InstallPath, _dataDirectory);
            installed.SaveChoice("installed\n" + InstallPath);
            if (rememberChoice) SaveChoice("installed\n" + InstallPath);
            if (!samePath) RestartPath = InstallPath;
        }
        catch
        {
            // The destination was created by this attempt; leave the original download untouched.
            if (copied)
            {
                File.Delete(InstallPath);
                RestartPath = null;
            }
            throw;
        }
    }

    private static string GetDataDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightDl");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var root = !string.IsNullOrEmpty(xdg) && Path.IsPathFullyQualified(xdg) ? xdg :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "lightdl");
    }

    public static async Task IntegrateLinuxAsync(string executable, string dataDirectory, Stream icon)
    {
        var root = Path.GetDirectoryName(dataDirectory)!;
        var iconPath = Path.Combine(root, "icons", "hicolor", "512x512", "apps", "lightdl.png");
        var desktopPath = Path.Combine(root, "applications", "io.lightdl.LightDl.desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
        await using (var output = File.Create(iconPath)) await icon.CopyToAsync(output);
        await File.WriteAllTextAsync(desktopPath, CreateLinuxDesktopEntry(executable, iconPath));
        // Cache tools are optional. A missing cache command must not prevent the installed app running.
        await TryRefreshAsync("update-desktop-database", Path.GetDirectoryName(desktopPath)!);
        await TryRefreshAsync("gtk-update-icon-cache", "-f", "-t", Path.Combine(root, "icons", "hicolor"));
    }

    public static string CreateLinuxDesktopEntry(string executable, string iconPath)
    {
        static string Value(string text) => text.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        static string Exec(string text)
        {
            if (text.IndexOfAny(['\n', '\r', '\t']) >= 0) throw new IOException(L.T("程序路径不能包含换行或制表符。"));
            // Desktop Entry string escaping is applied after Exec argument quoting.
            var quoted = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
            return "\"" + quoted.Replace("\\", "\\\\") + "\"";
        }
        return $"""
[Desktop Entry]
Type=Application
Name=LightDl
Comment=LightDl Download Manager
Exec={Exec(executable)} %u
Icon={Value(iconPath)}
StartupWMClass=LightDl.Desktop
Terminal=false
MimeType=x-scheme-handler/lightdl;
Categories=Network;FileTransfer;

""";
    }

    public static Task IntegrateWindowsAsync(string executable, string? programsDirectory = null, string? desktopDirectory = null,
        bool createDesktopShortcut = true, bool createStartMenuShortcut = true)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (createStartMenuShortcut)
        {
            var programs = programsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            WindowsShortcut.Create(Path.Combine(programs, "LightDl", "LightDl.lnk"), executable);
        }
        if (createDesktopShortcut)
        {
            var desktop = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) throw new IOException(L.T("无法确定桌面目录。"));
            WindowsShortcut.Create(Path.Combine(desktop, "LightDl.lnk"), executable);
        }
        return Task.CompletedTask;
    }

    private static async Task TryRefreshAsync(string executable, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null) return;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); }
            await output; await error;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { }
    }

    public void RelaunchInstalled(IReadOnlyList<string> arguments)
    {
        if (RestartPath is null) return;
        var info = new ProcessStartInfo(RestartPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(RestartPath)! };
        foreach (var argument in arguments.Where(argument => argument != "--silent")) info.ArgumentList.Add(argument);
        foreach (var name in new[] { "APPIMAGE", "APPDIR", "ARGV0", "OWD" }) info.Environment.Remove(name);
        Process.Start(info)?.Dispose();
    }
}
