using LightDl.UI.Models;
using LightDl.UI.Services;
using LightDl.UI.ViewModels;
using Xunit;

namespace LightDl.UI.Tests;

public sealed class SettingsNavigationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lightdl-navigation-" + Guid.NewGuid());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task LeavingSettingsSavesDiscardsOrStays(bool? decision)
    {
        var service = new AppSettingsService(Path.Combine(_directory, "settings.json"));
        await service.SaveAsync(new AppSettings());
        var vm = new MainViewModel(service, new DownloadTaskStore(Path.Combine(_directory, "tasks.json")),
            new BrowserHostRegistrationService()) { SelectedSection = "settings" };
        var original = vm.Settings.MaxRetry;
        vm.Settings.MaxRetry++;
        var prompts = 0;
        vm.ConfirmLeaveSettingsAsync = () => { prompts++; return Task.FromResult(decision); };
        await vm.SelectSectionCommand.ExecuteAsync("all");
        Assert.Equal(1, prompts);
        Assert.Equal(decision is null ? "settings" : "all", vm.SelectedSection);
        Assert.Equal(decision is null, vm.Settings.HasUnsavedChanges);
        Assert.Equal(decision == false ? original : original + 1, vm.Settings.MaxRetry);
        Assert.Equal(decision == true ? original + 1 : original, service.Load().MaxRetry);
        Assert.Equal(decision is null, vm.ShowSettingsSave);
    }

    [Fact]
    public async Task FailedSaveKeepsSettingsAndEdits()
    {
        var vm = new MainViewModel(new AppSettingsService(Path.Combine(_directory, "settings.json")),
            new DownloadTaskStore(Path.Combine(_directory, "tasks.json")), new BrowserHostRegistrationService())
            { SelectedSection = "settings" };
        vm.Settings.DefaultDownloadDirectory = "";
        vm.ConfirmLeaveSettingsAsync = () => Task.FromResult<bool?>(true);
        await vm.SelectSectionCommand.ExecuteAsync("browser");
        Assert.Equal("settings", vm.SelectedSection);
        Assert.True(vm.Settings.HasUnsavedChanges);
        Assert.Contains("下载目录", vm.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData(WindowCloseAction.Exit, 1)]
    [InlineData(WindowCloseAction.RunInBackground, 2)]
    public async Task RememberCloseChoicePersistsOnlyThatChoice(WindowCloseAction action, int expected)
    {
        var service = new AppSettingsService(Path.Combine(_directory, "settings.json"));
        await service.SaveAsync(new AppSettings());
        var vm = new MainViewModel(service, new DownloadTaskStore(Path.Combine(_directory, "tasks.json")),
            new BrowserHostRegistrationService());
        var original = vm.Settings.MaxRetry;
        vm.Settings.MaxRetry++;
        Assert.True(await vm.RememberWindowCloseActionAsync(action));
        Assert.Equal(expected, service.Load().WindowCloseBehavior);
        Assert.Equal(original, service.Load().MaxRetry);
        Assert.Equal(original + 1, vm.Settings.MaxRetry);
        Assert.True(vm.Settings.HasUnsavedChanges);
        vm.Settings.DiscardChanges();
        Assert.Equal(expected, vm.Settings.WindowCloseBehavior);
        Assert.False(vm.Settings.HasUnsavedChanges);
        vm.Settings.WindowCloseBehavior = 0;
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal(0, service.Load().WindowCloseBehavior);
    }
}
