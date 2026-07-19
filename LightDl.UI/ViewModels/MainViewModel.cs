using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightDl.UI.Models;
using LightDl.UI.Services;
using SukiUI.Dialogs;

namespace LightDl.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppSettingsService _settingsService;
    private readonly DownloadTaskStore _taskStore;
    private readonly BrowserHostRegistrationService _browserHostRegistration;
    private TaskCompletionSource<BrowserCaptureResponse>? _browserCaptureCompletion;

    public MainViewModel() : this(
        new AppSettingsService(),
        new DownloadTaskStore(),
        new BrowserHostRegistrationService())
    {
    }

    public MainViewModel(
        AppSettingsService settingsService,
        DownloadTaskStore taskStore,
        BrowserHostRegistrationService browserHostRegistration)
    {
        _settingsService = settingsService;
        _taskStore = taskStore;
        _browserHostRegistration = browserHostRegistration;
        IsDesktopLayout = !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();
        Settings = new SettingsViewModel(_settingsService.Load());
        BrowserHostStatus = _browserHostRegistration.IsRegistered()
            ? "Native Messaging 宿主已注册"
            : "Native Messaging 宿主尚未注册";
        foreach (var record in _taskStore.Load().OrderByDescending(item => item.CreatedAt))
            AttachDownload(new DownloadItemViewModel(record, Settings.ToModel()));
        ActivationBroker.Register(HandleActivation);
    }

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];

    public ISukiDialogManager DialogManager { get; } = new SukiDialogManager();

    public SettingsViewModel Settings { get; }

    public IEnumerable<DownloadItemViewModel> VisibleDownloads => SelectedSection switch
    {
        "downloading" => Downloads.Where(item => item.State == DownloadState.Downloading),
        "completed" => Downloads.Where(item => item.State == DownloadState.Completed),
        "failed" => Downloads.Where(item => item.State == DownloadState.Failed),
        _ => Downloads
    };

    public bool IsDesktopLayout { get; }

    public bool IsMobileLayout => !IsDesktopLayout;

    public bool HasDownloads => Downloads.Count > 0;

    public bool HasNoDownloads => Downloads.Count == 0;

    public bool HasVisibleDownloads => VisibleDownloads.Any();

    public bool HasNoVisibleDownloads => !HasVisibleDownloads;

    public int ActiveCount => Downloads.Count(item => item.State == DownloadState.Downloading);

    public int CompletedCount => Downloads.Count(item => item.State == DownloadState.Completed);

    [ObservableProperty]
    private string _newUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "准备就绪";

    [ObservableProperty]
    private string _browserHostStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingBrowserCapture))]
    [NotifyPropertyChangedFor(nameof(HasNoPendingBrowserCapture))]
    private BrowserCaptureViewModel? _pendingBrowserCapture;

    public bool HasPendingBrowserCapture => PendingBrowserCapture is not null;

    public bool HasNoPendingBrowserCapture => PendingBrowserCapture is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadsPage))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPage))]
    [NotifyPropertyChangedFor(nameof(IsBrowserIntegrationPage))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    [NotifyPropertyChangedFor(nameof(IsAllSelected))]
    [NotifyPropertyChangedFor(nameof(IsDownloadingSelected))]
    [NotifyPropertyChangedFor(nameof(IsCompletedSelected))]
    [NotifyPropertyChangedFor(nameof(IsFailedSelected))]
    [NotifyPropertyChangedFor(nameof(IsBrowserSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSelected))]
    private string _selectedSection = "all";

    public bool IsDownloadsPage => SelectedSection is not "settings" and not "browser";

    public bool IsSettingsPage => SelectedSection == "settings";

    public bool IsBrowserIntegrationPage => SelectedSection == "browser";

    public bool IsAllSelected => SelectedSection == "all";

    public bool IsDownloadingSelected => SelectedSection == "downloading";

    public bool IsCompletedSelected => SelectedSection == "completed";

    public bool IsFailedSelected => SelectedSection == "failed";

    public bool IsBrowserSelected => SelectedSection == "browser";

    public bool IsSettingsSelected => SelectedSection == "settings";

    public string PageTitle => SelectedSection switch
    {
        "downloading" => "正在下载",
        "completed" => "已完成",
        "failed" => "下载失败",
        "settings" => "设置",
        "browser" => "浏览器集成",
        _ => "全部任务"
    };

    public string PageSubtitle => SelectedSection switch
    {
        "settings" => "调整下载、网络和应用行为",
        "browser" => "管理浏览器自动接管和 Native Messaging 宿主",
        _ => "管理桌面和移动设备上的下载队列"
    };

    [RelayCommand]
    private async Task AddDownloadAsync()
    {
        await AddDownloadCoreAsync(NewUrl);
    }

    [RelayCommand]
    private void PauseAll()
    {
        foreach (var item in Downloads.Where(item => item.IsActive))
            item.PauseCommand.Execute(null);
    }

    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return;

        if (SelectedSection == section)
        {
            RaiseSelectionChanged();
            return;
        }

        SelectedSection = section;
        RaiseDownloadListChanged();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.DefaultDownloadDirectory))
        {
            StatusMessage = "请选择默认下载目录";
            return;
        }

        if (Settings.MinChunkCount > Settings.MaxChunkCount)
        {
            StatusMessage = "最小连接数不能大于最大连接数";
            return;
        }

        if (Settings.ProxyMode == 2 &&
            (!Uri.TryCreate(Settings.ProxyUrl, UriKind.Absolute, out var proxyUri) ||
             proxyUri.Scheme is not "http" and not "https" and not "socks5"))
        {
            StatusMessage = "请输入有效的 HTTP、HTTPS 或 SOCKS5 代理地址";
            return;
        }

        await _settingsService.SaveAsync(Settings.ToModel());
        ApplyTheme(Settings.ThemeMode);
        StatusMessage = "设置已保存，新配置将在后续下载中生效";
        ScheduleDownloads();
    }

    [RelayCommand]
    private async Task RegisterBrowserHostAsync()
    {
        try
        {
            await _browserHostRegistration.RegisterAsync();
            BrowserHostStatus = "Native Messaging 宿主已注册，请重新启动浏览器";
            StatusMessage = "浏览器宿主注册完成";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BrowserHostStatus = ex.Message;
            StatusMessage = "浏览器宿主注册失败";
        }
    }

    [RelayCommand]
    private void UnregisterBrowserHost()
    {
        try
        {
            _browserHostRegistration.Unregister();
            BrowserHostStatus = "Native Messaging 宿主尚未注册";
            StatusMessage = "已移除浏览器宿主注册";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BrowserHostStatus = ex.Message;
            StatusMessage = "移除浏览器宿主失败";
        }
    }

    public Task<BrowserCaptureResponse> HandleBrowserCaptureAsync(BrowserCaptureRequest request)
    {
        if (!Settings.EnableBrowserIntegration)
            return Task.FromResult(BrowserCaptureResponse.Reject("Browser integration is disabled"));

        if (request.ProtocolVersion != 1 || request.Type != "capture-download")
            return Task.FromResult(BrowserCaptureResponse.Reject("Unsupported browser integration protocol"));

        var sourceUrl = request.FinalUrl.Length > 0 ? request.FinalUrl : request.Url;
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not "http" and not "https")
        {
            return Task.FromResult(BrowserCaptureResponse.Reject("Only HTTP and HTTPS downloads are supported"));
        }

        if (PendingBrowserCapture is not null)
            return Task.FromResult(BrowserCaptureResponse.Reject("Another browser download is awaiting confirmation"));

        PendingBrowserCapture = new BrowserCaptureViewModel(request, Settings.DefaultDownloadDirectory);
        _browserCaptureCompletion = new TaskCompletionSource<BrowserCaptureResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StatusMessage = $"收到来自 {PendingBrowserCapture.BrowserName} 的下载请求";
        return _browserCaptureCompletion.Task;
    }

    [RelayCommand]
    private async Task AcceptBrowserCaptureAsync()
    {
        if (PendingBrowserCapture is not { } capture)
            return;

        await AcceptBrowserCaptureFromDialogAsync(capture.FileName, capture.DownloadDirectory);
    }

    public async Task<bool> AcceptBrowserCaptureFromDialogAsync(string fileName, string downloadDirectory)
    {
        if (PendingBrowserCapture is not { } capture || _browserCaptureCompletion is not { } completion)
            return false;

        capture.FileName = fileName;
        capture.DownloadDirectory = downloadDirectory;

        var normalizedFileName = Path.GetFileName(capture.FileName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName) ||
            normalizedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(normalizedFileName, capture.FileName.Trim(), StringComparison.Ordinal))
        {
            StatusMessage = "请输入有效的文件名";
            return false;
        }

        if (string.IsNullOrWhiteSpace(capture.DownloadDirectory))
        {
            StatusMessage = "请选择下载目录";
            return false;
        }

        var headers = BuildBrowserHeaders(capture.Request);
        var item = await AddDownloadCoreAsync(
            capture.SourceUrl,
            capture.DownloadDirectory,
            normalizedFileName,
            headers);
        if (item is null)
            return false;

        PendingBrowserCapture = null;
        _browserCaptureCompletion = null;
        completion.TrySetResult(new BrowserCaptureResponse
        {
            Accepted = true,
            CancelBrowserDownload = true,
            Message = "Download accepted by LightDl",
            TaskId = item.Id.ToString("D")
        });
        return true;
    }

    [RelayCommand]
    private void RejectBrowserCapture()
    {
        ContinueBrowserDownloadFromDialog();
    }

    public void ContinueBrowserDownloadFromDialog()
    {
        var completion = _browserCaptureCompletion;
        PendingBrowserCapture = null;
        _browserCaptureCompletion = null;
        StatusMessage = "已让浏览器继续原下载";
        completion?.TrySetResult(BrowserCaptureResponse.Reject("The user declined the download"));
    }

    [RelayCommand]
    private void CancelBrowserCapture()
    {
        CancelBrowserDownloadFromDialog();
    }

    public void CancelBrowserDownloadFromDialog()
    {
        var completion = _browserCaptureCompletion;
        PendingBrowserCapture = null;
        _browserCaptureCompletion = null;
        StatusMessage = "已取消本次浏览器下载";
        completion?.TrySetResult(new BrowserCaptureResponse
        {
            Accepted = true,
            CancelBrowserDownload = true,
            Message = "The user cancelled the download"
        });
    }

    public async Task<bool> AddDownloadFromDialogAsync(
        string sourceUrl,
        string downloadDirectory,
        string? fileName)
    {
        var item = await AddDownloadCoreAsync(
            sourceUrl,
            downloadDirectory,
            string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim());
        return item is not null;
    }

    public void HandleActivation(string argument)
    {
        if (ProtocolActivation.TryGetDownloadUrl(argument, out var url))
            _ = AddDownloadCoreAsync(url);
    }

    private async Task<DownloadItemViewModel?> AddDownloadCoreAsync(
        string sourceUrl,
        string? destinationDirectory = null,
        string? requestedFileName = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusMessage = "请输入有效的 HTTP 或 HTTPS 下载地址";
            return null;
        }

        destinationDirectory ??= Settings.DefaultDownloadDirectory;
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            StatusMessage = "请选择下载目录";
            return null;
        }

        if (DownloadPlatform.EnsureDownloadDirectoryAccessAsync is { } ensureAccess &&
            !await ensureAccess())
        {
            StatusMessage = "请授予下载目录访问权限，然后再次开始下载";
            return null;
        }

        var item = new DownloadItemViewModel(
            uri.AbsoluteUri,
            destinationDirectory,
            Settings.ToModel(),
            requestedFileName,
            headers);
        AttachDownload(item, insertAtStart: true);

        NewUrl = string.Empty;
        StatusMessage = $"已添加 {item.FileName}";
        RaiseSummaryChanged();
        RaiseDownloadListChanged();
        await PersistDownloadsAsync();
        ScheduleDownloads();
        return item;
    }

    private void OnRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is not DownloadItemViewModel item)
            return;

        item.RemoveRequested -= OnRemoveRequested;
        item.StartRequested -= OnStartRequested;
        item.PropertyChanged -= OnDownloadPropertyChanged;
        Downloads.Remove(item);
        RaiseSummaryChanged();
        RaiseDownloadListChanged();
        PersistDownloads();
    }

    private void OnDownloadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadItemViewModel.State))
        {
            RaiseSummaryChanged();
            RaiseDownloadListChanged();
            PersistDownloads();
            ScheduleDownloads();
        }
    }

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(HasDownloads));
        OnPropertyChanged(nameof(HasNoDownloads));
    }

    private void RaiseDownloadListChanged()
    {
        OnPropertyChanged(nameof(VisibleDownloads));
        OnPropertyChanged(nameof(HasVisibleDownloads));
        OnPropertyChanged(nameof(HasNoVisibleDownloads));
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsDownloadingSelected));
        OnPropertyChanged(nameof(IsCompletedSelected));
        OnPropertyChanged(nameof(IsFailedSelected));
        OnPropertyChanged(nameof(IsBrowserSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    private void AttachDownload(DownloadItemViewModel item, bool insertAtStart = false)
    {
        item.RemoveRequested += OnRemoveRequested;
        item.StartRequested += OnStartRequested;
        item.PropertyChanged += OnDownloadPropertyChanged;
        if (insertAtStart)
            Downloads.Insert(0, item);
        else
            Downloads.Add(item);
    }

    private void PersistDownloads()
    {
        _ = _taskStore.SaveAsync(Downloads.Select(item => item.ToRecord()));
    }

    private void OnStartRequested(object? sender, EventArgs e)
    {
        if (sender is not DownloadItemViewModel item)
            return;

        item.Queue();
        PersistDownloads();
        ScheduleDownloads();
    }

    private void ScheduleDownloads()
    {
        var availableSlots = Settings.MaxConcurrentDownloads - ActiveCount;
        if (availableSlots <= 0)
            return;

        foreach (var item in Downloads.Where(item => item.State == DownloadState.Queued).Take(availableSlots).ToList())
            _ = RunDownloadAsync(item);
    }

    private async Task RunDownloadAsync(DownloadItemViewModel item)
    {
        await item.StartAsync();
        ScheduleDownloads();
    }

    private Task PersistDownloadsAsync()
    {
        return _taskStore.SaveAsync(Downloads.Select(item => item.ToRecord()));
    }

    private static IReadOnlyDictionary<string, string> BuildBrowserHeaders(BrowserCaptureRequest request)
    {
        var allowedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Cookie",
            "Authorization",
            "Referer"
        };
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers.Take(32))
        {
            if (header.Name.Length is > 0 and <= 64 &&
                header.Value.Length <= 32 * 1024 &&
                allowedHeaders.Contains(header.Name))
            {
                headers[header.Name] = header.Value;
            }
        }

        if (!headers.ContainsKey("Referer") &&
            request.Referrer.Length is > 0 and <= 4096 &&
            Uri.TryCreate(request.Referrer, UriKind.Absolute, out var referrer) &&
            referrer.Scheme is "http" or "https")
        {
            headers["Referer"] = request.Referrer;
        }

        return headers;
    }

    public static void ApplyTheme(int themeMode)
    {
        if (Application.Current is not { } application)
            return;

        application.RequestedThemeVariant = themeMode switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
