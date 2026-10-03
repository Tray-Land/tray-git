namespace TrayGit.Core;

/// <summary>Orders and filters the repo list for the flyout.</summary>
public static class RepoListPolicy
{
    /// <summary>How many recently used repos are remembered.</summary>
    public const int MaxRecent = 50;

    /// <summary>
    /// Recently used repos first (newest first), then the rest by name. With a query, keeps repos
    /// whose name or relative path contains every word, ranking name-prefix matches first. When
    /// <paramref name="onlyPaths"/> is given, only repos at those paths are kept.
    /// </summary>
    public static List<GitRepo> Arrange(
        IEnumerable<GitRepo> repos,
        IReadOnlyDictionary<string, long> lastUsed,
        string? query,
        IReadOnlySet<string>? onlyPaths = null)
    {
        string[] words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string first = words.Length > 0 ? words[0] : "";

        return repos
            .Where(r => onlyPaths is null || onlyPaths.Contains(r.Path))
            .Where(r => words.All(w => Matches(r, w)))
            .OrderByDescending(r => first.Length > 0 && r.Name.StartsWith(first, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => lastUsed.TryGetValue(r.Path, out long ticks) ? ticks : 0)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Records a use and drops the oldest entries past <see cref="MaxRecent"/>.</summary>
    public static Dictionary<string, long> RecordUse(IReadOnlyDictionary<string, long> lastUsed, string path, DateTime utcNow)
    {
        Dictionary<string, long> updated = new(lastUsed, StringComparer.OrdinalIgnoreCase) { [path] = utcNow.Ticks };
        return updated
            .OrderByDescending(p => p.Value)
            .Take(MaxRecent)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The caption under a repo's name: "main · tools\cli".</summary>
    public static string Describe(GitRepo repo)
    {
        string? parent = Path.GetDirectoryName(repo.RelativePath);
        return (repo.Branch, string.IsNullOrEmpty(parent)) switch
        {
            (null, true) => "",
            (null, false) => parent!,
            (_, true) => repo.Branch,
            _ => $"{repo.Branch} · {parent}",
        };
    }

    private static bool Matches(GitRepo repo, string word) =>
        repo.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
        || repo.RelativePath.Contains(word, StringComparison.OrdinalIgnoreCase);
}
