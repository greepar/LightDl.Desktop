using System.Text.Json;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

public sealed class AppSettingsService
{
    private readonly string _settingsPath;

    public AppSettingsService()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LightDl");
        _settingsPath = Path.Combine(dataDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                using var stream = File.OpenRead(_settingsPath);
                if (JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings) is { } settings)
                    return Normalize(settings);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Normalize(new AppSettings());
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings = Normalize(settings);
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                AppSettingsJsonContext.Default.AppSettings,
                cancellationToken);
        }

        File.Move(temporaryPath, _settingsPath, true);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Version = 1;
        settings.DefaultDownloadDirectory = string.IsNullOrWhiteSpace(settings.DefaultDownloadDirectory)
            ? GetDefaultDownloadDirectory()
            : settings.DefaultDownloadDirectory.Trim();
        settings.MaxConcurrentDownloads = Math.Clamp(settings.MaxConcurrentDownloads, 1, 16);
        settings.ChunkCount = Math.Clamp(settings.ChunkCount, 1, 64);
        settings.MinChunkCount = Math.Clamp(settings.MinChunkCount, 1, 64);
        settings.MaxChunkCount = Math.Clamp(settings.MaxChunkCount, settings.MinChunkCount, 64);
        settings.ChunkCount = Math.Clamp(settings.ChunkCount, settings.MinChunkCount, settings.MaxChunkCount);
        settings.SegmentSizeMb = Math.Clamp(settings.SegmentSizeMb, 1, 256);
        settings.SpeedLimitKib = Math.Clamp(settings.SpeedLimitKib, 0, 1024 * 1024);
        settings.TimeoutSeconds = Math.Clamp(settings.TimeoutSeconds, 5, 600);
        settings.MaxRetry = Math.Clamp(settings.MaxRetry, 0, 100);
        settings.FileConflictPolicy = Math.Clamp(settings.FileConflictPolicy, 0, 3);
        settings.ProxyMode = Math.Clamp(settings.ProxyMode, 0, 2);
        settings.ThemeMode = Math.Clamp(settings.ThemeMode, 0, 2);
        settings.ProxyUrl = settings.ProxyUrl.Trim();
        settings.UserAgent = settings.UserAgent.Trim();
        return settings;
    }

    private static string GetDefaultDownloadDirectory()
    {
        if (DownloadPlatform.DefaultDownloadDirectoryFactory?.Invoke() is { Length: > 0 } platformDirectory)
            return platformDirectory;

        var root = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(root, "Downloads");
    }
}
