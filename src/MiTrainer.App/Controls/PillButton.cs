using System.ComponentModel;

namespace MiTrainer.App.Controls;

/// <summary>Botón redondeado. Primary = morado; normal = gris; Nav = elemento del menú lateral.</summary>
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
        set
        {
            _selected = value;
            Invalidate();
        }
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

        FillRounded(g, Bounds0, Nav ? 8 : Height / 2f, fill, Primary || Nav ? null : Theme.Border);
        if (Nav && Selected) FillRounded(g, new RectangleF(0, Height * 0.25f, 4, Height * 0.5f), 2, Theme.Accent);

        Color color = !Enabled ? Theme.TextDim : Primary ? Color.White : Nav && !Selected ? Theme.TextDim : Theme.Text;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (Nav ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, Nav ? new Rectangle(18, 0, Width - 18, Height) : ClientRectangle, color, flags);
    }
}
