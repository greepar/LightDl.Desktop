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

    public int ProxyMode { get; set; }

    public string ProxyUrl { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public bool IgnoreSslErrors { get; set; }

    public bool CloseToTray { get; set; } = true;

    public bool ShowCompletionNotification { get; set; } = true;

    public int ThemeMode { get; set; }

    public bool EnableBrowserIntegration { get; set; } = true;
}
