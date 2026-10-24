using CommunityToolkit.Mvvm.Input;
using LightDl.UI.Models;

namespace LightDl.UI.ViewModels;

public partial class CloseConfirmationViewModel(bool hasActiveDownloads, bool hasUnsavedSettings) : ViewModelBase
{
    public event Action<WindowCloseAction>? DecisionMade;

    public string Message => hasActiveDownloads
        ? "后台运行会继续下载；退出应用会暂停下载并保存进度。"
        : "请选择退出应用或最小化到后台。";

    public bool HasUnsavedSettings => hasUnsavedSettings;

    [RelayCommand]
    private void Exit() => DecisionMade?.Invoke(WindowCloseAction.Exit);

    [RelayCommand]
    private void RunInBackground() => DecisionMade?.Invoke(WindowCloseAction.RunInBackground);
}
