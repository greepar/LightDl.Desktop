using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using LightDl.UI.Services;
using LightDl.UI.ViewModels;
using SukiUI.Controls;

namespace LightDl.Desktop.Views;

public partial class MainWindow : SukiWindow, ITrayManagedWindow
{
    private CancellationTokenSource? _backgroundGcCancellation;
    private bool _isWindowActive = true;

    public bool ExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
            UpdateBackgroundState();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ExitRequested)
            return;

        if (DataContext is MainViewModel { Settings.CloseToTray: true })
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();
            UpdateBackgroundState();
            return;
        }

        ExitRequested = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _isWindowActive = true;
        UpdateBackgroundState();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _isWindowActive = false;
        UpdateBackgroundState();
    }

    private void UpdateBackgroundState()
    {
        var uiUpdatesEnabled = _isWindowActive && WindowState != WindowState.Minimized && IsVisible;
        if (DataContext is MainViewModel viewModel)
            viewModel.SetUiUpdatesEnabled(uiUpdatesEnabled);

        _backgroundGcCancellation?.Cancel();
        _backgroundGcCancellation?.Dispose();
        _backgroundGcCancellation = null;
        if (uiUpdatesEnabled)
            return;

        var cancellation = new CancellationTokenSource();
        _backgroundGcCancellation = cancellation;
        _ = CollectGarbageInBackgroundAsync(cancellation);
    }

    private async Task CollectGarbageInBackgroundAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellation.Token);
            GC.Collect(
                GC.MaxGeneration,
                GCCollectionMode.Optimized,
                blocking: false,
                compacting: false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_backgroundGcCancellation, cancellation))
            {
                _backgroundGcCancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
