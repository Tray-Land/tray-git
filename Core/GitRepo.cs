using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TrayGit.Core;

/// <summary>A working copy found under the source folder.</summary>
/// <remarks>
/// A <c>partial class</c>, not a record: CsWinRT only generates the trim-safe vtable for partial classes
/// (it skips records), and without it the trimmed Release build crashes when the list wraps an item.
/// </remarks>
public sealed partial class GitRepo : INotifyPropertyChanged
{
    public GitRepo(string name, string path, string relativePath, string? branch, string? remoteUrl, string? webUrl)
    {
        Name = name;
        Path = path;
        RelativePath = relativePath;
        Branch = branch;
        RemoteUrl = remoteUrl;
        WebUrl = webUrl;
    }

    /// <summary>The repo's folder name.</summary>
    public string Name { get; }

    /// <summary>Full path of the working copy.</summary>
    public string Path { get; }

    /// <summary>Path relative to the source folder ("." when the folder itself is the repo).</summary>
    public string RelativePath { get; }

    /// <summary>Checked-out branch, a short commit id when detached, or null if unreadable.</summary>
    public string? Branch { get; }

    /// <summary>The fetch URL of <c>origin</c> (or the first remote), as written in the config.</summary>
    public string? RemoteUrl { get; }

    /// <summary>A browser URL for the remote, or null when it has no web page (local path, unknown scheme).</summary>
    public string? WebUrl { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    // List items take their accessible name from ToString; the record's default would read every property.
    public override string ToString() => Name;

    /// <summary>The caption under the name in the list.</summary>
    [JsonIgnore]
    public string Description => RepoListPolicy.Describe(this);

    [JsonIgnore]
    public bool HasWebUrl => WebUrl is not null;

    [JsonIgnore]
    public string RemoteTooltip => WebUrl is not null ? $"Open {WebUrl}"
        : RemoteUrl is not null ? $"No web page for {RemoteUrl}"
        : "No remote";

    /// <summary>Changed lines and commits to pull/push; null until git has been asked.</summary>
    [JsonIgnore]
    public RepoStatus? Status { get; private set; }

    [JsonIgnore]
    public bool IsFetching { get; private set; }

    [JsonIgnore]
    public bool IsFavorite { get; private set; }

    [JsonIgnore]
    public string FavoriteActionText => IsFavorite ? "Unfavorite" : "Favorite";

    [JsonIgnore]
    public string AddedText => Status is { Added: > 0 } s ? $"+{s.Added}" : "";

    [JsonIgnore]
    public string RemovedText => Status is { Removed: > 0 } s ? $"\u2212{s.Removed}" : "";

    [JsonIgnore]
    public string BehindText => Status is { Behind: > 0 } s ? $"\u2193{s.Behind}" : "";

    [JsonIgnore]
    public string AheadText => Status is { Ahead: > 0 } s ? $"\u2191{s.Ahead}" : "";

    [JsonIgnore]
    public bool HasAdded => AddedText.Length > 0;

    [JsonIgnore]
    public bool HasRemoved => RemovedText.Length > 0;

    [JsonIgnore]
    public bool HasBehind => BehindText.Length > 0;

    [JsonIgnore]
    public bool HasAhead => AheadText.Length > 0;

    /// <summary>The folder above the repo, relative to the source folder; empty at the top level.</summary>
    [JsonIgnore]
    public string ParentText => System.IO.Path.GetDirectoryName(RelativePath) ?? "";

    [JsonIgnore]
    public string ParentCaption => ParentText.Length == 0 ? "" : Branch is null ? ParentText : $"\u00B7 {ParentText}";

    [JsonIgnore]
    public bool CanFetch => !IsFetching;

    [JsonIgnore]
    public string BranchText => Branch ?? "";

    [JsonIgnore]
    public bool HasBranch => Branch is not null;

    public void SetStatus(RepoStatus? status)
    {
        if (Equals(Status, status))
        {
            return;
        }

        Status = status;
        Raise();
    }

    public void SetFavorite(bool isFavorite)
    {
        if (IsFavorite != isFavorite)
        {
            IsFavorite = isFavorite;
            Raise();
        }
    }

    public void SetFetching(bool isFetching)
    {
        if (IsFetching != isFetching)
        {
            IsFetching = isFetching;
            Raise();
        }
    }

    // The status segment is a handful of related bindings; refresh them together.
    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}