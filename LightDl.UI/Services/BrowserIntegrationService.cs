using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

public sealed class BrowserIntegrationService : IDisposable
{
    public const string PipeName = "LightDl.BrowserIntegration.v1";
    public const int MaxMessageSize = 1024 * 1024;

    private readonly CancellationTokenSource _shutdown = new();
    private Func<BrowserCaptureRequest, Task<BrowserCaptureResponse>>? _handler;

    public void Start(Func<BrowserCaptureRequest, Task<BrowserCaptureResponse>> handler)
    {
        _handler = handler;
        _ = AcceptLoopAsync(_shutdown.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                4,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                _ = HandleConnectionAsync(server, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
            }
            catch (IOException)
            {
                server.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using (stream)
        {
            BrowserCaptureResponse response;
            try
            {
                var request = await ReadRequestAsync(stream, cancellationToken);
                response = _handler is null
                    ? BrowserCaptureResponse.Reject("LightDl is not ready")
                    : await _handler(request);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                response = BrowserCaptureResponse.Reject("Invalid browser integration request");
            }
            catch
            {
                response = BrowserCaptureResponse.Reject("LightDl could not accept the browser download");
            }

            await WriteResponseAsync(stream, response, cancellationToken);
        }
    }

    private static async Task<BrowserCaptureRequest> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length is <= 0 or > MaxMessageSize)
            throw new InvalidDataException("Invalid browser integration message length");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize(payload, BrowserIntegrationJsonContext.Default.BrowserCaptureRequest)
               ?? throw new InvalidDataException("Empty browser integration request");
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        BrowserCaptureResponse response,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            response,
            BrowserIntegrationJsonContext.Default.BrowserCaptureResponse);
        var lengthBuffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
        await stream.WriteAsync(lengthBuffer, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
