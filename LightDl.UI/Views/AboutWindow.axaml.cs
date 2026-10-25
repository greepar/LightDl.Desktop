using System.Reflection;
using Avalonia.Interactivity;
using SukiUI.Controls;

namespace LightDl.UI.Views;

public partial class AboutWindow : SukiWindow
{
    public string VersionText { get; } =
        (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.3");

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void CloseAboutWindow(object? sender, RoutedEventArgs e) => Close();
}
