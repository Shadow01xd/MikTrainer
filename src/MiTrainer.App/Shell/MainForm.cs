using MiTrainer.App.Pages;

namespace MiTrainer.App.Shell;

/// <summary>
/// Ventana principal: menú lateral, cabecera con la conexión al juego y las páginas.
/// No contiene lógica del trainer: solo une las páginas con la sesión y la mantiene en marcha.
/// </summary>
sealed class MainForm : Form
{
    readonly AppServices _services;
    readonly Label _title = new() { AutoSize = true, Font = Theme.PageTitle, ForeColor = Theme.Text, Location = new Point(22, 14) };
    readonly StatusPill _status = new() { Location = new Point(26, 56) };
    readonly ComboBox _processBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 });
    readonly List<(Page Page, PillButton Nav)> _pages = [];
    readonly GamesPage _games;
    readonly CheatsPage _cheats;
    readonly ScannerPage _scanner;
    readonly PointersPage _pointers;

    // La sesión necesita dos latidos: congelar valores (rápido) y vigilar si el juego se abre o se cierra (lento).
    readonly System.Windows.Forms.Timer _freezeTimer = new() { Interval = 50 };
    readonly System.Windows.Forms.Timer _watchTimer = new() { Interval = 1500 };

    public MainForm(AppServices services)
    {
        _services = services;
        _games = new GamesPage(services);
        _cheats = new CheatsPage(services);
        _scanner = new ScannerPage(services);
        _pointers = new PointersPage(services);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "MiTrainer";
        ClientSize = new Size(1060, 680);
        MinimumSize = new Size(1000, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12, 0, 0, 0) };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 4, 24, 20) };
        AddPage("Mis juegos", _games, nav, content);
        AddPage("Cheats", _cheats, nav, content);
        AddPage("Escáner", _scanner, nav, content);
        AddPage("Punteros", _pointers, nav, content);

        Controls.Add(content);
        Controls.Add(BuildHeader());
        Controls.Add(BuildSidebar(nav));
        ResumeLayout(true);

        ConnectEvents();
        ShowPage(_games);
    }

    TrainerSession Session => _services.Session;

    void ConnectEvents()
    {
        _games.ProfileOpened += (_, _) => ShowPage(_cheats);
        _scanner.PointerScanRequested += (_, request) =>
        {
            _pointers.Prepare(request.Address, request.Kind);
            ShowPage(_pointers);
        };
        Session.StatusChanged += (_, status) => _status.Display(status);
        Session.ProfileChanged += (_, _) => UpdateTitle();
        Session.ConnectionChanged += (_, _) => UpdateTitle();
        _freezeTimer.Tick += (_, _) => Session.Tick();
        _watchTimer.Tick += (_, _) => Session.Watch();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Initialize();
    }

    /// <summary>Carga procesos, perfil y juegos y arranca los latidos de la sesión (separado de OnLoad para las pruebas).</summary>
    internal void Initialize()
    {
        _status.Display(new SessionStatus("Sin conectar", StatusKind.Error));
        LoadProcessList();
        _cheats.OpenProfile(path: null);
        _ = _games.LoadAsync();
        _freezeTimer.Start();
        _watchTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _freezeTimer.Stop();
        _watchTimer.Stop();
        Session.DisableAll(); // los parches devuelven el juego a como estaba
        base.OnFormClosing(e);
    }

    // ================= Páginas =================

    void AddPage(string title, Page page, FlowLayoutPanel nav, Panel content)
    {
        var button = new PillButton(title) { Nav = true, Width = 186, Height = 42 };
        button.Click += (_, _) => ShowPage(page);
        nav.Controls.Add(button);
        page.Visible = false;
        content.Controls.Add(page);
        _pages.Add((page, button));
    }

    void ShowPage(Page page)
    {
        foreach (var (p, nav) in _pages)
        {
            p.Visible = p == page;
            nav.Selected = p == page;
        }
    }

    // ================= Menú lateral y cabecera =================

    static Control BuildSidebar(Control nav)
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Theme.Sidebar };

        var logo = new Panel { Dock = DockStyle.Top, Height = 78 };
        logo.Controls.Add(new Label { Text = "◆", AutoSize = true, Font = Theme.Logo, ForeColor = Theme.Accent, Location = new Point(20, 24) });
        logo.Controls.Add(new Label { Text = "MiTrainer", AutoSize = true, Font = Theme.Logo, ForeColor = Theme.Text, Location = new Point(46, 24) });

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            Padding = new Padding(20, 0, 12, 0),
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            Text = "Solo para juegos de un jugador.\nNo lo uses en juegos online.",
        };

        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(footer);
        sidebar.Controls.Add(logo);
        return sidebar;
    }

    Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 96 };
        var refresh = Ui.Pill("↻", LoadProcessList);
        refresh.Width = 38;
        var connection = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, Padding = new Padding(0, 22, 20, 0), WrapContents = false };
        connection.Controls.AddRange([_processBox, refresh, Ui.Pill("Conectar", ConnectSelected, primary: true)]);

        header.Controls.Add(connection);
        header.Controls.Add(_title);
        header.Controls.Add(_status);
        return header;
    }

    void UpdateTitle() =>
        _title.Text = !string.IsNullOrWhiteSpace(Session.Profile.Game) ? Session.Profile.Game : Session.Memory?.ProcessName ?? "Ningún juego";

    // ================= Conexión manual =================

    void LoadProcessList()
    {
        _processBox.Items.Clear();
        _processBox.Items.AddRange([.. _services.Connector.ListProcesses()]);
    }

    void ConnectSelected()
    {
        if (_processBox.SelectedItem is ProcessInfo process) Session.Connect(process.Id);
        else _status.Display(new SessionStatus("Elige un proceso de la lista", StatusKind.Error));
    }
}
