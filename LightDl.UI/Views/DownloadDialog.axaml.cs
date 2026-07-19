using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LightDl.UI.Models;
using LightDl.UI.ViewModels;

namespace LightDl.UI.Views;

public partial class DownloadDialog : UserControl
{
    public event EventHandler<DownloadDialogResult?>? Completed;

    public DownloadDialog()
    {
        InitializeComponent();
    }

    public DownloadDialog(DownloadDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private async void BrowseDownloadDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DownloadDialogViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择下载目录",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
            viewModel.DownloadDirectory = path;
    }

    private void ContinueInBrowser(object? sender, RoutedEventArgs e)
    {
        Completed?.Invoke(this, CreateResult(DownloadDialogAction.BrowserContinue));
    }

    private void CancelDownload(object? sender, RoutedEventArgs e)
    {
        Completed?.Invoke(this, CreateResult(DownloadDialogAction.CancelDownload));
    }

    private void DownloadWithLightDl(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DownloadDialogViewModel viewModel || !viewModel.ValidateForLightDl())
            return;

        Completed?.Invoke(this, CreateResult(DownloadDialogAction.DownloadWithLightDl));
    }

    private void CloseDialog(object? sender, RoutedEventArgs e)
    {
        Completed?.Invoke(this, null);
    }

    private DownloadDialogResult? CreateResult(DownloadDialogAction action)
    {
        return DataContext is DownloadDialogViewModel viewModel
            ? new DownloadDialogResult(
                action,
                viewModel.SourceUrl,
                viewModel.FileName,
                viewModel.DownloadDirectory)
            : null;
    }
}
