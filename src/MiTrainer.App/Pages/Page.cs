namespace MiTrainer.App.Pages;

/// <summary>
/// Base de las páginas de la ventana principal. Lleva un temporizador que solo corre mientras la página
/// está visible, para refrescar valores en directo sin gastar nada cuando no se ven.
/// </summary>
abstract class Page : UserControl
{
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };

    protected Page(AppServices services)
    {
        Services = services;
        Dock = DockStyle.Fill;
        _refresh.Tick += (_, _) => RefreshLiveValues();
    }

    protected AppServices Services { get; }
    protected TrainerSession Session => Services.Session;

    /// <summary>Se llama cada medio segundo mientras la página está a la vista.</summary>
    protected virtual void RefreshLiveValues() { }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _refresh.Enabled = Visible;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}
