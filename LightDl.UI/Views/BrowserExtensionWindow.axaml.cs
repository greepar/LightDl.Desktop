using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using LightDl.UI.Services;
using SukiUI.Controls;

namespace LightDl.UI.Views;

public partial class BrowserExtensionWindow : SukiWindow, Localization.ILocalizable, System.ComponentModel.INotifyPropertyChanged
{
    public string Heading { get; }
    public string ExportDirectory { get; }
    private readonly string _instructions;
    public string Instructions => L.Relocalize(_instructions);
    public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void RefreshLanguage()
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Instructions)));
        ActionStatus.Text = L.Relocalize(ActionStatus.Text ?? "");
    }
    public string ExtensionPage { get; }
    private readonly string _documentation;

    public BrowserExtensionWindow() : this("chrome", new BrowserExtensionExport("", "", "")) { }

    public BrowserExtensionWindow(string browser, BrowserExtensionExport export)
    {
        Heading = browser switch { "chrome" => "Chrome", "edge" => "Edge", _ => "Firefox" };
        ExportDirectory = export.Directory;
        ExtensionPage = browser switch { "chrome" => "chrome://extensions", "edge" => "edge://extensions", _ => "about:debugging#/runtime/this-firefox" };
        _instructions = browser == "firefox"
            ? L.T("1. 在 Firefox 地址栏打开 about:debugging#/runtime/this-firefox。\n2. 点击“临时载入附加组件”，选择上方文件夹中的 manifest.json。\n3. 返回 LightDl 点击“注册 / 修复宿主”，确认 LightDl 中“启用浏览器自动接管”已开启。\n\n内置 Firefox 扩展尚未签名，只能临时加载，重启 Firefox 后需重新加载。长期安装需要 Mozilla 签名的 XPI。")
            : L.F($"1. 在浏览器地址栏打开 {ExtensionPage}。\n2. 开启“开发者模式”，点击“加载已解压的扩展程序”，选择上方 extension 文件夹。\n3. 返回 LightDl 点击“注册 / 修复宿主”，确认 LightDl 中“启用浏览器自动接管”已开启。\n\n请保留该文件夹，浏览器会从这里加载扩展。");
        _documentation = browser == "firefox"
            ? "https://extensionworkshop.com/documentation/develop/temporary-installation-in-firefox/"
            : "https://developer.chrome.com/docs/extensions/get-started/tutorial/hello-world#load-unpacked";
        InitializeComponent();
        DataContext = this;
        L.Register(this);
    }

    private async void OpenFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await StorageProvider.TryGetFolderFromPathAsync(ExportDirectory);
            if (folder is not null) await Launcher.LaunchFileAsync(folder);
        }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }
    private async void CopyAddress(object? sender, RoutedEventArgs e)
    {
        try { if (Clipboard is not null) { await Clipboard.SetTextAsync(ExtensionPage); ShowStatus(L.T("扩展页面地址已复制，请粘贴到对应浏览器地址栏。")); } }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }
    private async void OpenDocumentation(object? sender, RoutedEventArgs e)
    {
        try { await Launcher.LaunchUriAsync(new Uri(_documentation)); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }
    private void ShowStatus(string text) { ActionStatus.Text = text; ActionStatus.IsVisible = true; }
    private void Done(object? sender, RoutedEventArgs e) => Close();
}
