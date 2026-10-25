using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LightDl.UI.Services.Updates;

public sealed record UpdateRelease(string Version, string Notes, string AssetName, string Url, long Size, string Sha256);

public static class UpdateFeed
{
    public const string Repository = "https://github.com/greepar/LightDl.Desktop";
    public const string LatestUrl = "https://api.github.com/repos/greepar/LightDl.Desktop/releases/latest";
    public const long MaximumPackageSize = 512L * 1024 * 1024;

    public static Version ParseVersion(string text)
    {
        if (!Regex.IsMatch(text, @"^\d+\.\d+\.\d+$") || !Version.TryParse(text, out var version))
            throw new InvalidDataException(L.T("版本号不是有效的正式版本。"));
        return version;
    }

    public static UpdateRelease? Parse(string json, string currentVersion, string rid)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException(L.T("更新源不是正式 Release。"));
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        if (ParseVersion(version) <= ParseVersion(currentVersion)) return null;
        var extension = rid.StartsWith("win-", StringComparison.Ordinal) ? "zip"
            : rid.StartsWith("osx-", StringComparison.Ordinal) ? "dmg" : "AppImage";
        if (rid is not ("win-x86" or "win-x64" or "win-arm64" or "linux-x64" or "linux-arm64" or "osx-x64" or "osx-arm64"))
            throw new PlatformNotSupportedException(L.T("此平台暂不支持自动更新。"));
        var name = $"lightdl-desktop-{rid}.{extension}";
        var matches = root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException(L.F($"Release 尚未提供 {name}，请稍后重试。"));
        var asset = matches[0];
        var size = asset.GetProperty("size").GetInt64();
        var digest = asset.GetProperty("digest").GetString() ?? "";
        var url = $"{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{name}";
        if (asset.GetProperty("state").GetString() != "uploaded" || size <= 0 || size > MaximumPackageSize ||
            !Regex.IsMatch(digest, "^sha256:[a-f0-9]{64}$") || asset.GetProperty("browser_download_url").GetString() != url)
            throw new InvalidDataException(L.T("更新包大小、校验值或下载地址无效，已拒绝更新。"));
        return new UpdateRelease(version, root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            name, url, size, digest[7..]);
    }

    public static async Task DownloadAsync(HttpClient client, UpdateRelease release, string destination,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } size && size != release.Size)
            throw new InvalidDataException(L.T("更新包大小与 Release 不匹配。"));
        var temporary = destination + ".download";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                long received = 0;
                var buffer = new byte[81920];
                var lastReport = Environment.TickCount64;
                int length;
                while ((length = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    received += length;
                    if (received > release.Size) throw new InvalidDataException(L.T("更新包超出预期大小。"));
                    hash.AppendData(buffer, 0, length);
                    await output.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
                    if (Environment.TickCount64 - lastReport > 150 || received == release.Size)
                    {
                        progress?.Report(received * 100d / release.Size);
                        lastReport = Environment.TickCount64;
                    }
                }
                if (received != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(L.T("更新包 SHA256 或大小校验失败，原程序未修改。"));
            }
            File.Move(temporary, destination);
        }
        finally { File.Delete(temporary); }
    }
}
