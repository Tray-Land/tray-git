using System.Text.Json.Serialization;

namespace TrayGit.Core;

/// <summary>A working copy found under the source folder.</summary>
/// <param name="Name">The repo's folder name.</param>
/// <param name="Path">Full path of the working copy.</param>
/// <param name="RelativePath">Path relative to the source folder ("." when the folder itself is the repo).</param>
/// <param name="Branch">Checked-out branch, a short commit id when detached, or null if unreadable.</param>
/// <param name="RemoteUrl">The fetch URL of <c>origin</c> (or the first remote), as written in the config.</param>
/// <param name="WebUrl">A browser URL for the remote, or null when it has no web page (local path, unknown scheme).</param>
public sealed record GitRepo(
    string Name,
    string Path,
    string RelativePath,
    string? Branch,
    string? RemoteUrl,
    string? WebUrl)
{
    /// <summary>The caption under the name in the list.</summary>
    [JsonIgnore]
    public string Description => RepoListPolicy.Describe(this);

    [JsonIgnore]
    public bool HasWebUrl => WebUrl is not null;

    [JsonIgnore]
    public string RemoteTooltip => WebUrl is not null ? $"Open {WebUrl}"
        : RemoteUrl is not null ? $"No web page for {RemoteUrl}"
        : "No remote";
}
