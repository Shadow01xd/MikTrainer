namespace MiTrainer.App.Controls;

/// <summary>Pastilla con un punto de color: "● Conectado a juego.exe".</summary>
sealed class StatusPill : PaintedControl
{
    Color _dot = Theme.Danger;

    public StatusPill()
    {
        Font = Theme.Small;
        Height = 26;
    }

    public void Display(SessionStatus status)
    {
        Text = status.Text;
        _dot = Theme.For(status.Kind);
        Width = TextRenderer.MeasureText(status.Text, Font).Width + 40;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        FillRounded(g, Bounds0, Height / 2f, Theme.Surface);

        using (var glow = new SolidBrush(Color.FromArgb(60, _dot)))
            g.FillEllipse(glow, 9, Height / 2f - 7, 14, 14);
        using (var dot = new SolidBrush(_dot))
            g.FillEllipse(dot, 12, Height / 2f - 4, 8, 8);

        TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 28, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
