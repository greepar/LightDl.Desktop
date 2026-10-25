using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LightDl.UI.Services.Updates;

public sealed class DesktopUpdateService
{
    private readonly HttpClient _client;
    public string CurrentVersion { get; }
    public string RuntimeId { get; }
    public static string StatusFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightDl", "updates", "last-install.txt");

    public DesktopUpdateService(string currentVersion, HttpClient? client = null)
    {
        CurrentVersion = currentVersion;
        RuntimeId = GetRuntimeId();
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LightDl-Updater/" + currentVersion);
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string GetRuntimeId()
    {
        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64", Architecture.Arm64 => "arm64", Architecture.X86 => "x86",
            _ => throw new PlatformNotSupportedException("不支持此架构的自动更新。")
        };
        return platform + "-" + arch;
    }

    public async Task<UpdateRelease?> CheckAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _client.GetAsync(UpdateFeed.LatestUrl, timeout.Token);
        if ((int)response.StatusCode == 404) return null;
        response.EnsureSuccessStatusCode();
        return UpdateFeed.Parse(await response.Content.ReadAsStringAsync(timeout.Token), CurrentVersion, RuntimeId);
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Assembly.Location intentionally identifies an independently executable single-file publish.")]
    public string GetInstallTarget()
    {
        // A development DLL cannot be copied and executed as an independent helper.
        if (!string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location))
            throw new InvalidOperationException("请使用 Release 单文件版本进行自动安装；开发运行可检查更新。");
        string target;
        if (OperatingSystem.IsLinux())
            target = DesktopExecutable.AppImagePath ?? throw new InvalidOperationException("Linux 自动更新需要从 AppImage 启动。");
        else if (OperatingSystem.IsMacOS())
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (!directory.Name.EndsWith(".app", StringComparison.Ordinal) && directory.Parent is not null) directory = directory.Parent;
            target = directory.FullName;
            if (!target.EndsWith(".app", StringComparison.Ordinal) || target.StartsWith("/Volumes/", StringComparison.Ordinal) ||
                target.Contains("/AppTranslocation/", StringComparison.Ordinal))
                throw new InvalidOperationException("请先把 LightDl 拖入“应用程序”或 ~/Applications，再运行更新。");
        }
        else target = Environment.ProcessPath ?? throw new IOException("无法确定当前程序位置。");
        if ((File.GetAttributes(target) & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
            throw new IOException("程序路径是符号链接或只读，请移到当前用户可写的位置后重试。");
        return Path.GetFullPath(target);
    }

    public async Task<PreparedUpdate> PrepareAsync(UpdateRelease release, IProgress<double>? progress, CancellationToken ct)
    {
        var target = GetInstallTarget();
        var stage = Path.Combine(Path.GetDirectoryName(target)!, ".lightdl-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(identity.User ?? throw new IOException("无法确定当前用户。"),
                    FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(stage).Create(security);
            }
            else Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("程序所在目录不可写。请将程序移到当前用户可写的位置后重试。", ex);
        }
        var plan = new UpdateTransaction
        {
            Target = target, Stage = stage, Version = release.Version, ParentPid = Environment.ProcessId,
            IsBundle = OperatingSystem.IsMacOS()
        };
        var prepared = new PreparedUpdate(plan);
        try
        {
            if (UpdateFeed.ParseVersion(release.Version) <= UpdateFeed.ParseVersion(CurrentVersion))
                throw new InvalidDataException("更新版本必须高于当前版本。");
            var package = Path.Combine(stage, release.AssetName);
            await UpdateFeed.DownloadAsync(_client, release, package, progress, ct);
            if (plan.IsBundle) await MacUpdateBundle.StageAsync(package, plan, ct);
            else if (OperatingSystem.IsWindows())
            {
                await ExtractWindowsExecutableAsync(package, plan.Candidate, ct);
                new FileInfo(plan.Candidate).SetAccessControl(new FileInfo(target).GetAccessControl(AccessControlSections.Access));
            }
            else
            {
                File.Move(package, plan.Candidate);
                File.SetUnixFileMode(plan.Candidate, File.GetUnixFileMode(target) | UnixFileMode.UserExecute);
            }
            await VerifyExecutableAsync(plan.Executable, RuntimeInformation.ProcessArchitecture, ct);
            plan.ExecutableHash = await UpdateTransaction.HashAsync(plan.Executable, ct);
            var helper = Path.Combine(stage, OperatingSystem.IsWindows() ? "updater.exe" : "updater");
            File.Copy(Environment.ProcessPath!, helper, overwrite: false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            plan.Validate();
            plan.Save();
            File.Delete(package);
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }

    public static async Task ExtractWindowsExecutableAsync(string archive, string destination, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(archive);
        var entries = zip.Entries.Where(e => e.FullName == "LightDl.Desktop.exe").ToArray();
        if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > UpdateFeed.MaximumPackageSize)
            throw new InvalidDataException("Windows 更新包中没有唯一有效的 LightDl.Desktop.exe。");
        await using var input = entries[0].Open();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int length;
        while ((length = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + length > entries[0].Length)
                throw new InvalidDataException("解压数据超出预期大小。");
            await output.WriteAsync(buffer.AsMemory(0, length), ct);
        }
        if (output.Length != entries[0].Length) throw new InvalidDataException("解压后的程序大小无效。");
    }

    public static async Task VerifyExecutableAsync(string path, Architecture architecture, CancellationToken ct)
    {
        // Read only the headers; avoid loading an untrusted executable into the current process.
        await using var file = File.OpenRead(path);
        var header = new byte[64];
        await file.ReadExactlyAsync(header, ct);
        if (OperatingSystem.IsWindows())
        {
            if (header[0] != 'M' || header[1] != 'Z') throw new InvalidDataException("更新程序不是 Windows EXE。");
            var offset = BitConverter.ToInt32(header, 60);
            if (offset < 64 || offset > file.Length - 6) throw new InvalidDataException("EXE 文件头无效。");
            file.Position = offset;
            var pe = new byte[6];
            await file.ReadExactlyAsync(pe, ct);
            var machine = architecture switch { Architecture.X64 => 0x8664, Architecture.X86 => 0x14c, Architecture.Arm64 => 0xaa64, _ => 0 };
            if (pe[0] != 'P' || pe[1] != 'E' || pe[2] != 0 || pe[3] != 0 || BitConverter.ToUInt16(pe, 4) != machine)
                throw new InvalidDataException("EXE 架构与当前进程不匹配。");
        }
        else if (OperatingSystem.IsLinux())
        {
            if (!header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }) || header[4] != 2 || header[5] != 1 ||
                BitConverter.ToUInt16(header, 18) != (architecture == Architecture.Arm64 ? 183 : 62) ||
                !header.AsSpan(8, 3).SequenceEqual(new byte[] { 0x41, 0x49, 2 }))
                throw new InvalidDataException("AppImage 文件头或架构无效。");
        }
    }

    public void NotifyReady(string? readyFile)
    {
        if (readyFile is null || Path.GetFileName(readyFile) != "ready") return;
        var stage = Path.GetDirectoryName(readyFile)!;
        try
        {
            var plan = UpdateTransaction.Load(Path.Combine(stage, "transaction.json"));
            plan.Validate();
            if (plan.Target == GetInstallTarget() && plan.Version == CurrentVersion && readyFile == plan.ReadyFile)
            {
                var temporary = Path.Combine(stage, "ready-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(temporary, CurrentVersion);
                File.Move(temporary, readyFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        { UpdateInstaller.TryWriteStatus("新版启动确认失败：" + ex.Message); }
    }

    public void CleanupCompletedUpdates()
    {
        try
        {
            var target = GetInstallTarget();
            foreach (var stage in Directory.EnumerateDirectories(Path.GetDirectoryName(target)!, ".lightdl-update-*"))
            {
                try
                {
                    var plan = UpdateTransaction.Load(Path.Combine(stage, "transaction.json"));
                    plan.Validate();
                    if (plan.Stage == stage && plan.Target == target &&
                        File.Exists(Path.Combine(stage, "completed")) &&
                        UpdateFeed.ParseVersion(plan.Version) <= UpdateFeed.ParseVersion(CurrentVersion))
                        Directory.Delete(stage, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }
}

public sealed class PreparedUpdate(UpdateTransaction plan) : IDisposable
{
    private bool _started;
    public async Task StartInstallerAsync()
    {
        File.Delete(Path.Combine(plan.Stage, "helper-ready"));
        var info = new ProcessStartInfo(Path.Combine(plan.Stage, OperatingSystem.IsWindows() ? "updater.exe" : "updater"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = plan.Stage };
        info.ArgumentList.Add("--apply-update");
        info.ArgumentList.Add(plan.PlanFile);
        foreach (var name in new[] { "APPIMAGE", "APPDIR", "ARGV0", "OWD" }) info.Environment.Remove(name);
        using var process = Process.Start(info) ?? throw new IOException("无法启动更新安装程序。");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(plan.Stage, "helper-ready")))
            {
                if (process.HasExited) throw new IOException("更新辅助进程未能初始化。");
                if (DateTime.UtcNow > deadline) throw new TimeoutException("更新辅助进程初始化超时。");
                await Task.Delay(100);
            }
            _started = true;
        }
        catch
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
    }

    public void Dispose()
    {
        if (_started) return;
        try { Directory.Delete(plan.Stage, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
