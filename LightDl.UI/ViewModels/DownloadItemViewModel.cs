using System.Net;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using LightDl.UI.Models;
using LightDl.UI.Services;

namespace LightDl.UI.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    private readonly string _destinationDirectory;
    private readonly AppSettings _settings;
    private readonly DateTimeOffset _createdAt;
    private readonly string? _requestedFileName;
    private readonly IReadOnlyDictionary<string, string>? _headers;
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource? _downloadCompletion;
    private double _displaySpeed;
    private long _lastSpeedUpdateTimestamp;
    private long _speedDropStartedTimestamp;
    private LightDownloadProgress? _pendingProgress;
    private bool _uiUpdatesEnabled = true;

    public DownloadItemViewModel(
        string sourceUrl,
        string destinationDirectory,
        AppSettings settings,
        string? requestedFileName = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        Id = Guid.NewGuid();
        SourceUrl = sourceUrl;
        _destinationDirectory = destinationDirectory;
        _settings = settings;
        _createdAt = DateTimeOffset.UtcNow;
        _requestedFileName = string.IsNullOrWhiteSpace(requestedFileName) ? null : Path.GetFileName(requestedFileName);
        _headers = headers;
        FileName = _requestedFileName ?? GetInitialFileName(sourceUrl);
    }

    public DownloadItemViewModel(DownloadTaskRecord record, AppSettings settings)
    {
        Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
        SourceUrl = record.SourceUrl;
        _destinationDirectory = record.DestinationDirectory;
        _settings = settings;
        _createdAt = record.CreatedAt == default ? DateTimeOffset.UtcNow : record.CreatedAt;
        _requestedFileName = record.RequestedFileName;
        FileName = string.IsNullOrWhiteSpace(record.FileName) ? GetInitialFileName(record.SourceUrl) : record.FileName;
        FilePath = record.FilePath;
        SizeText = record.SizeText;
        ProgressPercentage = record.ProgressPercentage;
        State = record.State is DownloadState.Downloading or DownloadState.Queued
            ? DownloadState.Paused
            : record.State;
        StatusText = State switch
        {
            DownloadState.Completed => "已完成",
            DownloadState.Failed => "下载失败",
            DownloadState.Paused => "已暂停",
            _ => "等待中"
        };
        RemainingText = State == DownloadState.Completed ? "已完成" : "--";
    }

    public event EventHandler? RemoveRequested;

    public event EventHandler? StartRequested;

    public Guid Id { get; }

    public string SourceUrl { get; }

    public string DestinationDirectory => _destinationDirectory;

    [ObservableProperty]
    private string _fileName;

    [ObservableProperty]
    private string _statusText = "等待中";

    [ObservableProperty]
    private string _sizeText = "未知大小";

    [ObservableProperty]
    private string _speedText = "--";

    [ObservableProperty]
    private string _remainingText = "--";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private DownloadState _state = DownloadState.Queued;

    public bool IsActive => State == DownloadState.Downloading;

    public bool CanResume => State is DownloadState.Paused or DownloadState.Failed;

    public bool IsCompleted => State == DownloadState.Completed;

    public bool HasLocalFile => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    public bool CanDeleteLocalFile => !IsCompleted || GetDownloadFilePaths().Any(File.Exists);

    public bool CanOpenFile => IsCompleted && HasLocalFile;

    public bool CanRestart => State is DownloadState.Completed or DownloadState.Failed;

    public string DetailsSavePath => FilePath ?? _destinationDirectory;

    [ObservableProperty]
    private bool _isDetailsVisible;

    [ObservableProperty]
    private bool _isSelected;

    public async Task StartAsync()
    {
        if (State == DownloadState.Downloading)
            return;

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloadCompletion = completion;
        ResetDisplayedSpeed();
        State = DownloadState.Downloading;
        StatusText = "正在下载";

        try
        {
            Directory.CreateDirectory(_destinationDirectory);

            using var downloader = new LightDownloader(CreateDownloadConfig());

            var request = (_requestedFileName is { Length: > 0 }
                    ? LightDownloadRequest.ToFile(SourceUrl, Path.Combine(_destinationDirectory, _requestedFileName), _headers)
                    : LightDownloadRequest.ToDirectory(SourceUrl, _destinationDirectory, _headers))
                .OnFileInfo(info =>
                {
                    FileName = info.FileName;
                    SizeText = FormatBytes(info.Size);
                });

            var progress = new Progress<LightDownloadProgress>(UpdateProgress);
            var result = await downloader.DownloadAsync(request, progress, cancellationToken: _cancellation.Token);

            FileName = result.FileName;
            FilePath = result.FilePath;
            ProgressPercentage = 100;
            SpeedText = "--";
            RemainingText = "已完成";
            StatusText = result.Skipped ? "已存在" : "已完成";
            State = DownloadState.Completed;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            SpeedText = "--";
            RemainingText = "--";
            StatusText = "已暂停";
            State = DownloadState.Paused;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            SpeedText = "--";
            RemainingText = "--";
            StatusText = ex.Message;
            State = DownloadState.Failed;
        }
        finally
        {
            completion.TrySetResult();
            if (ReferenceEquals(_downloadCompletion, completion))
                _downloadCompletion = null;
        }
    }

    [RelayCommand]
    private void Pause()
    {
        if (State == DownloadState.Downloading)
            _cancellation?.Cancel();
    }

    [RelayCommand]
    private void Resume()
    {
        if (CanResume)
            StartRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Remove()
    {
        _cancellation?.Cancel();
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RemoveWithFileAsync()
    {
        var downloadFiles = GetDownloadFilePaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
        _cancellation?.Cancel();

        if (_downloadCompletion is { } completion)
            await completion.Task;

        downloadFiles.UnionWith(GetDownloadFilePaths());

        foreach (var path in downloadFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleDetails()
    {
        IsDetailsVisible = !IsDetailsVisible;
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (CanOpenFile)
            Process.Start(new ProcessStartInfo(FilePath!) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = !string.IsNullOrWhiteSpace(FilePath) ? FilePath : _destinationDirectory;
        var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{folder}\"") { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", File.Exists(path) ? $"-R \"{path}\"" : $"\"{folder}\"") { UseShellExecute = false });
        else if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{folder}\"") { UseShellExecute = false });
    }

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        await CopyTextAsync(SourceUrl);
    }

    [RelayCommand]
    private async Task CopyFileNameAsync()
    {
        await CopyTextAsync(FileName);
    }

    [RelayCommand]
    private async Task CopyFilePathAsync()
    {
        if (HasLocalFile)
            await CopyTextAsync(FilePath!);
    }

    [RelayCommand]
    private void Restart()
    {
        if (!CanRestart)
            return;

        ProgressPercentage = 0;
        SpeedText = "--";
        RemainingText = "--";
        State = DownloadState.Queued;
        StatusText = "等待重新下载";
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Queue()
    {
        if (State == DownloadState.Downloading)
            return;

        State = DownloadState.Queued;
        StatusText = "等待中";
        SpeedText = "--";
        RemainingText = "--";
    }

    partial void OnStateChanged(DownloadState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanOpenFile));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(CanDeleteLocalFile));
    }

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(CanOpenFile));
        OnPropertyChanged(nameof(HasLocalFile));
        OnPropertyChanged(nameof(CanDeleteLocalFile));
        OnPropertyChanged(nameof(DetailsSavePath));
    }

    partial void OnFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanDeleteLocalFile));
        OnPropertyChanged(nameof(DetailsSavePath));
    }

    private IEnumerable<string> GetDownloadFilePaths()
    {
        var destinationPath = FilePath ?? ResolveDestinationPath();
        yield return destinationPath;
        yield return destinationPath + ".lightdl";
        yield return destinationPath + ".lightdl.meta";
        yield return destinationPath + ".lightdl.meta.tmp";
    }

    private string ResolveDestinationPath()
    {
        var path = Path.Combine(_destinationDirectory, _requestedFileName ?? FileName);
        if (_settings.FileConflictPolicy != (int)LightDownloadFileConflictPolicy.Rename || !File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; ; index++)
        {
            var candidateName = string.IsNullOrEmpty(extension)
                ? $"{name} ({index})"
                : $"{name} ({index}){extension}";
            var candidate = string.IsNullOrWhiteSpace(directory)
                ? candidateName
                : Path.Combine(directory, candidateName);
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static async Task CopyTextAsync(string text)
    {
        var topLevel = Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } => window,
            ISingleViewApplicationLifetime { MainView: { } view } => TopLevel.GetTopLevel(view),
            _ => null
        };

        if (topLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private void UpdateProgress(LightDownloadProgress progress)
    {
        if (!_uiUpdatesEnabled)
        {
            _pendingProgress = progress;
            return;
        }

        UpdateProgressCore(progress);
    }

    private void UpdateProgressCore(LightDownloadProgress progress)
    {
        var displaySpeed = UpdateDisplayedSpeed(progress.Speed);
        ProgressPercentage = Math.Clamp(progress.ProgressPercentage, 0, 100);
        SizeText = $"{FormatBytes(progress.DownloadedBytes)} / {FormatBytes(progress.TotalBytes)}";
        SpeedText = displaySpeed > 0 ? $"{FormatBytes(displaySpeed)}/s" : "--";

        if (displaySpeed > 0 && progress.TotalBytes > progress.DownloadedBytes)
        {
            var remaining = TimeSpan.FromSeconds((progress.TotalBytes - progress.DownloadedBytes) / displaySpeed);
            RemainingText = remaining.TotalHours >= 1
                ? $"{remaining:hh\\:mm\\:ss}"
                : $"{remaining:mm\\:ss}";
        }
        else
        {
            RemainingText = "--";
        }
    }

    public void SetUiUpdatesEnabled(bool enabled)
    {
        if (_uiUpdatesEnabled == enabled)
            return;

        _uiUpdatesEnabled = enabled;
        if (!enabled)
            return;

        if (State == DownloadState.Downloading && _pendingProgress is { } progress)
            UpdateProgressCore(progress);

        _pendingProgress = null;
    }

    private double UpdateDisplayedSpeed(double speed)
    {
        var actualSpeed = double.IsFinite(speed) ? Math.Max(0, speed) : 0;
        var now = Stopwatch.GetTimestamp();
        if (_lastSpeedUpdateTimestamp == 0 || _displaySpeed <= 0)
        {
            _displaySpeed = actualSpeed;
            _lastSpeedUpdateTimestamp = now;
            _speedDropStartedTimestamp = 0;
            return _displaySpeed;
        }

        var elapsedSeconds = Math.Clamp(
            (now - _lastSpeedUpdateTimestamp) / (double)Stopwatch.Frequency,
            0.01,
            2);
        _lastSpeedUpdateTimestamp = now;

        if (actualSpeed >= _displaySpeed)
        {
            _speedDropStartedTimestamp = 0;
            var riseFactor = 1 - Math.Exp(-elapsedSeconds / 0.15);
            _displaySpeed += (actualSpeed - _displaySpeed) * riseFactor;
        }
        else
        {
            if (_speedDropStartedTimestamp == 0)
                _speedDropStartedTimestamp = now;

            var dropDuration = (now - _speedDropStartedTimestamp) / (double)Stopwatch.Frequency;
            if (dropDuration >= 1)
            {
                var fallFactor = 1 - Math.Exp(-elapsedSeconds / 3.5);
                _displaySpeed += (actualSpeed - _displaySpeed) * fallFactor;
            }
        }

        if (_displaySpeed < 1)
            _displaySpeed = 0;

        return _displaySpeed;
    }

    private void ResetDisplayedSpeed()
    {
        _displaySpeed = 0;
        _lastSpeedUpdateTimestamp = 0;
        _speedDropStartedTimestamp = 0;
        SpeedText = "--";
        RemainingText = "--";
    }

    private LightDownloadConfig CreateDownloadConfig()
    {
        var config = new LightDownloadConfig
        {
            ChunkCount = _settings.ChunkCount,
            MinChunkCount = _settings.MinChunkCount,
            MaxChunkCount = _settings.MaxChunkCount,
            EnableDynamicConcurrency = _settings.EnableDynamicConcurrency,
            SegmentSize = _settings.SegmentSizeMb * 1024L * 1024L,
            Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds),
            MaxRetry = _settings.MaxRetry,
            EnableResume = _settings.EnableResume,
            FileConflictPolicy = (LightDownloadFileConflictPolicy)_settings.FileConflictPolicy,
            IgnoreSslErrors = _settings.IgnoreSslErrors,
            HttpMessageHandlerFactory = DownloadPlatform.HttpMessageHandlerFactory,
            UseProxy = _settings.ProxyMode != 1,
            Proxy = _settings.ProxyMode == 2 ? new WebProxy(_settings.ProxyUrl) : null,
            SpeedLimitProvider = _settings.SpeedLimitKib > 0
                ? () => _settings.SpeedLimitKib * 1024d
                : null
        };

        if (!string.IsNullOrWhiteSpace(_settings.UserAgent))
            config.UserAgent = _settings.UserAgent;

        return config;
    }

    public DownloadTaskRecord ToRecord() => new()
    {
        Id = Id,
        SourceUrl = SourceUrl,
        DestinationDirectory = _destinationDirectory,
        RequestedFileName = _requestedFileName,
        FileName = FileName,
        FilePath = FilePath,
        State = State,
        ProgressPercentage = ProgressPercentage,
        SizeText = SizeText,
        CreatedAt = _createdAt,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static string GetInitialFileName(string sourceUrl)
    {
        return Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            ? Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath)) is { Length: > 0 } name ? name : uri.Host
            : sourceUrl;
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
