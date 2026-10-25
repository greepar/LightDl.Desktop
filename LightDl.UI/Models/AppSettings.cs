namespace LightDl.UI.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public string DefaultDownloadDirectory { get; set; } = string.Empty;

    public int MaxConcurrentDownloads { get; set; } = 3;

    public int ChunkCount { get; set; } = 16;

    public bool EnableDynamicConcurrency { get; set; } = true;

    public int MinChunkCount { get; set; } = 4;

    public int MaxChunkCount { get; set; } = 24;

    public int SegmentSizeMb { get; set; } = 8;

    public int SpeedLimitKib { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxRetry { get; set; } = 10;

    public bool EnableResume { get; set; } = true;

    public int FileConflictPolicy { get; set; } = 3;

    // 0 = auto H2/H3 comparison, 1 = strict H3, 2 = disable H3.
    // Missing values in existing settings retain automatic mode.
    public int Http3Mode { get; set; }

    public int ProxyMode { get; set; }

    public string ProxyUrl { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public bool IgnoreSslErrors { get; set; }

    public bool CloseToTray { get; set; } = true;

    // 0 = ask, 1 = exit, 2 = run in background.
    public int WindowCloseBehavior { get; set; }

    public bool StartWithSystem { get; set; }

    public bool ShowCompletionNotification { get; set; } = true;

    public int ThemeMode { get; set; }

    // 0 = system language, 1 = Simplified Chinese, 2 = English.
    public int LanguageMode { get; set; }

    public bool EnableBrowserIntegration { get; set; } = true;

    public bool CheckForUpdatesAutomatically { get; set; } = true;
}
