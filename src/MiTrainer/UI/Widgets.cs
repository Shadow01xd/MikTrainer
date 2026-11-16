using System.ComponentModel;

namespace MiTrainer.UI;

/// <summary>Base para controles dibujados a mano (sin parpadeo, esquinas sobre el fondo del padre).</summary>
abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    protected bool Hover { get; private set; }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e)
    {
        // Pasar por encima de un control hijo no cuenta como salir.
        Hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected Graphics BeginPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(Parent?.BackColor ?? Theme.Background);
        return g;
    }
}

/// <summary>Botón redondeado. Primary = morado, normal = gris, Nav = elemento del menú lateral.</summary>
sealed class PillButton : PaintedControl
{
    bool _selected;

    public PillButton(string text, bool primary = false)
    {
        Primary = primary;
        Font = Theme.Bold;
        Text = text;
        Height = 34;
        Margin = new Padding(4);
        Cursor = Cursors.Hand;
    }

    public bool Primary { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Nav { get; init; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (!Nav) Width = TextRenderer.MeasureText(Text, Font).Width + 34;
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        var parent = Parent?.BackColor ?? Theme.Background;
        Color fill = Nav ? (Selected ? Theme.Surface : Hover ? Theme.Blend(parent, Theme.Surface, 0.5f) : parent)
            : Primary ? (Hover ? Theme.AccentHover : Theme.Accent)
            : (Hover ? Theme.SurfaceHover : Theme.Surface);
        if (!Enabled) fill = Theme.Blend(fill, parent, 0.55f);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Rounded(rect, Nav ? 8 : Height / 2f))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
            if (!Primary && !Nav)
            {
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
        }

        if (Nav && Selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            using var bar = Theme.Rounded(new RectangleF(0, Height * 0.25f, 4, Height * 0.5f), 2);
            g.FillPath(accent, bar);
        }

        Color color = !Enabled ? Theme.TextDim : Primary ? Color.White : Nav && !Selected ? Theme.TextDim : Theme.Text;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    (Nav ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        var textRect = Nav ? new Rectangle(18, 0, Width - 18, Height) : ClientRectangle;
        TextRenderer.DrawText(g, Text, Font, textRect, color, flags);
    }
}

/// <summary>Interruptor animado. No cambia solo: avisa con ToggleRequested y el dueño fija Checked.</summary>
sealed class ToggleSwitch : PaintedControl
{
    readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    bool _checked;
    float _position; // 0 = apagado, 1 = encendido

    public ToggleSwitch()
    {
        Size = new Size(48, 26);
        Cursor = Cursors.Hand;
        _animation.Tick += (_, _) =>
        {
            float target = _checked ? 1 : 0;
            _position += Math.Sign(target - _position) * 0.2f;
            if (Math.Abs(target - _position) < 0.2f)
            {
                _position = target;
                _animation.Stop();
            }
            Invalidate();
        };
    }

    public event EventHandler? ToggleRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _animation.Start();
        }
    }

    protected override void OnClick(EventArgs e)
    {
        ToggleRequested?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        var track = Theme.Blend(Theme.ToggleOff, Theme.Accent, _position);
        if (Hover) track = Theme.Blend(track, Color.White, 0.08f);
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
        using (var brush = new SolidBrush(track))
            g.FillPath(brush, path);

        float knob = Height - 8;
        float x = 4 + _position * (Width - Height);
        g.FillEllipse(Brushes.White, x, 4, knob, knob);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Pastilla con un punto de color: "● Conectado a juego.exe".</summary>
sealed class StatusPill : PaintedControl
{
    Color _dot = Theme.Danger;

    public StatusPill()
    {
        Font = Theme.Small;
        Height = 26;
    }

    public void Set(string text, Color dot)
    {
        Text = text;
        _dot = dot;
        Width = TextRenderer.MeasureText(text, Font).Width + 40;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
        using (var brush = new SolidBrush(Theme.Surface))
            g.FillPath(brush, path);

        using (var glow = new SolidBrush(Color.FromArgb(60, _dot)))
            g.FillEllipse(glow, 9, Height / 2f - 7, 14, 14);
        using (var dot = new SolidBrush(_dot))
            g.FillEllipse(dot, 12, Height / 2f - 4, 8, 8);

        TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 28, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
