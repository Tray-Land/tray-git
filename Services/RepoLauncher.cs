using System.ComponentModel;
using System.Diagnostics;
using TrayGit.Core;

namespace TrayGit.Services;

/// <summary>The three one-click actions on a repo. Each records the repo as recently used.</summary>
internal static class RepoLauncher
{
    /// <summary>Opens Windows Terminal in the repo, falling back to PowerShell if it isn't installed.</summary>
    public static void OpenTerminal(GitRepo repo)
    {
        RecordUse(repo);
        try
        {
            // wt treats ';' as a command separator, and a trailing '\' before the closing quote
            // escapes it ("D:\" breaks the argument), so normalize both.
            string dir = repo.Path.TrimEnd('\\');
            if (dir.EndsWith(':'))
            {
                dir += "\\.";
            }

            Start(new ProcessStartInfo("wt.exe", $"-d \"{dir.Replace(";", "\\;")}\"") { UseShellExecute = true });
            return;
        }
        catch (Win32Exception)
        {
            // Windows Terminal isn't installed (or its app alias is off); use a console host instead.
        }

        foreach (string shell in new[] { "pwsh.exe", "powershell.exe" })
        {
            try
            {
                Start(new ProcessStartInfo(shell) { WorkingDirectory = repo.Path, UseShellExecute = true });
                return;
            }
            catch (Win32Exception)
            {
            }
        }
    }

    public static void OpenFolder(GitRepo repo)
    {
        RecordUse(repo);
        Start(new ProcessStartInfo("explorer.exe", $"\"{repo.Path}\"") { UseShellExecute = true });
    }

    public static async Task OpenRemoteAsync(GitRepo repo)
    {
        if (repo.WebUrl is null || !Uri.TryCreate(repo.WebUrl, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        RecordUse(repo);
        await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private static void Start(ProcessStartInfo info)
    {
        using Process? _ = Process.Start(info);
    }

    private static void RecordUse(GitRepo repo) =>
        SettingsService.LastUsed = RepoListPolicy.RecordUse(SettingsService.LastUsed, repo.Path, DateTime.UtcNow);
}
