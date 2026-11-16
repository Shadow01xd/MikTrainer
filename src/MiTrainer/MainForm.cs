using System.Diagnostics;
using System.Media;
using MiTrainer.Core;
using MiTrainer.UI;

namespace MiTrainer;

public sealed class MainForm : Form
{
    const int WM_HOTKEY = 0x0312;
    const int MaxShownResults = 200;

    readonly MemoryEngine _mem = new();
    readonly ValueScanner _scanner;
    readonly string _profilesDir = Path.Combine(AppContext.BaseDirectory, "perfiles");
    readonly List<Cheat> _cheats = [];
    readonly List<CheatCard> _cards = [];
    GameProfile _profile = new();
    string? _profilePath;
    int _registeredHotkeys;
    bool _updatingProfileList;

    // Cabecera
    readonly Label _gameTitle = new() { AutoSize = true, Font = Theme.PageTitle, ForeColor = Theme.Text, Location = new Point(22, 14) };
    readonly StatusPill _statusPill = new() { Location = new Point(26, 56) };
    readonly ComboBox _processBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 });

    // Navegación
    readonly PillButton _navCheats = new("Cheats") { Nav = true, Width = 186, Height = 42 };
    readonly PillButton _navScanner = new("Escáner") { Nav = true, Width = 186, Height = 42 };
    readonly PillButton _navPointers = new("Punteros") { Nav = true, Width = 186, Height = 42 };
    readonly Panel _cheatsPage = new() { Dock = DockStyle.Fill };
    readonly Panel _scannerPage = new() { Dock = DockStyle.Fill, Visible = false };
    readonly Panel _pointersPage = new() { Dock = DockStyle.Fill, Visible = false };

    // Página Cheats
    readonly ComboBox _profileBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 });
    readonly Label _profileInfo = Theme.Label("");
    readonly FlowLayoutPanel _cardsPanel = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(0, 10, 0, 0),
    };
    readonly Label _emptyLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = Theme.Body,
        ForeColor = Theme.TextDim,
        Visible = false,
        Text = "Este perfil todavía no tiene cheats.\n\nPulsa «+ Nuevo cheat», o ve al Escáner, encuentra un valor y pulsa «Añadir como cheat».",
    };

    // Página Escáner
    readonly ComboBox _kindBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 });
    readonly TextBox _valueBox = Theme.Input(new TextBox { Width = 85 });
    readonly ComboBox _modeBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 });
    readonly PillButton _firstScanButton = new("Primer escaneo", primary: true);
    readonly PillButton _nextScanButton = new("Siguiente") { Enabled = false };
    readonly Label _scanLabel = Theme.Label("Sin escaneo", Theme.Text);
    readonly ListView _resultList = DarkList();
    readonly TextBox _newValueBox = Theme.Input(new TextBox { Width = 100 });

    // Página Punteros
    readonly TextBox _ptrAddressBox = Theme.Input(new TextBox { Width = 140 });
    readonly ComboBox _ptrDepthBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50 });
    readonly TextBox _ptrOffsetBox = Theme.Input(new TextBox { Width = 65, Text = "1000" });
    readonly PillButton _ptrScanButton = new("Buscar punteros", primary: true);
    readonly TextBox _ptrFilterBox = Theme.Input(new TextBox { Width = 140 });
    readonly Label _ptrLabel = Theme.Label("Sin resultados", Theme.Text);
    readonly ListView _ptrList = DarkList();
    List<PointerPath> _pointers = [];
    ValueKind _pointerKind = ValueKind.Int32;
    IReadOnlyDictionary<string, nint> _moduleBases = new Dictionary<string, nint>();
    CancellationTokenSource? _pointerScanCancel;
    readonly string _pointersDir = Path.Combine(AppContext.BaseDirectory, "punteros");
    string? _pointerProcess; // de qué juego son las cadenas en memoria
    int _pointerFilters;

    readonly System.Windows.Forms.Timer _freezeTimer = new() { Interval = 50 };
    readonly System.Windows.Forms.Timer _watchTimer = new() { Interval = 1500 };
    readonly System.Windows.Forms.Timer _resultsTimer = new() { Interval = 500 };

    record ProcessItem(int Id, string Name)
    {
        public override string ToString() => $"{Name}.exe  ({Id})";
    }

    record ProfileItem(string FilePath)
    {
        public override string ToString() => Path.GetFileNameWithoutExtension(FilePath);
    }

    public MainForm()
    {
        _scanner = new ValueScanner(_mem);
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

        BuildCheatsPage();
        BuildScannerPage();
        BuildPointersPage();
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 4, 24, 20) };
        content.Controls.Add(_cheatsPage);
        content.Controls.Add(_scannerPage);
        content.Controls.Add(_pointersPage);

        Controls.Add(content);
        Controls.Add(BuildHeader());
        Controls.Add(BuildSidebar());
        ResumeLayout(true);

        SetStatus("Sin conectar", Theme.Danger);
        ShowPage(_cheatsPage);

        _freezeTimer.Tick += (_, _) =>
        {
            if (!_mem.IsAttached) return;
            foreach (var cheat in _cheats) cheat.Tick(_mem);
        };
        _watchTimer.Tick += (_, _) => WatchProcess();
        _resultsTimer.Tick += (_, _) =>
        {
            RefreshResultValues();
            RefreshPointerValues();
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LoadProcessList();
        LoadProfileList();
        ActiveControl = _cardsPanel; // que ningún desplegable aparezca resaltado al abrir
        _freezeTimer.Start();
        _watchTimer.Start();
        _resultsTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        DisableAll(); // restaura los parches para no dejar el juego modificado
        UnregisterHotkeys();
        _mem.Dispose();
        base.OnFormClosing(e);
    }

    // ================= Construcción de la interfaz =================

    Control BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Theme.Sidebar };

        var logo = new Panel { Dock = DockStyle.Top, Height = 78 };
        logo.Controls.Add(new Label { Text = "◆", AutoSize = true, Font = Theme.Logo, ForeColor = Theme.Accent, Location = new Point(20, 24) });
        logo.Controls.Add(new Label { Text = "MiTrainer", AutoSize = true, Font = Theme.Logo, ForeColor = Theme.Text, Location = new Point(46, 24) });

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12, 0, 0, 0) };
        _navCheats.Click += (_, _) => ShowPage(_cheatsPage);
        _navScanner.Click += (_, _) => ShowPage(_scannerPage);
        _navPointers.Click += (_, _) => ShowPage(_pointersPage);
        nav.Controls.Add(_navCheats);
        nav.Controls.Add(_navScanner);
        nav.Controls.Add(_navPointers);

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
        var refresh = new PillButton("↻") { Width = 38 };
        refresh.Click += (_, _) => LoadProcessList();
        var connect = new PillButton("Conectar", primary: true);
        connect.Click += (_, _) => AttachSelected();

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, Padding = new Padding(0, 22, 20, 0), WrapContents = false };
        right.Controls.AddRange([_processBox, refresh, connect]);

        header.Controls.Add(right);
        header.Controls.Add(_gameTitle);
        header.Controls.Add(_statusPill);
        return header;
    }

    void BuildCheatsPage()
    {
        _profileBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingProfileList && _profileBox.SelectedItem is ProfileItem item) LoadProfile(item.FilePath);
        };
        _cardsPanel.ClientSizeChanged += (_, _) => ResizeCards();

        var toolbar = Row(Theme.Label("Perfil"), _profileBox,
            Pill("+ Nuevo cheat", NewCheat, primary: true),
            Pill("Recargar", () => LoadProfileList(_profilePath)),
            Pill("Guardar", SaveProfile),
            Pill("Abrir carpeta", () =>
            {
                Directory.CreateDirectory(_profilesDir);
                Process.Start("explorer.exe", _profilesDir);
            }),
            _profileInfo);
        toolbar.Dock = DockStyle.Top;

        _cheatsPage.Controls.Add(_cardsPanel);
        _cheatsPage.Controls.Add(_emptyLabel);
        _cheatsPage.Controls.Add(toolbar);
    }

    void BuildScannerPage()
    {
        _kindBox.Items.AddRange(["int", "long", "float", "double"]);
        _kindBox.SelectedIndex = 0;
        _modeBox.Items.AddRange(["Exacto", "Aumentó", "Disminuyó", "Cambió", "Sin cambios"]);
        _modeBox.SelectedIndex = 0;
        _firstScanButton.Click += async (_, _) => await RunScan(first: true);
        _nextScanButton.Click += async (_, _) => await RunScan(first: false);

        _resultList.Columns.Add("Dirección", 200);
        _resultList.Columns.Add("Valor actual", 160);
        _resultList.Columns.Add("Anterior", 160);
        _resultList.Resize += (_, _) => _resultList.Columns[^1].Width = -2; // la última columna llena el hueco
        _resultList.DoubleClick += (_, _) => AddSelectedAsCheat();

        var toolbar = Row(Theme.Label("Tipo"), _kindBox, Theme.Label("Valor"), _valueBox, Theme.Label("Modo"), _modeBox,
            _firstScanButton, _nextScanButton,
            Pill("Nuevo", () =>
            {
                _scanner.Reset();
                ShowResults();
                SetScanButtons(true);
            }));
        toolbar.Dock = DockStyle.Top;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            Padding = new Padding(6, 4, 0, 0),
            Text = "Busca el valor que ves  →  cámbialo jugando  →  «Siguiente» con el valor nuevo  →  " +
                   "repite y haz doble clic en el resultado para crear el cheat",
        };

        var actions = Row(Theme.Label("Nuevo valor"), _newValueBox,
            Pill("Escribir valor", WriteSelected),
            Pill("Buscar punteros", UseInPointerScan),
            Pill("Añadir como cheat", AddSelectedAsCheat, primary: true));
        actions.Dock = DockStyle.Bottom;

        _scannerPage.Controls.Add(_resultList);
        _scannerPage.Controls.Add(StatusRow(_scanLabel));
        _scannerPage.Controls.Add(actions);
        _scannerPage.Controls.Add(hint);
        _scannerPage.Controls.Add(toolbar);
    }

    void BuildPointersPage()
    {
        _ptrDepthBox.Items.AddRange(["1", "2", "3", "4", "5", "6", "7"]);
        _ptrDepthBox.SelectedIndex = 3;
        _ptrScanButton.Click += async (_, _) => await RunPointerScan();

        _ptrList.Columns.Add("Base (estática)", 260);
        _ptrList.Columns.Add("Offsets", 200);
        _ptrList.Columns.Add("Apunta a", 160);
        _ptrList.Columns.Add("Valor", 100);
        _ptrList.Resize += (_, _) => _ptrList.Columns[^1].Width = -2;
        _ptrList.DoubleClick += (_, _) => AddSelectedPointerAsCheat();

        var toolbar = Row(Theme.Label("Dirección"), _ptrAddressBox, Theme.Label("Profundidad"), _ptrDepthBox,
            Theme.Label("Offset máx."), _ptrOffsetBox, _ptrScanButton);
        toolbar.Dock = DockStyle.Top;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            Padding = new Padding(6, 4, 0, 0),
            Text = "1. En el Escáner, elige el resultado y pulsa «Buscar punteros».   2. Reinicia el juego y vuelve a encontrar el valor.\n" +
                   "3. Pon la nueva dirección abajo y pulsa «Filtrar». Repite hasta que queden pocas cadenas y conviértelas en cheat.",
        };

        var actions = Row(Theme.Label("Nueva dirección"), _ptrFilterBox,
            Pill("Filtrar", async () => await FilterPointers()),
            Pill("Añadir como cheat", AddSelectedPointerAsCheat, primary: true),
            Pill("Guardar como…", SavePointersAs),
            Pill("Abrir…", OpenPointers));
        actions.Dock = DockStyle.Bottom;

        _pointersPage.Controls.Add(_ptrList);
        _pointersPage.Controls.Add(StatusRow(_ptrLabel));
        _pointersPage.Controls.Add(actions);
        _pointersPage.Controls.Add(hint);
        _pointersPage.Controls.Add(toolbar);
    }

    /// <summary>Línea de estado bajo una lista (resultados, progreso, errores).</summary>
    static FlowLayoutPanel StatusRow(Label label)
    {
        var row = Row(label);
        row.Dock = DockStyle.Bottom;
        row.Padding = new Padding(0, 2, 0, 0);
        return row;
    }

    static ListView DarkList() => new()
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

    static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 4, 0, 4) };
        row.Controls.AddRange(controls);
        return row;
    }

    static PillButton Pill(string text, Action onClick, bool primary = false)
    {
        var button = new PillButton(text, primary);
        button.Click += (_, _) => onClick();
        return button;
    }

    void ShowPage(Panel page)
    {
        _cheatsPage.Visible = page == _cheatsPage;
        _scannerPage.Visible = page == _scannerPage;
        _pointersPage.Visible = page == _pointersPage;
        _navCheats.Selected = page == _cheatsPage;
        _navScanner.Selected = page == _scannerPage;
        _navPointers.Selected = page == _pointersPage;
    }

    void SetStatus(string text, Color color) => _statusPill.Set(text, color);

    void UpdateTitle() =>
        _gameTitle.Text = !string.IsNullOrWhiteSpace(_profile.Game) ? _profile.Game
            : _mem.Process?.ProcessName ?? "Ningún juego";

    // ================= Conexión al juego =================

    void LoadProcessList()
    {
        int session = Process.GetCurrentProcess().SessionId;
        var items = Process.GetProcesses()
            .Where(p => p.SessionId == session && p.Id != Environment.ProcessId)
            .Select(p => new ProcessItem(p.Id, p.ProcessName))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _processBox.Items.Clear();
        _processBox.Items.AddRange(items);
    }

    void AttachSelected()
    {
        if (_processBox.SelectedItem is not ProcessItem item)
        {
            SetStatus("Elige un proceso de la lista", Theme.Danger);
            return;
        }
        try { Attach(Process.GetProcessById(item.Id)); }
        catch (ArgumentException) { SetStatus("Ese proceso ya no existe", Theme.Danger); LoadProcessList(); }
    }

    void Attach(Process process)
    {
        DisableAll();
        _scanner.Reset();
        ShowResults();
        SetScanButtons(true);
        try
        {
            _mem.Attach(process);
            SetStatus($"Conectado a {process.ProcessName}.exe · PID {process.Id} · {(_mem.Is32Bit ? 32 : 64)} bits", Theme.Success);
            _moduleBases = _mem.ModuleBases();
            // Los resultados de punteros se conservan (en memoria o en disco): justo sirven para cuando el juego se reinicia.
            if (_pointerProcess != process.ProcessName) LoadAutoSavedPointers(process.ProcessName);
            else if (_pointers.Count > 0)
                _ptrLabel.Text = $"{_pointers.Count:N0} cadenas · vuelve a encontrar el valor y filtra con su nueva dirección";
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message, Theme.Danger);
        }
        UpdateTitle();
    }

    void WatchProcess()
    {
        if (_mem.Process != null && !_mem.IsAttached)
        {
            foreach (var cheat in _cheats) cheat.Reset();
            UpdateCards();
            _mem.Detach();
            SetStatus("El juego se cerró", Theme.Danger);
            UpdateTitle();
        }
        TryAutoAttach();
    }

    /// <summary>Si el perfil indica un proceso, nos conectamos solos en cuanto se abre el juego.</summary>
    void TryAutoAttach()
    {
        if (_mem.IsAttached || string.IsNullOrWhiteSpace(_profile.Process)) return;
        var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_profile.Process)).FirstOrDefault();
        if (process != null) Attach(process);
        else if (_mem.Process == null) SetStatus($"Esperando a {_profile.Process}...", Theme.TextDim);
    }

    // ================= Perfiles y cheats =================

    void LoadProfileList(string? select = null)
    {
        Directory.CreateDirectory(_profilesDir);
        var files = Directory.GetFiles(_profilesDir, "*.json").Order(StringComparer.OrdinalIgnoreCase).ToArray();

        _updatingProfileList = true;
        _profileBox.Items.Clear();
        foreach (var file in files) _profileBox.Items.Add(new ProfileItem(file));
        _updatingProfileList = false;

        if (files.Length == 0)
        {
            SetProfile(new GameProfile(), null);
            return;
        }
        int index = select == null ? 0 : Math.Max(0, Array.FindIndex(files, f => string.Equals(f, select, StringComparison.OrdinalIgnoreCase)));
        _profileBox.SelectedIndex = index; // dispara LoadProfile
    }

    void LoadProfile(string path)
    {
        try
        {
            SetProfile(GameProfile.Load(path), path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo leer el perfil:\n{ex.Message}", "MiTrainer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void SetProfile(GameProfile profile, string? path)
    {
        DisableAll();
        _profile = profile;
        _profilePath = path;
        _cheats.Clear();
        foreach (var card in _cards) card.Dispose();
        _cards.Clear();
        _cardsPanel.Controls.Clear();
        foreach (var def in profile.Cheats) _cheats.Add(new Cheat(def));
        RebuildCards();
        RegisterHotkeys();
        _profileInfo.Text = path == null ? "" : CheatCountText();
        UpdateTitle();
        if (!_mem.IsAttached) TryAutoAttach();
    }

    void SaveProfile()
    {
        string? path = _profilePath;
        if (path == null)
        {
            using var dialog = new SaveFileDialog
            {
                InitialDirectory = _profilesDir,
                Filter = "Perfil (*.json)|*.json",
                FileName = "mi-juego.json",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            path = dialog.FileName;
        }
        if (string.IsNullOrWhiteSpace(_profile.Process) && _mem.Process != null)
            _profile.Process = _mem.Process.ProcessName + ".exe";
        if (string.IsNullOrWhiteSpace(_profile.Game))
            _profile.Game = Path.GetFileNameWithoutExtension(path);
        _profile.Save(path);

        if (_profilePath == null)
        {
            _profilePath = path;
            _updatingProfileList = true;
            var item = new ProfileItem(path);
            _profileBox.Items.Add(item);
            _profileBox.SelectedItem = item;
            _updatingProfileList = false;
        }
        _profileInfo.Text = $"✓ Guardado en {Path.GetFileName(path)}";
        UpdateTitle();
    }

    string CheatCountText() => _cheats.Count == 1 ? "1 cheat" : $"{_cheats.Count} cheats";

    /// <summary>Vuelve a crear las tarjetas a partir de _cheats (los cheats activos siguen activos).</summary>
    void RebuildCards()
    {
        _cardsPanel.SuspendLayout();
        foreach (var card in _cards) card.Dispose();
        _cards.Clear();
        _cardsPanel.Controls.Clear();
        foreach (var cheat in _cheats)
        {
            var card = new CheatCard(cheat) { Width = CardWidth() };
            card.ToggleRequested += (_, _) => SetCheat(cheat, !cheat.Enabled);
            card.EditRequested += (_, _) => EditCheat(cheat);
            card.DuplicateRequested += (_, _) => DuplicateCheat(cheat);
            card.DeleteRequested += (_, _) => DeleteCheat(cheat);
            card.UpdateState();
            _cards.Add(card);
            _cardsPanel.Controls.Add(card);
        }
        _cardsPanel.ResumeLayout();
        UpdateEmptyState();
    }

    /// <summary>Añade un cheat al perfil (desde el editor, el escáner o los punteros).</summary>
    void AddCheat(CheatDef def)
    {
        _profile.Cheats.Add(def);
        _cheats.Add(new Cheat(def));
        RebuildCards();
        RegisterHotkeys();
        ProfileChanged();
    }

    /// <summary>Si el perfil ya tiene archivo, los cambios se guardan solos.</summary>
    void ProfileChanged()
    {
        if (_profilePath == null)
        {
            _profileInfo.Text = $"{CheatCountText()} · sin guardar: pulsa «Guardar»";
            return;
        }
        try
        {
            _profile.Save(_profilePath);
            _profileInfo.Text = $"{CheatCountText()} · ✓ guardado";
        }
        catch (Exception ex)
        {
            _profileInfo.Text = "No se pudo guardar: " + ex.Message;
        }
    }

    // ---------- Crear, editar, duplicar y eliminar ----------

    CheatDef? ShowEditor(CheatDef? def, Cheat? editing)
    {
        // Mientras el editor está abierto soltamos los atajos globales, para poder "grabar" F1, F2...
        UnregisterHotkeys();
        try
        {
            var used = _cheats.Where(c => c != editing).Select(c => c.Def.Hotkey).OfType<string>();
            using var editor = new CheatEditor(def, _mem, used);
            return editor.ShowDialog(this) == DialogResult.OK ? editor.Result : null;
        }
        finally
        {
            RegisterHotkeys();
        }
    }

    void NewCheat()
    {
        var template = new CheatDef { Name = "", Type = CheatTypes.Freeze, ValueType = "int", Hotkey = NextFreeHotkey() };
        if (ShowEditor(template, null) is { } def) AddCheat(def);
    }

    void EditCheat(Cheat cheat)
    {
        if (ShowEditor(cheat.Def, cheat) is not { } def) return;
        cheat.Disable(_mem); // un parche activo se deshace con los datos viejos antes de cambiarlos
        int index = _cheats.IndexOf(cheat);
        _cheats[index] = new Cheat(def);
        _profile.Cheats[_profile.Cheats.IndexOf(cheat.Def)] = def;
        RebuildCards();
        RegisterHotkeys();
        ProfileChanged();
    }

    void DuplicateCheat(Cheat cheat)
    {
        var copy = GameProfile.Clone(cheat.Def);
        copy.Name += " (copia)";
        copy.Hotkey = NextFreeHotkey();
        AddCheat(copy);
    }

    void DeleteCheat(Cheat cheat)
    {
        var answer = MessageBox.Show(this, $"¿Eliminar «{cheat.Def.Name}»?", "MiTrainer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        cheat.Disable(_mem);
        _cheats.Remove(cheat);
        _profile.Cheats.Remove(cheat.Def);
        RebuildCards();
        RegisterHotkeys();
        ProfileChanged();
    }

    int CardWidth() => Math.Max(300, _cardsPanel.ClientSize.Width - _cardsPanel.Padding.Horizontal - 4);

    void ResizeCards()
    {
        int width = CardWidth();
        _cardsPanel.SuspendLayout();
        foreach (var card in _cards) card.Width = width;
        _cardsPanel.ResumeLayout();
    }

    void UpdateEmptyState()
    {
        _emptyLabel.Visible = _cards.Count == 0;
        _cardsPanel.Visible = _cards.Count > 0;
    }

    void UpdateCards()
    {
        foreach (var card in _cards) card.UpdateState();
    }

    void UpdateCard(Cheat cheat) => _cards.Find(c => c.Cheat == cheat)?.UpdateState();

    void SetCheat(Cheat cheat, bool on)
    {
        if (!on) cheat.Disable(_mem);
        else if (_mem.IsAttached) cheat.Enable(_mem);
        else cheat.Fail("Conecta el juego primero");
        UpdateCard(cheat);
    }

    void DisableAll()
    {
        foreach (var cheat in _cheats.Where(c => c.Enabled))
        {
            cheat.Disable(_mem);
            UpdateCard(cheat);
        }
    }

    // ================= Atajos de teclado globales =================

    void RegisterHotkeys()
    {
        UnregisterHotkeys();
        for (int i = 0; i < _cheats.Count; i++)
        {
            string? hotkey = _cheats[i].Def.Hotkey;
            if (string.IsNullOrWhiteSpace(hotkey)) continue;
            if (!Hotkeys.TryParse(hotkey, out uint modifiers, out uint key))
                _cheats[i].Status = $"Atajo no válido: {hotkey}";
            else if (!Native.RegisterHotKey(Handle, i + 1, modifiers | Native.MOD_NOREPEAT, key))
                _cheats[i].Status = $"El atajo {hotkey} lo usa otra aplicación";
        }
        _registeredHotkeys = _cheats.Count;
        UpdateCards();
    }

    void UnregisterHotkeys()
    {
        for (int id = 1; id <= _registeredHotkeys; id++) Native.UnregisterHotKey(Handle, id);
        _registeredHotkeys = 0;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int index = (int)m.WParam - 1;
            if (index >= 0 && index < _cheats.Count)
            {
                var cheat = _cheats[index];
                SetCheat(cheat, !cheat.Enabled);
                bool ok = cheat.Enabled || (cheat.Def.Type == CheatTypes.Set && !cheat.Failed);
                (ok ? SystemSounds.Asterisk : SystemSounds.Beep).Play();
            }
        }
        base.WndProc(ref m);
    }

    // ================= Escáner =================

    async Task RunScan(bool first)
    {
        if (!_mem.IsAttached)
        {
            _scanLabel.Text = "Conecta un proceso primero";
            return;
        }
        var mode = first ? ScanMode.Exact : (ScanMode)_modeBox.SelectedIndex;
        double value = 0;
        if (mode == ScanMode.Exact && !ValueKinds.TryParseNumber(_valueBox.Text, out value))
        {
            _scanLabel.Text = "Escribe un número en 'Valor'";
            return;
        }
        var kind = ValueKinds.Parse((string)_kindBox.SelectedItem!);

        SetScanButtons(false);
        _scanLabel.Text = "Escaneando...";
        try
        {
            var timer = Stopwatch.StartNew();
            await Task.Run(() =>
            {
                if (first) _scanner.FirstScan(kind, value);
                else _scanner.NextScan(mode, value);
            });
            ShowResults();
            _scanLabel.Text += $"  ({timer.Elapsed.TotalSeconds:0.0} s)";
        }
        catch (Exception ex)
        {
            _scanLabel.Text = "Error: " + ex.Message;
        }
        finally
        {
            SetScanButtons(true);
        }
    }

    void SetScanButtons(bool enabled)
    {
        _firstScanButton.Enabled = enabled;
        _nextScanButton.Enabled = enabled && _scanner.HasResults;
        _kindBox.Enabled = enabled && !_scanner.HasResults;
    }

    void ShowResults()
    {
        var kind = _scanner.Kind;
        _resultList.BeginUpdate();
        _resultList.Items.Clear();
        for (int i = 0; i < Math.Min(_scanner.Count, MaxShownResults); i++)
        {
            long address = _scanner.Addresses[i];
            string value = kind.Format(_scanner.Values[i]);
            var item = new ListViewItem($"0x{address:X}") { Tag = address };
            item.SubItems.Add(value);
            item.SubItems.Add(value);
            _resultList.Items.Add(item);
        }
        _resultList.EndUpdate();

        _scanLabel.Text = !_scanner.HasResults ? "Sin escaneo"
            : _scanner.Count > MaxShownResults ? $"{_scanner.Count:N0} resultados (mostrando {MaxShownResults})"
            : $"{_scanner.Count:N0} resultados";
    }

    void RefreshResultValues()
    {
        if (!_mem.IsAttached || _resultList.Items.Count == 0 || !_resultList.Visible) return;
        foreach (ListViewItem item in _resultList.Items)
        {
            double? value = _mem.ReadValue((nint)(long)item.Tag!, _scanner.Kind);
            item.SubItems[1].Text = value is { } v ? _scanner.Kind.Format(v) : "??";
        }
    }

    void WriteSelected()
    {
        if (!_mem.IsAttached || _resultList.SelectedItems.Count == 0) return;
        if (!ValueKinds.TryParseNumber(_newValueBox.Text, out double value))
        {
            _scanLabel.Text = "Escribe un número en 'Nuevo valor'";
            return;
        }
        foreach (ListViewItem item in _resultList.SelectedItems)
            _mem.WriteValue((nint)(long)item.Tag!, _scanner.Kind, value);
        RefreshResultValues();
    }

    void AddSelectedAsCheat()
    {
        if (_resultList.SelectedItems.Count == 0)
        {
            _scanLabel.Text = "Selecciona un resultado de la lista";
            return;
        }
        long address = (long)_resultList.SelectedItems[0].Tag!;
        var kind = _scanner.Kind;
        double value = ValueKinds.TryParseNumber(_newValueBox.Text, out double typed) ? typed
            : _mem.ReadValue((nint)address, kind) ?? 0;

        var def = new CheatDef
        {
            Name = $"Congelar 0x{address:X}",
            Type = CheatTypes.Freeze,
            Address = $"{address:X}",
            ValueType = kind.Name(),
            Value = value,
            Hotkey = NextFreeHotkey(),
        };
        AddCheat(def);
        _scanLabel.Text = $"✓ Añadido a Cheats ({def.Hotkey}). Las direcciones directas cambian al reiniciar el juego.";
    }

    // ================= Escáner de punteros =================

    void UseInPointerScan()
    {
        if (_resultList.SelectedItems.Count == 0)
        {
            _scanLabel.Text = "Selecciona un resultado de la lista";
            return;
        }
        string address = $"{(long)_resultList.SelectedItems[0].Tag!:X}";
        _pointerKind = _scanner.Kind;
        _ptrAddressBox.Text = address;
        _ptrFilterBox.Text = address;
        ShowPage(_pointersPage);
        _ptrLabel.Text = _pointers.Count > 0
            ? "«Filtrar» para quedarte con las cadenas que llegan a esta dirección, o «Buscar punteros» para empezar de cero"
            : "Pulsa «Buscar punteros»";
    }

    async Task RunPointerScan()
    {
        if (_pointerScanCancel != null)
        {
            _pointerScanCancel.Cancel();
            return;
        }
        if (!_mem.IsAttached)
        {
            _ptrLabel.Text = "Conecta un proceso primero";
            return;
        }
        long target, maxOffset;
        try
        {
            target = Hex.ParseNumber(_ptrAddressBox.Text);
            maxOffset = Hex.ParseNumber(_ptrOffsetBox.Text);
        }
        catch (FormatException)
        {
            _ptrLabel.Text = "La dirección y el offset van en hexadecimal (p. ej. 1A2B3C)";
            return;
        }
        int depth = _ptrDepthBox.SelectedIndex + 1;

        _pointerScanCancel = new CancellationTokenSource();
        var cancel = _pointerScanCancel.Token;
        var progress = new Progress<string>(text => _ptrLabel.Text = text);
        _ptrScanButton.Text = "Cancelar";
        try
        {
            var timer = Stopwatch.StartNew();
            _pointers = await Task.Run(() =>
            {
                var map = PointerMap.Build(_mem, cancel, progress);
                return PointerScanner.Scan(map, target, depth, maxOffset, cancel, progress);
            });
            _moduleBases = _mem.ModuleBases();
            _pointerProcess = _mem.Process!.ProcessName;
            _pointerFilters = 0;
            ShowPointers();
            _ptrLabel.Text += $"  ({timer.Elapsed.TotalSeconds:0.0} s)";
            AutoSavePointers();
        }
        catch (OperationCanceledException)
        {
            _ptrLabel.Text = "Búsqueda cancelada";
        }
        catch (Exception ex)
        {
            _ptrLabel.Text = "Error: " + ex.Message;
        }
        finally
        {
            _pointerScanCancel.Dispose();
            _pointerScanCancel = null;
            _ptrScanButton.Text = "Buscar punteros";
        }
    }

    async Task FilterPointers()
    {
        if (!_mem.IsAttached || _pointers.Count == 0)
        {
            _ptrLabel.Text = _pointers.Count == 0 ? "Primero busca punteros" : "Conecta el juego primero";
            return;
        }
        long target;
        try { target = Hex.ParseNumber(_ptrFilterBox.Text); }
        catch (FormatException)
        {
            _ptrLabel.Text = "Escribe la nueva dirección en hexadecimal";
            return;
        }
        int before = _pointers.Count;
        var bases = _mem.ModuleBases();
        var paths = _pointers;
        _pointers = await Task.Run(() => PointerScanner.Filter(_mem, paths, bases, a => a == (nint)target));
        _moduleBases = bases;
        _pointerFilters++;
        ShowPointers();
        _ptrLabel.Text = $"{_pointers.Count:N0} de {before:N0} cadenas siguen llegando a 0x{target:X}";
        AutoSavePointers();
    }

    // ---------- Guardar / cargar cadenas ----------

    string AutoSavePath(string process) => Path.Combine(_pointersDir, process + ".json");

    PointerSave CurrentPointers() => new(_pointerProcess ?? "", _pointerKind, _pointerFilters, DateTime.Now, _pointers);

    /// <summary>Tras cada búsqueda o filtro se guarda solo en punteros/&lt;juego&gt;.json.</summary>
    void AutoSavePointers()
    {
        if (_pointerProcess == null) return;
        try
        {
            Directory.CreateDirectory(_pointersDir);
            PointerFile.Save(AutoSavePath(_pointerProcess), CurrentPointers());
            _ptrLabel.Text += " · guardado";
        }
        catch (Exception ex)
        {
            _ptrLabel.Text += $" · no se pudo guardar: {ex.Message}";
        }
    }

    /// <summary>Al conectar a un juego recuperamos sus cadenas de la última vez (si las hay).</summary>
    void LoadAutoSavedPointers(string process)
    {
        _pointers = [];
        _pointerFilters = 0;
        _pointerProcess = null;
        _ptrList.Items.Clear();
        _ptrLabel.Text = "Sin resultados";
        string file = AutoSavePath(process);
        if (File.Exists(file)) LoadPointers(file);
    }

    void LoadPointers(string file)
    {
        try
        {
            var save = PointerFile.Load(file);
            _pointers = save.Paths;
            _pointerKind = save.Kind;
            _pointerFilters = save.Filters;
            _pointerProcess = string.IsNullOrEmpty(save.Process) ? _mem.Process?.ProcessName : save.Process;
            ShowPointers();
            string filtered = save.Filters > 0 ? $", filtradas {save.Filters} {(save.Filters == 1 ? "vez" : "veces")}" : "";
            _ptrLabel.Text = $"{_pointers.Count:N0} cadenas guardadas el {save.Date:g}{filtered} · encuentra el valor y pulsa «Filtrar»";
        }
        catch (Exception ex)
        {
            _ptrLabel.Text = $"No se pudo abrir {Path.GetFileName(file)}: {ex.Message}";
        }
    }

    void SavePointersAs()
    {
        if (_pointers.Count == 0)
        {
            _ptrLabel.Text = "No hay cadenas que guardar";
            return;
        }
        Directory.CreateDirectory(_pointersDir);
        using var dialog = new SaveFileDialog
        {
            InitialDirectory = _pointersDir,
            Filter = "Punteros (*.json)|*.json",
            FileName = $"{_pointerProcess ?? "punteros"}-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            PointerFile.Save(dialog.FileName, CurrentPointers());
            _ptrLabel.Text = $"✓ {_pointers.Count:N0} cadenas guardadas en {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            _ptrLabel.Text = "No se pudo guardar: " + ex.Message;
        }
    }

    void OpenPointers()
    {
        Directory.CreateDirectory(_pointersDir);
        using var dialog = new OpenFileDialog { InitialDirectory = _pointersDir, Filter = "Punteros (*.json)|*.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadPointers(dialog.FileName);
    }

    void ShowPointers()
    {
        _ptrList.BeginUpdate();
        _ptrList.Items.Clear();
        // Primero las cadenas cortas del ejecutable principal: suelen ser las más fiables.
        string main = _mem.Process?.MainModule?.ModuleName ?? "";
        foreach (var path in _pointers
                     .OrderBy(p => !string.Equals(p.Module, main, StringComparison.OrdinalIgnoreCase))
                     .ThenBy(p => p.Offsets.Length)
                     .Take(MaxShownResults))
        {
            var item = new ListViewItem(path.BaseText) { Tag = path };
            item.SubItems.Add(path.OffsetsText);
            item.SubItems.Add("");
            item.SubItems.Add("");
            _ptrList.Items.Add(item);
        }
        _ptrList.EndUpdate();
        RefreshPointerValues();

        string capped = _pointers.Count >= PointerScanner.MaxResults ? " · límite alcanzado, reinicia el juego y filtra para afinar" : "";
        _ptrLabel.Text = _pointers.Count == 0 ? "No se encontraron cadenas: prueba más profundidad u offset"
            : _pointers.Count > MaxShownResults ? $"{_pointers.Count:N0} cadenas (se ven {MaxShownResults}){capped}"
            : $"{_pointers.Count:N0} cadenas";
    }

    void RefreshPointerValues()
    {
        if (!_mem.IsAttached || _ptrList.Items.Count == 0 || !_ptrList.Visible) return;
        foreach (ListViewItem item in _ptrList.Items)
        {
            nint address = ((PointerPath)item.Tag!).Resolve(_mem, _moduleBases);
            item.SubItems[2].Text = address == 0 ? "roto" : $"0x{address:X}";
            item.SubItems[3].Text = address != 0 && _mem.ReadValue(address, _pointerKind) is { } v ? _pointerKind.Format(v) : "??";
        }
    }

    void AddSelectedPointerAsCheat()
    {
        if (_ptrList.SelectedItems.Count == 0)
        {
            _ptrLabel.Text = "Selecciona una cadena de la lista";
            return;
        }
        var path = (PointerPath)_ptrList.SelectedItems[0].Tag!;
        nint address = _mem.IsAttached ? path.Resolve(_mem, _moduleBases) : 0;
        double value = address != 0 ? _mem.ReadValue(address, _pointerKind) ?? 0 : 0;

        var def = new CheatDef
        {
            Name = $"Congelar {path.Module}+{path.BaseOffset:X}",
            Type = CheatTypes.Freeze,
            Module = path.Module,
            Address = $"{path.BaseOffset:X}",
            Offsets = path.Offsets.Select(o => o.ToString("X")).ToList(),
            ValueType = _pointerKind.Name(),
            Value = value,
            Hotkey = NextFreeHotkey(),
        };
        AddCheat(def);
        _ptrLabel.Text = $"✓ Añadido a Cheats ({def.Hotkey}). Edítalo allí con ✎ para darle un nombre.";
    }

    string? NextFreeHotkey()
    {
        var used = _cheats.Select(c => c.Def.Hotkey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, 12).Select(n => $"F{n}").FirstOrDefault(k => !used.Contains(k));
    }
}
