using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LightDl.UI.Services.Updates;

public static class MacUpdateBundle
{
    public static async Task StageAsync(string dmg, UpdateTransaction plan, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var mount = Path.Combine(plan.Stage, "volume");
        Directory.CreateDirectory(mount);
        var mounted = false;
        try
        {
            await ExecuteAsync("/usr/bin/hdiutil", ct, "attach", "-readonly", "-nobrowse", "-noautoopen", "-mountpoint", mount, dmg);
            mounted = true;
            var bundle = Path.Combine(mount, "LightDl.app");
            await VerifyAsync(bundle, plan.Target, plan.Version, ct);
            // ditto preserves the complete signature/resources; never overwrite only Contents/MacOS.
            await ExecuteAsync("/usr/bin/ditto", ct, bundle, plan.Candidate);
            await VerifyAsync(plan.Candidate, plan.Target, plan.Version, ct);
        }
        finally
        {
            if (mounted) await ExecuteAsync("/usr/bin/hdiutil", CancellationToken.None, "detach", mount);
        }
    }

    public static async Task VerifyAsync(string candidate, string current, string version, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (!Directory.Exists(candidate) || (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(L.T("更新包中没有有效的 LightDl.app。"));
        await ExecuteAsync("/usr/bin/codesign", ct, "--verify", "--deep", "--strict", candidate);
        async Task<string> Plist(string app, string key) =>
            (await ExecuteAsync("/usr/libexec/PlistBuddy", ct, "-c", "Print :" + key, Path.Combine(app, "Contents/Info.plist"))).Output.Trim();
        if (await Plist(current, "CFBundleIdentifier") != "io.lightdl.LightDl" ||
            await Plist(candidate, "CFBundleIdentifier") != "io.lightdl.LightDl" ||
            await Plist(candidate, "CFBundleShortVersionString") != version ||
            await Plist(candidate, "CFBundleExecutable") != "LightDl.Desktop")
            throw new InvalidDataException(L.T("新版应用的标识、版本或可执行文件不匹配。"));
        await ExecuteAsync("/usr/bin/lipo", ct, Path.Combine(candidate, "Contents/MacOS/LightDl.Desktop"),
            "-verify_arch", RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64");
        var oldSignature = (await ExecuteAsync("/usr/bin/codesign", ct, "-dv", "--verbose=4", current)).Error;
        var signature = (await ExecuteAsync("/usr/bin/codesign", ct, "-dv", "--verbose=4", candidate)).Error;
        var oldTeam = Regex.Match(oldSignature, @"^TeamIdentifier=(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
        var newTeam = Regex.Match(signature, @"^TeamIdentifier=(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
        if (!string.IsNullOrEmpty(oldTeam) && oldTeam != "not set")
        {
            if (newTeam != oldTeam || signature.Contains("Signature=adhoc", StringComparison.Ordinal))
                throw new InvalidDataException(L.T("更新应用的签名团队发生变化。"));
        }
        else if (!oldSignature.Contains("Signature=adhoc", StringComparison.Ordinal) || !signature.Contains("CDHash=", StringComparison.Ordinal))
            throw new InvalidDataException(L.T("无法验证应用签名。"));
    }

    private static async Task<(string Output, string Error)> ExecuteAsync(string tool, CancellationToken ct, params string[] arguments)
    {
        var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException(L.T("无法运行 ") + tool);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var result = (Output: await output, Error: await error);
        if (process.ExitCode != 0) throw new IOException(L.F($"{Path.GetFileName(tool)} 失败：{result.Error.Trim()}"));
        return result;
    }
}
