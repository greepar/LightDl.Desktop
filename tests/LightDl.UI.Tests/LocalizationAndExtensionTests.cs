using System.Globalization;
using System.IO.Compression;
using System.Resources;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using LightDl.UI.Services;
using LightDl.UI.Views;
using Xunit;

namespace LightDl.UI.Tests;

[Collection("Download transport")]
public sealed class LocalizationAndExtensionTests(HeadlessAppFixture fixture) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightdl-bundle-").FullName;

    [Fact]
    public async Task ScrollingSettingsClosesDropdownWithoutChangingTheSelectedValue()
    {
        await fixture.Session.Dispatch(() =>
        {
            var model = new LightDl.UI.ViewModels.MainViewModel(new AppSettingsService(Path.Combine(_root, "settings.json")),
                new DownloadTaskStore(Path.Combine(_root, "tasks.json")), new BrowserHostRegistrationService())
                { SelectedSection = "settings" };
            var view = new MainView { DataContext = model };
            var window = new Window { Content = view, Width = 1180, Height = 760 };
            window.Show();
            try
            {
                var scroll = view.FindControl<ScrollViewer>("SettingsScrollViewer")!;
                var selector = view.FindControl<ComboBox>("Http3ModeSelector")!;
                selector.BringIntoView();
                window.UpdateLayout();
                var selected = selector.SelectedIndex;
                var dirty = model.Settings.HasUnsavedChanges;
                selector.IsDropDownOpen = true;
                Assert.True(selector.IsDropDownOpen);
                scroll.Offset += new Avalonia.Vector(0, 80);
                Assert.False(selector.IsDropDownOpen);
                Assert.Equal(selected, selector.SelectedIndex);
                Assert.Equal(dirty, model.Settings.HasUnsavedChanges);
                selector.IsDropDownOpen = true;
                Assert.True(selector.IsDropDownOpen);
                model.SelectedSection = "all";
                Assert.False(selector.IsDropDownOpen);
            }
            finally { window.Close(); }
        }, default);
    }

    [Theory]
    [InlineData(0, "zh-TW", "zh-CN")]
    [InlineData(0, "en-US", "en-US")]
    [InlineData(0, "de-DE", "en-US")]
    [InlineData(1, "en-US", "zh-CN")]
    [InlineData(2, "zh-CN", "en-US")]
    [InlineData(99, "zh-CN", "zh-CN")]
    public void LanguagePreferenceOverridesOrFollowsTheSystem(int mode, string system, string expected)
        => Assert.Equal(expected, LightDl.UI.Localization.L.ResolveCulture(mode, CultureInfo.GetCultureInfo(system)).Name);

    [Fact]
    public async Task LanguageSelectionIsSavedAndUnsavedEditsCanBeDiscarded()
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var culture = new CultureScope(L.Culture.Name);
            var service = new AppSettingsService(Path.Combine(_root, "settings.json"));
            var viewModel = new LightDl.UI.ViewModels.MainViewModel(service,
                new DownloadTaskStore(Path.Combine(_root, "tasks.json")), new BrowserHostRegistrationService());
            var settings = viewModel.Settings;
            Assert.Equal(0, settings.LanguageMode);
            settings.LanguageMode = 2;
            Assert.True(settings.HasUnsavedChanges);
            Assert.Equal(0, service.Load().LanguageMode);
            settings.DiscardChanges();
            Assert.Equal(0, settings.LanguageMode);
            Assert.False(settings.HasUnsavedChanges);
            settings.LanguageMode = 1;
            await viewModel.SaveSettingsCommand.ExecuteAsync(null);
            Assert.False(settings.HasUnsavedChanges);
            Assert.Equal(1, service.Load().LanguageMode);
            Assert.Equal(L.T("设置已保存，新配置将在后续下载中生效"), viewModel.StatusMessage);
            settings.LanguageMode = 2;
            settings.DiscardChanges();
            Assert.Equal(1, settings.LanguageMode);
            return true;
        }, default);
    }

    [Fact]
    public async Task LanguageSelectorOffersSystemChineseAndEnglish()
    {
        await fixture.Session.Dispatch(() =>
        {
            var viewModel = new LightDl.UI.ViewModels.MainViewModel(new AppSettingsService(Path.Combine(_root, "settings.json")),
                new DownloadTaskStore(Path.Combine(_root, "tasks.json")), new BrowserHostRegistrationService());
            var view = new MainView { DataContext = viewModel };
            var window = new Window { Content = view };
            window.Show();
            try
            {
                var selector = view.FindControl<ComboBox>("LanguageSelector")!;
                Assert.Equal(3, selector.Items.Count);
                selector.SelectedIndex = 2;
                Assert.Equal(2, viewModel.Settings.LanguageMode);
                Assert.True(viewModel.Settings.HasUnsavedChanges);
                Assert.Equal("English", Assert.IsType<TextBlock>(((ComboBoxItem)selector.Items[2]!).Content).Text);
            }
            finally { window.Close(); }
        }, default);
    }

    [Fact]
    public async Task SavingLanguageRefreshesTheExistingViewAndPreservesDownloadsAndNavigation()
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var culture = new CultureScope("zh-CN");
            var service = new AppSettingsService(Path.Combine(_root, "settings.json"));
            var model = new LightDl.UI.ViewModels.MainViewModel(service,
                new DownloadTaskStore(Path.Combine(_root, "tasks.json")), new BrowserHostRegistrationService())
                { SelectedSection = "settings" };
            var item = new LightDl.UI.ViewModels.DownloadItemViewModel(new LightDl.UI.Models.DownloadTaskRecord
            {
                SourceUrl = "https://example.test/file", DestinationDirectory = _root,
                FileName = "准备就绪.zip", State = LightDl.UI.Models.DownloadState.Paused,
                ProgressPercentage = 42.5, SizeText = "未知大小", ProtocolText = "HTTP/2（本任务已回退）", UseHttp3Fallback = true
            }, new LightDl.UI.Models.AppSettings());
            model.Downloads.Add(item);
            var view = new MainView { DataContext = model };
            var window = new Window { Content = view, Width = 1180, Height = 760 };
            window.Show();
            try
            {
                model.StatusMessage = L.F($"已添加 {item.FileName}");
                L.SetCulture(CultureInfo.GetCultureInfo("en-US"));
                Assert.Equal("Added 准备就绪.zip", model.StatusMessage);
                Assert.Contains(view.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Save settings"));
                Assert.Equal("Paused", item.StatusText);
                Assert.Equal("HTTP/2 (fallback for this task)", item.ProtocolText);
                L.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
                Assert.Equal("已添加 准备就绪.zip", model.StatusMessage);
                Assert.Equal("已暂停", item.StatusText);

                // The closed selector must refresh too, not just its dropdown items.
                foreach (var selector in view.GetLogicalDescendants().OfType<ComboBox>())
                {
                    if (selector.SelectedItem is not ComboBoxItem { Content: TextBlock text }) continue;
                    Assert.NotNull(selector.SelectionBoxItem);
                    var chinese = text.Text;
                    var selection = selector.SelectedIndex;
                    L.SetCulture(CultureInfo.GetCultureInfo("en-US"));
                    Assert.Equal(L.Relocalize(chinese!), text.Text);
                    Assert.Contains(selector.GetVisualDescendants().OfType<TextBlock>(), rendered => rendered.Text == text.Text);
                    Assert.Equal(selection, selector.SelectedIndex);
                    Assert.Same(text, selector.SelectionBoxItem);
                    L.SetCulture(CultureInfo.GetCultureInfo("zh-CN"));
                    Assert.Equal(chinese, text.Text);
                    Assert.Contains(selector.GetVisualDescendants().OfType<TextBlock>(), rendered => rendered.Text == chinese);
                }

                model.Settings.MaxRetry = 11;
                model.Settings.LanguageMode = 2;
                await model.SaveSettingsCommand.ExecuteAsync(null);
                Assert.Equal("en-US", L.CultureName);
                Assert.Contains(view.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Save settings"));
                Assert.Equal("settings", model.SelectedSection);
                Assert.Same(view, window.Content);
                Assert.Same(item, Assert.Single(model.Downloads));
                Assert.Equal(42.5, item.ProgressPercentage);
                Assert.Equal("准备就绪.zip", item.FileName);
                Assert.Equal(11, model.Settings.MaxRetry);
                Assert.Equal(2, service.Load().LanguageMode);
                Assert.False(model.Settings.HasUnsavedChanges);
                model.Settings.LanguageMode = 1;
                Assert.Equal("en-US", L.CultureName);
                model.Settings.DiscardChanges();
                Assert.Equal(2, model.Settings.LanguageMode);
                Assert.Equal("en-US", L.CultureName);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Theory]
    [InlineData("zh-CN", "直接运行")]
    [InlineData("zh-TW", "直接运行")]
    [InlineData("en-US", "Run directly")]
    [InlineData("de-DE", "Run directly")]
    public async Task FirstRunUsesTheSystemLanguageWithEnglishFallback(string culture, string expected)
    {
        await fixture.Session.Dispatch(() =>
        {
            using var scope = new CultureScope(culture);
            var window = new FirstRunWindow(new DesktopInstallationService(Path.Combine(_root, "app"), _root));
            window.Show();
            try
            {
                Assert.Equal(expected, window.FindControl<Button>("PortableButton")!.Content);
                Assert.Equal(culture.StartsWith("zh") ? "zh-CN" : "en-US", L.CultureName);
                var title = new LightDl.UI.ViewModels.DownloadDialogViewModel().DialogTitle;
                Assert.Equal(culture.StartsWith("zh") ? "新建下载任务" : "New download", title);
            }
            finally { window.Close(); }
        }, default);
    }

    [Theory]
    [InlineData("en-US", "3/16 connections (active/limit)")]
    [InlineData("zh-CN", "3/16 线程（活动/上限）")]
    public async Task InterpolatedLabelsKeepTheirArguments(string culture, string expected)
    {
        await fixture.Session.Dispatch(() =>
        {
            using var scope = new CultureScope(culture);
            Assert.Equal(expected, L.F($"{3}/{16} 线程（活动/上限）"));
        }, default);
    }

    [Fact]
    public void EnglishCatalogHasNoUntranslatedChineseAndPreservesFormatArguments()
    {
        var resources = new ResourceManager("LightDl.UI.Localization.English", typeof(LightDl.UI.Localization.L).Assembly);
        using var set = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        Assert.NotNull(set);
        Assert.True(set.Cast<System.Collections.DictionaryEntry>().Count() > 250);
        foreach (System.Collections.DictionaryEntry entry in set)
        {
            var source = (string)entry.Key;
            var translation = Assert.IsType<string>(entry.Value);
            Assert.DoesNotMatch("[\\u4e00-\\u9fff]", translation);
            var arguments = System.Text.RegularExpressions.Regex.Matches(source, @"\{\d+(?:[^}]*)\}").Select(m => m.Value).Order().ToArray();
            var translated = System.Text.RegularExpressions.Regex.Matches(translation, @"\{\d+(?:[^}]*)\}").Select(m => m.Value).Order().ToArray();
            Assert.Equal(arguments, translated);
        }
    }

    [Theory]
    [InlineData("en-US", "未知大小", "Unknown size", "HTTP/2 (fallback for this task)")]
    [InlineData("zh-CN", "Unknown size", "未知大小", "HTTP/2（本任务已回退）")]
    public async Task RestoredDownloadsUseTheCurrentLanguage(string culture, string savedSize, string expectedSize, string expectedProtocol)
    {
        await fixture.Session.Dispatch(() =>
        {
            using var scope = new CultureScope(culture);
            var record = new LightDl.UI.Models.DownloadTaskRecord
            {
                SourceUrl = "https://example.test/file", DestinationDirectory = _root,
                SizeText = savedSize, ProtocolText = "HTTP/2（本任务已回退）", UseHttp3Fallback = true,
                State = LightDl.UI.Models.DownloadState.Paused
            };
            var item = new LightDl.UI.ViewModels.DownloadItemViewModel(record, new LightDl.UI.Models.AppSettings());
            Assert.Equal(expectedSize, item.SizeText);
            Assert.Equal(expectedProtocol, item.ProtocolText);
        }, default);
    }

    [Theory]
    [InlineData("chrome", "chromium")]
    [InlineData("edge", "chromium")]
    [InlineData("firefox", "firefox")]
    public async Task BundledExtensionsExportCompleteArchivesAndUnpackedFolders(string browser, string bundle)
    {
        var result = await BrowserExtensionBundle.ExportAsync(browser, _root);
        using var zip = ZipFile.OpenRead(result.Archive);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("background.js"));
        Assert.NotNull(zip.GetEntry("icons/icon-128.png"));
        Assert.Contains(bundle, Path.GetFileName(result.Archive));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.Manifest));
        Assert.Equal(3, manifest.RootElement.GetProperty("manifest_version").GetInt32());
        if (browser == "firefox") Assert.Equal("automatic-integration@lightdl.io", manifest.RootElement.GetProperty("browser_specific_settings").GetProperty("gecko").GetProperty("id").GetString());
        else Assert.True(manifest.RootElement.TryGetProperty("key", out _));
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var data = new MemoryStream();
            await stream.CopyToAsync(data);
            Assert.Equal(data.ToArray(), await File.ReadAllBytesAsync(Path.Combine(result.Directory, entry.FullName)));
        }
        var next = await BrowserExtensionBundle.ExportAsync(browser, _root);
        Assert.NotEqual(result.Directory, next.Directory);
        Assert.True(File.Exists(result.Manifest));
    }

    [Fact]
    public async Task ExtensionGuideExplainsFirefoxTemporaryLoadingInBothLanguages()
    {
        await fixture.Session.Dispatch(() =>
        {
            foreach (var culture in new[] { "zh-CN", "en-US" })
            {
                using var scope = new CultureScope(culture);
                var window = new BrowserExtensionWindow("firefox", new BrowserExtensionExport(_root, "archive", "manifest"));
                window.Show();
                try
                {
                    Assert.Equal("about:debugging#/runtime/this-firefox", window.ExtensionPage);
                    Assert.Contains("manifest.json", window.Instructions);
                    Assert.Contains(culture == "zh-CN" ? "重启 Firefox 后需重新加载" : "Reload it after restarting Firefox", window.Instructions);
                }
                finally { window.Close(); }
            }
        }, default);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task WindowsInstallCreatesOnlySelectedShortcutsInRedirectedUserFolders(bool desktopShortcut, bool startMenuShortcut)
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = Path.Combine(_root, "LightDl 测试 $app.exe");
        // Exercise an actual installed EXE with a Unicode and shell-special filename.
        File.Copy(Environment.ProcessPath!, target);
        var programs = Path.Combine(_root, "Programs");
        var desktop = Path.Combine(_root, "OneDrive Desktop");
        await DesktopInstallationService.IntegrateWindowsAsync(target, programs, desktop,
            createDesktopShortcut: desktopShortcut, createStartMenuShortcut: startMenuShortcut);
        Assert.Equal(startMenuShortcut, File.Exists(Path.Combine(programs, "LightDl", "LightDl.lnk")));
        Assert.Equal(desktopShortcut, File.Exists(Path.Combine(desktop, "LightDl.lnk")));
        if (desktopShortcut) Assert.Equal(target, WindowsShortcut.ReadTarget(Path.Combine(desktop, "LightDl.lnk")));
        if (startMenuShortcut) Assert.Equal(target, WindowsShortcut.ReadTarget(Path.Combine(programs, "LightDl", "LightDl.lnk")));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _previousLanguage = L.Culture;
        private readonly CultureInfo? _previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        public CultureScope(string name) => L.SetCulture(CultureInfo.GetCultureInfo(name));
        public void Dispose()
        {
            L.SetCulture(_previousLanguage);
            CultureInfo.CurrentUICulture = _previous;
            CultureInfo.DefaultThreadCurrentUICulture = _previousDefault;
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
