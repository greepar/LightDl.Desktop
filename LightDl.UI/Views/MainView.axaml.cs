using System.ComponentModel;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
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
    private bool _updatePromptDisplayed;
    private ContextMenu? _downloadContextMenu;
    private TopLevel? _contextMenuOwner;
    private readonly SemaphoreSlim _fallbackDialogLock = new(1, 1);

    public MainView()
    {
        InitializeComponent();
        SettingsScrollViewer.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty ||
                (e.Property == IsVisibleProperty && !SettingsScrollViewer.IsVisible))
                CloseSettingsDropdowns();
        };
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => CloseDownloadContextMenu();
    }

    private void CloseSettingsDropdowns()
    {
        foreach (var selector in SettingsScrollViewer.GetLogicalDescendants().OfType<ComboBox>())
            if (selector.IsDropDownOpen) selector.IsDropDownOpen = false;
    }

    private async void ExportBrowserExtension(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string browser } button || TopLevel.GetTopLevel(this) is not Window owner) return;
        button.IsEnabled = false;
        try
        {
            var downloads = await owner.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
            var directory = downloads?.TryGetLocalPath() ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var export = await Services.BrowserExtensionBundle.ExportAsync(browser, directory);
            await new BrowserExtensionWindow(browser, export).ShowDialog(owner);
        }
        catch (Exception ex)
        {
            if (_viewModel is not null) _viewModel.StatusMessage = L.T("无法释放浏览器扩展：") + ex.Message;
        }
        finally { button.IsEnabled = true; }
    }

    private void OnDownloadContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || ReferenceEquals(menu, _downloadContextMenu)) return;
        CloseDownloadContextMenu();
        _downloadContextMenu = menu;
        _contextMenuOwner = TopLevel.GetTopLevel(this);
        // Observe the owning window in the tunnel, including handled clicks on
        // buttons and empty content. Native popup dismissal can miss these clicks.
        _contextMenuOwner?.AddHandler(InputElement.PointerPressedEvent, OnContextMenuOwnerPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnDownloadContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _downloadContextMenu))
            ReleaseContextMenuOwner();
    }

    private void OnContextMenuOwnerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_downloadContextMenu is not { } menu) return;
        if (e.Source is Visual source && (ReferenceEquals(source, menu) || menu.IsVisualAncestorOf(source)))
            return;
        CloseDownloadContextMenu();
        // Preserve the original click so navigation/buttons still work normally.
    }

    private void CloseDownloadContextMenu()
    {
        var menu = _downloadContextMenu;
        ReleaseContextMenuOwner();
        menu?.Close();
    }

    private void ReleaseContextMenuOwner()
    {
        _contextMenuOwner?.RemoveHandler(InputElement.PointerPressedEvent, OnContextMenuOwnerPointerPressed);
        _contextMenuOwner = null;
        _downloadContextMenu = null;
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
            Title = L.T("选择默认下载目录"),
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
            viewModel.Settings.DefaultDownloadDirectory = path;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Updates.PropertyChanged -= OnUpdatePropertyChanged;
            _viewModel.Updates.PromptDialogManager.DismissDialog();
            _updatePromptDisplayed = false;
            _viewModel.ConfirmHttp3FallbackAsync = null;
            _viewModel.ConfirmLeaveSettingsAsync = null;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
            return;

        _viewModel.ConfirmHttp3FallbackAsync = ConfirmHttp3FallbackAsync;
        _viewModel.ConfirmLeaveSettingsAsync = ConfirmLeaveSettingsAsync;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Updates.PropertyChanged += OnUpdatePropertyChanged;
        RefreshUpdatePrompt();
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

    private void OnUpdatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppUpdateViewModel.IsUpdatePromptOpen)) RefreshUpdatePrompt();
    }

    private void RefreshUpdatePrompt()
    {
        if (_viewModel?.Updates is not { } updates) return;
        if (!updates.IsUpdatePromptOpen)
        {
            if (_updatePromptDisplayed) updates.PromptDialogManager.DismissDialog();
            return;
        }
        if (_updatePromptDisplayed) return;
        _updatePromptDisplayed = true;
        // Use SukiUI's host and its standard show/dismiss animation, without activating the OS window.
        var shown = updates.PromptDialogManager.CreateDialog()
            .WithTitle(L.F($"发现新版本 v{updates.AvailableVersion}"))
            .WithContent(new UpdatePromptView { DataContext = updates })
            .OnDismissed(_ =>
            {
                _updatePromptDisplayed = false;
                updates.DismissUpdatePromptCommand.Execute(null);
            })
            .TryShow();
        if (!shown) _updatePromptDisplayed = false;
    }

    private Task<bool?> ConfirmLeaveSettingsAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return Task.FromResult<bool?>(null);
        return new UnsavedSettingsWindow().ShowDialog<bool?>(owner);
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
                    result.FileName,
                    result.Cookie,
                    result.Referer,
                    result.UserAgent);
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

    private async Task<bool> ConfirmHttp3FallbackAsync(
        DownloadItemViewModel item, Exception error, CancellationToken cancellationToken)
    {
        await _fallbackDialogLock.WaitAsync(cancellationToken);
        try
        {
            // The browser capture/new-task dialogs share this host. Keep the
            // failed task pending until the current dialog has closed.
            while (_isDialogOpen)
                await Task.Delay(100, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_viewModel is not { } viewModel) return false;
            _isDialogOpen = true;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SukiUI.Dialogs.ISukiDialog? shownDialog = null;
            var topLevel = TopLevel.GetTopLevel(this);
            EventHandler<KeyEventArgs>? keyHandler = null;
            void Complete(bool accepted)
            {
                completion.TrySetResult(accepted);
                if (shownDialog is not null)
                    viewModel.DialogManager.TryDismissDialog(shownDialog);
            }
            keyHandler = (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Complete(false);
            };
            topLevel?.AddHandler(InputElement.KeyDownEvent, keyHandler, RoutingStrategies.Tunnel, handledEventsToo: true);
            try
            {
                // Show a stable summary instead of raw URLs/response messages.
                var content = new TextBlock
                {
                    Text = L.F($"“{item.FileName}”无法通过 HTTP/3 建立或维持连接。\n\n是否仅将当前下载回退到 HTTP/2 / HTTP/1.1？已有可续传分片会继续使用，全局强制 H3 设置保持不变。"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 480
                };
                var shown = viewModel.DialogManager.CreateDialog()
                    .WithTitle(L.T("HTTP/3 连接失败"))
                    .WithContent(content)
                    .WithActionButton(L.T("保持 H3，停止本次下载"), dialog => { shownDialog = dialog; Complete(false); }, true)
                    .WithActionButton(L.T("回退 H2 / H1.1 继续"), dialog => { shownDialog = dialog; Complete(true); }, true)
                    .OnDismissed(_ => completion.TrySetResult(false))
                    .TryShow();
                if (!shown) return false;
                // This method exclusively owns the host until its finally block.
                using var registration = cancellationToken.Register(() =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (completion.TrySetCanceled(cancellationToken))
                            viewModel.DialogManager.DismissDialog();
                    }));
                return await completion.Task;
            }
            finally
            {
                if (topLevel is not null)
                    topLevel.RemoveHandler(InputElement.KeyDownEvent, keyHandler);
                viewModel.DialogManager.DismissDialog();
                _isDialogOpen = false;
                if (viewModel.PendingBrowserCapture is not null)
                    _ = ShowPendingBrowserDialogAsync();
            }
        }
        finally { _fallbackDialogLock.Release(); }
    }

    private Task<DownloadDialogResult?> ShowDownloadDialogAsync(
        MainViewModel mainViewModel,
        DownloadDialogViewModel dialogViewModel)
    {
        var completion = new TaskCompletionSource<DownloadDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var content = new DownloadDialog(dialogViewModel);
        var topLevel = TopLevel.GetTopLevel(this);
        EventHandler<KeyEventArgs>? keyHandler = null;

        void Complete(DownloadDialogResult? result)
        {
            if (!completion.TrySetResult(result))
                return;

            if (topLevel is not null && keyHandler is not null)
                topLevel.RemoveHandler(InputElement.KeyDownEvent, keyHandler);
            mainViewModel.DialogManager.DismissDialog();
        }

        keyHandler = (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;
            Complete(null);
        };
        topLevel?.AddHandler(
            InputElement.KeyDownEvent,
            keyHandler,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        content.Completed += (_, result) => Complete(result);

        var shown = mainViewModel.DialogManager.CreateDialog()
            .WithTitle(dialogViewModel.DialogTitle)
            .WithContent(content)
            .TryShow();
        if (!shown)
            Complete(null);

        return completion.Task;
    }
}
