namespace MiTrainer.App.Controls;

/// <summary>
/// Tarjeta de un cheat: inicial, nombre, estado, atajo, botón ✎ y un interruptor
/// (o un botón "Aplicar" en los cheats de tipo "fijar", que se aplican una sola vez).
/// </summary>
sealed class CheatCard : CardControl
{
    const int Gap = 12;

    readonly ToggleSwitch? _toggle;
    readonly PillButton? _apply;
    readonly PillButton _edit = new("✎") { Width = 34, Height = 30 };

    public CheatCard(Cheat cheat)
    {
        Cheat = cheat;
        Height = 72;
        Margin = new Padding(0, 0, 0, 10);
        Cursor = Cursors.Hand;

        if (cheat.Definition.Type == CheatType.Set)
        {
            _apply = new PillButton("Aplicar", primary: true) { Height = 32 };
            _apply.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(_apply);
        }
        else
        {
            _toggle = new ToggleSwitch();
            _toggle.ToggleRequested += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(_toggle);
        }

        _edit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_edit);
        ContextMenuStrip = BuildMenu();
        UpdateState();
    }

    public Cheat Cheat { get; }

    /// <summary>El usuario quiere activar/desactivar (o aplicar) el cheat.</summary>
    public event EventHandler? ToggleRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? DuplicateRequested;
    public event EventHandler? DeleteRequested;

    // null mientras corre el constructor: asignar Height ya provoca un Layout.
    Control? Action => (Control?)_toggle ?? _apply;

    public void UpdateState()
    {
        if (_toggle != null) _toggle.Checked = Cheat.Enabled;
        Invalidate();
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Editar…", null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Duplicar", null, (_, _) => DuplicateRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Eliminar", null, (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        // Clic izquierdo en la tarjeta = activar; el derecho abre el menú.
        if (e.Button == MouseButtons.Left) ToggleRequested?.Invoke(this, EventArgs.Empty);
        base.OnMouseClick(e);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Action is not { } action) return;
        action.Location = new Point(Width - action.Width - 20, (Height - action.Height) / 2);
        _edit.Location = new Point(action.Left - _edit.Width - Gap, (Height - _edit.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        bool active = Cheat.Enabled;
        FillRounded(g, Bounds0, 12, Fill, active ? Theme.Accent : Theme.Border, active ? 1.5f : 1f);

        var tile = DrawInitial(g, active);
        int right = DrawHotkey(g, _edit.Left - Gap);
        DrawTexts(g, tile.Right + 14, right);
    }

    Rectangle DrawInitial(Graphics g, bool active)
    {
        int size = Height - 30;
        var tile = new Rectangle(16, (Height - size) / 2, size, size);
        FillRounded(g, tile, 10, active ? Theme.Accent : Theme.ToggleOff);
        string name = Cheat.Definition.Name;
        string initial = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
        TextRenderer.DrawText(g, initial, Theme.CardTitle, tile, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return tile;
    }

    /// <summary>Dibuja la etiqueta del atajo terminando en <paramref name="right"/>; devuelve dónde acaba el espacio libre.</summary>
    int DrawHotkey(Graphics g, int right)
    {
        string? hotkey = Cheat.Definition.Hotkey;
        if (string.IsNullOrWhiteSpace(hotkey)) return right;
        var size = TextRenderer.MeasureText(hotkey, Theme.Bold);
        var badge = new Rectangle(right - size.Width - 16, (Height - 26) / 2, size.Width + 16, 26);
        FillRounded(g, badge, 6, Theme.Background, Theme.Border);
        TextRenderer.DrawText(g, hotkey, Theme.Bold, badge, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return badge.Left - 14;
    }

    void DrawTexts(Graphics g, int left, int right)
    {
        var flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Cheat.Definition.Name, Theme.CardTitle, new Rectangle(left, Height / 2 - 23, right - left, 24), Theme.Text, flags);
        var (status, color) = StatusLine();
        TextRenderer.DrawText(g, status, Theme.Small, new Rectangle(left, Height / 2 + 3, right - left, 20), color, flags);
    }

    (string Text, Color Color) StatusLine()
    {
        if (Cheat.Failed) return ("⚠ " + Cheat.Status, Theme.Danger);
        if (Cheat.Enabled) return ("● " + Cheat.Status, Theme.Success);
        if (Cheat.Definition.Type == CheatType.Set && Cheat.Status.Length > 0) return ("✓ " + Cheat.Status, Theme.Success);
        if (Cheat.Status.Length > 0 && Cheat.Status != "Desactivado") return (Cheat.Status, Theme.TextDim);
        return (Cheat.Definition.Describe(), Theme.TextDim);
    }
}
