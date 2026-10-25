using System.IO.Compression;

namespace LightDl.UI.Services;

public sealed record BrowserExtensionExport(string Directory, string Archive, string Manifest);

public static class BrowserExtensionBundle
{
    public static async Task<BrowserExtensionExport> ExportAsync(string browser, string downloadsDirectory)
    {
        if (browser is not ("chrome" or "edge" or "firefox")) throw new ArgumentException("Unknown browser", nameof(browser));
        if (!Path.IsPathFullyQualified(downloadsDirectory)) throw new IOException(L.T("请选择有效的下载目录。"));
        Directory.CreateDirectory(downloadsDirectory);
        var bundle = browser == "firefox" ? "firefox" : "chromium";
        var assembly = typeof(BrowserExtensionBundle).Assembly;
        var prefix = "LightDl.Extensions." + bundle + "/";
        var resources = assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order().ToArray();
        if (!resources.Contains(prefix + "manifest.json")) throw new IOException(L.T("内置扩展文件不完整。"));
        // A unique directory keeps previously exported extensions and browser-loaded files intact.
        var directory = Path.Combine(downloadsDirectory, "LightDl-" + browser + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "lightdl-automatic-" + bundle + ".zip");
        var unpacked = Path.Combine(directory, "extension");
        try
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var name in resources)
            {
                var relative = name[prefix.Length..].Replace('\\', '/');
                var destination = Path.GetFullPath(Path.Combine(unpacked, relative));
                if (!destination.StartsWith(unpacked + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException(L.T("内置扩展路径无效。"));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = assembly.GetManifestResourceStream(name)!;
                await using (var output = File.Create(destination)) await input.CopyToAsync(output);
                zip.CreateEntryFromFile(destination, relative);
            }
            return new BrowserExtensionExport(unpacked, archive, Path.Combine(unpacked, "manifest.json"));
        }
        catch { Directory.Delete(directory, recursive: true); throw; }
    }
}
