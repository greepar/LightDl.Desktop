using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using LightDl.UI.Models;
using LightDl.UI.Services;

namespace LightDl.BrowserHost;

internal static class Program
{
    public static async Task<int> Main()
    {
        BrowserCaptureResponse response;
        try
        {
            var request = await ReadNativeRequestAsync(Console.OpenStandardInput());
            response = await ForwardToDesktopAsync(request);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            response = BrowserCaptureResponse.Reject("LightDl browser host could not process the request");
        }

        await WriteNativeResponseAsync(Console.OpenStandardOutput(), response);
        return 0;
    }

    private static async Task<BrowserCaptureResponse> ForwardToDesktopAsync(BrowserCaptureRequest request)
    {
        var client = await TryConnectAsync(TimeSpan.FromSeconds(1));
        if (client is null)
        {
            TryStartDesktop();
            client = await TryConnectAsync(TimeSpan.FromSeconds(10));
        }

        if (client is null)
            return BrowserCaptureResponse.Reject("LightDl Desktop is not running");

        await using (client)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                request,
                BrowserIntegrationJsonContext.Default.BrowserCaptureRequest);
            await WriteFrameAsync(client, payload);

            var responsePayload = await ReadFrameAsync(client);
            return JsonSerializer.Deserialize(
                       responsePayload,
                       BrowserIntegrationJsonContext.Default.BrowserCaptureResponse)
                   ?? BrowserCaptureResponse.Reject("LightDl Desktop returned an empty response");
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
        var executableName = OperatingSystem.IsWindows()
            ? "LightDl.Desktop.exe"
            : "LightDl.Desktop";
        var executablePath = FindDesktopExecutable(executableName);
        if (executablePath is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch
        {
        }
    }

    private static string? FindDesktopExecutable(string executableName)
    {
        var installedPath = Path.Combine(AppContext.BaseDirectory, executableName);
        if (File.Exists(installedPath))
            return installedPath;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDirectory = Path.Combine(directory.FullName, "LightDl.Desktop");
            if (Directory.Exists(projectDirectory))
            {
                return Directory.EnumerateFiles(projectDirectory, executableName, SearchOption.AllDirectories)
                    .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            }

            directory = directory.Parent;
        }

        return null;
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

    private static async Task<byte[]> ReadFrameAsync(Stream stream)
    {
        var lengthBuffer = new byte[4];
        await stream.ReadExactlyAsync(lengthBuffer);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length is <= 0 or > BrowserIntegrationService.MaxMessageSize)
            throw new InvalidDataException("Invalid native message length");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload);
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
