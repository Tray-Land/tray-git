using System.Globalization;

namespace TrayGit.Core;

/// <summary>What <c>git</c> reports for a working copy.</summary>
/// <param name="Added">Lines added in the working tree and index, relative to HEAD.</param>
/// <param name="Removed">Lines removed in the working tree and index, relative to HEAD.</param>
/// <param name="Behind">Commits on the upstream branch that aren't local yet (to pull).</param>
/// <param name="Ahead">Local commits the upstream branch doesn't have (to push).</param>
public sealed record RepoStatus(int Added, int Removed, int Behind, int Ahead)
{
    public bool IsClean => Added == 0 && Removed == 0 && Behind == 0 && Ahead == 0;
}

/// <summary>Parses the output of the git commands the app runs for the status segment.</summary>
public static class GitStatusParser
{
    /// <summary>Arguments (after <c>git</c>) that list changed lines per file.</summary>
    public static readonly string[] DiffArguments = ["--no-optional-locks", "diff", "HEAD", "--numstat", "--no-renames"];

    /// <summary>Arguments (after <c>git</c>) that report the branch's upstream distance.</summary>
    public static readonly string[] StatusArguments = ["--no-optional-locks", "status", "--porcelain=v2", "--branch", "--untracked-files=no"];

    /// <summary>
    /// Sums <c>git diff --numstat</c> ("12\t3\tpath"). Binary files are reported as "-\t-" and have no
    /// line counts, so they're skipped.
    /// </summary>
    public static (int Added, int Removed) ParseNumstat(string output)
    {
        long added = 0;
        long removed = 0;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\t', 3);
            if (parts.Length >= 3
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int a)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int r))
            {
                added += a;
                removed += r;
            }
        }

        return (Clamp(added), Clamp(removed));
    }

    /// <summary>
    /// Reads "# branch.ab +2 -1" from <c>git status --porcelain=v2 --branch</c> (ahead 2, behind 1).
    /// The line is absent when the branch has no upstream, which reads as (0, 0).
    /// </summary>
    public static (int Ahead, int Behind) ParseBranchAheadBehind(string output)
    {
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4
                && parts[2].StartsWith('+') && parts[3].StartsWith('-')
                && int.TryParse(parts[2][1..], NumberStyles.None, CultureInfo.InvariantCulture, out int ahead)
                && int.TryParse(parts[3][1..], NumberStyles.None, CultureInfo.InvariantCulture, out int behind))
            {
                return (ahead, behind);
            }
        }

        return (0, 0);
    }

    public static RepoStatus Combine(string numstatOutput, string statusOutput)
    {
        (int added, int removed) = ParseNumstat(numstatOutput);
        (int ahead, int behind) = ParseBranchAheadBehind(statusOutput);
        return new RepoStatus(added, removed, behind, ahead);
    }

    private static int Clamp(long value) => (int)Math.Min(value, int.MaxValue);
}
