using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using LightDl.UI.Services;
using LightDl.UI.Models;

namespace LightDl.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private string _savedSnapshot;

    public SettingsViewModel(AppSettings settings)
    {
        ApplyModel(settings);
        _savedSnapshot = SerializeSnapshot(ToModel());
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(HasUnsavedChanges))
                UpdateUnsavedChanges();
        };
    }

    private void ApplyModel(AppSettings settings)
    {
        DefaultDownloadDirectory = settings.DefaultDownloadDirectory;
        MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
        ChunkCount = settings.ChunkCount;
        EnableDynamicConcurrency = settings.EnableDynamicConcurrency;
        MinChunkCount = settings.MinChunkCount;
        MaxChunkCount = settings.MaxChunkCount;
        SegmentSizeMb = settings.SegmentSizeMb;
        SpeedLimitKib = settings.SpeedLimitKib;
        TimeoutSeconds = settings.TimeoutSeconds;
        MaxRetry = settings.MaxRetry;
        EnableResume = settings.EnableResume;
        FileConflictPolicy = settings.FileConflictPolicy;
        Http3Mode = settings.Http3Mode;
        ProxyMode = settings.ProxyMode;
        ProxyUrl = settings.ProxyUrl;
        UserAgent = settings.UserAgent;
        IgnoreSslErrors = settings.IgnoreSslErrors;
        CloseToTray = settings.CloseToTray;
        StartWithSystem = settings.StartWithSystem;
        ShowCompletionNotification = settings.ShowCompletionNotification;
        ThemeMode = settings.ThemeMode;
        EnableBrowserIntegration = settings.EnableBrowserIntegration;
        CheckForUpdatesAutomatically = settings.CheckForUpdatesAutomatically;
    }

    public void DiscardChanges()
    {
        var saved = JsonSerializer.Deserialize(_savedSnapshot, AppSettingsJsonContext.Default.AppSettings)
            ?? throw new InvalidOperationException("无法恢复已保存的设置。");
        ApplyModel(saved);
        UpdateUnsavedChanges();
    }

    [ObservableProperty] private bool _hasUnsavedChanges;

    public void MarkSaved(AppSettings submittedSettings)
    {
        _savedSnapshot = SerializeSnapshot(submittedSettings);
        UpdateUnsavedChanges();
    }

    private void UpdateUnsavedChanges() =>
        HasUnsavedChanges = _savedSnapshot != SerializeSnapshot(ToModel());

    private static string SerializeSnapshot(AppSettings settings) =>
        JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);

    [ObservableProperty] private string _defaultDownloadDirectory = string.Empty;
    [ObservableProperty] private int _maxConcurrentDownloads;
    [ObservableProperty] private int _chunkCount;
    [ObservableProperty] private bool _enableDynamicConcurrency;
    [ObservableProperty] private int _minChunkCount;
    [ObservableProperty] private int _maxChunkCount;
    [ObservableProperty] private int _segmentSizeMb;
    [ObservableProperty] private int _speedLimitKib;
    [ObservableProperty] private int _timeoutSeconds;
    [ObservableProperty] private int _maxRetry;
    [ObservableProperty] private bool _enableResume;
    [ObservableProperty] private int _fileConflictPolicy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Http3ModeDescription))]
    [NotifyPropertyChangedFor(nameof(ShowHttp3ProxyNote))]
    private int _http3Mode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHttp3ProxyNote))]
    private int _proxyMode;
    [ObservableProperty] private string _proxyUrl = string.Empty;
    [ObservableProperty] private string _userAgent = string.Empty;
    [ObservableProperty] private bool _ignoreSslErrors;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _showCompletionNotification;
    [ObservableProperty] private int _themeMode;
    [ObservableProperty] private bool _enableBrowserIntegration;
    [ObservableProperty] private bool _checkForUpdatesAutomatically;

    public string Http3ModeDescription => Http3Mode switch
    {
        1 => "所有请求仅使用 HTTP/3；服务器、网络或设备不支持时下载失败，不回退。仅支持 HTTPS。",
        2 => "不探测 HTTP/3，使用 HTTP/2，并可回退到 HTTP/1.1。",
        _ => "用等长分片实测 H2/H3 速度，后续持续使用更快的协议；H3 不可用时自动回退。小文件和不支持分片的文件不测速。"
    };

    public bool ShowHttp3ProxyNote => Http3Mode == 0 && ProxyMode != 1;

    public bool UsesCustomProxy => ProxyMode == 2;

    partial void OnProxyModeChanged(int value) => OnPropertyChanged(nameof(UsesCustomProxy));

    public AppSettings ToModel() => new()
    {
        DefaultDownloadDirectory = DefaultDownloadDirectory,
        MaxConcurrentDownloads = MaxConcurrentDownloads,
        ChunkCount = ChunkCount,
        EnableDynamicConcurrency = EnableDynamicConcurrency,
        MinChunkCount = MinChunkCount,
        MaxChunkCount = MaxChunkCount,
        SegmentSizeMb = SegmentSizeMb,
        SpeedLimitKib = SpeedLimitKib,
        TimeoutSeconds = TimeoutSeconds,
        MaxRetry = MaxRetry,
        EnableResume = EnableResume,
        FileConflictPolicy = FileConflictPolicy,
        Http3Mode = Http3Mode,
        ProxyMode = ProxyMode,
        ProxyUrl = ProxyUrl,
        UserAgent = UserAgent,
        IgnoreSslErrors = IgnoreSslErrors,
        CloseToTray = CloseToTray,
        StartWithSystem = StartWithSystem,
        ShowCompletionNotification = ShowCompletionNotification,
        ThemeMode = ThemeMode,
        EnableBrowserIntegration = EnableBrowserIntegration,
        CheckForUpdatesAutomatically = CheckForUpdatesAutomatically
    };
}
