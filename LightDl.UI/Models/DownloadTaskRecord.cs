namespace LightDl.UI.Models;

public sealed class DownloadTaskRecord
{
    public Guid Id { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public string DestinationDirectory { get; set; } = string.Empty;

    public string? RequestedFileName { get; set; }

    public Dictionary<string, string>? RequestHeaders { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? FilePath { get; set; }
    public bool UsesIsolatedStorage { get; set; }
    public string? WorkingFilePath { get; set; }

    public DownloadState State { get; set; }

    public double ProgressPercentage { get; set; }

    public string SizeText { get; set; } = L.T("未知大小");

    public string? ProtocolText { get; set; }

    public bool UseHttp3Fallback { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
