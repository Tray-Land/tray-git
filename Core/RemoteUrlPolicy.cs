namespace TrayGit.Core;

/// <summary>Turns a git remote URL into the web page for that repo.</summary>
public static class RemoteUrlPolicy
{
    /// <summary>
    /// Maps https, http, ssh, git:// and scp-style (<c>git@host:owner/repo.git</c>) remotes to an
    /// https page, dropping credentials, ports and the <c>.git</c> suffix. Returns null for local
    /// paths and anything else with no web page.
    /// </summary>
    public static string? ToWebUrl(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return null;
        }

        remote = remote.Trim();
        string host;
        string path;
        string scheme = "https";

        if (remote.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(remote, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host))
            {
                return null;
            }

            switch (uri.Scheme)
            {
                case "http":
                    scheme = "http";
                    break;
                case "https" or "ssh" or "git" or "git+ssh" or "ssh+git":
                    break;
                default:
                    return null; // file:// and friends
            }

            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);

            // An explicit port belongs to the ssh/git daemon, not the web server, except on http(s).
            if (scheme == "http" || uri.Scheme == "https")
            {
                if (!uri.IsDefaultPort)
                {
                    host = $"{host}:{uri.Port}";
                }
            }
        }
        else
        {
            // scp-like syntax: [user@]host:path. A drive letter (C:\repo) is a local path.
            int colon = remote.IndexOf(':');
            int slash = remote.IndexOfAny(['/', '\\']);
            if (colon <= 1 || (slash >= 0 && slash < colon))
            {
                return null;
            }

            host = remote[..colon];
            int at = host.LastIndexOf('@');
            if (at >= 0)
            {
                host = host[(at + 1)..];
            }

            path = "/" + remote[(colon + 1)..].TrimStart('/');
        }

        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        path = path.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        // Azure DevOps ssh remotes: ssh.dev.azure.com:v3/org/project/repo
        if (host.Equals("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            && path.Split('/', StringSplitOptions.RemoveEmptyEntries) is ["v3", var org, var project, var repo])
        {
            return $"https://dev.azure.com/{org}/{project}/_git/{repo}";
        }

        if (path.Length <= 1)
        {
            return null;
        }

        return $"{scheme}://{host}{path}";
    }
}
