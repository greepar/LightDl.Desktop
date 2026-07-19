using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public async Task StartAsync()
    {
        if (State == DownloadState.Downloading)
            return;

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
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
    }

    private void UpdateProgress(LightDownloadProgress progress)
    {
        ProgressPercentage = Math.Clamp(progress.ProgressPercentage, 0, 100);
        SizeText = $"{FormatBytes(progress.DownloadedBytes)} / {FormatBytes(progress.TotalBytes)}";
        SpeedText = progress.Speed > 0 ? $"{FormatBytes(progress.Speed)}/s" : "--";

        if (progress.Speed > 0 && progress.TotalBytes > progress.DownloadedBytes)
        {
            var remaining = TimeSpan.FromSeconds((progress.TotalBytes - progress.DownloadedBytes) / progress.Speed);
            RemainingText = remaining.TotalHours >= 1
                ? $"{remaining:hh\\:mm\\:ss}"
                : $"{remaining:mm\\:ss}";
        }
        else
        {
            RemainingText = "--";
        }
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
