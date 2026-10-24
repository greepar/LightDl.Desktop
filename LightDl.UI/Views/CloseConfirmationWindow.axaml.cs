using Avalonia.Input;
using Avalonia.Interactivity;
using LightDl.UI.ViewModels;
using SukiUI.Controls;

namespace LightDl.UI.Views;

public partial class CloseConfirmationWindow : SukiWindow
{
    public CloseConfirmationWindow() : this(new CloseConfirmationViewModel(false, false)) { }

    public CloseConfirmationWindow(CloseConfirmationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.DecisionMade += decision => Close(decision);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }, RoutingStrategies.Tunnel);
    }
}
