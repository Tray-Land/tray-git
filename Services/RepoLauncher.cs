using System.ComponentModel;
using System.Diagnostics;
using TrayGit.Core;

namespace TrayGit.Services;

/// <summary>The three one-click actions on a repo. Each records the repo as recently used.</summary>
internal static class RepoLauncher
{
    /// <summary>Runs a terminal or folder action. Remote is async and goes through <see cref="OpenRemoteAsync"/>.</summary>
    public static void Launch(ActionDef action, GitRepo repo)
    {
        switch (action.Kind)
        {
            case ActionKind.Terminal:
                OpenTerminal(repo, null);
                break;
            case ActionKind.Command:
                OpenTerminal(repo, action.Command);
                break;
            case ActionKind.Folder:
                OpenFolder(repo);
                break;
        }
    }

    /// <summary>Opens Windows Terminal in the repo (running <paramref name="command"/> if given), falling back to PowerShell if it isn't installed.</summary>
    public static void OpenTerminal(GitRepo repo, string? command)
    {
        RecordUse(repo);
        try
        {
            string args = Core.TerminalCommand.BuildWindowsTerminalArguments(repo.Path, FindShell() ?? "powershell.exe", command);
            Start(new ProcessStartInfo("wt.exe", args) { UseShellExecute = true });
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
                Start(new ProcessStartInfo(shell, Core.TerminalCommand.BuildShellArguments(command))
                {
                    WorkingDirectory = repo.Path,
                    UseShellExecute = true,
                });
                return;
            }
            catch (Win32Exception)
            {
            }
        }
    }

    /// <summary>The first PowerShell on PATH, preferring PowerShell 7, or null if neither is found.</summary>
    private static string? FindShell()
    {
        string[] dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string shell in new[] { "pwsh.exe", "powershell.exe" })
        {
            foreach (string dir in dirs)
            {
                try
                {
                    if (File.Exists(Path.Combine(dir.Trim('"'), shell)))
                    {
                        return shell;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry; skip it.
                }
            }
        }

        return null;
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
