using Avalonia.Input;
using Avalonia.Interactivity;
using SukiUI.Controls;

namespace LightDl.UI.Views;

public partial class UnsavedSettingsWindow : SukiWindow
{
    public UnsavedSettingsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }, RoutingStrategies.Tunnel);
    }

    private void Save(object? sender, RoutedEventArgs e) => Close((bool?)true);
    private void Discard(object? sender, RoutedEventArgs e) => Close((bool?)false);
}
