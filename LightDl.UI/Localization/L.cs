using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace LightDl.UI.Localization;

public interface ILocalizable
{
    void RefreshLanguage();
}

public static class L
{
    private static readonly ResourceManager English = new("LightDl.UI.Localization.English", typeof(L).Assembly);
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static CultureInfo _culture = SystemCulture;
    private static readonly ConditionalWeakTable<string, Translation> Translations = new();
    private static readonly List<WeakReference<ILocalizable>> Listeners = [];
    private static readonly Lazy<(string Chinese, string English)[]> Catalog = new(() =>
        English.GetResourceSet(CultureInfo.InvariantCulture, true, false)!
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => ((string)entry.Key, (string)entry.Value!))
            .OrderByDescending(entry => Math.Max(entry.Item1.Length, entry.Item2.Length)).ToArray());
    private sealed record Translation(string Key, object?[] Arguments);

    public static CultureInfo Culture => _culture;
    public static bool IsChinese => Culture.TwoLetterISOLanguageName == "zh";
    public static string CultureName => IsChinese ? "zh-CN" : "en-US";
    private static string Lookup(string key) => IsChinese ? key : English.GetString(key, CultureInfo.InvariantCulture) ?? key;

    public static string T(string chinese) => Track(Lookup(chinese), chinese, []);
    public static string F(FormattableString text) => Render(new Translation(text.Format, text.GetArguments()));
    private static string Track(string value, string key, object?[] arguments)
    {
        Translations.GetValue(value, _ => new Translation(key, arguments));
        return value;
    }
    private static string Render(Translation text) => Track(
        string.Format(CultureInfo.CurrentCulture, Lookup(text.Key), text.Arguments), text.Key, text.Arguments);

    // Keep format arguments (file names, URLs and errors) intact when refreshing existing messages.
    public static string Relocalize(string text)
    {
        if (text.Length == 0 || text == "--") return text;
        if (Translations.TryGetValue(text, out var translation)) return Render(translation);
        foreach (var entry in Catalog.Value)
            if (text == entry.Chinese || text == entry.English) return T(entry.Chinese);
        foreach (var entry in Catalog.Value)
        {
            if (entry.Chinese.EndsWith('：') || entry.Chinese.EndsWith(' '))
            {
                if (text.StartsWith(entry.Chinese, StringComparison.Ordinal)) return T(entry.Chinese) + text[entry.Chinese.Length..];
                if (text.StartsWith(entry.English, StringComparison.Ordinal)) return T(entry.Chinese) + text[entry.English.Length..];
            }
            if (entry.Chinese.StartsWith('（'))
            {
                if (text.EndsWith(entry.Chinese, StringComparison.Ordinal)) return text[..^entry.Chinese.Length] + T(entry.Chinese);
                if (text.EndsWith(entry.English, StringComparison.Ordinal)) return text[..^entry.English.Length] + T(entry.Chinese);
            }
        }
        return text;
    }

    public static CultureInfo ResolveCulture(int languageMode, CultureInfo systemCulture) => CultureInfo.GetCultureInfo(
        languageMode switch
        {
            1 => "zh-CN",
            2 => "en-US",
            _ => systemCulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US"
        });

    public static void ConfigureAtStartup(int languageMode) => ChangeLanguage(languageMode);
    public static void ChangeLanguage(int languageMode) => SetCulture(ResolveCulture(languageMode, SystemCulture));

    public static void Register(ILocalizable listener)
    {
        lock (Listeners)
        {
            Listeners.RemoveAll(reference => !reference.TryGetTarget(out _));
            Listeners.Add(new WeakReference<ILocalizable>(listener));
        }
    }

    public static void SetCulture(CultureInfo culture)
    {
        if (Culture.Name == culture.Name) return;
        _culture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        void Refresh()
        {
            WeakReference<ILocalizable>[] listeners;
            lock (Listeners) listeners = Listeners.ToArray();
            foreach (var reference in listeners)
                if (reference.TryGetTarget(out var listener)) listener.RefreshLanguage();
        }
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }
}
