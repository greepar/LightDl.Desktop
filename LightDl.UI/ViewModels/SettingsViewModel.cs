using CommunityToolkit.Mvvm.ComponentModel;
using LightDl.UI.Models;

namespace LightDl.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(AppSettings settings)
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
        ProxyMode = settings.ProxyMode;
        ProxyUrl = settings.ProxyUrl;
        UserAgent = settings.UserAgent;
        IgnoreSslErrors = settings.IgnoreSslErrors;
        CloseToTray = settings.CloseToTray;
        StartWithSystem = settings.StartWithSystem;
        ShowCompletionNotification = settings.ShowCompletionNotification;
        ThemeMode = settings.ThemeMode;
        EnableBrowserIntegration = settings.EnableBrowserIntegration;
    }

    [ObservableProperty] private string _defaultDownloadDirectory;
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
    [ObservableProperty] private int _proxyMode;
    [ObservableProperty] private string _proxyUrl;
    [ObservableProperty] private string _userAgent;
    [ObservableProperty] private bool _ignoreSslErrors;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _showCompletionNotification;
    [ObservableProperty] private int _themeMode;
    [ObservableProperty] private bool _enableBrowserIntegration;

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
        ProxyMode = ProxyMode,
        ProxyUrl = ProxyUrl,
        UserAgent = UserAgent,
        IgnoreSslErrors = IgnoreSslErrors,
        CloseToTray = CloseToTray,
        StartWithSystem = StartWithSystem,
        ShowCompletionNotification = ShowCompletionNotification,
        ThemeMode = ThemeMode,
        EnableBrowserIntegration = EnableBrowserIntegration
    };
}
