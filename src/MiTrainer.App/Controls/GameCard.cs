using System.Drawing.Drawing2D;

namespace MiTrainer.App.Controls;

/// <summary>Fila de un juego instalado: icono, nombre, tienda, perfil y botones Jugar / Cheats.</summary>
sealed class GameCard : CardControl
{
    readonly PillButton _play = new("Jugar", primary: true) { Height = 32 };
    readonly PillButton _profile = new("Crear perfil") { Height = 32 };
    readonly Image? _icon;
    int _cheatCount;

    public GameCard(InstalledGame game, Image? icon)
    {
        Game = game;
        _icon = icon;
        Height = 64;
        Margin = new Padding(0, 0, 0, 8);
        _play.Enabled = game.CanLaunch;
        _play.Click += (_, _) => PlayRequested?.Invoke(this, EventArgs.Empty);
        _profile.Click += (_, _) => ProfileRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_play);
        Controls.Add(_profile);
    }

    public InstalledGame Game { get; }

    /// <summary>Perfil asociado al juego, o null si todavía no tiene.</summary>
    public string? ProfilePath { get; private set; }

    public event EventHandler? PlayRequested;
    public event EventHandler? ProfileRequested;

    /// <summary>El icono que Windows muestra para el .exe (null si no hay .exe o no se puede leer).</summary>
    public static Image? LoadIcon(string? exe)
    {
        if (exe is null || !File.Exists(exe)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exe);
            return icon?.ToBitmap();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SetProfile(string? path, int cheatCount)
    {
        ProfilePath = path;
        _cheatCount = cheatCount;
        _profile.Text = path == null ? "Crear perfil" : "Ver cheats";
        PerformLayout();
        Invalidate();
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
        var border = ProfilePath != null ? Theme.Blend(Theme.Border, Theme.Accent, 0.45f) : Theme.Border;
        FillRounded(g, Bounds0, 12, Fill, border);

        var icon = DrawIcon(g);
        DrawTexts(g, icon.Right + 14, _profile.Left - 14);
    }

    Rectangle DrawIcon(Graphics g)
    {
        int size = Height - 26;
        var rect = new Rectangle(14, (Height - size) / 2, size, size);
        if (_icon != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_icon, rect);
            return rect;
        }
        FillRounded(g, rect, 8, Theme.ToggleOff);
        TextRenderer.DrawText(g, Game.Name[..1].ToUpperInvariant(), Theme.CardTitle, rect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return rect;
    }

    void DrawTexts(Graphics g, int left, int right)
    {
        var flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, Game.Name, Theme.CardTitle, new Rectangle(left, Height / 2 - 21, right - left, 22), Theme.Text, flags);

        // "Steam · bg3.exe" en gris y, si tiene perfil, "· 3 cheats" en morado
        string detail = $"{Game.Store} · {Game.ProcessName ?? ".exe desconocido"}";
        var line = new Rectangle(left, Height / 2 + 3, right - left, 18);
        TextRenderer.DrawText(g, detail, Theme.Small, line, Theme.TextDim, flags);
        if (ProfilePath == null) return;

        int used = TextRenderer.MeasureText(g, detail, Theme.Small, line.Size, flags).Width;
        string cheats = _cheatCount switch { 0 => "  ·  perfil sin cheats", 1 => "  ·  1 cheat", var n => $"  ·  {n} cheats" };
        TextRenderer.DrawText(g, cheats, Theme.Small, new Rectangle(line.X + used, line.Y, Math.Max(0, line.Width - used), line.Height), Theme.AccentHover, flags);
    }
}
