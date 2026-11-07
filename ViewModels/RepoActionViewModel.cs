using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayGit.Core;

namespace TrayGit.ViewModels;

public sealed partial class RepoActionViewModel : ObservableObject
{
    private readonly Func<ActionDef, GitRepo, Task> _runAsync;

    public RepoActionViewModel(ActionDef action, GitRepo repo, bool expanded, Func<ActionDef, GitRepo, Task> runAsync)
    {
        Action = action;
        Repo = repo;
        IsExpanded = expanded;
        _runAsync = runAsync;
    }

    public ActionDef Action { get; }

    public GitRepo Repo { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opacity))]
    public partial bool IsExpanded { get; set; }

    public double Opacity => IsExpanded ? 1 : 0;

    public string Name => Action.Name;

    public string Tooltip => Action.Kind == ActionKind.Remote ? Repo.RemoteTooltip : Action.Name;

    public bool IsEnabled => Action.Kind != ActionKind.Remote || Repo.HasWebUrl;

    public string Image => Action.Image ?? "";

    public bool HasImage => Action.Image is not null;

    public string Glyph => Action.Glyph ?? "";

    public bool HasGlyph => !HasImage && Action.Glyph is not null;

    public string Badge => Action.Badge ?? "";

    public bool HasBadge => !HasImage && Action.Glyph is null && Action.Badge is not null;

    [RelayCommand]
    private Task RunAsync() => _runAsync(Action, Repo);
}
