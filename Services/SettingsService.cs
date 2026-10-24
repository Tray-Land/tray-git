using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TrayGit.Core;
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

    /// <summary>Actions the user defined, each a command run in a new terminal.</summary>
    public static List<ActionDef> CustomActions
    {
        get => GetJson(nameof(CustomActions), AppJsonContext.Default.ActionDefArray) is { } actions ? [.. actions] : [];
        set => SetJson(nameof(CustomActions), value.ToArray(), AppJsonContext.Default.ActionDefArray);
    }

    /// <summary>Ids of the actions shown as buttons on each repo row.</summary>
    public static List<string> ToolbarActionIds
    {
        get => GetJson(nameof(ToolbarActionIds), AppJsonContext.Default.StringArray) is { } ids ? [.. ids] : [.. ActionCatalog.DefaultToolbar];
        set => SetJson(nameof(ToolbarActionIds), value.ToArray(), AppJsonContext.Default.StringArray);
    }

    /// <summary>What Enter does on the top match in the filter box, or the selected row.</summary>
    public static string EnterActionId
    {
        get => Get(nameof(EnterActionId), ActionCatalog.DefaultEnterId);
        set => Set(nameof(EnterActionId), value);
    }

    /// <summary>What a single click on a row does. Nothing by default.</summary>
    public static string ClickActionId
    {
        get => Get(nameof(ClickActionId), ActionCatalog.DefaultClickId);
        set => Set(nameof(ClickActionId), value);
    }

    public static string DoubleClickActionId
    {
        get => Get(nameof(DoubleClickActionId), ActionCatalog.DefaultDoubleClickId);
        set => Set(nameof(DoubleClickActionId), value);
    }

    /// <summary>Raised on the setter's thread when the global hotkey is switched or rebound.</summary>
    public static event EventHandler? HotKeyChanged;

    /// <summary>Whether the global hotkey that opens the flyout is on. Off by default.</summary>
    public static bool HotKeyEnabled
    {
        get => Get(nameof(HotKeyEnabled), false);
        set
        {
            if (value == HotKeyEnabled)
            {
                return;
            }

            Set(nameof(HotKeyEnabled), value);
            HotKeyChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static HotKey HotKey
    {
        get => HotKey.TryParse(Get<string?>(nameof(HotKey), null), out HotKey hotKey) ? hotKey : HotKey.Default;
        set
        {
            Set(nameof(HotKey), value.Serialize());
            HotKeyChanged?.Invoke(null, EventArgs.Empty);
        }
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
