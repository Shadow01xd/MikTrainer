using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MiTrainer.App.Theming;

/// <summary>Colores, fuentes y utilidades de dibujo del tema oscuro. Para cambiar el aspecto de la app, se toca aquí.</summary>
static class Theme
{
    public static readonly Color Background = Color.FromArgb(15, 17, 23);
    public static readonly Color Sidebar = Color.FromArgb(20, 22, 31);
    public static readonly Color Surface = Color.FromArgb(27, 30, 42);
    public static readonly Color SurfaceHover = Color.FromArgb(35, 39, 56);
    public static readonly Color Border = Color.FromArgb(44, 48, 66);
    public static readonly Color Text = Color.FromArgb(232, 234, 242);
    public static readonly Color TextDim = Color.FromArgb(139, 144, 167);
    public static readonly Color Accent = Color.FromArgb(124, 92, 255);
    public static readonly Color AccentHover = Color.FromArgb(148, 121, 255);
    public static readonly Color Success = Color.FromArgb(61, 220, 151);
    public static readonly Color Danger = Color.FromArgb(255, 92, 122);
    public static readonly Color ToggleOff = Color.FromArgb(58, 62, 82);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 11f);
    public static readonly Font PageTitle = new("Segoe UI Semibold", 18f);
    public static readonly Font Logo = new("Segoe UI", 14f, FontStyle.Bold);

    /// <summary>Color con el que se muestra cada tipo de mensaje de estado.</summary>
    public static Color For(StatusKind kind) => kind switch
    {
        StatusKind.Success => Success,
        StatusKind.Error => Danger,
        _ => TextDim,
    };

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color from, Color to, float t) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * t),
        (int)(from.G + (to.G - from.G) * t),
        (int)(from.B + (to.B - from.B) * t));

    /// <summary>Aplica el tema a un TextBox / ComboBox estándar.</summary>
    public static T Input<T>(T control) where T : Control
    {
        control.BackColor = Surface;
        control.ForeColor = Text;
        control.Font = Body;
        control.Margin = new Padding(4, 8, 4, 4);
        if (control is TextBox textBox) textBox.BorderStyle = BorderStyle.FixedSingle;
        if (control is ComboBox comboBox) comboBox.FlatStyle = FlatStyle.Flat;
        return control;
    }
}
