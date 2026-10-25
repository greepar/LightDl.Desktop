using Avalonia;
using Avalonia.Markup.Xaml;

namespace LightDl.UI.Localization;

public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => new TranslatedText(Key).ToBinding();

    private sealed class TranslatedText(string key) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            var subscription = new Subscription(key, observer);
            L.Register(subscription);
            subscription.RefreshLanguage();
            return subscription;
        }
    }
    private sealed class Subscription(string key, IObserver<string> observer) : IDisposable, ILocalizable
    {
        private IObserver<string>? _observer = observer;
        public void RefreshLanguage() => _observer?.OnNext(L.T(key));
        public void Dispose() => _observer = null;
    }
}
