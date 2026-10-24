using LightDl.UI.Models;
using LightDl.UI.Services;
using LightDl.UI.ViewModels;
using Xunit;

namespace LightDl.UI.Tests;

public sealed class Http3SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lightdl-settings-tests-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    public async Task SelectionSurvivesViewModelSaveReloadAndConfigCreation(int mode, bool enabled, bool forced)
    {
        var service = new AppSettingsService(SettingsPath);
        var viewModel = new SettingsViewModel(service.Load()) { Http3Mode = mode, ProxyMode = 1 };
        await service.SaveAsync(viewModel.ToModel());
        var restored = new SettingsViewModel(new AppSettingsService(SettingsPath).Load());
        Assert.Equal(mode, restored.Http3Mode);
        var config = DownloadConfigFactory.Create(restored.ToModel());
        Assert.Equal(enabled, config.EnableHttp3);
        Assert.Equal(forced, config.ForceHttp3);
        Assert.False(config.UseProxy);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Http3Mode\":-1}")]
    [InlineData("{\"Http3Mode\":99}")]
    public void ExistingOrInvalidSettingsUseAutomaticMode(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);
        var settings = new AppSettingsService(SettingsPath).Load();
        Assert.Equal(0, settings.Http3Mode);
        Assert.True(DownloadConfigFactory.Create(settings).EnableHttp3);
        Assert.False(DownloadConfigFactory.Create(settings).ForceHttp3);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, true)]
    [InlineData(1, 0, false)]
    [InlineData(2, 0, false)]
    public void ProxyExplanationMatchesMode(int mode, int proxy, bool showNote)
    {
        var vm = new SettingsViewModel(new AppSettings { Http3Mode = mode, ProxyMode = proxy });
        Assert.Equal(showNote, vm.ShowHttp3ProxyNote);
        Assert.False(string.IsNullOrWhiteSpace(vm.Http3ModeDescription));
    }

    [Fact]
    public void ChangingSelectionRefreshesDescriptionAndProxyNote()
    {
        var vm = new SettingsViewModel(new AppSettings());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.Http3Mode = 1;
        Assert.Contains(nameof(vm.Http3ModeDescription), changed);
        Assert.Contains(nameof(vm.ShowHttp3ProxyNote), changed);
        changed.Clear();
        vm.ProxyMode = 1;
        Assert.Contains(nameof(vm.ShowHttp3ProxyNote), changed);
        Assert.Contains(nameof(vm.UsesCustomProxy), changed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ProtocolChoicePreservesProxyAndOtherDownloadSettings(int mode)
    {
        Action<LightDownloadProtocolSelection> callback = _ => { };
        var config = DownloadConfigFactory.Create(new AppSettings
        {
            Http3Mode = mode, ProxyMode = 2, ProxyUrl = "http://127.0.0.1:8888",
            IgnoreSslErrors = true, TimeoutSeconds = 45, ChunkCount = 6,
            SpeedLimitKib = 128, UserAgent = "LightDl-settings-test", EnableResume = false
        }, callback);
        Assert.True(config.UseProxy);
        Assert.Equal(new Uri("http://127.0.0.1:8888"), config.Proxy!.GetProxy(new Uri("https://example.org")));
        Assert.True(config.IgnoreSslErrors);
        Assert.Equal(TimeSpan.FromSeconds(45), config.Timeout);
        Assert.Equal(6, config.ChunkCount);
        Assert.Equal(128 * 1024d, config.SpeedLimitProvider!());
        Assert.Equal("LightDl-settings-test", config.UserAgent);
        Assert.False(config.EnableResume);
        Assert.Same(callback, config.ProtocolSelectionHandler);
    }

    [Fact]
    public void UnsavedChangesTrackAllEditsAndHideWhenValuesAreRestored()
    {
        var vm = new SettingsViewModel(new AppSettings());
        Assert.False(vm.HasUnsavedChanges);
        vm.Http3Mode = 1;
        Assert.True(vm.HasUnsavedChanges);
        vm.UserAgent = "changed";
        vm.Http3Mode = 0;
        Assert.True(vm.HasUnsavedChanges);
        vm.UserAgent = string.Empty;
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void SavingAnEarlierSnapshotKeepsNewerEditsUnsaved()
    {
        var vm = new SettingsViewModel(new AppSettings());
        vm.MaxRetry = 12;
        var submitted = vm.ToModel();
        vm.MaxRetry = 14;
        vm.MarkSaved(submitted);
        Assert.True(vm.HasUnsavedChanges);
        vm.MaxRetry = 12;
        Assert.False(vm.HasUnsavedChanges);
        vm.ProxyMode = 1;
        vm.MarkSaved(vm.ToModel());
        Assert.False(vm.HasUnsavedChanges);
        vm.ProxyMode = 0;
        Assert.True(vm.HasUnsavedChanges);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
