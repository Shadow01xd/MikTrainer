using MiTrainer.App.Hotkeys;

namespace MiTrainer.App.Controls;

/// <summary>Caja que "graba" un atajo: pulsas la combinación y la escribe. Supr o Retroceso lo borran.</summary>
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
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return; // falta la tecla principal
        Text = key is Keys.Back or Keys.Delete && modifiers == Keys.None ? "" : HotkeyText.Format(modifiers, key);
        SelectionStart = Text.Length;
    }
}
