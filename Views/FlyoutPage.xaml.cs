using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TrayGit.Core;
using TrayGit.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace TrayGit.Views;

/// <summary>
/// The repo list. It shows the cached scan as soon as the flyout opens and rescans the source
/// folder in the background when that scan is stale. Nothing runs while the flyout is hidden.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    // Local disk only, but a deep tree can still take a moment; don't rescan on every open.
    private static readonly TimeSpan RescanAfter = TimeSpan.FromMinutes(5);

    // Status comes from git itself, so it is read in the background when the flyout opens, and
    // only for repos not read in the last half minute.
    private static readonly TimeSpan StatusFreshFor = TimeSpan.FromSeconds(30);
    private const int StatusParallelism = 4;

    private List<GitRepo> _repos = [];
    private DateTime _scannedUtc = DateTime.MinValue;
    private string? _scannedRoot;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _statusCts;
    private readonly CancellationTokenSource _fetchCts = new();
    private bool _isVisible;
    private HashSet<string> _favorites = SettingsService.Favorites;

    // How long a click waits for a second tap before it counts as a single click.
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(400);

    private List<ActionDef> _toolbar = [];
    private ActionDef _enterAction = ActionCatalog.None;
    private ActionDef _clickAction = ActionCatalog.None;
    private ActionDef _doubleClickAction = ActionCatalog.None;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clickTimer;
    private readonly Dictionary<string, List<RepoActionItem>> _rowItems = new(StringComparer.OrdinalIgnoreCase);
    private string? _hoverPath;
    private string? _focusPath;
    private GitRepo? _pendingClick;

    public FlyoutPage()
    {
        InitializeComponent();
        TitleText.Text = App.DisplayName;
    }

    /// <summary>The flyout opened: show what we have, rescan if stale, and focus the filter.</summary>
    public void OnShown()
    {
        if (!_isVisible)
        {
            _isVisible = true;
            SettingsService.SourceFolderChanged += OnSourceFolderChanged;
        }

        SearchBox.Text = "";
        LoadActions();
        _favorites = SettingsService.Favorites;
        FavoritesToggle.IsChecked = SettingsService.ShowFavoritesOnly;
        UpdateFavoritesToggleIcon();
        LoadAndMaybeScan(force: false);

        // Coming back from settings, the page isn't in the tree yet; focus once it is.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => SearchBox.Focus(FocusState.Programmatic));
    }

    /// <summary>Clears the filter and returns focus to it. False when there was nothing to clear.</summary>
    public bool ClearFilter()
    {
        if (SearchBox.Text.Length == 0)
        {
            return false;
        }

        SearchBox.Text = "";
        ShowList();
        SearchBox.Focus(FocusState.Programmatic);
        return true;
    }

    /// <summary>The flyout hid: stop listening and abandon any scan in flight.</summary>
    public void OnHidden()
    {
        if (_isVisible)
        {
            _isVisible = false;
            SettingsService.SourceFolderChanged -= OnSourceFolderChanged;
        }

        _clickTimer?.Stop();
        _pendingClick = null;
        _hoverPath = null;
        _focusPath = null;
        CancelScan();
        CancelStatusRead();
    }

    // The user's choices, read each time the flyout opens so Settings changes apply at once.
    private void LoadActions()
    {
        List<ActionDef> custom = SettingsService.CustomActions;
        _toolbar = ActionCatalog.Toolbar(SettingsService.ToolbarActionIds, custom);
        _enterAction = ActionCatalog.Resolve(SettingsService.EnterActionId, custom, ActionCatalog.DefaultEnterId);
        _clickAction = ActionCatalog.Resolve(SettingsService.ClickActionId, custom, ActionCatalog.DefaultClickId);
        _doubleClickAction = ActionCatalog.Resolve(SettingsService.DoubleClickActionId, custom, ActionCatalog.DefaultDoubleClickId);
        SearchBox.PlaceholderText = _enterAction.Kind == ActionKind.None ? "Filter repos" : $"Filter repos, Enter: {_enterAction.Name}";
    }

    // A fetch the user started keeps going when the flyout hides; closing the window ends it.
    public void Dispose()
    {
        OnHidden();
        _fetchCts.Cancel();
    }

    private void OnSourceFolderChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => LoadAndMaybeScan(force: true));

    private void LoadAndMaybeScan(bool force)
    {
        string? root = SettingsService.SourceFolder;
        if (string.IsNullOrEmpty(root))
        {
            CancelScan();
            CancelStatusRead();
            SetRepos([]);
            _scannedRoot = null;
            ShowList();
            return;
        }

        // A new flyout window starts empty: fill it from the cache before scanning.
        if (!string.Equals(_scannedRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            RepoScan? cached = RepoStore.LoadCache(root);
            SetRepos(cached?.Repos ?? []);
            _scannedUtc = cached?.ScannedUtc ?? DateTime.MinValue;
            _scannedRoot = root;
        }

        ShowList();
        _ = ReadStatusesAsync();

        if (force || DateTime.UtcNow - _scannedUtc > RescanAfter)
        {
            _ = ScanAsync(root);
        }
    }

    private async Task ScanAsync(string root)
    {
        CancelScan();
        CancellationTokenSource cts = new();
        _scanCts = cts;
        LoadingRing.IsActive = true;

        try
        {
            if (!Directory.Exists(root))
            {
                ShowStatus("Source folder not found", $"{root} is missing. Choose another folder in Settings.");
                return;
            }

            RepoScan scan = await RepoStore.ScanAsync(root, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            SetRepos(scan.Repos);
            _scannedUtc = scan.ScannedUtc;
            _scannedRoot = root;
            StatusBar.IsOpen = false;
            ShowList();
            _ = ReadStatusesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowStatus("Couldn't scan the source folder", ex.Message);
        }
        finally
        {
            if (_scanCts == cts)
            {
                _scanCts = null;
                LoadingRing.IsActive = false;
            }

            cts.Dispose();
        }
    }

    private void CancelScan()
    {
        _scanCts?.Cancel();
        _scanCts = null;
        LoadingRing.IsActive = false;
    }

    private void CancelStatusRead()
    {
        _statusCts?.Cancel();
        _statusCts = null;
    }

    // Repos remember their last status across scans and flyout windows.
    private void SetRepos(List<GitRepo> repos)
    {
        foreach (GitRepo repo in repos)
        {
            repo.SetFavorite(_favorites.Contains(repo.Path));
            if (GitService.TryGetKnown(repo.Path, out RepoStatus? status, out _))
            {
                repo.SetStatus(status);
            }
        }

        _repos = repos;
    }

    private async Task ReadStatusesAsync()
    {
        CancelStatusRead();
        CancellationTokenSource cts = new();
        _statusCts = cts;

        // The rows on screen first.
        List<GitRepo> order = RepoList.ItemsSource is List<GitRepo> visible ? [.. visible, .. _repos.Except(visible)] : [.. _repos];
        List<GitRepo> stale = order
            .Where(r => !GitService.TryGetKnown(r.Path, out _, out DateTime readUtc) || DateTime.UtcNow - readUtc > StatusFreshFor)
            .ToList();

        try
        {
            await Parallel.ForEachAsync(
                stale,
                new ParallelOptions { MaxDegreeOfParallelism = StatusParallelism, CancellationToken = cts.Token },
                async (repo, token) =>
                {
                    RepoStatus? status = await GitService.ReadStatusAsync(repo.Path, token);
                    if (status is not null)
                    {
                        DispatcherQueue.TryEnqueue(() => ApplyStatus(repo.Path, status));
                    }
                });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_statusCts == cts)
            {
                _statusCts = null;
            }

            cts.Dispose();
        }
    }

    private void ApplyStatus(string path, RepoStatus status)
    {
        foreach (GitRepo repo in _repos)
        {
            if (string.Equals(repo.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                repo.SetStatus(status);
            }
        }
    }

    private Task FetchAsync(GitRepo repo) => RunGitAsync(repo, "fetch", GitService.FetchAsync);

    private Task PullAsync(GitRepo repo) => RunGitAsync(repo, "pull", GitService.PullAsync);

    private Task PushAsync(GitRepo repo) => RunGitAsync(repo, "push", GitService.PushAsync);

    // Fetch, pull and push share the row's busy spinner, so only one runs per repo at a time.
    private async Task RunGitAsync(GitRepo repo, string verb, Func<string, CancellationToken, Task> action)
    {
        if (repo.IsFetching)
        {
            return;
        }

        repo.SetFetching(true);
        try
        {
            await action(repo.Path, _fetchCts.Token);
            if (await GitService.ReadStatusAsync(repo.Path, _fetchCts.Token) is { } status)
            {
                ApplyStatus(repo.Path, status);
            }

            StatusBar.IsOpen = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't {verb} {repo.Name}", ex.Message);
        }
        finally
        {
            repo.SetFetching(false);
        }
    }

    private void ToggleFavorite(GitRepo repo)
    {
        bool favorite = !repo.IsFavorite;
        if (favorite)
        {
            _favorites.Add(repo.Path);
        }
        else
        {
            _favorites.Remove(repo.Path);
        }

        SettingsService.Favorites = _favorites;
        foreach (GitRepo r in _repos.Where(r => string.Equals(r.Path, repo.Path, StringComparison.OrdinalIgnoreCase)))
        {
            r.SetFavorite(favorite);
        }

        if (FavoritesToggle.IsChecked == true)
        {
            ShowList();
        }
    }

    private void FavoriteSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (args.SwipeControl.DataContext is GitRepo repo)
        {
            ToggleFavorite(repo);
        }
    }

    private void FavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GitRepo repo)
        {
            ToggleFavorite(repo);
        }
    }

    private void FavoritesToggle_Changed(object sender, RoutedEventArgs e)
    {
        SettingsService.ShowFavoritesOnly = FavoritesToggle.IsChecked == true;
        UpdateFavoritesToggleIcon();
        ShowList();
    }

    // Outline star when off, filled when the list is narrowed to favorites.
    private void UpdateFavoritesToggleIcon() =>
        FavoritesToggleIcon.Glyph = FavoritesToggle.IsChecked == true ? "\uE735" : "\uE734";

    private void FetchSwipeItem_Invoked(SwipeItem sender, SwipeItemInvokedEventArgs args)
    {
        if (args.SwipeControl.DataContext is GitRepo repo)
        {
            _ = FetchAsync(repo);
        }
    }

    private void FetchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GitRepo repo)
        {
            _ = FetchAsync(repo);
        }
    }

    private void PullMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GitRepo repo)
        {
            _ = PullAsync(repo);
        }
    }

    private void PushMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GitRepo repo)
        {
            _ = PushAsync(repo);
        }
    }

    private void ShowList()
    {
        string? root = SettingsService.SourceFolder;
        bool favoritesOnly = FavoritesToggle.IsChecked == true;
        List<GitRepo> visible = RepoListPolicy.Arrange(_repos, SettingsService.LastUsed, SearchBox.Text, favoritesOnly ? _favorites : null);
        _rowItems.Clear();
        RepoList.ItemsSource = visible;

        bool configured = !string.IsNullOrEmpty(root);
        bool scanned = _scannedUtc != DateTime.MinValue;
        SearchBox.IsEnabled = configured;
        RefreshButton.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        FavoritesToggle.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        ChooseFolderButton.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;

        string? empty = !configured ? "Choose the folder that holds your git repos, such as D:\\source."
            : !scanned ? null // first scan in progress; the ring says so
            : _repos.Count == 0 ? $"No git repos found in {root}."
            : visible.Count == 0 && favoritesOnly && SearchBox.Text.Length == 0 ? "No favorites yet. Right-click a repo, or swipe it left, and choose Favorite."
            : visible.Count == 0 ? "No repos match the filter."
            : null;
        EmptyText.Text = empty ?? "";
        EmptyPanel.Visibility = empty is null ? Visibility.Collapsed : Visibility.Visible;

        string count = _repos.Count == 1 ? "1 repo" : $"{_repos.Count} repos";
        int favoriteCount = _repos.Count(r => r.IsFavorite);
        string shown = favoritesOnly ? (favoriteCount == 1 ? "1 favorite" : $"{favoriteCount} favorites") : count;
        UpdatedText.Text = !configured ? "" : scanned ? $"{shown} in {root}" : $"Scanning {root}";
        App.Current.SetTrayStatus(configured && scanned ? count : null);
    }

    private void ShowStatus(string title, string message)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput || sender.Text.Length == 0)
        {
            ShowList();
        }
    }

    // Enter acts on the top match: type a few letters, press Enter, you're there.
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (RepoList.ItemsSource is List<GitRepo> { Count: > 0 } visible)
        {
            Perform(_enterAction, visible[0]);
        }
    }

    private void CopyPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GitRepo repo)
        {
            CopyPath(repo);
        }
    }

    // Like Explorer's "Copy as path": the full path in quotes.
    private void CopyPath(GitRepo repo)
    {
        try
        {
            DataPackage data = new();
            data.SetText($"\"{repo.Path}\"");
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't copy the path of {repo.Name}", ex.Message);
        }
    }

    // Down from the filter box enters the list. Escape is handled here because the box consumes it
    // before the window's accelerator sees it: clear the text first, then hide.
    private void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            if (!ClearFilter())
            {
                App.Current.HideFlyout();
            }
        }
        else if (e.Key == VirtualKey.Down && RepoList.Items.Count > 0)
        {
            e.Handled = true;
            RepoList.SelectedIndex = 0;
            FocusRow(0);
        }
    }

    // Up/Down move between rows (ListView does that); Left/Right step through the row's actions.
    private void RepoList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        int index = RepoList.SelectedIndex;
        switch (e.Key)
        {
            case VirtualKey.Up when index <= 0:
                e.Handled = true;
                RepoList.SelectedItem = null;
                SearchBox.Focus(FocusState.Keyboard);
                break;

            case VirtualKey.Left or VirtualKey.Right when index >= 0:
                e.Handled = true;
                StepAction(index, e.Key == VirtualKey.Right ? 1 : -1);
                break;

            case VirtualKey.Enter when index >= 0 && FocusManager.GetFocusedElement(XamlRoot) is ListViewItem:
                e.Handled = true;
                Perform(_enterAction, (GitRepo)RepoList.SelectedItem);
                break;

            case VirtualKey.C when index >= 0 && IsCtrlDown() && FocusManager.GetFocusedElement(XamlRoot) is not TextBox:
                e.Handled = true;
                CopyPath((GitRepo)RepoList.SelectedItem);
                break;
        }
    }

    private static bool IsCtrlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void FocusRow(int index)
    {
        if (RepoList.ContainerFromIndex(index) is not ListViewItem item)
        {
            RepoList.UpdateLayout();
            item = (ListViewItem)RepoList.ContainerFromIndex(index);
        }

        item?.Focus(FocusState.Keyboard);
    }

    // The row itself is stop 0, then each enabled action button in visual order.
    private void StepAction(int index, int delta)
    {
        if (RepoList.ContainerFromIndex(index) is not ListViewItem item)
        {
            return;
        }

        List<Button> buttons = [];
        CollectButtons(item, buttons);
        List<Control> stops = [item, .. buttons.Where(b => b.IsEnabled)];

        object? focused = FocusManager.GetFocusedElement(XamlRoot);
        int current = Math.Max(0, stops.FindIndex(s => ReferenceEquals(s, focused)));
        int next = Math.Clamp(current + delta, 0, stops.Count - 1);
        stops[next].Focus(FocusState.Keyboard);
    }

    private static void CollectButtons(DependencyObject parent, List<Button> buttons)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button)
            {
                buttons.Add(button);
            }
            else
            {
                CollectButtons(child, buttons);
            }
        }
    }

    // Each row's buttons are built from the user's toolbar choice.
    private void ActionStrip_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is ItemsControl strip && args.NewValue is GitRepo repo)
        {
            List<RepoActionItem> items = _toolbar.Select(action => new RepoActionItem(action, repo, IsExpandedFor(repo.Path))).ToList();
            _rowItems[repo.Path] = items;
            strip.ItemsSource = items;
        }
    }

    // Only one row shows its buttons: the keyboard-focused row, else the hovered row, else the first.
    private bool IsExpandedFor(string path)
    {
        string? active = _focusPath ?? _hoverPath ?? (RepoList.ItemsSource is List<GitRepo> { Count: > 0 } visible ? visible[0].Path : null);
        return string.Equals(path, active, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshStrips()
    {
        foreach ((string path, List<RepoActionItem> items) in _rowItems)
        {
            bool expanded = IsExpandedFor(path);
            foreach (RepoActionItem item in items)
            {
                item.IsExpanded = expanded;
            }
        }
    }

    private void RepoRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hoverPath = RepoOfRow(sender)?.Path;
        RefreshStrips();
    }

    private void RepoRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (string.Equals(_hoverPath, RepoOfRow(sender)?.Path, StringComparison.OrdinalIgnoreCase))
        {
            _hoverPath = null;
            RefreshStrips();
        }
    }

    // Focus on the row or any of its buttons (not a mouse click) keeps the row expanded.
    private void RepoList_GotFocus(object sender, RoutedEventArgs e)
    {
        _focusPath = e.OriginalSource is Control { FocusState: FocusState.Keyboard } ? RowPathOf(e.OriginalSource) : null;
        RefreshStrips();
    }

    private void RepoList_LostFocus(object sender, RoutedEventArgs e)
    {
        _focusPath = null;
        RefreshStrips();
    }

    private static string? RowPathOf(object? source)
    {
        for (DependencyObject? d = source as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ListViewItem { Content: GitRepo repo })
            {
                return repo.Path;
            }
        }

        return null;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RepoActionItem item)
        {
            Perform(item.Action, item.Repo);
        }
    }

    private void RepoRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_clickAction.Kind == ActionKind.None || IsFromButton(e.OriginalSource) || RepoOfRow(sender) is not { } repo)
        {
            return;
        }

        if (_doubleClickAction.Kind == ActionKind.None)
        {
            Perform(_clickAction, repo);
            return;
        }

        // Both are set: hold the click back to see whether a second tap follows.
        _pendingClick = repo;
        if (_clickTimer is null)
        {
            _clickTimer = DispatcherQueue.CreateTimer();
            _clickTimer.IsRepeating = false;
            _clickTimer.Interval = DoubleClickWindow;
            _clickTimer.Tick += (_, _) =>
            {
                if (_pendingClick is { } pending)
                {
                    _pendingClick = null;
                    Perform(_clickAction, pending);
                }
            };
        }

        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void RepoRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _clickTimer?.Stop();
        _pendingClick = null;
        if (!IsFromButton(e.OriginalSource) && RepoOfRow(sender) is { } repo)
        {
            Perform(_doubleClickAction, repo);
        }
    }

    // A tap on one of the row's own buttons is that button's click, not the row's.
    private static bool IsFromButton(object? source)
    {
        for (DependencyObject? d = source as DependencyObject; d is not null and not ListViewItem; d = VisualTreeHelper.GetParent(d))
        {
            if (d is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private static GitRepo? RepoOfRow(object sender) => (sender as FrameworkElement)?.DataContext as GitRepo;

    private void Perform(ActionDef action, GitRepo repo)
    {
        switch (action.Kind)
        {
            case ActionKind.None:
                return;

            case ActionKind.Remote:
                if (repo.HasWebUrl)
                {
                    _ = OpenRemoteAsync(repo);
                }

                return;

            default:
                try
                {
                    App.Current.HideFlyout();
                    RepoLauncher.Launch(action, repo);
                }
                catch (Exception ex)
                {
                    App.Current.ShowFlyout();
                    ShowStatus($"Couldn't open {repo.Name}", ex.Message);
                }

                return;
        }
    }

    private async Task OpenRemoteAsync(GitRepo repo)
    {
        App.Current.HideFlyout();
        await RepoLauncher.OpenRemoteAsync(repo);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadAndMaybeScan(force: true);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e) => _ = App.Current.ChooseSourceFolderAsync();
}
