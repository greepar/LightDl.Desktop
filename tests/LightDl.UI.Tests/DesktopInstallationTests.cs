using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using LightDl.UI.Services;
using LightDl.UI.Views;
using Xunit;

namespace LightDl.UI.Tests;

[Collection("Download transport")]
public sealed class DesktopInstallationTests(HeadlessAppFixture fixture) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightdl-first-run-").FullName;

    [Fact]
    public void PortableChoiceDoesNotCopyOrCreateLauncher()
    {
        var source = Path.Combine(_root, "Original.AppImage");
        File.WriteAllText(source, "original");
        var data = Path.Combine(_root, "data", "lightdl");
        var installation = new DesktopInstallationService(source, data);
        Assert.True(installation.NeedsChoice);
        Assert.False(Directory.Exists(data));
        installation.ChoosePortable();
        Assert.False(new DesktopInstallationService(source, data).NeedsChoice);
        Assert.False(File.Exists(installation.InstallPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "data", "applications")));
        Assert.Null(installation.RestartPath);
    }

    [Fact]
    public async Task ExplicitInstallCopiesIntegratesAndDoesNotPromptAtEitherPathAgain()
    {
        if (!OperatingSystem.IsLinux()) return;
        await fixture.Session.Dispatch(async () =>
        {
            if (!OperatingSystem.IsLinux()) return true;
            var source = Path.Combine(_root, "Original.AppImage");
            await File.WriteAllTextAsync(source, "appimage-test-content");
            var data = Path.Combine(_root, "data", "lightdl");
            var installation = new DesktopInstallationService(source, data);
            await installation.InstallAsync();
            Assert.Equal("appimage-test-content", await File.ReadAllTextAsync(installation.InstallPath));
            Assert.True(File.Exists(source));
            Assert.Equal(installation.InstallPath, installation.RestartPath);
            Assert.False(new DesktopInstallationService(source, data).NeedsChoice);
            var installed = new DesktopInstallationService(installation.InstallPath, data);
            Assert.True(installed.IsInstalledCopy);
            var desktop = Path.Combine(_root, "data", "applications", "io.lightdl.LightDl.desktop");
            var entry = await File.ReadAllTextAsync(desktop);
            Assert.Contains(installation.InstallPath, entry);
            Assert.Contains("StartupWMClass=LightDl.Desktop", entry);
            Assert.True(File.Exists(Path.Combine(_root, "data", "icons", "hicolor", "512x512", "apps", "lightdl.png")));
            Assert.True((File.GetUnixFileMode(installation.InstallPath) & UnixFileMode.UserExecute) != 0);
            using var validation = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("desktop-file-validate")
            {
                ArgumentList = { desktop }, UseShellExecute = false
            });
            await validation!.WaitForExitAsync();
            Assert.Equal(0, validation.ExitCode);
            return true;
        }, default);
    }

    [Fact]
    public async Task ExistingInstallationIsNotOverwrittenByAnotherDownloadedCopy()
    {
        var source = Path.Combine(_root, "New.AppImage");
        File.WriteAllText(source, "new");
        var data = Path.Combine(_root, "data", "lightdl");
        var installation = new DesktopInstallationService(source, data);
        Directory.CreateDirectory(data);
        File.WriteAllText(installation.InstallPath, "existing");
        await Assert.ThrowsAsync<IOException>(() => installation.InstallAsync());
        Assert.Equal("existing", File.ReadAllText(installation.InstallPath));
        Assert.True(installation.NeedsChoice);
    }

    [Fact]
    public async Task FirstRunWindowPresentsBothChoicesWithoutInstallingOnOpen()
    {
        await fixture.Session.Dispatch(() =>
        {
            var installation = new DesktopInstallationService(Path.Combine(_root, "test.AppImage"), Path.Combine(_root, "data", "lightdl"));
            var window = new FirstRunWindow(installation);
            window.Show();
            try
            {
                var buttons = window.GetLogicalDescendants().OfType<Button>().Select(b => b.Content as string).ToArray();
                Assert.Contains("安装到系统", buttons);
                Assert.Contains("直接运行", buttons);
                Assert.True(installation.NeedsChoice);
                Assert.False(File.Exists(installation.InstallPath));
                Assert.False(window.ContinueRequested);
            }
            finally { window.Close(); }
        }, default);
    }

    [Fact]
    public void DesktopEntryQuotesSpecialCharactersWithoutShellExpansion()
    {
        var entry = DesktopInstallationService.CreateLinuxDesktopEntry("/home/test/a $b%/LightDl.AppImage", "/home/test/icons/lightdl.png");
        Assert.Contains("Exec=\"/home/test/a \\\\$b%%/LightDl.AppImage\" %u", entry);
        Assert.Throws<IOException>(() => DesktopInstallationService.CreateLinuxDesktopEntry("/tmp/bad\npath", "icon.png"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortableCheckboxControlsWhetherTheNextLaunchPrompts(bool remember)
    {
        await fixture.Session.Dispatch(async () =>
        {
            var data = Path.Combine(_root, "data", "lightdl");
            var source = Path.Combine(_root, "portable.AppImage");
            var service = new DesktopInstallationService(source, data);
            var owner = new Window();
            owner.Show();
            var window = new FirstRunWindow(service);
            var result = window.ShowDialog<bool>(owner);
            try
            {
                var checkbox = window.FindControl<CheckBox>("RememberChoiceBox")!;
                Assert.False(checkbox.IsChecked);
                checkbox.IsChecked = remember;
                window.FindControl<Button>("PortableButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(await result);
                Assert.True(window.ContinueRequested);
                Assert.Equal(!remember, new DesktopInstallationService(source, data).NeedsChoice);
                Assert.False(File.Exists(service.InstallPath));
            }
            finally { window.Close(); owner.Close(); }
            return true;
        }, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomInstallationKeepsIntegrationInUserDataAndRemembersTheActualTarget(bool remember)
    {
        if (!OperatingSystem.IsLinux()) return;
        await fixture.Session.Dispatch(async () =>
        {
            var source = Path.Combine(_root, "download.AppImage");
            await File.WriteAllTextAsync(source, "custom install");
            var data = Path.Combine(_root, "data", "lightdl");
            var chosen = Path.Combine(_root, "My Apps");
            var service = new DesktopInstallationService(source, data);
            await service.InstallAsync(chosen, remember);
            Assert.Equal(Path.Combine(chosen, "LightDl.AppImage"), service.InstallPath);
            var installed = new DesktopInstallationService(service.InstallPath, data);
            Assert.Equal(service.InstallPath, installed.InstallPath);
            Assert.True(installed.IsInstalledCopy);
            Assert.False(installed.NeedsChoice);
            Assert.Equal(!remember, new DesktopInstallationService(source, data).NeedsChoice);
            var entry = await File.ReadAllTextAsync(Path.Combine(_root, "data", "applications", "io.lightdl.LightDl.desktop"));
            Assert.Contains(service.InstallPath, entry);
            Assert.True(File.Exists(source));
            return true;
        }, default);
    }

    [Fact]
    public async Task SettingsInstallationDialogCanBeOpenedAfterPortableChoiceAndCancelled()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var service = new DesktopInstallationService(Path.Combine(_root, "portable.AppImage"), Path.Combine(_root, "data", "lightdl"));
            service.ChoosePortable();
            var owner = new Window();
            owner.Show();
            var window = new FirstRunWindow(service, fromSettings: true);
            var result = window.ShowDialog<bool>(owner);
            try
            {
                Assert.False(window.FindControl<CheckBox>("RememberChoiceBox")!.IsVisible);
                Assert.NotNull(window.FindControl<TextBox>("InstallDirectoryBox"));
                var cancel = window.FindControl<Button>("PortableButton")!;
                Assert.Equal("取消", cancel.Content);
                cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(await result);
                Assert.False(window.ContinueRequested);
                Assert.False(service.NeedsChoice);
                Assert.False(File.Exists(service.InstallPath));
            }
            finally { window.Close(); owner.Close(); }
            return true;
        }, default);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
