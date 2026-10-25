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
using LightDl.UI.Services.Updates;

namespace LightDl.UI;

public partial class App : Application
{
    public const string DisplayName = "Light Downloader Manager";

    public App() => Name = DisplayName;

    private const string SilentArgument = "--silent";

    public static bool StartMinimizedToTray { get; set; }
    public static bool SkipFirstRunForUpdate { get; set; }

    public static Func<Window>? DesktopWindowFactory { get; set; }

    public static Func<IStyle>? DesktopThemeFactory { get; set; }

    public static Func<IStyle>? MobileThemeFactory { get; set; }

    private Window? _mainWindow;
    private AboutWindow? _aboutWindow;
    private TrayIcon? _trayIcon;
    private MainViewModel? _mainViewModel;
    private BrowserIntegrationService? _browserIntegrationService;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private bool _exitInProgress;
    private DispatcherTimer? _updateCheckTimer;
    private bool _installationPromptActive;
    private bool _installationPromptShown;
    private FirstRunWindow? _firstRunWindow;
    private readonly List<string[]> _pendingDesktopArguments = [];

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS())
        {
            var menu = new NativeMenu();
            var about = new NativeMenuItem($"About {DisplayName}");
            about.Click += (_, _) => ShowAboutWindow();
            menu.Items.Add(about);
            NativeMenu.SetMenu(this, menu);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;
            if (DesktopThemeFactory?.Invoke() is { } desktopTheme)
                Styles.Add(desktopTheme);

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!StartMinimizedToTray && !SkipFirstRunForUpdate &&
                DesktopInstallationService.Current is { NeedsChoice: true } installation)
                ShowFirstRunWindow(desktop, installation);
            else
            {
                if (SkipFirstRunForUpdate) _installationPromptShown = true;
                InitializeDesktop(desktop);
            }
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityPlatform)
        {
            AddMobileTheme();
            var viewModel = new MainViewModel();
            MainViewModel.ApplyTheme(viewModel.Settings.ThemeMode);
            activityPlatform.MainViewFactory = () => new MainView
            {
                DataContext = viewModel
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
        StartDesktopServices();
    }

    private void InitializeDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
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
        _mainViewModel.Updates.UpdateDiscovered += ShowUpdatePromptIfVisible;
        _mainWindow.Activated += async (_, _) =>
        {
            await ShowInstallationChoiceAsync();
            ShowUpdatePromptIfVisible();
        };
        _mainWindow.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.IsVisibleProperty || e.Property == Window.WindowStateProperty)
                Dispatcher.UIThread.Post(ShowUpdatePromptIfVisible);
        };
        if (StartMinimizedToTray)
            _mainWindow.ShowInTaskbar = false;
        else
            desktop.MainWindow = _mainWindow;
        CreateTrayIcon(desktop);
        ActualThemeVariantChanged += (_, _) => UpdateDesktopIcons();
        _browserIntegrationService = new BrowserIntegrationService();
        _browserIntegrationService.Start(HandleBrowserCaptureAsync);
        HandleActivationArguments(_mainViewModel, Environment.GetCommandLineArgs().Skip(1));
        foreach (var arguments in _pendingDesktopArguments) HandleExternalArguments(arguments);
        _pendingDesktopArguments.Clear();
    }

    private void ShowFirstRunWindow(IClassicDesktopStyleApplicationLifetime desktop, DesktopInstallationService installation)
    {
        _installationPromptActive = true;
        _installationPromptShown = true;
        var welcome = new FirstRunWindow(installation) { ShowInTaskbar = true };
        _firstRunWindow = welcome;
        desktop.MainWindow = welcome;
        welcome.Closed += (_, _) =>
        {
            _firstRunWindow = null;
            _installationPromptActive = false;
            if (!welcome.ContinueRequested || installation.RestartPath is not null)
            {
                _exitInProgress = true;
                desktop.Shutdown();
                return;
            }
            // Construct the main view, task list, tray and browser server only after the choice.
            InitializeDesktop(desktop);
            _mainWindow!.Show();
            StartDesktopServices();
        };
    }

    private void StartDesktopServices()
    {
        if (_updateCheckTimer is null && _mainViewModel is not null && UpdatePlatform.Service is not null)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                UpdatePlatform.StartupCompleted?.Invoke();
                if (DesktopInstallationService.Current is { IsInstalledCopy: true })
                {
                    try
                    {
                        if (_mainViewModel.Settings.StartWithSystem && StartupPlatform.ConfigureAutoStartAsync is { } configure)
                            await configure(true);
                        var registration = new BrowserHostRegistrationService();
                        if (registration.IsRegistered()) await registration.RegisterAsync();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        _mainViewModel.StatusMessage = "安装后启动路径更新失败：" + ex.Message;
                    }
                }
                await ShowInstallationChoiceAsync();
                if (_exitInProgress) return;
                CheckForUpdatesAutomatically();
            }, DispatcherPriority.Background);
            _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            _updateCheckTimer.Tick += (_, _) => CheckForUpdatesAutomatically();
            _updateCheckTimer.Start();
        }
    }

    private void CheckForUpdatesAutomatically()
    {
        if (_mainViewModel?.Updates is { AutomaticChecksEnabled: true } updates &&
            updates.CheckForUpdatesCommand.CanExecute(null))
            _ = updates.CheckForUpdatesCommand.ExecuteAsync(null);
    }

    private void ShowUpdatePromptIfVisible()
    {
        _mainViewModel?.Updates.TryShowUpdatePrompt(!_exitInProgress && !_installationPromptActive &&
            _mainWindow is { IsVisible: true, WindowState: not WindowState.Minimized });
    }

    public async Task ShowInstallationChoiceAsync(bool fromSettings = false)
    {
        if (_exitInProgress || _installationPromptActive || (!fromSettings && _installationPromptShown) ||
            DesktopInstallationService.Current is not { } installation || (!fromSettings && !installation.NeedsChoice) ||
            _mainWindow is not { IsVisible: true, WindowState: not WindowState.Minimized } owner)
            return;
        if (fromSettings && _mainViewModel?.Settings.HasUnsavedChanges == true)
        {
            _mainViewModel.StatusMessage = "请先保存设置，再安装到系统。";
            return;
        }
        _installationPromptActive = true;
        _installationPromptShown = true;
        try
        {
            var installed = await new FirstRunWindow(installation, fromSettings).ShowDialog<bool>(owner);
            if (installed && installation.RestartPath is not null)
                await ExitAsync();
        }
        finally { _installationPromptActive = false; }
    }

    private void ShowAboutWindow()
    {
        if (_aboutWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new AboutWindow();
        _aboutWindow = window;
        window.Closed += (_, _) => _aboutWindow = null;
        if (_mainWindow is { IsVisible: true } owner)
            _ = window.ShowDialog(owner);
        else
            window.Show();
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
        exitItem.Click += async (_, _) => await ExitAsync();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "LightDl Download Manager",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
        UpdateDesktopIcons();
        // Avalonia 12 creates the native tray icon when it is attached to the application.
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void UpdateDesktopIcons()
    {
        var theme = ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
        using var iconStream = AssetLoader.Open(new Uri($"avares://LightDl.UI/Assets/lightdl-{theme}.ico"));
        var icon = new WindowIcon(iconStream);
        if (_mainWindow is not null)
            _mainWindow.Icon = icon;
        if (_trayIcon is not null)
            _trayIcon.Icon = icon;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;

        _mainWindow.ShowInTaskbar = true;
        if (_desktopLifetime is not null)
            _desktopLifetime.MainWindow = _mainWindow;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void HandleExternalArguments(IEnumerable<string> arguments)
    {
        var receivedArguments = arguments.ToArray();
        if (_firstRunWindow is not null)
        {
            _pendingDesktopArguments.Add(receivedArguments);
            return;
        }
        HandleActivationArguments(_mainViewModel, receivedArguments);
        if (receivedArguments.Length == 0 || receivedArguments.Any(argument => !IsSilentArgument(argument)))
            ShowMainWindow();
    }

    public async Task ExitAsync()
    {
        if (_exitInProgress || _desktopLifetime is not { } desktop) return;
        _exitInProgress = true;
        try
        {
            if (_mainViewModel is not null)
                await _mainViewModel.PrepareForExitAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex);
        }

        ShutdownDesktop(desktop);
    }

    public async Task InstallUpdateAndExitAsync(Func<Task> startInstaller)
    {
        if (_exitInProgress || _desktopLifetime is not { } desktop)
            throw new InvalidOperationException("应用正在退出，请稍后重试。");
        if (_mainViewModel?.Settings.HasUnsavedChanges == true)
            throw new InvalidOperationException("请先保存设置，再重启安装。");
        _exitInProgress = true;
        try
        {
            if (_mainViewModel is not null) await _mainViewModel.PrepareForExitAsync();
            await startInstaller();
        }
        catch
        {
            _mainViewModel?.CancelExitPreparation();
            _exitInProgress = false;
            throw;
        }
        ShutdownDesktop(desktop);
    }

    private void ShutdownDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _updateCheckTimer?.Stop();
        _mainViewModel?.Updates.Cleanup();
        if (_mainWindow is ITrayManagedWindow trayManagedWindow)
            trayManagedWindow.ExitRequested = true;

        if (_mainWindow is not null)
        {
            _mainWindow.Close();
        }

        TrayIcon.SetIcons(this, null);
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

        foreach (var argument in arguments.Where(argument => !IsSilentArgument(argument)))
            viewModel.HandleActivation(argument);
    }

    private static bool IsSilentArgument(string argument) =>
        string.Equals(argument, SilentArgument, StringComparison.OrdinalIgnoreCase);

    private Task<BrowserCaptureResponse> HandleBrowserCaptureAsync(BrowserCaptureRequest request)
    {
        var completion = new TaskCompletionSource<BrowserCaptureResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_mainViewModel is null)
                {
                    completion.TrySetResult(BrowserCaptureResponse.Reject("LightDl is not ready"));
                    return;
                }

                if (_mainViewModel.ValidateBrowserCapture(request) is { } rejected)
                {
                    completion.TrySetResult(rejected);
                    return;
                }

                ShowMainWindow();
                var response = await _mainViewModel.HandleBrowserCaptureAsync(request);
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
