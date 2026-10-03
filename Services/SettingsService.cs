using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Windows.Storage;

namespace TrayGit.Services;

/// <summary>
/// Typed wrapper over <see cref="ApplicationData.LocalSettings"/>. Every read falls back to a
/// default so a missing or corrupt value never crashes the app.
/// </summary>
/// <remarks>
/// LocalSettings values must be small (8 KB each) WinRT primitives. Store enums as int, and lists
/// or records as JSON through a source-generated <c>JsonSerializerContext</c> (trim-safe) with
/// <see cref="GetJson"/> / <see cref="SetJson"/>. Anything larger belongs in a file under
/// <c>ApplicationData.Current.LocalFolder</c>.
/// </remarks>
internal static class SettingsService
{
    /// <summary>False until the first launch has shown the flyout.</summary>
    public static bool HasLaunchedBefore
    {
        get => Get(nameof(HasLaunchedBefore), false);
        set => Set(nameof(HasLaunchedBefore), value);
    }

    /// <summary>Raised on the setter's thread when <see cref="SourceFolder"/> changes.</summary>
    public static event EventHandler? SourceFolderChanged;

    /// <summary>The folder scanned for git repos, or null until the user picks one.</summary>
    public static string? SourceFolder
    {
        get => Get<string?>(nameof(SourceFolder), null);
        set
        {
            if (string.Equals(value, SourceFolder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Set(nameof(SourceFolder), value);
            SourceFolderChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Repo path → UTC ticks of its last one-click action, for "recent first" ordering.</summary>
    public static Dictionary<string, long> LastUsed
    {
        get => GetJson(nameof(LastUsed), AppJsonContext.Default.DictionaryStringInt64) is { } map
            ? new Dictionary<string, long>(map, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        set => SetJson(nameof(LastUsed), value, AppJsonContext.Default.DictionaryStringInt64);
    }

    /// <summary>Paths of the repos the user starred.</summary>
    public static HashSet<string> Favorites
    {
        get => GetJson(nameof(Favorites), AppJsonContext.Default.StringArray) is { } paths
            ? new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        set => SetJson(nameof(Favorites), value.Order(StringComparer.OrdinalIgnoreCase).ToArray(), AppJsonContext.Default.StringArray);
    }

    /// <summary>Whether the list is narrowed to favorites.</summary>
    public static bool ShowFavoritesOnly
    {
        get => Get(nameof(ShowFavoritesOnly), false);
        set => Set(nameof(ShowFavoritesOnly), value);
    }

    public static T? GetJson<T>(string key, JsonTypeInfo<T> typeInfo)
    {
        if (Get<string?>(key, null) is not { } json)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static void SetJson<T>(string key, T? value, JsonTypeInfo<T> typeInfo) =>
        Set(key, value is null ? null : JsonSerializer.Serialize(value, typeInfo));

    private static T Get<T>(string key, T defaultValue)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value) && value is T typed
                ? typed
                : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void Set(string key, object? value)
    {
        try
        {
            if (value is null)
            {
                ApplicationData.Current.LocalSettings.Values.Remove(key);
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
        }
        catch
        {
            // Settings are best-effort; ignore storage failures.
        }
    }
}
