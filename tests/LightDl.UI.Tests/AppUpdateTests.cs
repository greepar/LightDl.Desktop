using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LightDl.UI.Models;
using LightDl.UI.Services;
using LightDl.UI.Services.Updates;
using LightDl.UI.ViewModels;
using Xunit;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using LightDl.UI.Views;

namespace LightDl.UI.Tests;

[Collection("Download transport")]
public sealed class AppUpdateTests(HeadlessAppFixture fixture) : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lightdl-update-test-" + Guid.NewGuid().ToString("N"))).FullName;


    private static JsonObject Feed(string rid, string version = "1.0.4")
    {
        var extension = rid.StartsWith("win-") ? "zip" : rid.StartsWith("osx-") ? "dmg" : "AppImage";
        var name = $"lightdl-desktop-{rid}.{extension}";
        return new JsonObject
        {
            ["tag_name"] = "v" + version, ["draft"] = false, ["prerelease"] = false, ["body"] = "Release notes",
            ["assets"] = new JsonArray(new JsonObject
            {
                ["name"] = name, ["state"] = "uploaded", ["size"] = 3,
                ["digest"] = "sha256:" + new string('a', 64),
                ["browser_download_url"] = $"{UpdateFeed.Repository}/releases/download/v{version}/{name}"
            })
        };
    }

    [Theory]
    [InlineData("win-x86", "zip")]
    [InlineData("win-x64", "zip")]
    [InlineData("win-arm64", "zip")]
    [InlineData("linux-x64", "AppImage")]
    [InlineData("linux-arm64", "AppImage")]
    [InlineData("osx-x64", "dmg")]
    [InlineData("osx-arm64", "dmg")]
    public void SelectsOnlyTheExactPlatformPackage(string rid, string extension)
    {
        var release = UpdateFeed.Parse(Feed(rid).ToJsonString(), "1.0.3", rid);
        Assert.Equal($"lightdl-desktop-{rid}.{extension}", release!.AssetName);
        Assert.Equal("1.0.4", release.Version);
    }

    [Theory]
    [InlineData("1.0.3")]
    [InlineData("1.0.2")]
    public void NeverDowngradesOrReinstalls(string version) =>
        Assert.Null(UpdateFeed.Parse(Feed("linux-x64", version).ToJsonString(), "1.0.3", "linux-x64"));

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("tag_name")]
    [InlineData("digest")]
    [InlineData("size")]
    [InlineData("url")]
    [InlineData("state")]
    public void RejectsUntrustedOrIncompleteRelease(string mutation)
    {
        var feed = Feed("linux-x64");
        var asset = feed["assets"]![0]!;
        switch (mutation)
        {
            case "draft": case "prerelease": feed[mutation] = true; break;
            case "tag_name": feed[mutation] = "v1.0.4-beta"; break;
            case "digest": asset[mutation] = null; break;
            case "size": asset[mutation] = UpdateFeed.MaximumPackageSize + 1; break;
            case "url": asset["browser_download_url"] = "https://example.com/program"; break;
            case "state": asset[mutation] = "new"; break;
        }
        Assert.Throws<InvalidDataException>(() => UpdateFeed.Parse(feed.ToJsonString(), "1.0.3", "linux-x64"));
    }

    [Fact]
    public void MissingArchitectureDoesNotFallbackToAnotherPackage() =>
        Assert.Throws<InvalidDataException>(() => UpdateFeed.Parse(Feed("linux-arm64").ToJsonString(), "1.0.3", "linux-x64"));

    [Theory]
    [InlineData("abc", true)]
    [InlineData("bad", false)]
    [InlineData("abcdef", false)]
    [InlineData("ab", false)]
    public async Task OnlyPublishesDownloadsWithMatchingDigestAndSize(string body, bool valid)
    {
        using var client = new HttpClient(new StaticHandler(body));
        var expectedHash = Convert.ToHexString(SHA256.HashData("abc"u8.ToArray())).ToLowerInvariant();
        var release = new UpdateRelease("1.0.4", "", "test", "https://example.com/test", 3, expectedHash);
        var destination = Path.Combine(_directory, "package");
        if (valid)
        {
            await UpdateFeed.DownloadAsync(client, release, destination, null, default);
            Assert.Equal("abc", await File.ReadAllTextAsync(destination));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => UpdateFeed.DownloadAsync(client, release, destination, null, default));
            Assert.False(File.Exists(destination));
        }
        Assert.False(File.Exists(destination + ".download"));
    }

    [Fact]
    public async Task WindowsArchiveDoesNotExtractUnrelatedPaths()
    {
        var archive = Path.Combine(_directory, "test.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("../escape").Open())) writer.Write("malicious");
            using var executable = new StreamWriter(zip.CreateEntry("LightDl.Desktop.exe").Open());
            executable.Write("program");
        }
        var destination = Path.Combine(_directory, "Next");
        await DesktopUpdateService.ExtractWindowsExecutableAsync(archive, destination, default);
        Assert.Equal("program", await File.ReadAllTextAsync(destination));
        Assert.False(File.Exists(Path.Combine(_directory, "escape")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_directory)!, "escape")));
    }

    private async Task<UpdateTransaction> PlanAsync()
    {
        var plan = new UpdateTransaction
        {
            Target = Path.Combine(_directory, "program"),
            Stage = Path.Combine(_directory, ".lightdl-update-" + Guid.NewGuid().ToString("N")),
            Version = "1.0.4", ParentPid = Environment.ProcessId
        };
        Directory.CreateDirectory(plan.Stage);
        await File.WriteAllTextAsync(plan.Target, "old");
        await File.WriteAllTextAsync(plan.Candidate, "new");
        plan.ExecutableHash = await UpdateTransaction.HashAsync(plan.Candidate);
        plan.Save();
        return plan;
    }

    [Fact]
    public async Task ReplacesOnlyAfterVerificationAndRetainsPreviousFile()
    {
        var plan = await PlanAsync();
        await UpdateInstaller.ApplyAsync(plan, async (p, rollback, _) =>
        {
            Assert.False(rollback);
            Assert.Equal("new", await File.ReadAllTextAsync(p.Target));
        }, default);
        Assert.Equal("old", await File.ReadAllTextAsync(plan.Backup));
        Assert.True(File.Exists(Path.Combine(plan.Stage, "completed")));
    }

    [Fact]
    public async Task LaunchFailureRestoresAndRelaunchesOldProgram()
    {
        var plan = await PlanAsync();
        var rollbackSeen = false;
        await Assert.ThrowsAsync<IOException>(() => UpdateInstaller.ApplyAsync(plan, async (p, rollback, _) =>
        {
            if (!rollback) throw new IOException("startup failure");
            rollbackSeen = true;
            Assert.Equal("old", await File.ReadAllTextAsync(p.Target));
        }, default));
        Assert.True(rollbackSeen);
        Assert.Equal("old", await File.ReadAllTextAsync(plan.Target));
        Assert.Equal("new", await File.ReadAllTextAsync(plan.Candidate));
        Assert.False(File.Exists(Path.Combine(plan.Stage, "completed")));
    }

    [Fact]
    public async Task ReceiptWriteFailureDoesNotRollbackAnAlreadyRunningNewVersion()
    {
        var plan = await PlanAsync();
        var launched = 0;
        await UpdateInstaller.ApplyAsync(plan, (p, rollback, _) =>
        {
            Assert.False(rollback);
            launched++;
            Directory.CreateDirectory(Path.Combine(p.Stage, "completed"));
            return Task.CompletedTask;
        }, default);
        Assert.Equal(1, launched);
        Assert.Equal("new", await File.ReadAllTextAsync(plan.Target));
        Assert.Equal("old", await File.ReadAllTextAsync(plan.Backup));
    }

    [Fact]
    public async Task SettingsViewBindsUpdateActionsAndDiscoveryStatus()
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var client = new HttpClient(new StaticHandler(Feed(DesktopUpdateService.GetRuntimeId()).ToJsonString()));
            var viewModel = new MainViewModel(new AppSettingsService(Path.Combine(_directory, "settings.json")),
                new DownloadTaskStore(Path.Combine(_directory, "tasks.json")), new BrowserHostRegistrationService(),
                new DesktopUpdateService("1.0.3", client)) { SelectedSection = "settings" };
            var view = new MainView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 1180, Height = 760 };
            window.Show();
            try
            {
                await viewModel.Updates.CheckForUpdatesCommand.ExecuteAsync(null);
                Assert.True(viewModel.Updates.HasUpdate);
                var buttons = view.FindControl<Border>("SettingsUpdateCard")!.GetLogicalDescendants().OfType<Button>().ToArray();
                var download = Assert.Single(buttons, b => b.Content as string == "下载更新");
                Assert.Same(viewModel.Updates.DownloadUpdateCommand, download.Command);
                Assert.True(download.IsVisible);
                Assert.True(download.Command!.CanExecute(null));
                Assert.Contains("1.0.4", viewModel.Updates.Status);
                Assert.Same(viewModel.Updates.InstallUpdateCommand,
                    Assert.Single(buttons, b => b.Content as string == "重启安装").Command);
                var host = view.FindControl<SukiUI.Controls.SukiDialogHost>("UpdateDialogHost");
                Assert.NotNull(host);
                Assert.Same(viewModel.Updates.PromptDialogManager, host.Manager);
                viewModel.Updates.TryShowUpdatePrompt(true);
                window.UpdateLayout();
                Assert.True(viewModel.Updates.IsUpdatePromptOpen);
                viewModel.Updates.PromptDialogManager.DismissDialog();
                Assert.False(viewModel.Updates.IsUpdatePromptOpen);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TamperedStagedProgramNeverReplacesCurrentProgram()
    {
        var plan = await PlanAsync();
        await File.WriteAllTextAsync(plan.Candidate, "changed after download");
        await Assert.ThrowsAsync<IOException>(() => UpdateInstaller.ApplyAsync(plan, (_, rollback, _) =>
        {
            Assert.True(rollback);
            return Task.CompletedTask;
        }, default));
        Assert.Equal("old", await File.ReadAllTextAsync(plan.Target));
        Assert.False(File.Exists(plan.Backup));
    }

    [Fact]
    public async Task SerializedTransactionDoesNotAcceptAnOutsideStagingDirectory()
    {
        var plan = await PlanAsync();
        var loaded = UpdateTransaction.Load(plan.PlanFile);
        Assert.Equal(plan.Candidate, loaded.Candidate);
        loaded.Stage = Path.Combine(_directory, "unrelated");
        Assert.Throws<InvalidDataException>(loaded.Validate);
    }

    [Fact]
    public void UnsavedSettingsPreventRestartAndNewSettingRoundTrips()
    {
        var settings = new SettingsViewModel(new AppSettings());
        var updates = new AppUpdateViewModel(null, settings) { IsReadyToInstall = true };
        Assert.True(updates.InstallUpdateCommand.CanExecute(null));
        settings.CheckForUpdatesAutomatically = false;
        Assert.False(updates.InstallUpdateCommand.CanExecute(null));
        Assert.False(settings.ToModel().CheckForUpdatesAutomatically);
        settings.MarkSaved(settings.ToModel());
        Assert.True(updates.InstallUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpdatePromptWaitsForVisibleUiAndDoesNotRepeatSameVersion()
    {
        using var client = new HttpClient(new StaticHandler(Feed(DesktopUpdateService.GetRuntimeId()).ToJsonString()));
        var vm = new AppUpdateViewModel(new DesktopUpdateService("1.0.3", client), new SettingsViewModel(new AppSettings()));
        var discovered = 0;
        vm.UpdateDiscovered += () => { discovered++; vm.TryShowUpdatePrompt(false); };
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(1, discovered);
        Assert.True(vm.HasUpdate);
        Assert.False(vm.IsUpdatePromptOpen);
        vm.TryShowUpdatePrompt(true);
        Assert.True(vm.IsUpdatePromptOpen);
        Assert.Equal("1.0.4", vm.AvailableVersion);
        vm.DismissUpdatePromptCommand.Execute(null);
        Assert.False(vm.IsUpdatePromptOpen);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.TryShowUpdatePrompt(true);
        Assert.False(vm.IsUpdatePromptOpen);
        Assert.Equal(0, vm.Progress);
        Assert.False(vm.IsReadyToInstall);
    }

    [Fact]
    public void NotesRemoveDownloadTableAndKeepActualChanges()
    {
        const string body = "## Downloads / 下载\n\n| Platform | Download |\n| Linux | [AppImage](https://example.com/file) |\n\nLinux: chmod +x file\n\n## What's Changed\n* **Fix resume** by @author\n\n**Full Changelog**: https://example.com/compare";
        var notes = ReleaseNotesFormatter.Format(body);
        Assert.Contains("Fix resume", notes);
        Assert.DoesNotContain("AppImage", notes);
        Assert.DoesNotContain("chmod", notes);
        Assert.DoesNotContain("**", notes);
        Assert.DoesNotContain("https://", notes);
    }

    [Fact]
    public void DownloadOnlyBodyShowsHonestFallbackInsteadOfInventedChanges()
    {
        var notes = ReleaseNotesFormatter.Format("## Downloads / 下载\n| Linux | URL |\n\n**Full Changelog**: https://example.com/compare");
        Assert.Contains("未提供具体更新说明", notes);
    }

    [Fact]
    public void MarkedDownloadsAreRemovedWithoutEatingFollowingChanges()
    {
        var notes = ReleaseNotesFormatter.Format("<!-- lightdl-downloads:start -->\n## Downloads\n| URL |\n<!-- lightdl-downloads:end -->\n\n## 更新内容\n- [修复任务](https://example.com)\n");
        Assert.Equal("更新内容\n• 修复任务", notes);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class StaticHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content)) });
    }
}
