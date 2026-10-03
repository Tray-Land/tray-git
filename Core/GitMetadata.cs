namespace TrayGit.Core;

/// <summary>
/// Reads the little a list needs (branch and remote) straight from the <c>.git</c> files, so the
/// app never spawns git.exe and works without git on the PATH.
/// </summary>
public static class GitMetadata
{
    /// <summary>
    /// Returns the git directory for a working copy: <c>.git</c> itself, or the target of a
    /// <c>.git</c> file (<c>gitdir: ...</c>) as used by worktrees and submodules.
    /// </summary>
    public static string? FindGitDir(string workingCopy)
    {
        string dotGit = Path.Combine(workingCopy, ".git");
        if (Directory.Exists(dotGit))
        {
            return dotGit;
        }

        if (!File.Exists(dotGit))
        {
            return null;
        }

        try
        {
            string text = File.ReadAllText(dotGit).Trim();
            const string prefix = "gitdir:";
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string target = text[prefix.Length..].Trim();
            string full = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(workingCopy, target));
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The shared git directory: worktrees keep config in the main repo's (<c>commondir</c>).</summary>
    public static string CommonDir(string gitDir)
    {
        try
        {
            string commonFile = Path.Combine(gitDir, "commondir");
            if (File.Exists(commonFile))
            {
                string target = File.ReadAllText(commonFile).Trim();
                if (target.Length > 0)
                {
                    return Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(gitDir, target));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return gitDir;
    }

    public static string? ReadBranch(string gitDir)
    {
        try
        {
            return ParseHead(File.ReadAllText(Path.Combine(gitDir, "HEAD")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? ReadRemoteUrl(string gitDir)
    {
        try
        {
            return ParseRemoteUrl(File.ReadAllText(Path.Combine(CommonDir(gitDir), "config")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>"ref: refs/heads/main" → "main"; a detached commit id → its first 7 characters.</summary>
    public static string? ParseHead(string head)
    {
        head = head.Trim();
        const string refPrefix = "ref:";
        if (head.StartsWith(refPrefix, StringComparison.Ordinal))
        {
            string reference = head[refPrefix.Length..].Trim();
            const string heads = "refs/heads/";
            return reference.StartsWith(heads, StringComparison.Ordinal) ? reference[heads.Length..] : reference;
        }

        return head.Length >= 7 && head.All(Uri.IsHexDigit) ? head[..7] : null;
    }

    /// <summary>The <c>url</c> of <c>[remote "origin"]</c>, or of the first remote if there's no origin.</summary>
    public static string? ParseRemoteUrl(string config)
    {
        string? currentRemote = null;
        string? first = null;

        foreach (string rawLine in config.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                currentRemote = ParseRemoteSection(line);
                continue;
            }

            if (currentRemote is null)
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0 || !line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = StripValue(line[(equals + 1)..]);
            if (value.Length == 0)
            {
                continue;
            }

            if (currentRemote == "origin")
            {
                return value;
            }

            first ??= value;
        }

        return first;
    }

    // [remote "origin"] → origin; any other section → null.
    private static string? ParseRemoteSection(string line)
    {
        int close = line.IndexOf(']');
        string inner = (close > 0 ? line[1..close] : line[1..]).Trim();
        if (!inner.StartsWith("remote", StringComparison.OrdinalIgnoreCase) || inner.Length <= 6 || !char.IsWhiteSpace(inner[6]))
        {
            return null;
        }

        return inner[6..].Trim().Trim('"');
    }

    private static string StripValue(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"')
        {
            int end = value.IndexOf('"', 1);
            return end > 0 ? value[1..end] : value[1..];
        }

        // Inline comments start with # or ; outside quotes.
        int comment = value.IndexOfAny(['#', ';']);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }
}
