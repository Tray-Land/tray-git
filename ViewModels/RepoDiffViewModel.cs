using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayGit.Core;
using TrayGit.Services;

namespace TrayGit.ViewModels;

public sealed partial class RepoDiffViewModel : ObservableObject, IDisposable
{
    private readonly Action _goBack;
    private CancellationTokenSource? _readCts;

    public RepoDiffViewModel(GitRepo repo, Action goBack)
    {
        Repo = repo;
        _goBack = goBack;
    }

    public GitRepo Repo { get; }

    public string RepoName => Repo.Name;

    [ObservableProperty]
    public partial string DiffText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    public bool HasDiff => DiffText.Length > 0;

    public bool HasNoDiff => !HasDiff;

    public void OnShown() => _ = LoadAsync();

    public void OnHidden()
    {
        _readCts?.Cancel();
        _readCts = null;
    }

    [RelayCommand]
    private void Back() => _goBack();

    [RelayCommand]
    private async Task LoadAsync()
    {
        OnHidden();
        CancellationTokenSource cts = new();
        _readCts = cts;
        IsLoading = true;
        IsStatusOpen = false;

        try
        {
            string diff = await GitService.ReadDiffAsync(Repo.Path, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                DiffText = diff.TrimEnd();
                OnPropertyChanged(nameof(HasDiff));
                OnPropertyChanged(nameof(HasNoDiff));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            IsStatusOpen = true;
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

    public void Dispose() => OnHidden();
}
