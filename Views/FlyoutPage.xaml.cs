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

    private List<GitRepo> _repos = [];
    private DateTime _scannedUtc = DateTime.MinValue;
    private string? _scannedRoot;
    private CancellationTokenSource? _scanCts;
    private bool _isVisible;

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
    }

    public void Dispose() => OnHidden();

    private void OnSourceFolderChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => LoadAndMaybeScan(force: true));

    private void LoadAndMaybeScan(bool force)
    {
        string? root = SettingsService.SourceFolder;
        if (string.IsNullOrEmpty(root))
        {
            CancelScan();
            _repos = [];
            _scannedRoot = null;
            ShowList();
            return;
        }

        // A new flyout window starts empty: fill it from the cache before scanning.
        if (!string.Equals(_scannedRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            RepoScan? cached = RepoStore.LoadCache(root);
            _repos = cached?.Repos ?? [];
            _scannedUtc = cached?.ScannedUtc ?? DateTime.MinValue;
            _scannedRoot = root;
        }

        ShowList();

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

            _repos = scan.Repos;
            _scannedUtc = scan.ScannedUtc;
            _scannedRoot = root;
            StatusBar.IsOpen = false;
            ShowList();
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

    private void ShowList()
    {
        string? root = SettingsService.SourceFolder;
        List<GitRepo> visible = RepoListPolicy.Arrange(_repos, SettingsService.LastUsed, SearchBox.Text);
        RepoList.ItemsSource = visible;

        bool configured = !string.IsNullOrEmpty(root);
        bool scanned = _scannedUtc != DateTime.MinValue;
        SearchBox.IsEnabled = configured;
        RefreshButton.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        ChooseFolderButton.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;

        string? empty = !configured ? "Choose the folder that holds your git repos, such as D:\\source."
            : !scanned ? null // first scan in progress; the ring says so
            : _repos.Count == 0 ? $"No git repos found in {root}."
            : visible.Count == 0 ? "No repos match the filter."
            : null;
        EmptyText.Text = empty ?? "";
        EmptyPanel.Visibility = empty is null ? Visibility.Collapsed : Visibility.Visible;

        string count = _repos.Count == 1 ? "1 repo" : $"{_repos.Count} repos";
        UpdatedText.Text = !configured ? "" : scanned ? $"{count} in {root}" : $"Scanning {root}";
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
