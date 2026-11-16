using MiTrainer.Core;

namespace MiTrainer.UI;

/// <summary>
/// Tarjeta de un cheat: icono, nombre, estado, atajo de teclado y un interruptor
/// (o un botón "Aplicar" para los cheats de tipo "fijar", que se aplican una vez).
/// </summary>
sealed class CheatCard : PaintedControl
{
    readonly ToggleSwitch? _toggle;
    readonly PillButton? _apply;
    readonly PillButton? _edit;

    public CheatCard(Cheat cheat)
    {
        Cheat = cheat;
        BackColor = Theme.Surface;
        Height = 72;
        Margin = new Padding(0, 0, 0, 10);
        Cursor = Cursors.Hand;

        if (cheat.Def.Type == CheatTypes.Set)
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

        _edit = new PillButton("✎") { Width = 34, Height = 30 };
        _edit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_edit);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Editar…", null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Duplicar", null, (_, _) => DuplicateRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Eliminar", null, (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = menu;
    }

    public event EventHandler? EditRequested;
    public event EventHandler? DuplicateRequested;
    public event EventHandler? DeleteRequested;

    public Cheat Cheat { get; }

    /// <summary>El usuario quiere activar/desactivar (o aplicar) el cheat.</summary>
    public event EventHandler? ToggleRequested;

    // Puede ser null mientras se ejecuta el constructor (fijar Height ya provoca un Layout).
    Control? Action => (Control?)_toggle ?? _apply;

    public void UpdateState()
    {
        if (_toggle != null) _toggle.Checked = Cheat.Enabled;
        Invalidate();
    }

    // El interruptor y el botón pintan sus esquinas con el BackColor del padre: lo igualamos al relleno de la tarjeta.
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        BackColor = Theme.SurfaceHover;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        BackColor = Hover ? Theme.SurfaceHover : Theme.Surface;
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
        if (_edit != null) _edit.Location = new Point(action.Left - _edit.Width - 12, (Height - _edit.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        bool active = Cheat.Enabled;

        // Fondo de la tarjeta
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12))
        {
            using var fill = new SolidBrush(Hover ? Theme.SurfaceHover : Theme.Surface);
            g.FillPath(fill, path);
            using var border = new Pen(active ? Theme.Accent : Theme.Border, active ? 1.5f : 1f);
            g.DrawPath(border, path);
        }

        // Icono: la inicial del cheat sobre un cuadrado de color
        int tile = Height - 30;
        var tileRect = new Rectangle(16, (Height - tile) / 2, tile, tile);
        using (var path = Theme.Rounded(tileRect, 10))
        using (var brush = new SolidBrush(active ? Theme.Accent : Theme.ToggleOff))
            g.FillPath(brush, path);
        string initial = Cheat.Def.Name.Length > 0 ? char.ToUpperInvariant(Cheat.Def.Name[0]).ToString() : "?";
        TextRenderer.DrawText(g, initial, Theme.CardTitle, tileRect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Atajo de teclado
        int right = (_edit?.Left ?? Action?.Left ?? Width) - 12;
        if (!string.IsNullOrWhiteSpace(Cheat.Def.Hotkey))
        {
            var size = TextRenderer.MeasureText(Cheat.Def.Hotkey, Theme.Bold);
            var badge = new Rectangle(right - size.Width - 16, (Height - 26) / 2, size.Width + 16, 26);
            using (var path = Theme.Rounded(badge, 6))
            {
                using var brush = new SolidBrush(Theme.Background);
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Cheat.Def.Hotkey, Theme.Bold, badge, Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            right = badge.Left - 14;
        }

        // Nombre y estado
        int x = tileRect.Right + 14;
        var flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Cheat.Def.Name, Theme.CardTitle, new Rectangle(x, Height / 2 - 23, right - x, 24), Theme.Text, flags);
        var (status, color) = StatusLine();
        TextRenderer.DrawText(g, status, Theme.Small, new Rectangle(x, Height / 2 + 3, right - x, 20), color, flags);
    }

    (string Text, Color Color) StatusLine()
    {
        if (Cheat.Failed) return ("⚠ " + Cheat.Status, Theme.Danger);
        if (Cheat.Enabled) return ("● " + Cheat.Status, Theme.Success);
        if (Cheat.Def.Type == CheatTypes.Set && Cheat.Status.Length > 0) return ("✓ " + Cheat.Status, Theme.Success);
        if (Cheat.Status.Length > 0 && Cheat.Status != "Desactivado") return (Cheat.Status, Theme.TextDim);
        return (Cheat.Def.Type switch
        {
            CheatTypes.Freeze => $"Mantiene el valor en {Cheat.Def.Value}",
            CheatTypes.Set => $"Pone el valor a {Cheat.Def.Value}",
            CheatTypes.Patch => "Modifica el código del juego",
            _ => Cheat.Def.Type,
        }, Theme.TextDim);
    }
}
