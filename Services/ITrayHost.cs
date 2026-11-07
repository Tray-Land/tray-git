using TrayGit.Core;

namespace TrayGit.Services;

/// <summary>Coordinates view-model requests that change the tray app's shell.</summary>
public interface ITrayHost
{
    bool HotKeyConflicts { get; }

    void ShowFlyout();

    void HideFlyout();

    void ShowSettings();

    void ShowRepoDetails(GitRepo repo);

    Task<bool> ChooseSourceFolderAsync();

    void SetTrayStatus(string? status);
}
