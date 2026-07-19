namespace LightDl.UI.Models;

public sealed class BrowserCaptureRequest
{
    public int ProtocolVersion { get; set; }

    public string RequestId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Browser { get; set; } = string.Empty;

    public string BrowserDownloadId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string FinalUrl { get; set; } = string.Empty;

    public string Referrer { get; set; } = string.Empty;

    public string SuggestedFileName { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public long? TotalBytes { get; set; }

    public List<BrowserCaptureHeader> Headers { get; set; } = [];
}

public sealed class BrowserCaptureHeader
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool Sensitive { get; set; }
}

public sealed class BrowserCaptureResponse
{
    public bool Accepted { get; set; }

    public bool CancelBrowserDownload { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? TaskId { get; set; }

    public static BrowserCaptureResponse Reject(string message) => new()
    {
        Message = message
    };
}
