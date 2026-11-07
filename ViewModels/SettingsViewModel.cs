using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayGit.Core;
using TrayGit.Services;
using Windows.ApplicationModel;

namespace TrayGit.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ITrayHost _host;
    private readonly Action _goBack;
    private bool _loadingState;
    private bool _isStartupUpdating;

    public SettingsViewModel(ITrayHost host, Action goBack)
    {
        _host = host;
        _goBack = goBack;
        VersionText = $"{App.DisplayName} {GetVersion()}";
    }

    public ObservableCollection<ActionSettingViewModel> Actions { get; } = [];

    public ObservableCollection<ActionDef> ActionChoices { get; } = [];

    [ObservableProperty]
    public partial string SourceFolderText { get; set; } = "";

    [ObservableProperty]
    public partial bool StartupEnabled { get; set; }

    partial void OnStartupEnabledChanged(bool value)
    {
        if (!_loadingState && !_isStartupUpdating)
        {
            SetStartupCommand.Execute(value);
        }
    }

    [ObservableProperty]
    public partial bool StartupToggleEnabled { get; set; }

    [ObservableProperty]
    public partial string StartupDescription { get; set; } = "";

    [ObservableProperty]
    public partial bool HotKeyEnabled { get; set; }

    partial void OnHotKeyEnabledChanged(bool value)
    {
        if (_loadingState)
        {
            return;
        }

        SettingsService.HotKeyEnabled = value;
        CancelHotKeyCapture();
    }

    [ObservableProperty]
    public partial bool HotKeyCaptureEnabled { get; set; }

    [ObservableProperty]
    public partial string HotKeyButtonText { get; set; } = "";

    [ObservableProperty]
    public partial string HotKeyDescription { get; set; } = "";

    [ObservableProperty]
    public partial bool IsCapturingHotKey { get; set; }

    [ObservableProperty]
    public partial string CustomActionName { get; set; } = "";

    partial void OnCustomActionNameChanged(string value) => AddActionCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    public partial string CustomActionCommand { get; set; } = "";

    partial void OnCustomActionCommandChanged(string value) => AddActionCommand.NotifyCanExecuteChanged();

    [ObservableProperty]
    public partial ActionDef? EnterAction { get; set; }

    partial void OnEnterActionChanged(ActionDef? value)
    {
        if (!_loadingState && value is not null)
        {
            SettingsService.EnterActionId = value.Id;
        }
    }

    [ObservableProperty]
    public partial ActionDef? ClickAction { get; set; }

    partial void OnClickActionChanged(ActionDef? value)
    {
        if (!_loadingState && value is not null)
        {
            SettingsService.ClickActionId = value.Id;
        }
    }

    [ObservableProperty]
    public partial ActionDef? DoubleClickAction { get; set; }

    partial void OnDoubleClickActionChanged(ActionDef? value)
    {
        if (!_loadingState && value is not null)
        {
            SettingsService.DoubleClickActionId = value.Id;
        }
    }

    public string VersionText { get; }

    public async Task OnShownAsync()
    {
        _loadingState = true;
        SourceFolderText = SettingsService.SourceFolder is { Length: > 0 } folder
            ? folder
            : "Choose the folder that holds your git repos. Repos up to four folders deep are listed.";
        ShowHotKeyState();
        ShowActions();
        _loadingState = false;

        ShowStartupState(await StartupService.GetStateAsync());
    }

    [RelayCommand]
    private void Back() => _goBack();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (await _host.ChooseSourceFolderAsync())
        {
            SourceFolderText = SettingsService.SourceFolder is { Length: > 0 } folder
                ? folder
                : "";
        }
    }

    [RelayCommand]
    private async Task SetStartupAsync(bool enabled)
    {
        if (_loadingState || _isStartupUpdating)
        {
            return;
        }

        _isStartupUpdating = true;
        try
        {
            ShowStartupState(await StartupService.SetEnabledAsync(enabled));
        }
        finally
        {
            _isStartupUpdating = false;
        }
    }

    [RelayCommand]
    private void BeginHotKeyCapture()
    {
        IsCapturingHotKey = true;
        UpdateHotKeyDisplay();
    }

    public bool CaptureHotKey(bool isEscape, uint virtualKey, uint modifiers)
    {
        if (!IsCapturingHotKey)
        {
            return false;
        }

        if (isEscape)
        {
            CancelHotKeyCapture();
            return true;
        }

        HotKey candidate = new(modifiers, virtualKey);
        if (!candidate.IsValid)
        {
            return false;
        }

        IsCapturingHotKey = false;
        SettingsService.HotKey = candidate;
        UpdateHotKeyDisplay();
        return true;
    }

    public void CancelHotKeyCapture()
    {
        if (!IsCapturingHotKey)
        {
            return;
        }

        IsCapturingHotKey = false;
        UpdateHotKeyDisplay();
    }

    public void RefreshHotKeyState() => UpdateHotKeyDisplay();

    [RelayCommand(CanExecute = nameof(CanAddAction))]
    private void AddAction()
    {
        if (string.IsNullOrWhiteSpace(CustomActionName) || string.IsNullOrWhiteSpace(CustomActionCommand))
        {
            return;
        }

        ActionDef action = ActionCatalog.NewCustom(CustomActionName, CustomActionCommand);
        List<ActionDef> custom = SettingsService.CustomActions;
        custom.Add(action);
        SettingsService.CustomActions = custom;

        List<string> shown = SettingsService.ToolbarActionIds;
        shown.Add(action.Id);
        SettingsService.ToolbarActionIds = shown;
        SettingsService.ActionOrderIds = ActionCatalog.Order(
            SettingsService.ActionOrderIds,
            ActionCatalog.All(custom)).Select(item => item.Id).ToList();

        CustomActionName = "";
        CustomActionCommand = "";
        ShowActions();
    }

    private bool CanAddAction =>
        !string.IsNullOrWhiteSpace(CustomActionName)
        && !string.IsNullOrWhiteSpace(CustomActionCommand);

    public void SaveActionOrder()
    {
        if (_loadingState)
        {
            return;
        }

        SettingsService.ActionOrderIds = Actions.Select(item => item.Id).ToList();
        SettingsService.ToolbarActionIds = Actions.Where(item => item.IsShown).Select(item => item.Id).ToList();
    }

    private void ShowActions()
    {
        bool wasLoading = _loadingState;
        _loadingState = true;
        List<ActionDef> custom = SettingsService.CustomActions;
        List<ActionDef> all = ActionCatalog.Order(SettingsService.ActionOrderIds, ActionCatalog.All(custom));
        HashSet<string> shown = [.. SettingsService.ToolbarActionIds];

        Actions.Clear();
        foreach (ActionDef action in all)
        {
            Actions.Add(new ActionSettingViewModel(action, shown.Contains(action.Id), SaveActionOrder, RemoveAction));
        }

        ActionChoices.Clear();
        ActionChoices.Add(ActionCatalog.None);
        foreach (ActionDef action in all)
        {
            ActionChoices.Add(action);
        }

        EnterAction = ActionChoices.First(choice =>
            choice.Id == ActionCatalog.Resolve(SettingsService.EnterActionId, custom, ActionCatalog.DefaultEnterId).Id);
        ClickAction = ActionChoices.First(choice =>
            choice.Id == ActionCatalog.Resolve(SettingsService.ClickActionId, custom, ActionCatalog.DefaultClickId).Id);
        DoubleClickAction = ActionChoices.First(choice =>
            choice.Id == ActionCatalog.Resolve(SettingsService.DoubleClickActionId, custom, ActionCatalog.DefaultDoubleClickId).Id);
        _loadingState = wasLoading;
    }

    private void RemoveAction(string id)
    {
        SettingsService.CustomActions = SettingsService.CustomActions.Where(action => action.Id != id).ToList();
        SettingsService.ToolbarActionIds = SettingsService.ToolbarActionIds.Where(item => item != id).ToList();
        SettingsService.ActionOrderIds = SettingsService.ActionOrderIds.Where(item => item != id).ToList();
        ShowActions();
    }

    private void ShowHotKeyState()
    {
        HotKeyEnabled = SettingsService.HotKeyEnabled;
        HotKeyCaptureEnabled = HotKeyEnabled;
        IsCapturingHotKey = false;
        UpdateHotKeyDisplay();
    }

    private void UpdateHotKeyDisplay()
    {
        HotKeyCaptureEnabled = HotKeyEnabled;
        HotKeyButtonText = IsCapturingHotKey
            ? "Press a shortcut… (Esc cancels)"
            : SettingsService.HotKey.Format();
        HotKeyDescription = _host.HotKeyConflicts
            ? "Another app is using this shortcut. Choose a different one."
            : "Open the list from anywhere, ready to search.";
    }

    private void ShowStartupState(StartupTaskState? state)
    {
        _loadingState = true;
        StartupEnabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        StartupToggleEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep the tray icon ready after you sign in.",
        };
        _loadingState = false;
    }

    private static string GetVersion()
    {
        try
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }
}
