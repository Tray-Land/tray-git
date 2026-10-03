namespace TrayGit.Core;

/// <summary>Finds git working copies under a folder.</summary>
public static class RepoScanner
{
    public const int DefaultMaxDepth = 4;

    // Big trees that never hold a repo of their own; skipped unless the folder itself is a repo.
    private static readonly HashSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "vendor", "target", "dist", "out",
        ".vs", ".vscode", ".idea", "__pycache__", ".venv", "venv", "AppPackages",
    };

    // Hidden folders are fine (.git is hidden on Windows, and so are some users' folders), but
    // system folders and junctions are not: junctions can loop or point at another drive.
    private static readonly EnumerationOptions ChildOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// Walks <paramref name="root"/> up to <paramref name="maxDepth"/> folders deep. A repo is
    /// listed and not descended into, so submodules and vendored checkouts stay out of the list.
    /// Results are sorted by name.
    /// </summary>
    public static List<GitRepo> Scan(string root, int maxDepth = DefaultMaxDepth, CancellationToken cancellationToken = default)
    {
        List<GitRepo> repos = [];
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return repos;
        }

        root = Path.GetFullPath(root);
        Queue<(string Path, int Depth)> pending = new();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string dir, int depth) = pending.Dequeue();

            if (TryRead(root, dir) is { } repo)
            {
                repos.Add(repo);
                continue;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir, "*", ChildOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // A skipped name can still be a repo itself; check that before skipping it.
                if (SkippedNames.Contains(name) || (name.StartsWith('.') && name.Length > 1))
                {
                    if (TryRead(root, child) is { } skippedRepo)
                    {
                        repos.Add(skippedRepo);
                    }

                    continue;
                }

                pending.Enqueue((child, depth + 1));
            }
        }

        repos.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return repos;
    }

    /// <summary>Reads one working copy, or returns null if <paramref name="dir"/> isn't one.</summary>
    public static GitRepo? TryRead(string root, string dir)
    {
        if (GitMetadata.FindGitDir(dir) is not { } gitDir)
        {
            return null;
        }

        string remote = GitMetadata.ReadRemoteUrl(gitDir) ?? "";
        string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new GitRepo(
            Name: string.IsNullOrEmpty(name) ? dir : name,
            Path: dir,
            RelativePath: Path.GetRelativePath(root, dir),
            Branch: GitMetadata.ReadBranch(gitDir),
            RemoteUrl: remote.Length > 0 ? remote : null,
            WebUrl: RemoteUrlPolicy.ToWebUrl(remote));
    }
}
