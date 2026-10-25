using CommunityToolkit.Mvvm.ComponentModel;
using LightDl.UI.Localization;

namespace LightDl.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject, ILocalizable
{
    protected ViewModelBase() => L.Register(this);
    public virtual void RefreshLanguage() => OnPropertyChanged(string.Empty);
}
