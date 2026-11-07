using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayGit.Core;

namespace TrayGit.ViewModels;

public sealed partial class RepoItemViewModel : ObservableObject, IDisposable
{
    private readonly Func<GitRepo, Task> _fetchAsync;
    private readonly Func<GitRepo, Task> _pullAsync;
    private readonly Func<GitRepo, Task> _pushAsync;
    private readonly Action<GitRepo> _showDetails;
    private readonly Action<GitRepo> _toggleFavorite;
    private readonly Action<GitRepo> _copyPath;

    public RepoItemViewModel(
        GitRepo repo,
        IEnumerable<ActionDef> toolbar,
        bool expanded,
        Func<GitRepo, Task> fetchAsync,
        Func<GitRepo, Task> pullAsync,
        Func<GitRepo, Task> pushAsync,
        Action<GitRepo> showDetails,
        Action<GitRepo> toggleFavorite,
        Action<GitRepo> copyPath,
        Func<ActionDef, GitRepo, Task> runActionAsync)
    {
        Repo = repo;
        _fetchAsync = fetchAsync;
        _pullAsync = pullAsync;
        _pushAsync = pushAsync;
        _showDetails = showDetails;
        _toggleFavorite = toggleFavorite;
        _copyPath = copyPath;
        Actions = new ObservableCollection<RepoActionViewModel>(
            toolbar.Select(action => new RepoActionViewModel(action, repo, expanded, runActionAsync)));
        Repo.PropertyChanged += Repo_PropertyChanged;
    }

    public GitRepo Repo { get; }

    public ObservableCollection<RepoActionViewModel> Actions { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        foreach (RepoActionViewModel action in Actions)
        {
            action.IsExpanded = value;
        }
    }

    private bool CanRunGitOperation => Repo.CanFetch;

    [RelayCommand(CanExecute = nameof(CanRunGitOperation))]
    private Task FetchAsync() => _fetchAsync(Repo);

    [RelayCommand(CanExecute = nameof(CanRunGitOperation))]
    private Task PullAsync() => _pullAsync(Repo);

    [RelayCommand(CanExecute = nameof(CanRunGitOperation))]
    private Task PushAsync() => _pushAsync(Repo);

    [RelayCommand]
    private void ToggleFavorite() => _toggleFavorite(Repo);

    [RelayCommand]
    private void ViewDetails() => _showDetails(Repo);

    [RelayCommand]
    private void CopyPath() => _copyPath(Repo);

    private void Repo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        FetchCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        Repo.PropertyChanged -= Repo_PropertyChanged;
    }
}
