using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using LightDl.UI.Services;
using LightDl.UI.ViewModels;
using SukiUI.Controls;

namespace LightDl.Desktop.Views;

public partial class MainWindow : SukiWindow, ITrayManagedWindow
{
    public bool ExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
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
            return;
        }

        ExitRequested = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
