using Avalonia;
using Avalonia.Threading;
using LightDl.Desktop.Views;
using LightDl.UI;
using SukiUI;
using SukiUI.Enums;

namespace LightDl.Desktop;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        App.DesktopWindowFactory = static () => new MainWindow();
        App.DesktopThemeFactory = static () => new SukiTheme
        {
            Locale = "zh-CN",
            ThemeColor = SukiColor.Blue
        };

        using var singleInstance = new SingleInstanceManager();
        if (!singleInstance.IsPrimary)
        {
            singleInstance.ForwardAsync(args).GetAwaiter().GetResult();
            return;
        }

        singleInstance.ArgumentsReceived += arguments =>
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current is App app)
                    app.HandleExternalArguments(arguments);
            });

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
