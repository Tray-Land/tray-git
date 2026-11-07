using System.Globalization;

namespace TrayGit.Core;

/// <summary>A recent commit shown on the repository detail page.</summary>
public sealed record GitCommit(
    string Hash,
    string ShortHash,
    string Author,
    DateTimeOffset AuthoredAt,
    string Subject)
{
    public string DateText => AuthoredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}

/// <summary>A local or remote-tracking branch in a repository.</summary>
public sealed record GitBranch(
    string Name,
    string ShortHash,
    bool IsCurrent,
    bool IsRemote,
    string? Upstream)
{
    public string KindText => IsRemote ? "Remote" : "Local";
}

/// <summary>A file changed relative to HEAD.</summary>
public sealed record GitFileChange(
    string Path,
    string Status,
    bool IsStaged,
    bool IsUnstaged,
    int? Added,
    int? Removed)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string DirectoryText
    {
        get
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            return string.IsNullOrEmpty(directory) ? "" : directory.Replace('\\', '/');
        }
    }

    public string LineChangeText => Status == "Untracked"
        ? ""
        : Added is null || Removed is null
            ? "Binary"
            : $"+{Added} -{Removed}";

    public string StateText => IsStaged && IsUnstaged
        ? "Staged and unstaged"
        : IsStaged
            ? "Staged"
            : IsUnstaged
                ? "Unstaged"
                : "";
}

/// <summary>Read-only details collected while the repository page is visible.</summary>
public sealed record RepoDetails(
    IReadOnlyList<GitFileChange> Changes,
    IReadOnlyList<GitCommit> Commits,
    IReadOnlyList<GitBranch> Branches);

/// <summary>Parses the machine-readable git output used by the repository detail page.</summary>
public static class GitDetailsParser
{
    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';

    public static IReadOnlyList<GitFileChange> ParseChanges(string porcelain, string numstat)
    {
        Dictionary<string, (int? Added, int? Removed)> counts =
            ParseNumstat(numstat, StringComparer.OrdinalIgnoreCase);

        List<GitFileChange> changes = [];
        string[] records = porcelain.Split('\0');
        for (int index = 0; index < records.Length; index++)
        {
            string record = records[index];
            if (record.Length < 4 || record[2] != ' ')
            {
                continue;
            }

            string status = record[..2];
            string path = record[3..];
            if (status.IndexOfAny(['R', 'C']) >= 0 && index + 1 < records.Length)
            {
                index++;
            }

            char code = status[0] is not (' ' or '?') ? status[0] : status[1];
            counts.TryGetValue(path, out (int? Added, int? Removed) lineCounts);
            changes.Add(new GitFileChange(
                path,
                StatusText(code),
                IsStaged(status),
                IsUnstaged(status),
                lineCounts.Added,
                lineCounts.Removed));
        }

        return changes;
    }

    public static IReadOnlyList<GitCommit> ParseCommits(string output)
    {
        List<GitCommit> commits = [];
        foreach (string record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.TrimStart('\r', '\n').Split(FieldSeparator);
            if (fields.Length < 5
                || !DateTimeOffset.TryParse(
                    fields[3],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset authoredAt))
            {
                continue;
            }

            commits.Add(new GitCommit(fields[0], fields[1], fields[2], authoredAt, fields[4]));
        }

        return commits;
    }

    public static IReadOnlyList<GitBranch> ParseBranches(string output)
    {
        List<GitBranch> branches = [];
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split(FieldSeparator);
            if (fields.Length < 6 || fields[5].Length > 0)
            {
                continue;
            }

            bool isRemote = fields[0].StartsWith("refs/remotes/", StringComparison.Ordinal);
            branches.Add(new GitBranch(
                fields[1],
                fields[2],
                fields[3] == "*",
                isRemote,
                fields[4].Length == 0 ? null : fields[4]));
        }

        return branches;
    }

    private static Dictionary<string, (int? Added, int? Removed)> ParseNumstat(
        string output,
        StringComparer comparer)
    {
        Dictionary<string, (int? Added, int? Removed)> counts = new(comparer);
        foreach (string record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\t', 3);
            if (fields.Length != 3)
            {
                continue;
            }

            int? added = ParseNullableCount(fields[0]);
            int? removed = ParseNullableCount(fields[1]);
            counts[fields[2]] = (added, removed);
        }

        return counts;
    }

    private static int? ParseNullableCount(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : null;

    private static bool IsStaged(string status) =>
        status[0] is not (' ' or '?');

    private static bool IsUnstaged(string status) =>
        status[1] is not (' ' or '?');

    private static string StatusText(char status) => status switch
    {
        'A' => "Added",
        'D' => "Deleted",
        'M' => "Modified",
        'R' => "Renamed",
        'C' => "Copied",
        'T' => "Type changed",
        'U' => "Conflict",
        '?' => "Untracked",
        _ => "Changed",
    };
}
