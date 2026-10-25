using Avalonia.Controls;
using Avalonia.Interactivity;
using LightDl.UI.Services;
using SukiUI.Controls;
using Avalonia.Platform.Storage;

namespace LightDl.UI.Views;

public partial class FirstRunWindow : SukiWindow
{
    private readonly DesktopInstallationService? _installation;
    private bool _installing;
    public string InstallPath => _installation?.InstallPath ?? "";
    public bool ContinueRequested { get; private set; }

    public FirstRunWindow() : this(null) { }
    public FirstRunWindow(DesktopInstallationService? installation, bool fromSettings = false)
    {
        _installation = installation;
        InitializeComponent();
        WindowsShortcutOptions.IsVisible = OperatingSystem.IsWindows();
        DataContext = this;
        InstallDirectoryBox.Text = Path.GetDirectoryName(InstallPath);
        UpdatePathPreview();
        if (fromSettings)
        {
            Title = L.T("安装到系统");
            ChoiceTitle.Text = L.T("安装到系统");
            PortableButton.Content = L.T("取消");
            RememberChoiceBox.IsVisible = false;
            PortableHint.Text = L.T("可以重新选择安装目录。取消不会修改现有设置或使用方式。");
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _fromSettings = fromSettings;
        Closing += (_, e) => { if (_installing) e.Cancel = true; };
    }

    private readonly bool _fromSettings;

    private void DirectoryChanged(object? sender, TextChangedEventArgs e) => UpdatePathPreview();

    private void UpdatePathPreview()
    {
        if (InstallPathPreview is null) return;
        try { InstallPathPreview.Text = L.T("程序位置：") + DesktopInstallationService.ResolveInstallPath(InstallDirectoryBox.Text ?? ""); }
        catch (IOException) { InstallPathPreview.Text = L.T("请选择有效的安装目录。"); }
    }

    private async void BrowseDirectory(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L.T("选择 LightDl 安装目录"), AllowMultiple = false
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) InstallDirectoryBox.Text = path;
        }
        catch (Exception ex)
        {
            InstallStatus.IsVisible = true;
            InstallStatus.Text = L.T("无法打开目录选择器，请直接填写目录：") + ex.Message;
        }
    }

    private void RunPortable(object? sender, RoutedEventArgs e)
    {
        if (_installation is null) return;
        try { if (!_fromSettings && RememberChoiceBox.IsChecked == true) _installation.ChoosePortable(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only profile must not prevent portable use.
        }
        ContinueRequested = !_fromSettings;
        Close(false);
    }

    private async void Install(object? sender, RoutedEventArgs e)
    {
        if (_installation is null || _installing) return;
        _installing = true;
        ChoiceButtons.IsEnabled = false;
        DirectoryPicker.IsEnabled = false;
        WindowsShortcutOptions.IsEnabled = false;
        InstallStatus.IsVisible = true;
        InstallStatus.Text = L.T("正在安装并配置应用菜单…");
        try
        {
            await _installation.InstallAsync(InstallDirectoryBox.Text, rememberChoice: _fromSettings || RememberChoiceBox.IsChecked == true,
                createDesktopShortcut: CreateDesktopShortcutBox.IsChecked == true,
                createStartMenuShortcut: CreateStartMenuShortcutBox.IsChecked == true);
            _installing = false;
            ContinueRequested = true;
            Close(true);
        }
        catch (Exception ex)
        {
            InstallStatus.Text = L.T("安装失败：") + ex.Message + L.T("\n你仍可以选择直接运行。");
        }
        finally { _installing = false; ChoiceButtons.IsEnabled = true; DirectoryPicker.IsEnabled = true; WindowsShortcutOptions.IsEnabled = true; }
    }
}
