using CommunityToolkit.Mvvm.ComponentModel;
using LightDl.UI.Models;

namespace LightDl.UI.ViewModels;

public partial class BrowserCaptureViewModel : ViewModelBase
{
    public BrowserCaptureViewModel(BrowserCaptureRequest request, string downloadDirectory)
    {
        Request = request;
        FileName = string.IsNullOrWhiteSpace(request.SuggestedFileName)
            ? GetFileName(request.FinalUrl.Length > 0 ? request.FinalUrl : request.Url)
            : Path.GetFileName(request.SuggestedFileName);
        DownloadDirectory = downloadDirectory;
    }

    public BrowserCaptureRequest Request { get; }

    public string SourceUrl => Request.FinalUrl.Length > 0 ? Request.FinalUrl : Request.Url;

    public string BrowserName => Request.Browser switch
    {
        "edge" => "Microsoft Edge",
        "firefox" => "Mozilla Firefox",
        _ => "Google Chrome"
    };

    public string SizeText => Request.TotalBytes is > 0 ? FormatBytes(Request.TotalBytes.Value) : "未知大小";

    [ObservableProperty] private string _fileName;

    [ObservableProperty] private string _downloadDirectory;

    private static string GetFileName(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            return "download";

        var fileName = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        return string.IsNullOrWhiteSpace(fileName) ? "download" : fileName;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
