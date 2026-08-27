using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using LightDl.UI.Models;
using LightDl.UI.Services;

namespace LightDl.Desktop;

internal static class NativeMessagingHost
{
    private const string FirefoxExtensionId = "automatic-integration@lightdl.io";
    private const string NativeHostLockFileName = "lightdl-native-messaging-host.lock";

    public static bool IsInvocation(IReadOnlyList<string> args)
    {
        return args.Any(argument =>
            argument.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--parent-window=", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "-mozilla", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, FirefoxExtensionId, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task RunAsync()
    {
        await using var hostLock = TryAcquireHostLock();
        if (hostLock is null)
        {
            await WriteNativeResponseAsync(
                Console.OpenStandardOutput(),
                BrowserCaptureResponse.Reject("Another browser download is awaiting confirmation"));
            return;
        }

        await RunCoreAsync();
    }

    private static async Task RunCoreAsync()
    {
        BrowserCaptureResponse response;
        try
        {
            var request = await ReadNativeRequestAsync(Console.OpenStandardInput());
            response = await ForwardToDesktopAsync(request);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException
                                       or OperationCanceledException or TimeoutException)
        {
            response = BrowserCaptureResponse.Reject("LightDl browser host could not process the request");
        }

        await WriteNativeResponseAsync(Console.OpenStandardOutput(), response);
    }

    private static async Task<BrowserCaptureResponse> ForwardToDesktopAsync(BrowserCaptureRequest request)
    {
        var client = await TryConnectAsync(TimeSpan.FromSeconds(1));
        if (client is null)
            client = await StartDesktopAndConnectAsync();

        if (client is null)
            return BrowserCaptureResponse.Reject("LightDl Desktop is not running");

        await using (client)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                request,
                BrowserIntegrationJsonContext.Default.BrowserCaptureRequest);
            await WriteFrameAsync(client, payload);

            var responsePayload = await ReadFrameAsync(client, TimeSpan.FromSeconds(90));
            return JsonSerializer.Deserialize(
                       responsePayload,
                       BrowserIntegrationJsonContext.Default.BrowserCaptureResponse)
                   ?? BrowserCaptureResponse.Reject("LightDl Desktop returned an empty response");
        }
    }

    private static async Task<NamedPipeClientStream?> StartDesktopAndConnectAsync()
    {
        var client = await TryConnectAsync(TimeSpan.FromMilliseconds(500));
        if (client is not null)
            return client;

        TryStartDesktop();
        return await TryConnectAsync(TimeSpan.FromSeconds(10));
    }

    private static FileStream? TryAcquireHostLock()
    {
        try
        {
            return new FileStream(
                Path.Combine(Path.GetTempPath(), NativeHostLockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<NamedPipeClientStream?> TryConnectAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var client = new NamedPipeClientStream(
                ".",
                BrowserIntegrationService.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                using var attempt = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync(attempt.Token);
                return client;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException)
            {
                await client.DisposeAsync();
                await Task.Delay(200);
            }
        }

        return null;
    }

    private static void TryStartDesktop()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) ||
            string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                ArgumentList = { "--silent" },
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch
        {
        }
    }

    private static async Task<BrowserCaptureRequest> ReadNativeRequestAsync(Stream input)
    {
        var payload = await ReadFrameAsync(input);
        return JsonSerializer.Deserialize(payload, BrowserIntegrationJsonContext.Default.BrowserCaptureRequest)
               ?? throw new InvalidDataException("The browser sent an empty request");
    }

    private static async Task WriteNativeResponseAsync(Stream output, BrowserCaptureResponse response)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            response,
            BrowserIntegrationJsonContext.Default.BrowserCaptureResponse);
        await WriteFrameAsync(output, payload);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, TimeSpan? timeout = null)
    {
        using var timeoutCts = timeout is { } duration
            ? new CancellationTokenSource(duration)
            : null;
        var lengthBuffer = new byte[4];
        await stream.ReadExactlyAsync(lengthBuffer, timeoutCts?.Token ?? default);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length is <= 0 or > BrowserIntegrationService.MaxMessageSize)
            throw new InvalidDataException("Invalid native message length");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, timeoutCts?.Token ?? default);
        return payload;
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] payload)
    {
        var lengthBuffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
        await stream.WriteAsync(lengthBuffer);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
