namespace TrayGit.Core;

/// <summary>A global shortcut: Win32 modifier flags plus a virtual-key code.</summary>
public readonly record struct HotKey(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 0x1;
    public const uint Control = 0x2;
    public const uint Shift = 0x4;
    public const uint Win = 0x8;

    public static HotKey Default { get; } = new(Control | Alt, 'G');

    /// <summary>
    /// A global shortcut needs at least one modifier (a bare letter would steal typing everywhere)
    /// and a key that isn't itself a modifier.
    /// </summary>
    public bool IsValid => Modifiers != 0 && (Modifiers & ~(Alt | Control | Shift | Win)) == 0 && !IsModifierKey(VirtualKey);

    /// <summary>"Ctrl+Alt+G".</summary>
    public string Format()
    {
        List<string> parts = [];
        if ((Modifiers & Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    /// <summary>"modifiers,vk", for storage.</summary>
    public string Serialize() => $"{Modifiers},{VirtualKey}";

    public static bool TryParse(string? text, out HotKey hotKey)
    {
        hotKey = default;
        string[] parts = (text ?? "").Split(',');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out uint modifiers) || !uint.TryParse(parts[1], out uint key))
        {
            return false;
        }

        HotKey candidate = new(modifiers, key);
        if (!candidate.IsValid)
        {
            return false;
        }

        hotKey = candidate;
        return true;
    }

    // VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN and their left/right variants.
    public static bool IsModifierKey(uint vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    private static string KeyName(uint vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x20 => "Space",
        0xC0 => "`",
        _ => $"Key {vk:X2}",
    };
}
