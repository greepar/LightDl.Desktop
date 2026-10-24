using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LightDl.UI.Views;
using SukiUI;
using SukiUI.Controls;
using LightDl.UI.Models;
using LightDl.UI.Services;
using LightDl.UI.ViewModels;
using Xunit;

namespace LightDl.UI.Tests;

[CollectionDefinition("Download transport", DisableParallelization = true)]
public sealed class DownloadTransportCollection;

[Collection("Download transport")]
public sealed class Http3FallbackTests
{
    public sealed class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    public sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new SukiTheme());
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task ConnectionFailureAsksOnceAndOnlyRetriesAfterApproval(bool accept, int tcpVersion)
    {
        await RunAsync(async (directory, origin) =>
        {
            origin.TcpVersion = tcpVersion;
            var settings = new AppSettings { Http3Mode = 1, ProxyMode = 1 };
            var item = new DownloadItemViewModel("https://download.test/file.bin", directory, settings);
            var asked = 0;
            item.ConfirmHttp3FallbackAsync = async (download, _, ct) =>
            {
                asked++;
                Assert.Same(item, download);
                Assert.All(origin.Requests, version => Assert.Equal(3, version.Major));
                Assert.Equal(DownloadState.Downloading, item.State);
                Assert.Contains("等待选择", item.StatusText);
                Assert.Contains("连接失败", item.ProtocolText);
                await Task.Delay(10, ct);
                return accept;
            };
            await item.StartAsync();
            Assert.Equal(1, asked);
            Assert.Equal(1, settings.Http3Mode);
            Assert.Equal(accept ? DownloadState.Completed : DownloadState.Failed, item.State);
            Assert.Equal(accept, item.ToRecord().UseHttp3Fallback);
            if (accept)
            {
                Assert.Contains($"HTTP/{(tcpVersion == 1 ? "1.1" : "2")}", item.ProtocolText);
                Assert.Contains("本任务已回退", item.ProtocolText);
                Assert.Equal(origin.Payload, await File.ReadAllBytesAsync(item.FilePath!));
                var record = item.ToRecord();
                record.State = DownloadState.Paused;
                var restored = new DownloadItemViewModel(record, settings);
                Assert.Equal(item.ProtocolText, restored.ProtocolText);
                var before = origin.Requests.Count;
                await restored.StartAsync();
                Assert.All(origin.Requests.Skip(before), version => Assert.NotEqual(3, version.Major));
            }
            else
            {
                Assert.All(origin.Requests, version => Assert.Equal(3, version.Major));
                Assert.False(File.Exists(Path.Combine(directory, "file.bin")));
            }
        });
    }

    [Fact]
    public async Task PausingWhilePromptIsPendingDoesNotStartTcpRequests()
    {
        await RunAsync(async (directory, origin) =>
        {
            var item = new DownloadItemViewModel("https://download.test/file.bin", directory,
                new AppSettings { Http3Mode = 1, ProxyMode = 1 });
            var asked = new TaskCompletionSource();
            item.ConfirmHttp3FallbackAsync = async (_, _, ct) =>
            {
                asked.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return true;
            };
            var running = item.StartAsync();
            await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            item.PauseCommand.Execute(null);
            await running;
            Assert.Equal(DownloadState.Paused, item.State);
            Assert.False(item.ToRecord().UseHttp3Fallback);
            Assert.All(origin.Requests, version => Assert.Equal(3, version.Major));
        });
    }

    [Fact]
    public async Task HttpStatusErrorDoesNotAskForProtocolFallback()
    {
        await RunAsync(async (directory, origin) =>
        {
            origin.NotFound = true;
            var item = new DownloadItemViewModel("https://download.test/file.bin", directory,
                new AppSettings { Http3Mode = 1, ProxyMode = 1 });
            item.ConfirmHttp3FallbackAsync = (_, _, _) => throw new Exception("Should not prompt for 404");
            await item.StartAsync();
            Assert.Equal(DownloadState.Failed, item.State);
            Assert.Contains("404", item.StatusText);
            Assert.All(origin.Requests, version => Assert.Equal(3, version.Major));
        });
    }

    [Fact]
    public async Task ProtocolIsDisplayedBeforeDownloadCompletes()
    {
        await RunAsync(async (directory, origin) =>
        {
            origin.AllowHttp3 = true;
            origin.BlockBody = true;
            var item = new DownloadItemViewModel("https://download.test/file.bin", directory,
                new AppSettings { Http3Mode = 1, ProxyMode = 1 });
            var running = item.StartAsync();
            try
            {
                await origin.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                for (var i = 0; i < 50 && item.ProtocolText != "HTTP/3"; i++) await Task.Delay(10);
                Assert.Equal("HTTP/3", item.ProtocolText);
                Assert.Equal(DownloadState.Downloading, item.State);
            }
            finally { origin.ReleaseBody.TrySetResult(); }
            await running;
            Assert.Equal(DownloadState.Completed, item.State);
        });
    }

    [Fact]
    public async Task FallbackRetainsCompletedRangesAndTheResolvedFileName()
    {
        await RunAsync(async (directory, origin) =>
        {
            origin.AllowHttp3 = true;
            origin.FailSecondHttp3Range = true;
            origin.Payload = Enumerable.Range(0, 3 * 1024 * 1024).Select(i => (byte)(i * 31)).ToArray();
            var item = new DownloadItemViewModel("https://download.test/file.bin", directory,
                new AppSettings { Http3Mode = 1, ProxyMode = 1, ChunkCount = 1, MinChunkCount = 1,
                    MaxChunkCount = 1, EnableDynamicConcurrency = false, SegmentSizeMb = 1 });
            item.ConfirmHttp3FallbackAsync = (_, _, _) => Task.FromResult(true);
            await item.StartAsync();
            Assert.Equal(DownloadState.Completed, item.State);
            Assert.Equal(origin.Payload, await File.ReadAllBytesAsync(item.FilePath!));
            Assert.DoesNotContain(origin.TransferRanges, range => range.Version.Major < 3 && range.Start < 1024 * 1024);
            Assert.Single(Directory.GetFiles(directory, "*.bin"));
        });
    }

    [Fact]
    public async Task PopupButtonsAndQueueWorkAndPauseDismissesOnlyCurrentPopup()
    {
        await RunAsync(async (directory, _) =>
        {
            var vm = new MainViewModel(new AppSettingsService(Path.Combine(directory, "settings.json")),
                new DownloadTaskStore(Path.Combine(directory, "downloads.json")), new BrowserHostRegistrationService());
            var view = new MainView { DataContext = vm };
            var window = new SukiWindow { Content = view, Width = 1000, Height = 760 };
            window.Hosts.Add(new SukiDialogHost { Manager = vm.DialogManager });
            window.Show();
            using var canceled = new CancellationTokenSource();
            var first = new DownloadItemViewModel("https://download.test/first.bin", directory, new AppSettings());
            var second = new DownloadItemViewModel("https://download.test/second.bin", directory, new AppSettings());
            var error = new HttpRequestException("unavailable");
            var firstResult = vm.ConfirmHttp3FallbackAsync!(first, error, canceled.Token);
            var secondResult = vm.ConfirmHttp3FallbackAsync!(second, error, CancellationToken.None);
            try
            {
                await FindButtonAsync(window, "回退 H2 / H1.1 继续");
                Assert.False(secondResult.IsCompleted);
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstResult);
                await WaitForDialogTextAsync(window, "second.bin");
                var yes = await FindButtonAsync(window, "回退 H2 / H1.1 继续");
                yes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await secondResult.WaitAsync(TimeSpan.FromSeconds(5)));
                var thirdResult = vm.ConfirmHttp3FallbackAsync(first, error, CancellationToken.None);
                await WaitForDialogTextAsync(window, "first.bin");
                var no = await FindButtonAsync(window, "保持 H3，停止本次下载");
                no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(await thirdResult.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally { window.Close(); }
        });
    }

    private static async Task WaitForDialogTextAsync(Window window, string fileName)
    {
        for (var i = 0; i < 100; i++)
        {
            if (window.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                t.IsEffectivelyVisible && t.Text?.Contains($"“{fileName}”") == true)) return;
            await Task.Delay(20);
        }
        throw new Exception("Missing popup for " + fileName);
    }

    private static async Task<Button> FindButtonAsync(Window window, string text)
    {
        for (var i = 0; i < 100; i++)
        {
            var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, text));
            if (button is not null) return button;
            await Task.Delay(20);
        }
        throw new Exception("Missing popup button: " + text);
    }

    [Fact]
    public void OnlyTransportErrorsOfferFallback()
    {
        Assert.True(Http3FailurePolicy.CanOfferFallback(new LightDownloadException("outer",
            new HttpRequestException(HttpRequestError.VersionNegotiationError, "negotiation failed"))));
        Assert.True(Http3FailurePolicy.CanOfferFallback(new PlatformNotSupportedException()));
        Assert.True(Http3FailurePolicy.CanOfferFallback(new TaskCanceledException()));
        Assert.False(Http3FailurePolicy.CanOfferFallback(new IOException("disk full")));
        Assert.False(Http3FailurePolicy.CanOfferFallback(new UnauthorizedAccessException()));
        Assert.False(Http3FailurePolicy.CanOfferFallback(new HttpRequestException("404", null, HttpStatusCode.NotFound)));
        Assert.False(Http3FailurePolicy.CanOfferFallback(new HttpRequestException(HttpRequestError.SecureConnectionError,
            "TLS", new AuthenticationException())));
        Assert.False(Http3FailurePolicy.CanOfferFallback(new LightDownloadException("checksum mismatch")));
    }

    private static async Task RunAsync(Func<string, Origin, Task> test)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "lightdl-fallback-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            var previous = DownloadPlatform.HttpMessageHandlerFactory;
            using var origin = new Origin();
            DownloadPlatform.HttpMessageHandlerFactory = () => origin;
            try { await test(directory, origin); return true; }
            finally
            {
                DownloadPlatform.HttpMessageHandlerFactory = previous;
                Directory.Delete(directory, true);
            }
        }, CancellationToken.None);
    }

    public sealed class Origin : HttpMessageHandler
    {
        public byte[] Payload { get; set; } = Enumerable.Range(0, 32768).Select(i => (byte)i).ToArray();
        public List<Version> Requests { get; } = [];
        public int TcpVersion { get; set; } = 2;
        public List<(Version Version, int Start)> TransferRanges { get; } = [];
        public bool FailSecondHttp3Range { get; set; }
        public bool NotFound { get; set; }
        public bool AllowHttp3 { get; set; }
        public bool BlockBody { get; set; }
        public TaskCompletionSource BodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseBody { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Version);
            if (NotFound) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            if (request.Version.Major == 3 && !AllowHttp3)
                throw new HttpRequestException(HttpRequestError.VersionNegotiationError, "H3 unavailable");
            var range = request.Headers.Range!.Ranges.Single();
            var start = (int)range.From!.Value;
            var end = (int)range.To!.Value;
            if (end > 0) TransferRanges.Add((request.Version, start));
            if (FailSecondHttp3Range && request.Version.Major == 3 && start >= 1024 * 1024)
                throw new HttpRequestException(HttpRequestError.ConnectionError, "H3 connection lost");
            HttpContent content = BlockBody && end > 0
                ? new StreamContent(new WaitingStream(Payload[start..(end + 1)], BodyStarted, ReleaseBody))
                : new ByteArrayContent(Payload[start..(end + 1)]);
            content.Headers.ContentLength = end - start + 1;
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, Payload.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Version = request.Version.Major == 3 ? HttpVersion.Version30 : TcpVersion == 2 ? HttpVersion.Version20 : HttpVersion.Version11,
                Content = content,
                Headers = { ETag = new EntityTagHeaderValue("\"same-file\"") }
            });
        }
        protected override void Dispose(bool disposing) { }
    }

    private sealed class WaitingStream(byte[] bytes, TaskCompletionSource started, TaskCompletionSource release) : MemoryStream(bytes, false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
