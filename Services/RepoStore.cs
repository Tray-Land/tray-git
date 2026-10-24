using System.Text.Json;
using System.Text.Json.Serialization;
using TrayGit.Core;
using Windows.Storage;

namespace TrayGit.Services;

/// <summary>The last scan of the source folder, kept so the flyout opens with a list immediately.</summary>
internal sealed record RepoScan(string Root, DateTime ScannedUtc, List<GitRepo> Repos);

[JsonSerializable(typeof(RepoScan))]
[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ActionDef[]))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>
/// Scans the source folder and caches the result in LocalFolder (it can outgrow LocalSettings'
/// 8 KB value limit). Reads and writes fail soft: a bad cache just means a fresh scan.
/// </summary>
internal static class RepoStore
{
    private const string CacheFileName = "repos.json";

    private static string CachePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, CacheFileName);

    public static RepoScan? LoadCache(string root)
    {
        try
        {
            using FileStream stream = File.OpenRead(CachePath);
            RepoScan? scan = JsonSerializer.Deserialize(stream, AppJsonContext.Default.RepoScan);
            return scan is not null && string.Equals(scan.Root, root, StringComparison.OrdinalIgnoreCase) ? scan : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Scans on a worker thread and caches the result.</summary>
    public static Task<RepoScan> ScanAsync(string root, CancellationToken cancellationToken) => Task.Run(() =>
    {
        List<GitRepo> repos = RepoScanner.Scan(root, RepoScanner.DefaultMaxDepth, cancellationToken);
        RepoScan scan = new(root, DateTime.UtcNow, repos);
        try
        {
            using FileStream stream = File.Create(CachePath);
            JsonSerializer.Serialize(stream, scan, AppJsonContext.Default.RepoScan);
        }
        catch
        {
            // The cache is an optimization; the scan result is still good.
        }

        return scan;
    }, cancellationToken);

    public static void ClearCache()
    {
        try
        {
            File.Delete(CachePath);
        }
        catch
        {
        }
    }
}
