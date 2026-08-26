using System.Text.Json;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

public sealed class DownloadTaskStore
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _tasksPath;

    public DownloadTaskStore()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LightDl");
        _tasksPath = Path.Combine(dataDirectory, "downloads.json");
    }

    public IReadOnlyList<DownloadTaskRecord> Load()
    {
        try
        {
            if (!File.Exists(_tasksPath))
                return [];

            using var stream = File.OpenRead(_tasksPath);
            return JsonSerializer.Deserialize(stream, DownloadTaskJsonContext.Default.ListDownloadTaskRecord) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<DownloadTaskRecord> tasks)
    {
        var snapshot = tasks.ToList();
        await _saveLock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_tasksPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _tasksPath + ".tmp";

            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using (var stream = new FileStream(temporaryPath, streamOptions))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    DownloadTaskJsonContext.Default.ListDownloadTaskRecord);
            }

            File.Move(temporaryPath, _tasksPath, true);
            RestrictTaskFileAccess();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void RestrictTaskFileAccess()
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(_tasksPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
