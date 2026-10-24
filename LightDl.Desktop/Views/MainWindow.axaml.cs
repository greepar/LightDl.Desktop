using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using LightDl.UI.Services;
using LightDl.UI.Models;
using LightDl.UI.Views;
using LightDl.UI.ViewModels;
using SukiUI.Controls;

namespace LightDl.Desktop.Views;

public partial class MainWindow : SukiWindow, ITrayManagedWindow
{
    private CancellationTokenSource? _backgroundGcCancellation;
    private bool _isWindowActive = true;
    private CloseConfirmationWindow? _closeConfirmation;

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

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ExitRequested || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        e.Cancel = true;
        if (_closeConfirmation is { } existingDialog)
        {
            existingDialog.Activate();
            return;
        }
        var viewModel = DataContext as MainViewModel;
        _closeConfirmation = new CloseConfirmationWindow(new CloseConfirmationViewModel(
            viewModel?.ActiveCount > 0, viewModel?.Settings.HasUnsavedChanges == true));
        WindowCloseAction? action;
        try { action = await _closeConfirmation.ShowDialog<WindowCloseAction?>(this); }
        finally { _closeConfirmation = null; }
        if (ExitRequested) return;
        if (action == WindowCloseAction.RunInBackground)
        {
            ShowInTaskbar = false;
            Hide();
            UpdateBackgroundState();
        }
        else if (action == WindowCloseAction.Exit)
        {
            if (Application.Current is LightDl.UI.App app)
                await app.ExitAsync();
            else
            {
                ExitRequested = true;
                Close();
            }
        }
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
