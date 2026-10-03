using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TrayGit.Core;
using TrayGit.Services;

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
        _favorites = SettingsService.Favorites;
        FavoritesToggle.IsChecked = SettingsService.ShowFavoritesOnly;
        UpdateFavoritesToggleIcon();
        LoadAndMaybeScan(force: false);

        // Coming back from settings, the page isn't in the tree yet; focus once it is.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => SearchBox.Focus(FocusState.Programmatic));
    }

    /// <summary>The flyout hid: stop listening and abandon any scan in flight.</summary>
    public void OnHidden()
    {
        if (_isVisible)
        {
            _isVisible = false;
            SettingsService.SourceFolderChanged -= OnSourceFolderChanged;
        }

        CancelScan();
        CancelStatusRead();
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

    private async Task FetchAsync(GitRepo repo)
    {
        if (repo.IsFetching)
        {
            return;
        }

        repo.SetFetching(true);
        try
        {
            await GitService.FetchAsync(repo.Path, _fetchCts.Token);
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
            ShowStatus($"Couldn't fetch {repo.Name}", ex.Message);
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

    private void ShowList()
    {
        string? root = SettingsService.SourceFolder;
        bool favoritesOnly = FavoritesToggle.IsChecked == true;
        List<GitRepo> visible = RepoListPolicy.Arrange(_repos, SettingsService.LastUsed, SearchBox.Text, favoritesOnly ? _favorites : null);
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

    // Enter opens a terminal in the top match: type a few letters, press Enter, you're there.
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (RepoList.ItemsSource is List<GitRepo> { Count: > 0 } visible)
        {
            Run(RepoLauncher.OpenTerminal, visible[0]);
        }
    }

    private void TerminalButton_Click(object sender, RoutedEventArgs e) => Run(RepoLauncher.OpenTerminal, sender);

    private void FolderButton_Click(object sender, RoutedEventArgs e) => Run(RepoLauncher.OpenFolder, sender);

    private void RepoRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Run(RepoLauncher.OpenFolder, sender);

    private async void RemoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (RepoOf(sender) is { } repo)
        {
            App.Current.HideFlyout();
            await RepoLauncher.OpenRemoteAsync(repo);
        }
    }

    private void Run(Action<GitRepo> action, object sender)
    {
        if (RepoOf(sender) is { } repo)
        {
            Run(action, repo);
        }
    }

    private void Run(Action<GitRepo> action, GitRepo repo)
    {
        try
        {
            App.Current.HideFlyout();
            action(repo);
        }
        catch (Exception ex)
        {
            App.Current.ShowFlyout();
            ShowStatus($"Couldn't open {repo.Name}", ex.Message);
        }
    }

    private static GitRepo? RepoOf(object sender) => (sender as FrameworkElement)?.DataContext as GitRepo;

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadAndMaybeScan(force: true);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e) => _ = App.Current.ChooseSourceFolderAsync();
}
