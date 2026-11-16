using System.Diagnostics;

namespace MiTrainer.App.Pages;

/// <summary>Página «Escáner»: encontrar en la memoria el valor que se ve en el juego (vida, dinero...).</summary>
sealed class ScannerPage : Page
{
    const int MaxShownResults = 200;
    internal const string DiscardedMessage = "Escaneo descartado: la sesión cambió mientras escaneaba";

    static readonly string[] Modes = ["Exacto", "Aumentó", "Disminuyó", "Cambió", "Sin cambios"]; // en el orden de ScanMode

    readonly ComboBox _kindBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 });
    readonly TextBox _valueBox = Theme.Input(new TextBox { Width = 85 });
    readonly ComboBox _modeBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 });
    readonly PillButton _firstScan = new("Primer escaneo", primary: true);
    readonly PillButton _nextScan = new("Siguiente") { Enabled = false };
    readonly Label _status = Ui.Label("Sin escaneo", Theme.Text);
    readonly ListView _results = Ui.List(("Dirección", 200), ("Valor actual", 160), ("Anterior", 160));
    readonly TextBox _newValueBox = Theme.Input(new TextBox { Width = 100 });

    // Resultados del último escaneo y la tarea con la que se obtuvieron (solo valen para esa conexión).
    ScanResults? _scan;
    SessionOperation? _scanOperation;

    public ScannerPage(AppServices services) : base(services)
    {
        _kindBox.Items.AddRange([.. ValueKinds.All.Select(k => (object)k.Name())]);
        _kindBox.SelectedIndex = 0;
        _modeBox.Items.AddRange(Modes);
        _modeBox.SelectedIndex = 0;
        _firstScan.Click += async (_, _) => await RunScan(first: true);
        _nextScan.Click += async (_, _) => await RunScan(first: false);
        _results.DoubleClick += (_, _) => AddSelectedAsCheat();

        var toolbar = Ui.Row(DockStyle.Top, Ui.Label("Tipo"), _kindBox, Ui.Label("Valor"), _valueBox, Ui.Label("Modo"), _modeBox,
            _firstScan, _nextScan, Ui.Pill("Nuevo", ResetScan));
        var hint = Ui.Hint("Busca el valor que ves  →  cámbialo jugando  →  «Siguiente» con el valor nuevo  →  " +
                           "repite y haz doble clic en el resultado para crear el cheat", height: 30);
        var actions = Ui.Row(DockStyle.Bottom, Ui.Label("Nuevo valor"), _newValueBox,
            Ui.Pill("Escribir valor", WriteSelected),
            Ui.Pill("Buscar punteros", RequestPointerScan),
            Ui.Pill("Añadir como cheat", AddSelectedAsCheat, primary: true));

        Controls.Add(_results);
        Controls.Add(Ui.StatusRow(_status));
        Controls.Add(actions);
        Controls.Add(hint);
        Controls.Add(toolbar);

        // Las direcciones solo valen para el proceso en el que se encontraron.
        Session.ConnectionChanged += (_, _) => ResetScan();
    }

    /// <summary>El usuario quiere buscar punteros hacia esta dirección.</summary>
    public event EventHandler<(long Address, ValueKind Kind)>? PointerScanRequested;

    // Para las pruebas automáticas
    internal TextBox ValueInput => _valueBox;
    internal string StatusText => _status.Text;
    internal int ResultCount => _scan?.Count ?? 0;

    ValueKind Kind => _scan?.Kind ?? ValueKinds.Parse((string?)_kindBox.SelectedItem);
    long? SelectedAddress => _results.SelectedItems.Count > 0 ? (long)_results.SelectedItems[0].Tag! : null;

    /// <summary>
    /// Lanza un escaneo atado a la conexión actual. Si la conexión cambia mientras corre, se cancela
    /// y su resultado se descarta sin mostrarse.
    /// </summary>
    internal async Task RunScan(bool first)
    {
        if (Session.BeginOperation() is not { } operation)
        {
            _status.Text = "Conecta un proceso primero";
            return;
        }
        var mode = first ? ScanMode.Exact : (ScanMode)_modeBox.SelectedIndex;
        double value = 0;
        if (mode == ScanMode.Exact && !NumberText.TryParseDecimal(_valueBox.Text, out value))
        {
            _status.Text = "Escribe un número en «Valor»";
            return;
        }
        var kind = Kind;
        var previous = _scan;
        if (!first && (previous is null || _scanOperation?.ConnectionId != operation.ConnectionId))
        {
            ResetScan(); // resultados de otra conexión: hay que empezar de nuevo
            return;
        }

        SetButtons(scanning: true);
        _status.Text = "Escaneando...";
        try
        {
            var timer = Stopwatch.StartNew();
            var memory = operation.Memory;
            var cancel = operation.Cancellation;
            var result = await Task.Run(() => first
                ? ValueScanner.First(memory, kind, value, cancel)
                : ValueScanner.Next(previous!, memory, mode, value, cancel));

            if (!Session.IsCurrent(operation))
            {
                _status.Text = DiscardedMessage;
                return;
            }
            _scan = result;
            _scanOperation = operation;
            ShowResults();
            _status.Text += $"  ({timer.Elapsed.TotalSeconds:0.0} s)";
        }
        catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested)
        {
            _status.Text = DiscardedMessage;
        }
        finally
        {
            SetButtons(scanning: false);
        }
    }

    void ResetScan()
    {
        _scan = null;
        _scanOperation = null;
        ShowResults();
        SetButtons(scanning: false);
    }

    void SetButtons(bool scanning)
    {
        _firstScan.Enabled = !scanning;
        _nextScan.Enabled = !scanning && _scan is not null;
        _kindBox.Enabled = !scanning && _scan is null;
    }

    void ShowResults()
    {
        _results.BeginUpdate();
        _results.Items.Clear();
        if (_scan is { } scan)
        {
            for (int i = 0; i < Math.Min(scan.Count, MaxShownResults); i++)
            {
                string value = scan.Kind.Format(scan.Values[i]);
                var item = new ListViewItem($"0x{scan.Addresses[i]:X}") { Tag = scan.Addresses[i] };
                item.SubItems.Add(value);
                item.SubItems.Add(value);
                _results.Items.Add(item);
            }
        }
        _results.EndUpdate();

        _status.Text = _scan is null ? "Sin escaneo"
            : _scan.Count > MaxShownResults ? $"{_scan.Count:N0} resultados (mostrando {MaxShownResults})"
            : $"{_scan.Count:N0} resultados";
    }

    protected override void RefreshLiveValues()
    {
        if (Session.Memory is not { IsAttached: true } memory) return;
        foreach (ListViewItem item in _results.Items)
            item.SubItems[1].Text = memory.ReadValue((long)item.Tag!, Kind) is { } v ? Kind.Format(v) : "??";
    }

    void WriteSelected()
    {
        if (Session.Memory is not { IsAttached: true } memory || _results.SelectedItems.Count == 0) return;
        if (!NumberText.TryParseDecimal(_newValueBox.Text, out double value))
        {
            _status.Text = "Escribe un número en «Nuevo valor»";
            return;
        }
        foreach (ListViewItem item in _results.SelectedItems) memory.WriteValue((long)item.Tag!, Kind, value);
        RefreshLiveValues();
    }

    void RequestPointerScan()
    {
        if (SelectedAddress is { } address) PointerScanRequested?.Invoke(this, (address, Kind));
        else _status.Text = "Selecciona un resultado de la lista";
    }

    void AddSelectedAsCheat()
    {
        if (SelectedAddress is not { } address)
        {
            _status.Text = "Selecciona un resultado de la lista";
            return;
        }
        var kind = Kind;
        double value = NumberText.TryParseDecimal(_newValueBox.Text, out double typed) ? typed
            : Session.Memory?.ReadValue(address, kind) ?? 0;

        var definition = new CheatDefinition
        {
            Name = $"Congelar 0x{address:X}",
            Type = CheatType.Freeze,
            Address = NumberText.ToHex(address),
            ValueType = kind,
            Value = value,
            Hotkey = Session.NextFreeHotkey(),
        };
        Session.AddCheat(definition);
        _status.Text = $"✓ Añadido a Cheats ({definition.Hotkey}). Las direcciones directas cambian al reiniciar el juego.";
    }
}
