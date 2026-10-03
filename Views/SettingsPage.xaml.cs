using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayGit.Services;
using Windows.ApplicationModel;

namespace TrayGit.Views;

/// <summary>Settings, shown in place of the repo list inside the flyout.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private bool _loading = true;

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
        _ = LoadStartupStateAsync();
        BackButton.Focus(FocusState.Programmatic);
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
