namespace LightDl.UI.Models;

public enum DownloadDialogAction
{
    BrowserContinue,
    CancelDownload,
    DownloadWithLightDl
}

public sealed record DownloadDialogResult(
    DownloadDialogAction Action,
    string SourceUrl,
    string FileName,
    string DownloadDirectory);
