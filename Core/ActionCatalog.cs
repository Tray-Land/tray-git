using System.ComponentModel;

namespace TrayGit.Core;

public static class ActionKind
{
    public const string None = "none";
    public const string Terminal = "terminal";
    public const string Folder = "folder";
    public const string Remote = "remote";

    /// <summary>Opens a terminal in the repo and runs <see cref="ActionDef.Command"/>.</summary>
    public const string Command = "command";
}

/// <summary>One thing a repo row can do. Shown as a button, or bound to Enter, click or double-click.</summary>
/// <param name="Glyph">Segoe Fluent icon character.</param>
/// <param name="Image">App-relative path of an SVG; wins over <paramref name="Glyph"/>.</param>
/// <param name="Badge">Short text shown when there is neither an image nor a glyph.</param>
public sealed record ActionDef(string Id, string Name, string Kind, string? Command = null, string? Glyph = null, string? Badge = null, string? Image = null);

/// <summary>The actions the app ships with, the user's custom ones, and the defaults for how they are wired up.</summary>
public static class ActionCatalog
{
    public const string TerminalId = "terminal";
    public const string FolderId = "folder";
    public const string RemoteId = "remote";
    public const string NoneId = "none";
    public const string CustomPrefix = "custom:";

    public static readonly ActionDef None = new(NoneId, "Do nothing", ActionKind.None);

    /// <summary>The plain actions, then the AI coding CLIs. Only the plain ones are buttons by default.</summary>
    public static readonly IReadOnlyList<ActionDef> BuiltIn =
    [
        new(TerminalId, "Open terminal", ActionKind.Terminal, Glyph: ""),
        new(FolderId, "Open folder", ActionKind.Folder, Glyph: ""),
        new(RemoteId, "Open remote", ActionKind.Remote, Glyph: ""),
        // Icons are from SuperTinyIcons (MIT); Codex uses the OpenAI mark.
        new("copilot", "Open in GitHub Copilot", ActionKind.Command, "copilot", Image: "Assets/ActionIcons/github.svg"),
        new("claude", "Open in Claude", ActionKind.Command, "claude", Image: "Assets/ActionIcons/claude.svg"),
        new("codex", "Open in Codex", ActionKind.Command, "codex", Image: "Assets/ActionIcons/chatgpt.svg"),
        new("gemini", "Open in Gemini", ActionKind.Command, "gemini", Image: "Assets/ActionIcons/gemini.svg"),
    ];

    public static readonly IReadOnlyList<string> DefaultToolbar = [TerminalId, FolderId, RemoteId];

    public const string DefaultEnterId = TerminalId;
    public const string DefaultClickId = NoneId;
    public const string DefaultDoubleClickId = FolderId;

    /// <summary>Built-ins followed by the valid custom actions (a duplicate or built-in id is dropped).</summary>
    public static List<ActionDef> All(IEnumerable<ActionDef> custom)
    {
        List<ActionDef> all = [.. BuiltIn];
        foreach (ActionDef action in custom)
        {
            if (IsValidCustom(action) && all.All(a => a.Id != action.Id))
            {
                all.Add(action);
            }
        }

        return all;
    }

    /// <summary>The actions to show as row buttons, in saved order; unknown and duplicate ids are ignored.</summary>
    public static List<ActionDef> Toolbar(IEnumerable<string> ids, IEnumerable<ActionDef> custom)
    {
        Dictionary<string, ActionDef> available = All(custom).ToDictionary(a => a.Id, StringComparer.Ordinal);
        return SelectInOrder(ids, available);
    }

    /// <summary>All available actions in saved order, with any newly added actions appended.</summary>
    public static List<ActionDef> Order(IEnumerable<string> ids, IEnumerable<ActionDef> available)
    {
        List<ActionDef> all = [.. available];
        Dictionary<string, ActionDef> byId = all.ToDictionary(a => a.Id, StringComparer.Ordinal);
        List<ActionDef> ordered = SelectInOrder(ids, byId);
        HashSet<string> included = ordered.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        ordered.AddRange(all.Where(a => included.Add(a.Id)));
        return ordered;
    }

    /// <summary>The action for <paramref name="id"/>, else the one for <paramref name="fallbackId"/>, else <see cref="None"/>.</summary>
    public static ActionDef Resolve(string? id, IEnumerable<ActionDef> custom, string fallbackId)
    {
        List<ActionDef> all = [None, .. All(custom)];
        return all.FirstOrDefault(a => a.Id == id) ?? all.FirstOrDefault(a => a.Id == fallbackId) ?? None;
    }

    public static ActionDef NewCustom(string name, string command)
    {
        name = name.Trim();
        return new ActionDef(CustomPrefix + Guid.NewGuid().ToString("N")[..8], name, ActionKind.Command, command.Trim(), Badge: BadgeFor(name));
    }

    public static bool IsValidCustom(ActionDef action) =>
        action.Id.StartsWith(CustomPrefix, StringComparison.Ordinal)
        && action.Kind == ActionKind.Command
        && !string.IsNullOrWhiteSpace(action.Name)
        && !string.IsNullOrWhiteSpace(action.Command);

    private static List<ActionDef> SelectInOrder(
        IEnumerable<string> ids,
        IReadOnlyDictionary<string, ActionDef> available)
    {
        List<ActionDef> ordered = [];
        HashSet<string> added = new(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (added.Add(id) && available.TryGetValue(id, out ActionDef? action))
            {
                ordered.Add(action);
            }
        }

        return ordered;
    }

    /// <summary>Two letters for a button with no icon: word initials ("My Tool" → "MT"), else the first two letters.</summary>
    public static string BadgeFor(string name)
    {
        string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(words[0][0]) + words[0][1..Math.Min(2, words[0].Length)].ToLowerInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}",
        };
    }
}

/// <summary>An action bound to one repo row, as the row's button template sees it.</summary>
public sealed class RepoActionItem(ActionDef action, GitRepo repo, bool expanded) : INotifyPropertyChanged
{
    private bool _expanded = expanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// True shows the button; false fades it out. It stays in the layout and the focus order either
    /// way, so row height stays stable and arrow-key focus can still reach it.
    /// </summary>
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
        }
    }

    public double Opacity => _expanded ? 1 : 0;

    public ActionDef Action => action;

    public GitRepo Repo => repo;

    public string Name => action.Name;

    public string Tooltip => action.Kind == ActionKind.Remote ? repo.RemoteTooltip : action.Name;

    public bool IsEnabled => action.Kind != ActionKind.Remote || repo.HasWebUrl;

    public string Image => action.Image ?? "";

    public bool HasImage => action.Image is not null;

    public string Glyph => action.Glyph ?? "";

    public bool HasGlyph => !HasImage && action.Glyph is not null;

    public string Badge => action.Badge ?? "";

    public bool HasBadge => !HasImage && action.Glyph is null && action.Badge is not null;
}
