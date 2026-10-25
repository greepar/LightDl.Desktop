using Avalonia;
using Avalonia.Threading;
using LightDl.Desktop.Views;
using LightDl.UI;
using LightDl.UI.Services;
using SukiUI;
using SukiUI.Enums;
using LightDl.UI.Services.Updates;

namespace LightDl.Desktop;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--apply-update", var planFile])
        {
            Environment.ExitCode = UpdateInstaller.RunAsync(planFile).GetAwaiter().GetResult();
            return;
        }

        if (NativeMessagingHost.IsInvocation(args))
        {
            NativeMessagingHost.RunAsync().GetAwaiter().GetResult();
            return;
        }

        var readyFile = args.FirstOrDefault(arg => arg.StartsWith("--update-ready=", StringComparison.Ordinal))?[15..];
        args = args.Where(arg => !arg.StartsWith("--update-ready=", StringComparison.Ordinal)).ToArray();
        var updates = new DesktopUpdateService(typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.3");
        UpdatePlatform.Service = updates;
        UpdatePlatform.StartupCompleted = () =>
        {
            updates.CleanupCompletedUpdates();
            updates.NotifyReady(readyFile);
        };

        App.StartMinimizedToTray = args.Any(argument =>
            string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase));
        App.DesktopWindowFactory = static () => new MainWindow();
        App.DesktopThemeFactory = static () => new SukiTheme
        {
            Locale = "zh-CN",
            ThemeColor = SukiColor.Blue
        };
        StartupPlatform.ConfigureAutoStartAsync = AutoStartService.ConfigureAsync;

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
            .UseWaylandWithFallback()
            .WithInterFont()
            .LogToTrace()
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                [
                    Win32RenderingMode.AngleEgl,Win32RenderingMode.Software
                ]
            })
            .With(new X11PlatformOptions
            {
                WmClass = "LightDl.Desktop",
                RenderingMode =
                [
                    X11RenderingMode.Glx,X11RenderingMode.Software
                ]
            })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode =
                [
                    AvaloniaNativeRenderingMode.OpenGl,AvaloniaNativeRenderingMode.Software
                ]
            });
}
