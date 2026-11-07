using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayGit.Core;

namespace TrayGit.ViewModels;

public sealed partial class ActionSettingViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<string> _remove;

    public ActionSettingViewModel(ActionDef action, bool isShown, Action changed, Action<string> remove)
    {
        Action = action;
        _changed = changed;
        _remove = remove;
        IsShown = isShown;
    }

    public ActionDef Action { get; }

    public string Id => Action.Id;

    public string Name => Action.Name;

    public bool IsCustom => ActionCatalog.IsValidCustom(Action);

    public string RemoveAutomationName => $"Remove {Action.Name}";

    [ObservableProperty]
    public partial bool IsShown { get; set; }

    partial void OnIsShownChanged(bool value) => _changed();

    [RelayCommand(CanExecute = nameof(IsCustom))]
    private void Remove() => _remove(Id);
}
