using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayGit.Core;
using TrayGit.Services;

namespace TrayGit.ViewModels;

public sealed partial class RepoDetailViewModel : ObservableObject, IDisposable
{
    private readonly Action _goBack;
    private readonly Action<GitRepo> _showDiff;
    private readonly IClipboardService _clipboard;
    private readonly CancellationTokenSource _operationCts = new();
    private CancellationTokenSource? _readCts;
    private bool _isVisible;

    public RepoDetailViewModel(
        GitRepo repo,
        Action goBack,
        Action<GitRepo> showDiff,
        IClipboardService clipboard)
    {
        Repo = repo;
        _goBack = goBack;
        _showDiff = showDiff;
        _clipboard = clipboard;
        Repo.PropertyChanged += Repo_PropertyChanged;
    }

    public GitRepo Repo { get; }

    public ObservableCollection<GitFileChange> Changes { get; } = [];

    public ObservableCollection<GitCommit> Commits { get; } = [];

    public ObservableCollection<GitBranch> Branches { get; } = [];

    public string BranchText => Repo.Branch ?? "Detached or unknown";

    public string RepoName => Repo.Name;

    public string RemoteText => Repo.RemoteUrl ?? "No remote configured";

    public string PathText => Repo.Path;

    public string ChangesText => Changes.Count switch
    {
        0 => "Working tree is clean",
        1 => "1 changed file",
        _ => $"{Changes.Count} changed files",
    };

    public string CommitsText => Commits.Count switch
    {
        0 => "No commits yet",
        1 => "1 recent commit",
        _ => $"{Commits.Count} recent commits",
    };

    public string BranchesText => Branches.Count switch
    {
        0 => "No branches",
        1 => "1 branch",
        _ => $"{Branches.Count} branches",
    };

    public string SyncText => Repo.Status is { } status && (status.Ahead > 0 || status.Behind > 0)
        ? $"↑{status.Ahead}  ↓{status.Behind}"
        : "Up to date";

    public bool HasChanges => Changes.Count > 0;

    public bool HasNoChanges => !HasChanges;

    public bool HasNoCommits => Commits.Count == 0;

    public bool HasNoBranches => Branches.Count == 0;

    public bool HasRemote => Repo.RemoteUrl is not null;

    public bool IsBusy => IsLoading || Repo.IsFetching;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial string OperationText { get; set; } = "";

    public void OnShown()
    {
        _isVisible = true;
        _ = RefreshAsync();
    }

    public void OnHidden()
    {
        _isVisible = false;
        _readCts?.Cancel();
        _readCts = null;
    }

    [RelayCommand]
    private void Back() => _goBack();

    [RelayCommand]
    private void CopyBranchName(string? branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        try
        {
            _clipboard.SetText(branchName);
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't copy {branchName}", ex.Message);
        }
    }

    [RelayCommand]
    private void CopyCurrentBranch() => CopyBranchName(Repo.Branch);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        CancelRead();
        CancellationTokenSource cts = new();
        _readCts = cts;
        IsLoading = true;
        IsStatusOpen = false;

        try
        {
            RepoDetails details = await GitService.ReadDetailsAsync(Repo.Path, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Replace(Changes, details.Changes);
            Replace(Commits, details.Commits);
            Replace(Branches, details.Branches);
            OnPropertyChanged(nameof(ChangesText));
            OnPropertyChanged(nameof(CommitsText));
            OnPropertyChanged(nameof(BranchesText));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasNoChanges));
            OnPropertyChanged(nameof(HasNoCommits));
            OnPropertyChanged(nameof(HasNoBranches));
            ViewDiffCommand.NotifyCanExecuteChanged();

            if (await GitService.ReadStatusAsync(Repo.Path, cts.Token) is { } status)
            {
                Repo.SetStatus(status);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowStatus("Couldn't read repository details", ex.Message);
        }
        finally
        {
            if (_readCts == cts)
            {
                _readCts = null;
                IsLoading = false;
            }

            cts.Dispose();
        }
    }

    private bool CanRunGitOperation => Repo.CanFetch;

    private bool CanRunRemoteGitOperation => CanRunGitOperation && HasRemote;

    [RelayCommand(CanExecute = nameof(CanRunGitOperation))]
    private Task FetchAsync() => RunGitAsync("Fetching...", "fetch", GitService.FetchAsync);

    [RelayCommand(CanExecute = nameof(CanRunRemoteGitOperation))]
    private Task PullAsync() => RunGitAsync("Pulling...", "pull", GitService.PullAsync);

    [RelayCommand(CanExecute = nameof(CanRunRemoteGitOperation))]
    private Task PushAsync() => RunGitAsync("Pushing...", "push", GitService.PushAsync);

    [RelayCommand(CanExecute = nameof(CanRunRemoteGitOperation))]
    private Task SyncAsync() => RunGitAsync("Syncing...", "sync", GitService.SyncAsync);

    private bool CanViewDiff => HasChanges;

    [RelayCommand(CanExecute = nameof(CanViewDiff))]
    private void ViewDiff() => _showDiff(Repo);

    private async Task RunGitAsync(
        string operationText,
        string verb,
        Func<string, CancellationToken, Task> operation)
    {
        if (Repo.IsFetching)
        {
            return;
        }

        Repo.SetFetching(true);
        OperationText = operationText;
        try
        {
            await operation(Repo.Path, _operationCts.Token);
            IsStatusOpen = false;
            if (_isVisible)
            {
                await RefreshAsync();
            }
            else if (await GitService.ReadStatusAsync(Repo.Path, _operationCts.Token) is { } status)
            {
                Repo.SetStatus(status);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't {verb} {Repo.Name}", ex.Message);
        }
        finally
        {
            OperationText = "";
            Repo.SetFetching(false);
        }
    }

    private void CancelRead()
    {
        _readCts?.Cancel();
        _readCts = null;
        IsLoading = false;
    }

    private void Repo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        FetchCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        SyncCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SyncText));
        OnPropertyChanged(nameof(IsBusy));
    }

    private void ShowStatus(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }

    public void Dispose()
    {
        OnHidden();
        _operationCts.Cancel();
        _operationCts.Dispose();
        Repo.PropertyChanged -= Repo_PropertyChanged;
    }
}
