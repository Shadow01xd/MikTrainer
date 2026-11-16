namespace MiTrainer.App.Controls;

/// <summary>Base para controles dibujados a mano: sin parpadeo, con estado de "ratón encima" y esquinas sobre el fondo del padre.</summary>
abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    protected bool Hover { get; private set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        Hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        // Pasar por encima de un control hijo no cuenta como salir.
        Hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected Graphics BeginPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        g.Clear(Parent?.BackColor ?? Theme.Background);
        return g;
    }

    protected void FillRounded(Graphics g, RectangleF rect, float radius, Color fill, Color? border = null, float borderWidth = 1f)
    {
        using var path = Theme.Rounded(rect, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (border is { } color)
        {
            using var pen = new Pen(color, borderWidth);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Rectángulo del control con medio píxel de margen para que el borde antialiasado no se corte.</summary>
    protected RectangleF Bounds0 => new(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
}

/// <summary>
/// Tarjeta con fondo que cambia al pasar el ratón. Su BackColor sigue al relleno para que los botones hijos
/// (que pintan sus esquinas con el color del padre) encajen sin bordes raros.
/// </summary>
abstract class CardControl : PaintedControl
{
    protected CardControl()
    {
        BackColor = Theme.Surface;
    }

    protected Color Fill => Hover ? Theme.SurfaceHover : Theme.Surface;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        BackColor = Fill;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        BackColor = Fill;
    }
}
