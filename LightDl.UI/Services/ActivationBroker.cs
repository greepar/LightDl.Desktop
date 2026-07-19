namespace LightDl.UI.Services;

public static class ActivationBroker
{
    private static readonly object SyncRoot = new();
    private static readonly Queue<string> PendingArguments = new();
    private static Action<string>? _handler;

    public static void Register(Action<string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (SyncRoot)
        {
            _handler = handler;
            while (PendingArguments.TryDequeue(out var argument))
                handler(argument);
        }
    }

    public static void Submit(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return;

        lock (SyncRoot)
        {
            if (_handler is null)
                PendingArguments.Enqueue(argument);
            else
                _handler(argument);
        }
    }
}
