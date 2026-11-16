namespace MiTrainer.UI;

/// <summary>Atajos de teclado en texto: "F1", "Ctrl+F2", "Alt+Shift+NumPad1"...</summary>
static class Hotkeys
{
    public const uint ModAlt = 1, ModCtrl = 2, ModShift = 4;

    public static bool TryParse(string text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
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

/// <summary>Caja que "graba" el atajo: pulsas la combinación y la escribe. Supr o Retroceso lo borran.</summary>
sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
        PlaceholderText = "Pulsa una tecla…";
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Tab;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        Record(e.KeyCode, e.Modifiers);
    }

    internal void Record(Keys key, Keys modifiers)
    {
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
        Text = key is Keys.Back or Keys.Delete && modifiers == Keys.None ? "" : Hotkeys.Format(modifiers, key);
        SelectionStart = Text.Length;
    }
}
