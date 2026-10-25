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
        string sizeText = "",
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
        _sizeText = string.IsNullOrEmpty(sizeText) ? L.T("未知大小") : sizeText;
        Cookie = cookie;
        Referer = referer;
        UserAgent = userAgent;
    }

    public bool IsBrowserCapture { get; }

    public bool IsNewTask { get; }

    public string BrowserName { get; }

    private readonly string _sizeText;
    public string SizeText => L.Relocalize(_sizeText);

    public string DialogTitle => IsBrowserCapture ? L.T("浏览器下载接管") : L.T("新建下载任务");

    public string DialogSubtitle => IsBrowserCapture
        ? L.T("确认下载信息，并选择由浏览器继续、取消下载或交给 LightDl。")
        : L.T("填写下载地址和保存位置后创建任务。");

    public override void RefreshLanguage()
    {
        ErrorMessage = L.Relocalize(ErrorMessage);
        base.RefreshLanguage();
    }

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
            ErrorMessage = L.T("请输入有效的 HTTP 或 HTTPS 下载地址");
            return false;
        }

        if (string.IsNullOrWhiteSpace(DownloadDirectory))
        {
            ErrorMessage = L.T("请选择下载目录");
            return false;
        }

        var trimmedFileName = FileName.Trim();
        if (trimmedFileName.Length > 0 &&
            (Path.GetFileName(trimmedFileName) != trimmedFileName ||
             trimmedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            ErrorMessage = L.T("请输入有效的文件名");
            return false;
        }

        var trimmedReferer = Referer.Trim();
        if (trimmedReferer.Length > 0 &&
            (!Uri.TryCreate(trimmedReferer, UriKind.Absolute, out var refererUri) ||
             refererUri.Scheme is not "http" and not "https"))
        {
            ErrorMessage = L.T("请输入有效的 HTTP 或 HTTPS Referer");
            return false;
        }

        var trimmedCookie = Cookie.Trim();
        if (trimmedCookie.Length > 32 * 1024 || trimmedCookie.IndexOfAny(['\r', '\n']) >= 0)
        {
            ErrorMessage = L.T("Cookie 格式无效");
            return false;
        }

        var trimmedUserAgent = UserAgent.Trim();
        if (trimmedUserAgent.Length > 4096 || trimmedUserAgent.IndexOfAny(['\r', '\n']) >= 0)
        {
            ErrorMessage = L.T("User-Agent 格式无效");
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
