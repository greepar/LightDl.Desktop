using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using LightDl.UI.Models;
using LightDl.UI.Services;
using LightDl.UI.ViewModels;
using LightDl.UI.Views;

namespace LightDl.UI;

public partial class App : Application
{
    public static Func<Window>? DesktopWindowFactory { get; set; }

    public static Func<IStyle>? DesktopThemeFactory { get; set; }

    public static Func<IStyle>? MobileThemeFactory { get; set; }

    private Window? _mainWindow;
    private TrayIcon? _trayIcon;
    private MainViewModel? _mainViewModel;
    private BrowserIntegrationService? _browserIntegrationService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (DesktopThemeFactory?.Invoke() is { } desktopTheme)
                Styles.Add(desktopTheme);

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainViewModel = new MainViewModel();
            MainViewModel.ApplyTheme(_mainViewModel.Settings.ThemeMode);
            _mainWindow = DesktopWindowFactory?.Invoke() ?? new Window
            {
                Title = "LightDl",
                Width = 1180,
                Height = 760,
                Content = new MainView()
            };
            _mainWindow.DataContext = _mainViewModel;
            desktop.MainWindow = _mainWindow;
            CreateTrayIcon(desktop);
            _browserIntegrationService = new BrowserIntegrationService();
            _browserIntegrationService.Start(HandleBrowserCaptureAsync);
            HandleActivationArguments(_mainViewModel, Environment.GetCommandLineArgs().Skip(1));
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            AddMobileTheme();
            singleViewFactoryApplicationLifetime.MainViewFactory = () =>
            {
                var viewModel = new MainViewModel();
                MainViewModel.ApplyTheme(viewModel.Settings.ThemeMode);
                return new MainView { DataContext = viewModel };
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            AddMobileTheme();
            var viewModel = new MainViewModel();
            MainViewModel.ApplyTheme(viewModel.Settings.ThemeMode);
            singleViewPlatform.MainView = new MainView
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void AddMobileTheme()
    {
        if (MobileThemeFactory?.Invoke() is { } mobileTheme)
            Styles.Add(mobileTheme);
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var showItem = new NativeMenuItem("显示 LightDl");
        showItem.Click += (_, _) => ShowMainWindow();

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => Exit(desktop);

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        using var iconStream = AssetLoader.Open(new Uri("avares://LightDl.UI/Assets/avalonia-logo.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "LightDl Download Manager",
            Menu = menu
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;

        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void HandleExternalArguments(IEnumerable<string> arguments)
    {
        HandleActivationArguments(_mainViewModel, arguments);
        ShowMainWindow();
    }

    private void Exit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_mainWindow is ITrayManagedWindow trayManagedWindow)
            trayManagedWindow.ExitRequested = true;

        if (_mainWindow is not null)
        {
            _mainWindow.Close();
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
        _browserIntegrationService?.Dispose();
        _browserIntegrationService = null;
        desktop.Shutdown();
    }

    private static void HandleActivationArguments(MainViewModel? viewModel, IEnumerable<string> arguments)
    {
        if (viewModel is null)
            return;

        foreach (var argument in arguments)
            viewModel.HandleActivation(argument);
    }

    private Task<BrowserCaptureResponse> HandleBrowserCaptureAsync(BrowserCaptureRequest request)
    {
        var completion = new TaskCompletionSource<BrowserCaptureResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                ShowMainWindow();
                var response = _mainViewModel is null
                    ? BrowserCaptureResponse.Reject("LightDl is not ready")
                    : await _mainViewModel.HandleBrowserCaptureAsync(request);
                completion.TrySetResult(response);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }
}
