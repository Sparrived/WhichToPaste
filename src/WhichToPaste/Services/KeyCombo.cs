namespace WhichToPaste.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

/// <summary>可配置快捷键，形如 <c>Ctrl+Shift+X</c>、<c>Esc</c>。</summary>
public sealed record KeyCombo(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Enter"] = 0x0D, ["Return"] = 0x0D,
        ["Space"] = 0x20, ["Tab"] = 0x09, ["Backspace"] = 0x08,
        ["Delete"] = 0x2E, ["Insert"] = 0x2D,
        ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
        ["`"] = 0xC0, ["-"] = 0xBD, ["="] = 0xBB,
        ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC,
        [";"] = 0xBA, ["'"] = 0xDE, [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF,
    };

    /// <summary>解析失败时返回 null，调用方保留旧值。</summary>
    public static KeyCombo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var mods = HotkeyModifiers.None;
        uint vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Control; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "win" or "meta" or "super" or "cmd": mods |= HotkeyModifiers.Win; break;
                default:
                    if (vk != 0) return null; // 两个主键，非法
                    vk = ResolveKey(raw);
                    break;
            }
        }

        return vk == 0 ? null : new KeyCombo(mods, vk);
    }

    private static uint ResolveKey(string token)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') && int.TryParse(token[1..], out int f) && f is >= 1 and <= 24)
            return (uint)(0x70 + f - 1);
        return NamedKeys.TryGetValue(token, out uint vk) ? vk : 0;
    }

    public bool Matches(HotkeyModifiers modifiers, uint virtualKey) =>
        VirtualKey == virtualKey && Modifiers == modifiers;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(uint vk) => vk switch
    {
        0x1B => "Esc",
        0x0D => "Enter",
        0x20 => "Space",
        0x09 => "Tab",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x26 => "Up",
        0x28 => "Down",
        0x25 => "Left",
        0x27 => "Right",
        0xC0 => "`",
        0xBD => "-",
        0xBB => "=",
        0xDB => "[",
        0xDD => "]",
        0xDC => "\\",
        0xBA => ";",
        0xDE => "'",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1),
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        _ => "VK" + vk.ToString("X2"),
    };
}