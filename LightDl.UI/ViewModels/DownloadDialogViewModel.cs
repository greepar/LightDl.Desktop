using CommunityToolkit.Mvvm.ComponentModel;

namespace LightDl.UI.ViewModels;

public partial class DownloadDialogViewModel : ViewModelBase
{
    public DownloadDialogViewModel() : this(false, string.Empty, string.Empty, string.Empty)
    {
    }

    public DownloadDialogViewModel(
        bool isBrowserCapture,
        string sourceUrl,
        string fileName,
        string downloadDirectory,
        string browserName = "",
        string sizeText = "未知大小",
        string cookie = "",
        string referer = "",
        string userAgent = "")
    {
        IsBrowserCapture = isBrowserCapture;
        IsNewTask = !isBrowserCapture;
        SourceUrl = sourceUrl;
        FileName = fileName;
        DownloadDirectory = downloadDirectory;
        BrowserName = browserName;
        SizeText = sizeText;
        Cookie = cookie;
        Referer = referer;
        UserAgent = userAgent;
        DialogTitle = isBrowserCapture ? "浏览器下载接管" : "新建下载任务";
        DialogSubtitle = isBrowserCapture
            ? "确认下载信息，并选择由浏览器继续、取消下载或交给 LightDl。"
            : "填写下载地址和保存位置后创建任务。";
    }

    public bool IsBrowserCapture { get; }

    public bool IsNewTask { get; }

    public string BrowserName { get; }

    public string SizeText { get; }

    public string DialogTitle { get; }

    public string DialogSubtitle { get; }

    [ObservableProperty] private string _sourceUrl;

    [ObservableProperty] private string _fileName;

    [ObservableProperty] private string _downloadDirectory;

    [ObservableProperty] private string _cookie;

    [ObservableProperty] private string _referer;

    [ObservableProperty] private string _userAgent;

    [ObservableProperty] private string _errorMessage = string.Empty;

    public bool ValidateForLightDl()
    {
        if (!Uri.TryCreate(SourceUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not "http" and not "https")
        {
            ErrorMessage = "请输入有效的 HTTP 或 HTTPS 下载地址";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DownloadDirectory))
        {
            ErrorMessage = "请选择下载目录";
            return false;
        }

        var trimmedFileName = FileName.Trim();
        if (trimmedFileName.Length > 0 &&
            (Path.GetFileName(trimmedFileName) != trimmedFileName ||
             trimmedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            ErrorMessage = "请输入有效的文件名";
            return false;
        }

        var trimmedReferer = Referer.Trim();
        if (trimmedReferer.Length > 0 &&
            (!Uri.TryCreate(trimmedReferer, UriKind.Absolute, out var refererUri) ||
             refererUri.Scheme is not "http" and not "https"))
        {
            ErrorMessage = "请输入有效的 HTTP 或 HTTPS Referer";
            return false;
        }

        var trimmedCookie = Cookie.Trim();
        if (trimmedCookie.Length > 32 * 1024 || trimmedCookie.IndexOfAny(['\r', '\n']) >= 0)
        {
            ErrorMessage = "Cookie 格式无效";
            return false;
        }

        var trimmedUserAgent = UserAgent.Trim();
        if (trimmedUserAgent.Length > 4096 || trimmedUserAgent.IndexOfAny(['\r', '\n']) >= 0)
        {
            ErrorMessage = "User-Agent 格式无效";
            return false;
        }

        SourceUrl = uri.AbsoluteUri;
        FileName = trimmedFileName;
        DownloadDirectory = DownloadDirectory.Trim();
        Cookie = trimmedCookie;
        Referer = trimmedReferer;
        UserAgent = trimmedUserAgent;
        ErrorMessage = string.Empty;
        return true;
    }
}
