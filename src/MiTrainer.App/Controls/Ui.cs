namespace MiTrainer.App.Controls;

/// <summary>Piezas pequeñas que se repiten en todas las páginas (filas, botones, listas, etiquetas).</summary>
static class Ui
{
    public static Label Label(string text, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Body,
        ForeColor = color ?? Theme.TextDim,
        Margin = new Padding(6, 12, 2, 0),
    };

    /// <summary>Texto de ayuda gris que ocupa todo el ancho.</summary>
    public static Label Hint(string text, int height) => new()
    {
        Dock = DockStyle.Top,
        Height = height,
        Font = Theme.Small,
        ForeColor = Theme.TextDim,
        Padding = new Padding(6, 4, 0, 0),
        Text = text,
    };

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 4, 0, 4) };
        row.Controls.AddRange(controls);
        return row;
    }

    public static FlowLayoutPanel Row(DockStyle dock, params Control[] controls)
    {
        var row = Row(controls);
        row.Dock = dock;
        return row;
    }

    /// <summary>Línea de estado bajo una lista (resultados, progreso, errores).</summary>
    public static FlowLayoutPanel StatusRow(Label label)
    {
        var row = Row(DockStyle.Bottom, label);
        row.Padding = new Padding(0, 2, 0, 0);
        return row;
    }

    public static PillButton Pill(string text, Action onClick, bool primary = false)
    {
        var button = new PillButton(text, primary);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Lista con columnas en tema oscuro; la última columna llena el hueco que sobra.</summary>
    public static ListView List(params (string Title, int Width)[] columns)
    {
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        foreach (var (title, width) in columns) list.Columns.Add(title, width);
        list.Resize += (_, _) => list.Columns[^1].Width = -2;
        return list;
    }

    /// <summary>Panel con desplazamiento vertical que apila tarjetas a lo ancho.</summary>
    public static FlowLayoutPanel CardList() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(0, 10, 0, 0),
    };

    /// <summary>Ancho que tiene que tener una tarjeta para llenar la lista (sin barra horizontal).</summary>
    public static int CardWidth(FlowLayoutPanel list) => Math.Max(300, list.ClientSize.Width - list.Padding.Horizontal - 4);

    /// <summary>Ajusta el ancho de todas las tarjetas cuando cambia el tamaño de la lista.</summary>
    public static void FitCards(FlowLayoutPanel list)
    {
        int width = CardWidth(list);
        list.SuspendLayout();
        foreach (Control card in list.Controls) card.Width = width;
        list.ResumeLayout();
    }
}
