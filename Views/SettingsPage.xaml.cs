using Microsoft.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TrayGit.Core;
using TrayGit.Services;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Core;

namespace TrayGit.Views;

/// <summary>Settings, shown in place of the repo list inside the flyout.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private bool _loading = true;
    private bool _isCapturingHotKey;

    public SettingsPage(Action goBack)
    {
        _goBack = goBack;
        InitializeComponent();
        VersionText.Text = $"{App.DisplayName} {GetVersion()}";
        _loading = false;
    }

    /// <summary>The page appeared: re-read state that may have changed elsewhere and focus Back.</summary>
    public void OnShown()
    {
        ShowSourceFolder();
        ShowHotKeyState();
        ShowActions();
        _ = LoadStartupStateAsync();
        BackButton.Focus(FocusState.Programmatic);
    }

    // Rebuilds the button checklist and the three pickers from what is stored.
    private void ShowActions()
    {
        _loading = true;
        List<ActionDef> custom = SettingsService.CustomActions;
        List<ActionDef> all = ActionCatalog.All(custom);
        HashSet<string> shown = [.. SettingsService.ToolbarActionIds];

        ActionsPanel.Children.Clear();
        foreach (ActionDef action in all)
        {
            CheckBox box = new() { Content = action.Name, Tag = action.Id, IsChecked = shown.Contains(action.Id), MinWidth = 0 };
            box.Checked += ActionBox_Changed;
            box.Unchecked += ActionBox_Changed;

            if (!ActionCatalog.IsValidCustom(action))
            {
                ActionsPanel.Children.Add(box);
                continue;
            }

            Button remove = new()
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Tag = action.Id,
                Style = (Style)Application.Current.Resources["FlyoutIconButtonStyle"],
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {action.Name}");
            ToolTipService.SetToolTip(remove, "Remove");
            remove.Click += RemoveActionButton_Click;

            Grid row = new() { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(box);
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            ActionsPanel.Children.Add(row);
        }

        List<ActionDef> choices = [ActionCatalog.None, .. all];
        Pick(EnterCombo, choices, SettingsService.EnterActionId, custom, ActionCatalog.DefaultEnterId);
        Pick(ClickCombo, choices, SettingsService.ClickActionId, custom, ActionCatalog.DefaultClickId);
        Pick(DoubleClickCombo, choices, SettingsService.DoubleClickActionId, custom, ActionCatalog.DefaultDoubleClickId);
        _loading = false;
    }

    private static void Pick(ComboBox combo, List<ActionDef> choices, string id, List<ActionDef> custom, string fallbackId)
    {
        string resolved = ActionCatalog.Resolve(id, custom, fallbackId).Id;
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.First(c => c.Id == resolved);
    }

    private void ActionBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        // Keep the catalog order so the buttons don't depend on the order they were ticked.
        HashSet<string> ticked = [];
        foreach (UIElement child in ActionsPanel.Children)
        {
            CheckBox? box = child as CheckBox ?? (child as Grid)?.Children.OfType<CheckBox>().FirstOrDefault();
            if (box is { IsChecked: true, Tag: string id })
            {
                ticked.Add(id);
            }
        }

        SettingsService.ToolbarActionIds = ActionCatalog.All(SettingsService.CustomActions).Select(a => a.Id).Where(ticked.Contains).ToList();
    }

    private void AddActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CustomNameBox.Text) || string.IsNullOrWhiteSpace(CustomCommandBox.Text))
        {
            return;
        }

        ActionDef action = ActionCatalog.NewCustom(CustomNameBox.Text, CustomCommandBox.Text);
        List<ActionDef> custom = SettingsService.CustomActions;
        custom.Add(action);
        SettingsService.CustomActions = custom;

        List<string> shown = SettingsService.ToolbarActionIds;
        shown.Add(action.Id);
        SettingsService.ToolbarActionIds = shown;

        CustomNameBox.Text = "";
        CustomCommandBox.Text = "";
        ShowActions();
    }

    private void RemoveActionButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string id)
        {
            return;
        }

        SettingsService.CustomActions = SettingsService.CustomActions.Where(a => a.Id != id).ToList();
        SettingsService.ToolbarActionIds = SettingsService.ToolbarActionIds.Where(i => i != id).ToList();
        ShowActions();
    }

    private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || (sender as ComboBox)?.SelectedItem is not ActionDef action)
        {
            return;
        }

        if (ReferenceEquals(sender, EnterCombo))
        {
            SettingsService.EnterActionId = action.Id;
        }
        else if (ReferenceEquals(sender, ClickCombo))
        {
            SettingsService.ClickActionId = action.Id;
        }
        else
        {
            SettingsService.DoubleClickActionId = action.Id;
        }
    }

    private void ShowSourceFolder() =>
        SourceFolderText.Text = SettingsService.SourceFolder is { Length: > 0 } folder
            ? folder
            : "Choose the folder that holds your git repos. Repos up to four folders deep are listed.";

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        // The flyout ignores a second pick while one is open, so the button can stay enabled and
        // keep focus for when the picker returns.
        if (await App.Current.ChooseSourceFolderAsync())
        {
            ShowSourceFolder();
        }

        BrowseButton.Focus(FocusState.Programmatic);
    }

    private void ShowHotKeyState()
    {
        _loading = true;
        bool enabled = SettingsService.HotKeyEnabled;
        HotKeyToggle.IsOn = enabled;
        HotKeyButton.IsEnabled = enabled;
        HotKeyButton.Content = _isCapturingHotKey ? "Press a shortcut… (Esc cancels)" : SettingsService.HotKey.Format();
        HotKeyDescription.Text = App.Current.HotKeyConflicts
            ? "Another app is using this shortcut. Choose a different one."
            : "Open the list from anywhere, ready to search.";
        _loading = false;
    }

    private void HotKeyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        SettingsService.HotKeyEnabled = HotKeyToggle.IsOn;
        _isCapturingHotKey = false;
        // The app re-registers on its dispatcher; look again once that has run.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ShowHotKeyState);
    }

    private void HotKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _isCapturingHotKey = true;
        ShowHotKeyState();
    }

    private void HotKeyButton_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isCapturingHotKey)
        {
            _isCapturingHotKey = false;
            ShowHotKeyState();
        }
    }

    private void HotKeyButton_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isCapturingHotKey)
        {
            return;
        }

        e.Handled = true;
        uint key = (uint)e.Key;
        if (e.Key == VirtualKey.Escape)
        {
            _isCapturingHotKey = false;
        }
        else if (!HotKey.IsModifierKey(key))
        {
            uint modifiers = 0;
            if (IsDown(VirtualKey.Control)) modifiers |= HotKey.Control;
            if (IsDown(VirtualKey.Menu)) modifiers |= HotKey.Alt;
            if (IsDown(VirtualKey.Shift)) modifiers |= HotKey.Shift;
            if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= HotKey.Win;

            HotKey candidate = new(modifiers, key);
            if (!candidate.IsValid)
            {
                return; // keep listening: a bare key isn't a usable global shortcut
            }

            _isCapturingHotKey = false;
            SettingsService.HotKey = candidate;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ShowHotKeyState);
        }
        else
        {
            return; // a modifier on its own: wait for the rest of the combination
        }

        ShowHotKeyState();
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void BackButton_Click(object sender, RoutedEventArgs e) => _goBack();

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadStartupStateAsync()
    {
        ShowStartupState(await StartupService.GetStateAsync());
    }

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say; the app
        // can't override it, so say where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep the tray icon ready after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }
}
