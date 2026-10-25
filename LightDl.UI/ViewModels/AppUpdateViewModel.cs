using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightDl.UI.Services.Updates;

namespace LightDl.UI.ViewModels;

public partial class AppUpdateViewModel : ViewModelBase
{
    private readonly DesktopUpdateService? _service;
    private readonly SettingsViewModel _settings;
    private readonly Func<Func<Task>, Task> _installAndExit;
    private UpdateRelease? _release;
    private PreparedUpdate? _prepared;
    private string? _promptedVersion;
    public event Action? UpdateDiscovered;
    public SukiUI.Dialogs.ISukiDialogManager PromptDialogManager { get; } = new SukiUI.Dialogs.SukiDialogManager();
    public string AvailableVersion => _release?.Version ?? "";
    [ObservableProperty] private bool _isUpdatePromptOpen;

    public AppUpdateViewModel(DesktopUpdateService? service, SettingsViewModel settings, Func<Func<Task>, Task>? installAndExit = null)
    {
        _service = service;
        _settings = settings;
        _installAndExit = installAndExit ?? (start => App.Current is App app
            ? app.InstallUpdateAndExitAsync(start) : Task.FromException(new InvalidOperationException("无法退出当前应用。")));
        AutomaticChecksEnabled = settings.CheckForUpdatesAutomatically;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.HasUnsavedChanges))
                InstallUpdateCommand.NotifyCanExecuteChanged();
        };
        if (service is not null)
        {
            Status = $"当前版本 {service.CurrentVersion}";
            try
            {
                if (File.Exists(DesktopUpdateService.StatusFile))
                    Status = File.ReadAllText(DesktopUpdateService.StatusFile);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public bool IsSupported => _service is not null;
    public bool AutomaticChecksEnabled { get; set; }
    public string CurrentVersion => _service?.CurrentVersion ?? "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _releaseNotes = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadUpdateCommand))]
    private bool _hasUpdate;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isReadyToInstall;

    private bool CanCheck => IsSupported && !IsBusy && !IsReadyToInstall;
    private bool CanDownload => HasUpdate && !IsBusy && !IsReadyToInstall;
    private bool CanInstall => IsReadyToInstall && !IsBusy && !_settings.HasUnsavedChanges;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckForUpdatesAsync(CancellationToken ct)
    {
        if (_service is null) return;
        IsBusy = true;
        Status = "正在检查更新…";
        try
        {
            _release = await _service.CheckAsync(ct);
            HasUpdate = _release is not null;
            ReleaseNotes = _release is null ? "" : ReleaseNotesFormatter.Format(_release.Notes);
            OnPropertyChanged(nameof(AvailableVersion));
            Status = _release is null ? $"当前已是最新版本（{CurrentVersion}）" : $"发现新版本 {_release.Version}，可以下载更新。";
        }
        catch (Exception ex) { Status = "检查更新失败：" + ex.Message; }
        finally { IsBusy = false; }
        if (_release is not null) UpdateDiscovered?.Invoke();
    }

    public void TryShowUpdatePrompt(bool mainWindowVisible)
    {
        if (!mainWindowVisible || _release is null || IsReadyToInstall || _promptedVersion == _release.Version) return;
        _promptedVersion = _release.Version;
        IsUpdatePromptOpen = true;
    }

    [RelayCommand]
    private void DismissUpdatePrompt() => IsUpdatePromptOpen = false;

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (_release is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                $"{UpdateFeed.Repository}/releases/tag/v{_release.Version}") { UseShellExecute = true });
        }
        catch (Exception ex) { Status = "无法打开 Release 页面：" + ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanDownload), IncludeCancelCommand = true)]
    private async Task DownloadUpdateAsync(CancellationToken ct)
    {
        if (_service is null || _release is null) return;
        IsUpdatePromptOpen = false;
        IsBusy = true;
        Progress = 0;
        Status = "正在下载并验证更新…";
        try
        {
            _prepared?.Dispose();
            _prepared = await _service.PrepareAsync(_release, new Progress<double>(value => Progress = value), ct);
            IsReadyToInstall = true;
            Status = $"{_release.Version} 已准备就绪。重启安装将暂停下载并保存任务；有未保存设置时请先保存。";
        }
        catch (OperationCanceledException) { Status = "更新下载已取消，原程序未修改。"; }
        catch (Exception ex) { Status = "更新准备失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallUpdateAsync()
    {
        if (_prepared is null) return;
        IsBusy = true;
        try
        {
            Status = "正在保存下载任务，随后重启安装…";
            await _installAndExit(_prepared.StartInstallerAsync);
        }
        catch (Exception ex)
        {
            Status = "安装尚未开始：" + ex.Message;
            IsBusy = false;
        }
    }

    public void Cleanup() => _prepared?.Dispose();
}
