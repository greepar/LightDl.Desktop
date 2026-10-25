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
    private bool _useHttp3Fallback;
    private object? _activeAttempt;
    private readonly bool _usesIsolatedStorage;
    private string? _workingFilePath;
    private string WorkingDirectory => Path.Combine(_destinationDirectory, ".lightdl-tasks", Id.ToString("N"));

    public DownloadItemViewModel(
        string sourceUrl,
        string destinationDirectory,
        AppSettings settings,
        string? requestedFileName = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        Id = Guid.NewGuid();
        _usesIsolatedStorage = true;
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
        _usesIsolatedStorage = record.UsesIsolatedStorage;
        SourceUrl = record.SourceUrl;
        _destinationDirectory = record.DestinationDirectory;
        _workingFilePath = record.WorkingFilePath is { } working &&
            Path.GetDirectoryName(Path.GetFullPath(working)) == Path.GetFullPath(WorkingDirectory)
            ? working : null;
        _settings = settings;
        _createdAt = record.CreatedAt == default ? DateTimeOffset.UtcNow : record.CreatedAt;
        _requestedFileName = record.RequestedFileName;
        _headers = record.RequestHeaders;
        FileName = string.IsNullOrWhiteSpace(record.FileName) ? GetInitialFileName(record.SourceUrl) : record.FileName;
        FilePath = record.FilePath;
        _useHttp3Fallback = record.UseHttp3Fallback;
        ProtocolText = RestoreProtocolText(record);
        // Earlier versions persisted display labels. Restore them in the current UI language.
        SizeText = record.SizeText is "未知大小" or "Unknown size" or "" ? L.T("未知大小") : record.SizeText;
        ProgressPercentage = record.ProgressPercentage;
        State = record.State is DownloadState.Downloading or DownloadState.Queued
            ? DownloadState.Paused
            : record.State;
        StatusText = State switch
        {
            DownloadState.Completed => L.T("已完成"),
            DownloadState.Failed => L.T("下载失败"),
            DownloadState.Paused => L.T("已暂停"),
            _ => L.T("等待中")
        };
        RemainingText = State == DownloadState.Completed ? L.T("已完成") : "--";
    }

    private static string RestoreProtocolText(DownloadTaskRecord record)
    {
        var text = record.ProtocolText ?? "";
        if (!text.StartsWith("HTTP/", StringComparison.Ordinal)) return L.T("等待连接");
        if (text is "HTTP/3（连接失败）" or "HTTP/3 (connection failed)") return L.T("HTTP/3（连接失败）");
        var suffix = text.IndexOfAny(['（', ' ', '(']);
        var protocol = suffix < 0 ? text : text[..suffix];
        if (record.UseHttp3Fallback) return protocol + L.T("（本任务已回退）");
        if (text.Contains("H3 中断", StringComparison.Ordinal) || text.Contains("H3 interrupted", StringComparison.Ordinal))
            return protocol + L.T("（H3 中断，已回退）");
        if (text.Contains("测速选中", StringComparison.Ordinal) || text.Contains("selected by speed test", StringComparison.Ordinal))
            return protocol + L.T("（测速选中）");
        return protocol;
    }

    public event EventHandler? RemoveRequested;
    public override void RefreshLanguage()
    {
        StatusText = L.Relocalize(StatusText);
        ProtocolText = L.Relocalize(ProtocolText);
        SizeText = L.Relocalize(SizeText);
        RemainingText = L.Relocalize(RemainingText);
        base.RefreshLanguage();
    }

    public event EventHandler? StartRequested;

    public Func<DownloadItemViewModel, Exception, CancellationToken, Task<bool>>? ConfirmHttp3FallbackAsync { get; set; }

    public Guid Id { get; }

    public string SourceUrl { get; }

    public string DestinationDirectory => _destinationDirectory;

    [ObservableProperty]
    private string _fileName;

    [ObservableProperty]
    private string _statusText = L.T("等待中");

    [ObservableProperty]
    private string _protocolText = L.T("等待连接");

    [ObservableProperty]
    private int _activeWorkers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkerText))]
    [NotifyPropertyChangedFor(nameof(ProtocolSummaryText))]
    private int _concurrencyLimit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkerText))]
    [NotifyPropertyChangedFor(nameof(ProtocolSummaryText))]
    private bool? _supportsRange;

    public string WorkerText => SupportsRange == false
        ? L.F($"{ActiveWorkers} 活动线程（服务器未支持分段）")
        : ConcurrencyLimit > 0 ? L.F($"{ActiveWorkers}/{ConcurrencyLimit} 线程（活动/上限）") : L.F($"{ActiveWorkers} 线程");

    public string ProtocolSummaryText => IsActive ? $"{ProtocolText} · {WorkerText}" : ProtocolText;

    partial void OnProtocolTextChanged(string value) => OnPropertyChanged(nameof(ProtocolSummaryText));

    partial void OnActiveWorkersChanged(int value)
    {
        OnPropertyChanged(nameof(WorkerText));
        OnPropertyChanged(nameof(ProtocolSummaryText));
    }

    [ObservableProperty]
    private string _sizeText = L.T("未知大小");

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
        SupportsRange = null;
        ConcurrencyLimit = 0;
        State = DownloadState.Downloading;
        StatusText = L.T("正在下载");
        SetConnectingProtocol();
        var cancellationToken = _cancellation.Token;
        var resumePath = _usesIsolatedStorage ? _workingFilePath : FilePath;
        string? retryDestination = _settings.EnableResume && resumePath is { } previousPath &&
            File.Exists(previousPath + ".lightdl") ? previousPath : null;

        try
        {
            Directory.CreateDirectory(_destinationDirectory);
            var transferDirectory = _usesIsolatedStorage ? WorkingDirectory : _destinationDirectory;
            Directory.CreateDirectory(transferDirectory);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attempt = new object();
                _activeAttempt = attempt;
                bool IsCurrentAttempt() => ReferenceEquals(_activeAttempt, attempt) &&
                    State == DownloadState.Downloading && !cancellationToken.IsCancellationRequested;
                var selectionProgress = new Progress<LightDownloadProtocolSelection>(selection =>
                {
                    if (IsCurrentAttempt())
                        ProtocolText = FormatProtocol(selection.SelectedVersion) +
                            (selection.IsFallback ? L.T("（H3 中断，已回退）") : L.T("（测速选中）"));
                });
                var config = DownloadConfigFactory.Create(_settings, selection =>
                    ((IProgress<LightDownloadProtocolSelection>)selectionProgress).Report(selection));
                if (_usesIsolatedStorage)
                    config.FileConflictPolicy = LightDownloadFileConflictPolicy.Overwrite;
                if (_useHttp3Fallback)
                {
                    config.EnableHttp3 = false;
                    config.ForceHttp3 = false;
                }
                var strictHttp3 = config.ForceHttp3;
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Exception? connectionFailure = null;
                if (strictHttp3)
                {
                    // Ask at the first transport failure instead of waiting through
                    // the normal retry/backoff budget. Other errors retain retries.
                    config.RetryHandler = retry =>
                    {
                        if (Http3FailurePolicy.CanOfferFallback(retry.Error) &&
                            Interlocked.CompareExchange(ref connectionFailure, retry.Error, null) is null)
                            attemptCancellation.Cancel();
                    };
                }
                using var downloader = new LightDownloader(config);
                string? resolvedDestination = retryDestination;
                var fileInfo = new Progress<LightDownloadFileInfo>(info =>
                {
                    if (!IsCurrentAttempt()) return;
                    FileName = info.FileName;
                    FilePath = info.FilePath is { } reportedPath ? DisplayDestination(reportedPath) : FilePath;
                    SizeText = FormatBytes(info.Size);
                    SupportsRange = info.SupportsRange;
                    UpdateProtocol(downloader.SelectedHttpVersion);
                });
                var request = (retryDestination is { } destination
                        ? LightDownloadRequest.ToFile(SourceUrl, destination, _headers)
                        : _requestedFileName is { Length: > 0 }
                            ? LightDownloadRequest.ToFile(SourceUrl, Path.Combine(transferDirectory, _requestedFileName), _headers)
                            : LightDownloadRequest.ToDirectory(SourceUrl, transferDirectory, _headers))
                    .OnFileInfo(info =>
                    {
                        resolvedDestination = info.FilePath;
                        if (_usesIsolatedStorage) _workingFilePath = info.FilePath;
                        ((IProgress<LightDownloadFileInfo>)fileInfo).Report(info);
                    });
                var progress = new Progress<LightDownloadProgress>(value =>
                {
                    if (!IsCurrentAttempt()) return;
                    UpdateProtocol(downloader.SelectedHttpVersion);
                    UpdateProgress(value);
                });
                try
                {
                    var result = await downloader.DownloadAsync(request, progress,
                        cancellationToken: attemptCancellation.Token);
                    _activeAttempt = null;
                    UpdateProtocol(downloader.SelectedHttpVersion);
                    var saved = _usesIsolatedStorage ? CommitIsolatedDownload(result.FilePath) : (result.FilePath, result.Skipped);
                    FilePath = saved.Item1;
                    FileName = Path.GetFileName(FilePath);
                    ProgressPercentage = 100;
                    SpeedText = "--";
                    RemainingText = L.T("已完成");
                    StatusText = saved.Item2 ? L.T("已存在") : L.T("已完成");
                    State = DownloadState.Completed;
                    break;
                }
                catch (Exception error) when (strictHttp3 && !cancellationToken.IsCancellationRequested &&
                    Http3FailurePolicy.CanOfferFallback(connectionFailure ?? error))
                {
                    _activeAttempt = null;
                    var failure = connectionFailure ?? error;
                    Console.Error.WriteLine(failure);
                    FilePath = resolvedDestination is { } resolved ? DisplayDestination(resolved) : FilePath;
                    ProtocolText = L.T("HTTP/3（连接失败）");
                    StatusText = L.T("H3 连接失败，等待选择是否回退");
                    SpeedText = "--";
                    RemainingText = "--";
                    var confirmed = ConfirmHttp3FallbackAsync is { } confirm &&
                        await confirm(this, failure, cancellationToken).WaitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!confirmed)
                        throw new LightDownloadException(L.T("HTTP/3 连接失败，已保持强制 H3，未回退。"), failure);
                    _useHttp3Fallback = true;
                    retryDestination = resolvedDestination;
                    ResetDisplayedSpeed();
                    StatusText = L.T("正在回退到 H2 / H1.1");
                    SetConnectingProtocol();
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            SpeedText = "--";
            RemainingText = "--";
            StatusText = L.T("已暂停");
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
            _activeAttempt = null;
            completion.TrySetResult();
            if (ReferenceEquals(_downloadCompletion, completion))
                _downloadCompletion = null;
        }
    }

    public async Task PauseAndWaitAsync()
    {
        var completion = _downloadCompletion;
        if (completion is null) return;
        _cancellation?.Cancel();
        await completion.Task;
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
        StatusText = L.T("等待重新下载");
        // Publishing Queued synchronously invokes the scheduler; initialize all
        // queue text first so it cannot overwrite the new transfer's status.
        State = DownloadState.Queued;
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Queue()
    {
        if (State == DownloadState.Downloading)
            return;

        StatusText = L.T("等待中");
        SpeedText = "--";
        RemainingText = "--";
        State = DownloadState.Queued;
    }

    partial void OnStateChanged(DownloadState value)
    {
        if (value != DownloadState.Downloading)
        {
            ActiveWorkers = 0;
            ConcurrencyLimit = 0;
        }
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ProtocolSummaryText));
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

    private string DisplayDestination(string transferPath) => _usesIsolatedStorage
        ? Path.Combine(_destinationDirectory, Path.GetFileName(transferPath)) : transferPath;

    private (string Path, bool Skipped) CommitIsolatedDownload(string source)
    {
        var destination = DisplayDestination(source);
        var policy = (LightDownloadFileConflictPolicy)_settings.FileConflictPolicy;
        var skipped = false;
        if (policy == LightDownloadFileConflictPolicy.Overwrite)
            File.Move(source, destination, overwrite: true);
        else if (policy == LightDownloadFileConflictPolicy.Skip && File.Exists(destination))
        {
            File.Delete(source);
            skipped = true;
        }
        else if (policy == LightDownloadFileConflictPolicy.Rename)
        {
            var original = destination;
            for (var index = 0; ; index++)
            {
                destination = index == 0 ? original : Path.Combine(_destinationDirectory,
                    $"{Path.GetFileNameWithoutExtension(original)} ({index}){Path.GetExtension(original)}");
                try { File.Move(source, destination); break; }
                catch (IOException) when (File.Exists(destination)) { }
            }
        }
        else File.Move(source, destination);
        _workingFilePath = null;
        try { Directory.Delete(WorkingDirectory); }
        catch (IOException) { } // Keep unexpected leftovers rather than deleting another file.
        return (destination, skipped);
    }

    private IEnumerable<string> GetDownloadFilePaths()
    {
        if (_usesIsolatedStorage)
        {
            if (IsCompleted && FilePath is { } completed) yield return completed;
            if (Directory.Exists(WorkingDirectory))
                foreach (var file in Directory.EnumerateFiles(WorkingDirectory)) yield return file;
            yield break;
        }
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
        ActiveWorkers = Math.Max(0, progress.ActiveWorkers);
        ConcurrencyLimit = Math.Max(0, progress.ConcurrencyLimit);
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

    private void SetConnectingProtocol() => ProtocolText = _useHttp3Fallback
        ? L.T("连接中（已允许 H2 / H1.1）")
        : _settings.Http3Mode switch
        {
            1 => L.T("连接中（强制 HTTP/3）"),
            2 => L.T("连接中（H2 / H1.1）"),
            _ => L.T("连接中（自动 H2/H3）")
        };

    private void UpdateProtocol(Version? version)
    {
        if (version is not null)
            ProtocolText = FormatProtocol(version) + (_useHttp3Fallback ? L.T("（本任务已回退）") : "");
    }

    private static string FormatProtocol(Version version) => version.Major >= 2
        ? $"HTTP/{version.Major}" : $"HTTP/{version.Major}.{version.Minor}";

    public DownloadTaskRecord ToRecord() => new()
    {
        Id = Id,
        SourceUrl = SourceUrl,
        DestinationDirectory = _destinationDirectory,
        RequestedFileName = _requestedFileName,
        RequestHeaders = _headers?.ToDictionary(
            header => header.Key,
            header => header.Value,
            StringComparer.OrdinalIgnoreCase),
        FileName = FileName,
        FilePath = FilePath,
        UsesIsolatedStorage = _usesIsolatedStorage,
        WorkingFilePath = _workingFilePath,
        State = State,
        ProgressPercentage = ProgressPercentage,
        SizeText = SizeText,
        ProtocolText = ProtocolText,
        UseHttp3Fallback = _useHttp3Fallback,
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
