namespace MiTrainer.App.Hotkeys;

/// <summary>Un atajo de teclado ya interpretado: modificadores y tecla, en el formato de RegisterHotKey.</summary>
readonly record struct Hotkey(uint Modifiers, uint Key);

/// <summary>Atajos de teclado como texto: "F1", "Ctrl+F2", "Alt+Shift+NumPad1"...</summary>
static class HotkeyText
{
    public const uint ModAlt = 1, ModCtrl = 2, ModShift = 4;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        uint modifiers = 0, key = 0;
        foreach (var part in (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": modifiers |= ModAlt; break;
                case "ctrl" or "control": modifiers |= ModCtrl; break;
                case "shift" or "mayus": modifiers |= ModShift; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out Keys parsed)) return false;
                    key = (uint)parsed;
                    break;
            }
        }
        hotkey = new Hotkey(modifiers, key);
        return key != 0;
    }

    public static string Format(Keys modifiers, Keys key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }
}
