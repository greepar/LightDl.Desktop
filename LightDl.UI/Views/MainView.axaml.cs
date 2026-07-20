using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LightDl.UI.Models;
using LightDl.UI.ViewModels;
using SukiUI.Dialogs;

namespace LightDl.UI.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _viewModel;
    private bool _isDialogOpen;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void SelectDownloadItem(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DownloadItemViewModel item } ||
            DataContext is not MainViewModel viewModel)
            return;

        viewModel.SelectDownload(item);
    }

    private async void BrowseDownloadDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择默认下载目录",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
            viewModel.Settings.DefaultDownloadDirectory = path;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_viewModel.PendingBrowserCapture is not null)
            _ = ShowPendingBrowserDialogAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PendingBrowserCapture) &&
            _viewModel?.PendingBrowserCapture is not null)
        {
            _ = ShowPendingBrowserDialogAsync();
        }
    }

    private async void OpenNewDownloadDialog(object? sender, RoutedEventArgs e)
    {
        if (_isDialogOpen ||
            _viewModel is not { } viewModel)
        {
            return;
        }

        _isDialogOpen = true;
        try
        {
            var dialogViewModel = new DownloadDialogViewModel(
                false,
                viewModel.NewUrl,
                string.Empty,
                viewModel.Settings.DefaultDownloadDirectory);
            var result = await ShowDownloadDialogAsync(viewModel, dialogViewModel);

            if (result?.Action == DownloadDialogAction.DownloadWithLightDl)
            {
                await viewModel.AddDownloadFromDialogAsync(
                    result.SourceUrl,
                    result.DownloadDirectory,
                    result.FileName);
            }
        }
        finally
        {
            _isDialogOpen = false;
            if (viewModel.PendingBrowserCapture is not null)
                _ = ShowPendingBrowserDialogAsync();
        }
    }

    private async Task ShowPendingBrowserDialogAsync()
    {
        if (_isDialogOpen ||
            _viewModel is not { PendingBrowserCapture: { } capture } viewModel)
        {
            return;
        }

        _isDialogOpen = true;
        try
        {
            var dialogViewModel = new DownloadDialogViewModel(
                true,
                capture.SourceUrl,
                capture.FileName,
                capture.DownloadDirectory,
                capture.BrowserName,
                capture.SizeText);
            var result = await ShowDownloadDialogAsync(viewModel, dialogViewModel);

            switch (result?.Action)
            {
                case DownloadDialogAction.DownloadWithLightDl:
                    await viewModel.AcceptBrowserCaptureFromDialogAsync(
                        result.FileName,
                        result.DownloadDirectory);
                    break;
                case DownloadDialogAction.CancelDownload:
                    viewModel.CancelBrowserDownloadFromDialog();
                    break;
                default:
                    viewModel.ContinueBrowserDownloadFromDialog();
                    break;
            }
        }
        finally
        {
            _isDialogOpen = false;
            if (viewModel.PendingBrowserCapture is not null)
                _ = ShowPendingBrowserDialogAsync();
        }
    }

    private static Task<DownloadDialogResult?> ShowDownloadDialogAsync(
        MainViewModel mainViewModel,
        DownloadDialogViewModel dialogViewModel)
    {
        var completion = new TaskCompletionSource<DownloadDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var content = new DownloadDialog(dialogViewModel);
        content.Completed += (_, result) =>
        {
            mainViewModel.DialogManager.DismissDialog();
            completion.TrySetResult(result);
        };

        var shown = mainViewModel.DialogManager.CreateDialog()
            .WithTitle(dialogViewModel.DialogTitle)
            .WithContent(content)
            .TryShow();
        if (!shown)
            completion.TrySetResult(null);

        return completion.Task;
    }
}
