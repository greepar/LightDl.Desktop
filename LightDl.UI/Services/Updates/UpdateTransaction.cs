using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LightDl.UI.Services.Updates;

public sealed class UpdateTransaction
{
    public string Target { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Version { get; set; } = "";
    public string ExecutableHash { get; set; } = "";
    public int ParentPid { get; set; }
    public bool IsBundle { get; set; }
    public string Candidate => Path.Combine(Stage, IsBundle ? "Next.app" : "Next");
    public string Backup => Path.Combine(Stage, IsBundle ? "Previous.app" : "Previous");
    public string Executable => IsBundle ? Path.Combine(Candidate, "Contents/MacOS/LightDl.Desktop") : Candidate;
    public string ReadyFile => Path.Combine(Stage, "ready");
    public string PlanFile => Path.Combine(Stage, "transaction.json");

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(Target) || !Path.IsPathFullyQualified(Stage) ||
            !Regex.IsMatch(Path.GetFileName(Stage), @"^\.lightdl-update-[a-f0-9]{32}$") ||
            Path.GetDirectoryName(Path.GetFullPath(Stage)) != Path.GetDirectoryName(Path.GetFullPath(Target)) ||
            !Regex.IsMatch(ExecutableHash, "^[A-Fa-f0-9]{64}$") || ParentPid <= 0)
            throw new InvalidDataException("更新事务路径无效。");
        UpdateFeed.ParseVersion(Version);
        if (IsBundle && !Target.EndsWith(".app", StringComparison.Ordinal))
            throw new InvalidDataException("更新目标不是应用包。");
        foreach (var path in new[] { Stage, Target, Candidate, Backup })
            if (File.Exists(path) || Directory.Exists(path))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("更新路径不能是符号链接。");
    }

    public static async Task<string> HashAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    public static UpdateTransaction Load(string path) =>
        JsonSerializer.Deserialize(File.ReadAllText(path), UpdateJsonContext.Default.UpdateTransaction)
        ?? throw new InvalidDataException("更新事务不存在。");

    public void Save() => File.WriteAllText(PlanFile,
        JsonSerializer.Serialize(this, UpdateJsonContext.Default.UpdateTransaction));
}

[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(UpdateTransaction))]
internal partial class UpdateJsonContext : JsonSerializerContext;

public static class UpdateInstaller
{
    // Runs before Avalonia initializes, from a copy of the old NativeAOT executable.
    public static async Task<int> RunAsync(string planFile)
    {
        UpdateTransaction? plan = null;
        try
        {
            plan = UpdateTransaction.Load(planFile);
            plan.Validate();
            File.WriteAllText(Path.Combine(plan.Stage, "helper-ready"), "");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                using var parent = Process.GetProcessById(plan.ParentPid);
                // Waiting for a reused PID is safer than replacing while an old process still runs.
                await parent.WaitForExitAsync(timeout.Token);
            }
            catch (ArgumentException) { } // Parent has already exited.
            await ApplyAsync(plan, LaunchAsync, timeout.Token, TryWriteStatus);
            return 0;
        }
        catch (Exception ex)
        {
            TryWriteStatus($"更新未完成：{ex.Message}\n事务目录：{plan?.Stage}");
            return 1;
        }
    }

    // An injected launcher lets tests exercise the real filesystem transaction and rollback.
    public static async Task ApplyAsync(UpdateTransaction plan,
        Func<UpdateTransaction, bool, CancellationToken, Task> launch, CancellationToken ct, Action<string>? report = null)
    {
        plan.Validate();
        if (File.Exists(plan.Backup) || Directory.Exists(plan.Backup))
            throw new IOException("备份已存在，已取消替换。");
        var oldMoved = false;
        var newMoved = false;
        try
        {
            if (!string.Equals(await UpdateTransaction.HashAsync(plan.Executable, ct), plan.ExecutableHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("待安装程序的校验值发生变化。");
            if (plan.IsBundle) await MacUpdateBundle.VerifyAsync(plan.Candidate, plan.Target, plan.Version, ct);
            await MoveWithRetryAsync(plan.Target, plan.Backup, plan.IsBundle, ct);
            oldMoved = true;
            await MoveWithRetryAsync(plan.Candidate, plan.Target, plan.IsBundle, ct);
            newMoved = true;
            await launch(plan, false, ct);
        }
        catch (Exception error)
        {
            try
            {
                // Rollback gets its own timeout, even if installation was cancelled/timed out.
                using var rollbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                if (newMoved) await MoveWithRetryAsync(plan.Target, plan.Candidate, plan.IsBundle, rollbackTimeout.Token);
                if (oldMoved) await MoveWithRetryAsync(plan.Backup, plan.Target, plan.IsBundle, rollbackTimeout.Token);
                report?.Invoke($"更新失败，旧版本已恢复：{error.Message}");
                if (File.Exists(plan.Target) || Directory.Exists(plan.Target))
                    await launch(plan, true, rollbackTimeout.Token);
            }
            catch (Exception rollbackError)
            {
                throw new IOException($"更新失败；自动恢复未完成，请从 {plan.Backup} 恢复程序。{rollbackError.Message}", error);
            }
            throw new IOException("旧版本已保留或恢复：" + error.Message, error);
        }
        // Once the new application is ready, a bookkeeping failure must not replace a running app.
        try { File.WriteAllText(Path.Combine(plan.Stage, "completed"), plan.Version); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report?.Invoke($"已启动 {plan.Version}，旧版备份保留在 {plan.Backup}：{ex.Message}");
            return;
        }
        report?.Invoke($"已更新到 {plan.Version}。");
        // Keep the backup until a later application startup cleans this completed transaction.
    }

    private static async Task MoveWithRetryAsync(string source, string target, bool directory, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (directory) Directory.Move(source, target);
                else File.Move(source, target);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(300, ct); }
        }
    }

    private static async Task LaunchAsync(UpdateTransaction plan, bool rollback, CancellationToken ct)
    {
        var executable = plan.IsBundle ? Path.Combine(plan.Target, "Contents/MacOS/LightDl.Desktop") : plan.Target;
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(plan.Target)! };
        // Do not pass the old AppImage mount/environment to a newly mounted image or rollback.
        foreach (var name in new[] { "APPIMAGE", "APPDIR", "ARGV0", "OWD" }) info.Environment.Remove(name);
        if (!rollback)
        {
            File.Delete(plan.ReadyFile);
            info.ArgumentList.Add("--update-ready=" + plan.ReadyFile);
        }
        using var process = Process.Start(info) ?? throw new IOException("无法启动更新后的程序。");
        if (rollback) return;
        var ready = false;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(plan.ReadyFile) && File.ReadAllText(plan.ReadyFile) == plan.Version)
                {
                    ready = true;
                    return;
                }
                if (process.HasExited) throw new IOException("新版程序在启动完成前退出。");
                await Task.Delay(200, ct);
            }
            throw new TimeoutException("新版程序未能完成启动。");
        }
        finally
        {
            if (!ready && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    public static void TryWriteStatus(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesktopUpdateService.StatusFile)!);
            File.WriteAllText(DesktopUpdateService.StatusFile, text);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
