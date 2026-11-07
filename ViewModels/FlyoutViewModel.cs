using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayGit.Core;
using TrayGit.Services;

namespace TrayGit.ViewModels;

public sealed partial class FlyoutViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RescanAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StatusFreshFor = TimeSpan.FromSeconds(30);
    private const int StatusParallelism = 4;

    private readonly ITrayHost _host;
    private readonly IClipboardService _clipboard;
    private readonly CancellationTokenSource _fetchCts = new();
    private List<GitRepo> _repos = [];
    private DateTime _scannedUtc = DateTime.MinValue;
    private string? _scannedRoot;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _statusCts;
    private HashSet<string> _favorites = SettingsService.Favorites;
    private List<ActionDef> _toolbar = [];
    private ActionDef _enterAction = ActionCatalog.None;
    private ActionDef _clickAction = ActionCatalog.None;
    private ActionDef _doubleClickAction = ActionCatalog.None;
    private string? _hoverPath;
    private string? _focusPath;
    private bool _isVisible;
    private bool _isRestoringState;

    public FlyoutViewModel(ITrayHost host, IClipboardService clipboard)
    {
        _host = host;
        _clipboard = clipboard;
    }

    public ObservableCollection<RepoItemViewModel> Repositories { get; } = [];

    public string DisplayName => App.DisplayName;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    partial void OnSearchTextChanged(string value) => ShowList();

    [ObservableProperty]
    public partial bool FavoritesOnly { get; set; }

    partial void OnFavoritesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoritesGlyph));
        if (!_isRestoringState)
        {
            SettingsService.ShowFavoritesOnly = value;
            ShowList();
        }
    }

    public string FavoritesGlyph => FavoritesOnly ? "\uE735" : "\uE734";

    [ObservableProperty]
    public partial bool DirtyOnly { get; set; }

    partial void OnDirtyOnlyChanged(bool value)
    {
        if (!_isRestoringState)
        {
            SettingsService.ShowDirtyOnly = value;
            ShowList();
        }
    }

    [ObservableProperty]
    public partial bool IsConfigured { get; set; }

    partial void OnIsConfiguredChanged(bool value) => OnPropertyChanged(nameof(NeedsConfiguration));

    public bool NeedsConfiguration => !IsConfigured;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "";

    [ObservableProperty]
    public partial string UpdatedText { get; set; } = "";

    [ObservableProperty]
    public partial string SearchPlaceholder { get; set; } = "Filter repos";

    public void OnShown()
    {
        if (!_isVisible)
        {
            _isVisible = true;
            SettingsService.SourceFolderChanged += OnSourceFolderChanged;
        }

        SearchText = "";
        LoadActions();
        _favorites = SettingsService.Favorites;

        _isRestoringState = true;
        FavoritesOnly = SettingsService.ShowFavoritesOnly;
        DirtyOnly = SettingsService.ShowDirtyOnly;
        _isRestoringState = false;

        LoadAndMaybeScan(force: false);
    }

    public bool ClearFilter()
    {
        if (SearchText.Length == 0)
        {
            return false;
        }

        SearchText = "";
        ShowList();
        return true;
    }

    public void OnHidden()
    {
        if (_isVisible)
        {
            _isVisible = false;
            SettingsService.SourceFolderChanged -= OnSourceFolderChanged;
        }

        _hoverPath = null;
        _focusPath = null;
        CancelScan();
        CancelStatusRead();
    }

    public void SetHoverPath(string? path)
    {
        _hoverPath = path;
        RefreshExpansion();
    }

    public void ClearHoverPath(string? path)
    {
        if (string.Equals(_hoverPath, path, StringComparison.OrdinalIgnoreCase))
        {
            SetHoverPath(null);
        }
    }

    public void SetFocusPath(string? path)
    {
        _focusPath = path;
        RefreshExpansion();
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadAndMaybeScan(force: true);
    }

    [RelayCommand]
    private void ShowSettings() => _host.ShowSettings();

    [RelayCommand]
    private async Task ChooseFolderAsync() => await _host.ChooseSourceFolderAsync();

    [RelayCommand]
    private Task SubmitSearchAsync() =>
        Repositories.FirstOrDefault() is { Repo: var repo } ? RunEnterActionAsync(repo) : Task.CompletedTask;

    [RelayCommand]
    private Task RunEnterActionAsync(GitRepo repo) => PerformAsync(_enterAction, repo);

    [RelayCommand]
    private Task RunClickActionAsync(GitRepo repo) => PerformAsync(_clickAction, repo);

    [RelayCommand]
    private Task RunDoubleClickActionAsync(GitRepo repo) => PerformAsync(_doubleClickAction, repo);

    public bool HasClickAction => _clickAction.Kind != ActionKind.None;

    public bool HasDoubleClickAction => _doubleClickAction.Kind != ActionKind.None;

    private void LoadActions()
    {
        List<ActionDef> custom = SettingsService.CustomActions;
        _toolbar = ActionCatalog.Toolbar(SettingsService.ToolbarActionIds, custom);
        _enterAction = ActionCatalog.Resolve(SettingsService.EnterActionId, custom, ActionCatalog.DefaultEnterId);
        _clickAction = ActionCatalog.Resolve(SettingsService.ClickActionId, custom, ActionCatalog.DefaultClickId);
        _doubleClickAction = ActionCatalog.Resolve(SettingsService.DoubleClickActionId, custom, ActionCatalog.DefaultDoubleClickId);
        SearchPlaceholder = _enterAction.Kind == ActionKind.None
            ? "Filter repos"
            : $"Filter repos, Enter: {_enterAction.Name}";
        OnPropertyChanged(nameof(HasClickAction));
        OnPropertyChanged(nameof(HasDoubleClickAction));
    }

    private void OnSourceFolderChanged(object? sender, EventArgs e) => LoadAndMaybeScan(force: true);

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
        IsLoading = true;

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
            IsStatusOpen = false;
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
                IsLoading = false;
            }

            cts.Dispose();
        }
    }

    private void CancelScan()
    {
        _scanCts?.Cancel();
        _scanCts = null;
        IsLoading = false;
    }

    private void CancelStatusRead()
    {
        _statusCts?.Cancel();
        _statusCts = null;
    }

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
        List<GitRepo> visible = GetVisibleRepos();
        List<GitRepo> order = [.. visible, .. _repos.Except(visible)];
        List<GitRepo> stale = order
            .Where(repo => !GitService.TryGetKnown(repo.Path, out _, out DateTime readUtc)
                || DateTime.UtcNow - readUtc > StatusFreshFor)
            .ToList();
        Progress<(string Path, RepoStatus Status)> progress = new(result => ApplyStatus(result.Path, result.Status));

        try
        {
            await Parallel.ForEachAsync(
                stale,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = StatusParallelism,
                    CancellationToken = cts.Token,
                },
                async (repo, token) =>
                {
                    if (await GitService.ReadStatusAsync(repo.Path, token) is { } status)
                    {
                        ((IProgress<(string, RepoStatus)>)progress).Report((repo.Path, status));
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
        foreach (GitRepo repo in _repos.Where(repo =>
                     string.Equals(repo.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            repo.SetStatus(status);
        }

        if (DirtyOnly)
        {
            ShowList();
        }
    }

    private Task FetchAsync(GitRepo repo) => RunGitAsync(repo, "fetch", GitService.FetchAsync);

    private Task PullAsync(GitRepo repo) => RunGitAsync(repo, "pull", GitService.PullAsync);

    private Task PushAsync(GitRepo repo) => RunGitAsync(repo, "push", GitService.PushAsync);

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

            IsStatusOpen = false;
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
        foreach (GitRepo item in _repos.Where(item =>
                     string.Equals(item.Path, repo.Path, StringComparison.OrdinalIgnoreCase)))
        {
            item.SetFavorite(favorite);
        }

        if (FavoritesOnly)
        {
            ShowList();
        }
    }

    private List<GitRepo> GetVisibleRepos() =>
        RepoListPolicy.Arrange(
            _repos,
            SettingsService.LastUsed,
            SearchText,
            FavoritesOnly ? _favorites : null,
            DirtyOnly);

    private void ShowList()
    {
        string? root = SettingsService.SourceFolder;
        List<GitRepo> visible = GetVisibleRepos();
        bool scanned = _scannedUtc != DateTime.MinValue;
        IsConfigured = !string.IsNullOrEmpty(root);

        ClearItems();
        foreach (GitRepo repo in visible)
        {
            Repositories.Add(new RepoItemViewModel(
                repo,
                _toolbar,
                IsExpandedFor(repo.Path, visible),
                FetchAsync,
                PullAsync,
                PushAsync,
                ToggleFavorite,
                CopyPath,
                PerformAsync));
        }

        string? empty = !IsConfigured
            ? "Choose the folder that holds your git repos, such as D:\\source."
            : !scanned
                ? null
                : _repos.Count == 0
                    ? $"No git repos found in {root}."
                    : visible.Count == 0 && SearchText.Length > 0
                        ? "No repos match the filters."
                        : visible.Count == 0 && FavoritesOnly && DirtyOnly
                            ? "No favorite repos are dirty."
                            : visible.Count == 0 && FavoritesOnly
                                ? "No favorites yet. Right-click a repo, or swipe it left, and choose Favorite."
                                : visible.Count == 0 && DirtyOnly
                                    ? "No dirty repos found."
                                    : visible.Count == 0
                                        ? "No repos match the filters."
                                        : null;
        EmptyText = empty ?? "";
        IsEmpty = empty is not null;

        string count = _repos.Count == 1 ? "1 repo" : $"{_repos.Count} repos";
        string shown = FavoritesOnly && DirtyOnly
            ? visible.Count == 1 ? "1 dirty favorite" : $"{visible.Count} dirty favorites"
            : FavoritesOnly
                ? visible.Count == 1 ? "1 favorite" : $"{visible.Count} favorites"
                : DirtyOnly
                    ? visible.Count == 1 ? "1 dirty repo" : $"{visible.Count} dirty repos"
                    : count;
        UpdatedText = !IsConfigured ? "" : scanned ? $"{shown} in {root}" : $"Scanning {root}";
        _host.SetTrayStatus(IsConfigured && scanned ? count : null);
    }

    private bool IsExpandedFor(string path, IReadOnlyList<GitRepo> visible)
    {
        string? active = _focusPath ?? _hoverPath ?? visible.FirstOrDefault()?.Path;
        return string.Equals(path, active, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshExpansion()
    {
        List<GitRepo> visible = GetVisibleRepos();
        foreach (RepoItemViewModel item in Repositories)
        {
            item.IsExpanded = IsExpandedFor(item.Repo.Path, visible);
        }
    }

    private void ClearItems()
    {
        foreach (RepoItemViewModel item in Repositories)
        {
            item.Dispose();
        }

        Repositories.Clear();
    }

    private void CopyPath(GitRepo repo)
    {
        try
        {
            _clipboard.SetText($"\"{repo.Path}\"");
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't copy the path of {repo.Name}", ex.Message);
        }
    }

    private async Task PerformAsync(ActionDef action, GitRepo repo)
    {
        if (action.Kind == ActionKind.None)
        {
            return;
        }

        try
        {
            _host.HideFlyout();
            if (action.Kind == ActionKind.Remote)
            {
                await RepoLauncher.OpenRemoteAsync(repo);
            }
            else
            {
                RepoLauncher.Launch(action, repo);
            }
        }
        catch (Exception ex)
        {
            _host.ShowFlyout();
            ShowStatus($"Couldn't open {repo.Name}", ex.Message);
        }
    }

    private void ShowStatus(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }

    public void Dispose()
    {
        OnHidden();
        _fetchCts.Cancel();
        _fetchCts.Dispose();
        ClearItems();
    }
}
