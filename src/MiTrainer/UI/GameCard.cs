using System.Drawing.Drawing2D;
using MiTrainer.Core;

namespace MiTrainer.UI;

/// <summary>Fila de un juego instalado: icono, nombre, tienda, perfil y botones Jugar / Cheats.</summary>
sealed class GameCard : PaintedControl
{
    readonly PillButton _play = new("Jugar", primary: true) { Height = 32 };
    readonly PillButton _profile = new("Crear perfil") { Height = 32 };
    readonly Image? _icon;
    int _cheatCount;

    public GameCard(InstalledGame game, Image? icon)
    {
        Game = game;
        _icon = icon;
        BackColor = Theme.Surface;
        Height = 64;
        Margin = new Padding(0, 0, 0, 8);
        _play.Click += (_, _) => PlayRequested?.Invoke(this, EventArgs.Empty);
        _profile.Click += (_, _) => ProfileRequested?.Invoke(this, EventArgs.Empty);
        _play.Enabled = game.LaunchUri != null || game.Executable != null;
        Controls.Add(_play);
        Controls.Add(_profile);
    }

    /// <summary>El icono que Windows muestra para el .exe (null si no hay .exe o no se puede leer).</summary>
    public static Image? LoadIcon(string? exe)
    {
        if (exe is null || !File.Exists(exe)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exe);
            return icon?.ToBitmap();
        }
        catch { return null; }
    }

    public InstalledGame Game { get; }
    public string? ProfilePath { get; private set; }

    public event EventHandler? PlayRequested;
    public event EventHandler? ProfileRequested;

    public void SetProfile(string? path, int cheatCount)
    {
        ProfilePath = path;
        _cheatCount = cheatCount;
        _profile.Text = path == null ? "Crear perfil" : "Ver cheats";
        PerformLayout();
        Invalidate();
    }

    // Igual que en CheatCard: los botones pintan sus esquinas con el color de la tarjeta.
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

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _play.Location = new Point(Width - _play.Width - 16, (Height - _play.Height) / 2);
        _profile.Location = new Point(_play.Left - _profile.Width - 8, (Height - _profile.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = BeginPaint(e);
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12))
        {
            using var fill = new SolidBrush(Hover ? Theme.SurfaceHover : Theme.Surface);
            g.FillPath(fill, path);
            using var border = new Pen(ProfilePath != null ? Theme.Blend(Theme.Border, Theme.Accent, 0.45f) : Theme.Border);
            g.DrawPath(border, path);
        }

        // Icono del .exe, o la inicial si no hay
        int size = Height - 26;
        var iconRect = new Rectangle(14, (Height - size) / 2, size, size);
        if (_icon != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_icon, iconRect);
        }
        else
        {
            using var path = Theme.Rounded(iconRect, 8);
            using var brush = new SolidBrush(Theme.ToggleOff);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, Game.Name[..1].ToUpperInvariant(), Theme.CardTitle, iconRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        int x = iconRect.Right + 14, right = (_profile?.Left ?? Width) - 14;
        var flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Game.Name, Theme.CardTitle, new Rectangle(x, Height / 2 - 21, right - x, 22), Theme.Text, flags);

        // "Steam · bg3.exe" en gris y, si tiene perfil, "· 3 cheats" en morado
        string detail = $"{Game.Store} · {Game.ProcessName ?? ".exe desconocido"}";
        var sub = new Rectangle(x, Height / 2 + 3, right - x, 18);
        TextRenderer.DrawText(g, detail, Theme.Small, sub, Theme.TextDim, flags);
        if (ProfilePath != null)
        {
            int used = TextRenderer.MeasureText(g, detail, Theme.Small, sub.Size, flags).Width;
            string cheats = _cheatCount switch { 0 => "  ·  perfil sin cheats", 1 => "  ·  1 cheat", var n => $"  ·  {n} cheats" };
            TextRenderer.DrawText(g, cheats, Theme.Small, new Rectangle(sub.X + used, sub.Y, Math.Max(0, sub.Width - used), sub.Height), Theme.AccentHover, flags);
        }
    }
}
