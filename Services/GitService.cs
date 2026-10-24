using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TrayGit.Core;

namespace TrayGit.Services;

/// <summary>
/// Runs git.exe for the status segment and for Fetch. Everything else in the app reads <c>.git</c>
/// files directly; these two need git's own diff and network code. Calls only start from the
/// visible flyout, never from a timer, and never prompt on a console.
/// </summary>
internal static class GitService
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(2);

    // Repos listed in a recent flyout keep their last status, so a new flyout window isn't blank.
    private static readonly Dictionary<string, (RepoStatus Status, DateTime ReadUtc)> Known = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetKnown(string path, out RepoStatus status, out DateTime readUtc)
    {
        lock (Known)
        {
            if (Known.TryGetValue(path, out var entry))
            {
                (status, readUtc) = entry;
                return true;
            }
        }

        status = null!;
        readUtc = default;
        return false;
    }

    /// <summary>Reads changed lines and commits ahead/behind; null when git isn't available or fails.</summary>
    public static async Task<RepoStatus?> ReadStatusAsync(string repoPath, CancellationToken cancellationToken)
    {
        GitResult? status = await RunAsync(repoPath, GitStatusParser.StatusArguments, StatusTimeout, cancellationToken);
        if (status is not { ExitCode: 0 })
        {
            return null;
        }

        // A repo with no commits has no HEAD to diff against; that just means no changed lines.
        GitResult? diff = await RunAsync(repoPath, GitStatusParser.DiffArguments, StatusTimeout, cancellationToken);
        RepoStatus result = GitStatusParser.Combine(diff is { ExitCode: 0 } ? diff.Output : "", status.Output);

        lock (Known)
        {
            Known[repoPath] = (result, DateTime.UtcNow);
        }

        return result;
    }

    /// <summary>Runs <c>git fetch</c>; throws with git's message when it fails.</summary>
    public static Task FetchAsync(string repoPath, CancellationToken cancellationToken) =>
        RunNetworkAsync(repoPath, "fetch", ["fetch", "--quiet"], cancellationToken);

    /// <summary>Runs <c>git pull --ff-only</c>, so it never creates a merge or rebases behind the user's back.</summary>
    public static Task PullAsync(string repoPath, CancellationToken cancellationToken) =>
        RunNetworkAsync(repoPath, "pull", ["pull", "--ff-only", "--quiet"], cancellationToken);

    /// <summary>Runs <c>git push</c>; throws with git's message when it fails.</summary>
    public static Task PushAsync(string repoPath, CancellationToken cancellationToken) =>
        RunNetworkAsync(repoPath, "push", ["push", "--quiet"], cancellationToken);

    private static async Task RunNetworkAsync(string repoPath, string verb, string[] arguments, CancellationToken cancellationToken)
    {
        GitResult? result = await RunAsync(repoPath, arguments, FetchTimeout, cancellationToken)
            ?? throw new InvalidOperationException("git was not found. Install Git for Windows and make sure it is on the PATH.");

        if (result.TimedOut)
        {
            throw new TimeoutException($"git {verb} took longer than two minutes and was stopped.");
        }

        if (result.ExitCode != 0)
        {
            string message = result.Error.Trim();
            throw new InvalidOperationException(message.Length > 0 ? message : $"git {verb} exited with code {result.ExitCode}.");
        }
    }

    private sealed record GitResult(int ExitCode, string Output, string Error, bool TimedOut);

    // Returns null when git can't be started.
    private static async Task<GitResult?> RunAsync(string repoPath, string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo info = new("git")
        {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Fail instead of waiting for a password nobody can type.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using Process process = new() { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return null;
        }

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new GitResult(-1, "", "", TimedOut: true);
        }

        return new GitResult(process.ExitCode, await output, await error, TimedOut: false);
    }
}
