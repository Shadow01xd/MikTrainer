using System.Diagnostics;

namespace MiTrainer.App.Pages;

/// <summary>
/// Página «Punteros»: busca cadenas de punteros estáticas hacia un valor, las filtra tras reiniciar el juego
/// y las convierte en cheats que sobreviven al reinicio. Los resultados se guardan solos por juego.
/// </summary>
sealed class PointersPage : Page
{
    const int MaxShownResults = 200;

    readonly TextBox _addressBox = Theme.Input(new TextBox { Width = 140 });
    readonly ComboBox _depthBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50 });
    readonly TextBox _maxOffsetBox = Theme.Input(new TextBox { Width = 65, Text = "1000" });
    readonly PillButton _scanButton = new("Buscar punteros", primary: true);
    readonly TextBox _filterBox = Theme.Input(new TextBox { Width = 140 });
    readonly Label _status = Ui.Label("Sin resultados", Theme.Text);
    readonly ListView _list = Ui.List(("Base (estática)", 260), ("Offsets", 200), ("Apunta a", 160), ("Valor", 100));

    PointerScanResults? _results;      // lo que hay en memoria (y en disco)
    ValueKind _kind = ValueKind.Int32; // tipo elegido antes de tener resultados
    IReadOnlyDictionary<string, long> _moduleBases = new Dictionary<string, long>();
    CancellationTokenSource? _scanCancel;

    public PointersPage(AppServices services) : base(services)
    {
        _depthBox.Items.AddRange(["1", "2", "3", "4", "5", "6", "7"]);
        _depthBox.SelectedIndex = 3;
        _scanButton.Click += async (_, _) => await ScanOrCancel();
        _list.DoubleClick += (_, _) => AddSelectedAsCheat();

        var toolbar = Ui.Row(DockStyle.Top, Ui.Label("Dirección"), _addressBox, Ui.Label("Profundidad"), _depthBox,
            Ui.Label("Offset máx."), _maxOffsetBox, _scanButton);
        var hint = Ui.Hint("1. En el Escáner, elige el resultado y pulsa «Buscar punteros».   2. Reinicia el juego y vuelve a encontrar el valor.\n" +
                           "3. Pon la nueva dirección abajo y pulsa «Filtrar». Repite hasta que queden pocas cadenas y conviértelas en cheat.", height: 48);
        var actions = Ui.Row(DockStyle.Bottom, Ui.Label("Nueva dirección"), _filterBox,
            Ui.Pill("Filtrar", async () => await Filter()),
            Ui.Pill("Añadir como cheat", AddSelectedAsCheat, primary: true),
            Ui.Pill("Guardar como…", SaveAs),
            Ui.Pill("Abrir…", Open));

        Controls.Add(_list);
        Controls.Add(Ui.StatusRow(_status));
        Controls.Add(actions);
        Controls.Add(hint);
        Controls.Add(toolbar);

        Session.ConnectionChanged += (_, _) => OnConnectionChanged();
    }

    /// <summary>Viene del Escáner: prepara la búsqueda (o el filtro) para esta dirección.</summary>
    public void Prepare(long address, ValueKind kind)
    {
        _addressBox.Text = _filterBox.Text = NumberText.ToHex(address);
        _kind = kind;
        if (_results is not null) _results = _results with { Kind = kind };
        _status.Text = _results is { Paths.Count: > 0 }
            ? "«Filtrar» para quedarte con las cadenas que llegan a esta dirección, o «Buscar punteros» para empezar de cero"
            : "Pulsa «Buscar punteros»";
    }

    /// <summary>Tipo del valor al que apuntan las cadenas (para mostrarlo y para los cheats).</summary>
    ValueKind Kind => _results?.Kind ?? _kind;

    // ================= Conexión =================

    // Los resultados se conservan al reiniciar el mismo juego (justo para eso sirven); con otro juego, se cargan los suyos.
    void OnConnectionChanged()
    {
        if (Session.Memory is not { IsAttached: true } memory) return;
        _moduleBases = memory.ModuleBases();
        if (_results?.Process == memory.ProcessName)
        {
            _status.Text = $"{_results.Paths.Count:N0} cadenas · vuelve a encontrar el valor y filtra con su nueva dirección";
            return;
        }
        _results = null;
        ShowResults();
        string saved = Services.PointerResults.AutoSavePathFor(memory.ProcessName);
        if (File.Exists(saved)) LoadResults(saved);
    }

    // ================= Buscar y filtrar =================

    async Task ScanOrCancel()
    {
        if (_scanCancel is not null)
        {
            _scanCancel.Cancel();
            return;
        }
        if (Session.Memory is not { IsAttached: true } memory)
        {
            _status.Text = "Conecta un proceso primero";
            return;
        }
        if (!NumberText.TryParseHex(_addressBox.Text, out long target) || !NumberText.TryParseHex(_maxOffsetBox.Text, out long maxOffset))
        {
            _status.Text = "La dirección y el offset van en hexadecimal (p. ej. 1A2B3C)";
            return;
        }
        int depth = _depthBox.SelectedIndex + 1;

        _scanCancel = new CancellationTokenSource();
        var cancel = _scanCancel.Token;
        var progress = new Progress<string>(text => _status.Text = text);
        _scanButton.Text = "Cancelar";
        try
        {
            var timer = Stopwatch.StartNew();
            var paths = await Task.Run(() => PointerScanner.Scan(PointerMap.Build(memory, cancel, progress), target, depth, maxOffset, cancel, progress));
            _results = new PointerScanResults(memory.ProcessName, Kind, Filters: 0, DateTime.Now, paths);
            _moduleBases = memory.ModuleBases();
            ShowResults();
            _status.Text += $"  ({timer.Elapsed.TotalSeconds:0.0} s)";
            AutoSave();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Búsqueda cancelada";
        }
        finally
        {
            _scanCancel.Dispose();
            _scanCancel = null;
            _scanButton.Text = "Buscar punteros";
        }
    }

    async Task Filter()
    {
        if (_results is not { Paths.Count: > 0 } results)
        {
            _status.Text = "Primero busca punteros";
            return;
        }
        if (Session.Memory is not { IsAttached: true } memory)
        {
            _status.Text = "Conecta el juego primero";
            return;
        }
        if (!NumberText.TryParseHex(_filterBox.Text, out long target))
        {
            _status.Text = "Escribe la nueva dirección en hexadecimal";
            return;
        }
        var kept = await Task.Run(() => PointerScanner.Filter(memory, results.Paths, address => address == target));
        _results = results with { Paths = kept, Filters = results.Filters + 1, Date = DateTime.Now };
        _moduleBases = memory.ModuleBases();
        ShowResults();
        _status.Text = $"{kept.Count:N0} de {results.Paths.Count:N0} cadenas siguen llegando a 0x{target:X}";
        AutoSave();
    }

    // ================= Mostrar =================

    void ShowResults()
    {
        var paths = _results?.Paths ?? [];
        string main = Session.Memory?.GetModules().FirstOrDefault()?.Name ?? "";
        _list.BeginUpdate();
        _list.Items.Clear();
        // Primero las cadenas cortas del ejecutable principal: suelen ser las más fiables.
        foreach (var path in paths
                     .OrderBy(p => !string.Equals(p.Module, main, StringComparison.OrdinalIgnoreCase))
                     .ThenBy(p => p.Offsets.Count)
                     .Take(MaxShownResults))
        {
            var item = new ListViewItem(path.BaseText) { Tag = path };
            item.SubItems.AddRange([path.OffsetsText, "", ""]);
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        RefreshLiveValues();

        string capped = paths.Count >= PointerScanner.MaxResults ? " · límite alcanzado, reinicia el juego y filtra para afinar" : "";
        _status.Text = _results is null ? "Sin resultados"
            : paths.Count == 0 ? "No se encontraron cadenas: prueba más profundidad u offset"
            : paths.Count > MaxShownResults ? $"{paths.Count:N0} cadenas (se ven {MaxShownResults}){capped}"
            : $"{paths.Count:N0} cadenas";
    }

    protected override void RefreshLiveValues()
    {
        if (Session.Memory is not { IsAttached: true } memory) return;
        foreach (ListViewItem item in _list.Items)
        {
            long address = ((PointerPath)item.Tag!).Resolve(memory, _moduleBases);
            item.SubItems[2].Text = address == 0 ? "roto" : $"0x{address:X}";
            item.SubItems[3].Text = address != 0 && memory.ReadValue(address, Kind) is { } v ? Kind.Format(v) : "??";
        }
    }

    void AddSelectedAsCheat()
    {
        if (_list.SelectedItems.Count == 0)
        {
            _status.Text = "Selecciona una cadena de la lista";
            return;
        }
        var path = (PointerPath)_list.SelectedItems[0].Tag!;
        long address = Session.Memory is { IsAttached: true } memory ? path.Resolve(memory, _moduleBases) : 0;
        double value = address != 0 ? Session.Memory!.ReadValue(address, Kind) ?? 0 : 0;

        var definition = new CheatDefinition
        {
            Name = $"Congelar {path.Module}+{path.BaseOffset:X}",
            Type = CheatType.Freeze,
            Module = path.Module,
            Address = NumberText.ToHex(path.BaseOffset),
            Offsets = [.. path.Offsets.Select(NumberText.ToHex)],
            ValueType = Kind,
            Value = value,
            Hotkey = Session.NextFreeHotkey(),
        };
        Session.AddCheat(definition);
        _status.Text = $"✓ Añadido a Cheats ({definition.Hotkey}). Edítalo allí con ✎ para darle un nombre.";
    }

    // ================= Guardar y abrir =================

    /// <summary>Tras cada búsqueda o filtro se guarda solo en punteros/&lt;juego&gt;.json.</summary>
    void AutoSave()
    {
        if (_results is null) return;
        try
        {
            Services.PointerResults.Save(Services.PointerResults.AutoSavePathFor(_results.Process), _results);
            _status.Text += " · guardado";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text += $" · no se pudo guardar: {ex.Message}";
        }
    }

    void LoadResults(string file)
    {
        try
        {
            _results = Services.PointerResults.Load(file);
            ShowResults();
            int filters = _results.Filters;
            string filtered = filters > 0 ? $", filtradas {filters} {(filters == 1 ? "vez" : "veces")}" : "";
            _status.Text = $"{_results.Paths.Count:N0} cadenas guardadas el {_results.Date:g}{filtered} · encuentra el valor y pulsa «Filtrar»";
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException)
        {
            _status.Text = $"No se pudo abrir {Path.GetFileName(file)}: {ex.Message}";
        }
    }

    void SaveAs()
    {
        if (_results is not { Paths.Count: > 0 } results)
        {
            _status.Text = "No hay cadenas que guardar";
            return;
        }
        Directory.CreateDirectory(Services.PointerResults.Folder);
        using var dialog = new SaveFileDialog
        {
            InitialDirectory = Services.PointerResults.Folder,
            Filter = "Punteros (*.json)|*.json",
            FileName = $"{results.Process}-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Services.PointerResults.Save(dialog.FileName, results);
            _status.Text = $"✓ {results.Paths.Count:N0} cadenas guardadas en {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = "No se pudo guardar: " + ex.Message;
        }
    }

    void Open()
    {
        Directory.CreateDirectory(Services.PointerResults.Folder);
        using var dialog = new OpenFileDialog { InitialDirectory = Services.PointerResults.Folder, Filter = "Punteros (*.json)|*.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadResults(dialog.FileName);
    }
}
