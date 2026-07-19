using System.IO.Pipes;
using System.Text;

namespace LightDl.Desktop;

internal sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "LightDl.Desktop.SingleInstance";
    private const string PipeName = "LightDl.Desktop.Activation";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();

    public SingleInstanceManager()
    {
        _mutex = new Mutex(true, MutexName, out var isPrimary);
        IsPrimary = isPrimary;

        if (IsPrimary)
            _ = ListenAsync(_cancellation.Token);
    }

    public bool IsPrimary { get; }

    public event Action<IReadOnlyList<string>>? ArgumentsReceived;

    public async Task ForwardAsync(IEnumerable<string> arguments)
    {
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
        var payload = string.Join('\0', arguments);
        await writer.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();

        if (IsPrimary)
            _mutex.ReleaseMutex();

        _mutex.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var encoded = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(encoded))
                    continue;

                var payload = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                ArgumentsReceived?.Invoke(payload.Split('\0', StringSplitOptions.RemoveEmptyEntries));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }
}
