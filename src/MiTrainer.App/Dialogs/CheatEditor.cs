using MiTrainer.App.Hotkeys;

namespace MiTrainer.App.Dialogs;

/// <summary>Ventana para crear o editar un cheat sin tocar el JSON.</summary>
sealed class CheatEditor : Form
{
    static readonly (CheatType Type, string Text)[] Types =
    [
        (CheatType.Freeze, "Congelar un valor (vida, munición…)"),
        (CheatType.Set, "Fijar un valor una vez (dinero, nivel…)"),
        (CheatType.Patch, "Parche de código (NOP, saltos…)"),
    ];

    readonly IProcessMemory? _memory;
    readonly HashSet<string> _usedHotkeys;
    readonly TableLayoutPanel _grid = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };

    readonly TextBox _name = Input(new TextBox());
    readonly ComboBox _type = Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
    readonly HotkeyBox _hotkey = Input(new HotkeyBox());
    readonly ComboBox _module = Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDown });
    readonly Label _moduleHint = Hint("");
    readonly TextBox _address = Input(new TextBox());
    readonly TextBox _offsets = Input(new TextBox());
    readonly ComboBox _valueType = Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
    readonly TextBox _value = Input(new TextBox());
    readonly TextBox _pattern = Input(new TextBox());
    readonly TextBox _patternOffset = Input(new TextBox());
    readonly TextBox _bytes = Input(new TextBox());
    readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(460, 0), Font = Theme.Small, ForeColor = Theme.TextDim, Margin = new Padding(4, 10, 4, 4) };
    readonly List<Control> _valueRows = [];
    readonly List<Control> _patchRows = [];

    /// <param name="definition">Cheat a editar, o una plantilla para uno nuevo.</param>
    /// <param name="memory">Juego conectado (para «Comprobar» y la lista de módulos), o null.</param>
    /// <param name="usedHotkeys">Atajos de los demás cheats, que no se pueden repetir.</param>
    public CheatEditor(CheatDefinition definition, bool isNew, IProcessMemory? memory, IEnumerable<string> usedHotkeys)
    {
        _memory = memory;
        _usedHotkeys = usedHotkeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Text = isNew ? "Nuevo cheat" : "Editar cheat";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 16, 20, 16);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        BuildFields();
        Controls.Add(BuildLayout());
        Fill(definition);
    }

    /// <summary>El cheat tal como quedó al pulsar Guardar.</summary>
    public CheatDefinition? Result { get; private set; }

    // Para las pruebas automáticas
    internal string ResultText => _result.Text;
    internal HotkeyBox HotkeyInput => _hotkey;

    CheatType SelectedType => Types[Math.Max(0, _type.SelectedIndex)].Type;

    // ================= Construcción =================

    void BuildFields()
    {
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _type.Items.AddRange([.. Types.Select(t => (object)t.Text)]);
        _type.SelectedIndexChanged += (_, _) => UpdateVisibility();
        _valueType.Items.AddRange([.. ValueKinds.All.Select(k => (object)k.Name())]);
        FillModules();

        AddRow("Nombre", _name);
        AddRow("Tipo", _type);
        AddRow("Atajo", _hotkey);
        AddHint("Haz clic y pulsa la combinación (p. ej. F5 o Ctrl+1). Supr para quitarlo.");
        AddRow("Módulo", _module);
        AddHint(_moduleHint);
        _valueRows.AddRange(AddRow("Dirección", _address));
        _valueRows.AddRange(AddHint("En hexadecimal. Con módulo, es la distancia desde el inicio del módulo."));
        _valueRows.AddRange(AddRow("Offsets", _offsets));
        _valueRows.AddRange(AddHint("Cadena de punteros, separados por comas (p. ej. 18, 10). Vacío si no hay."));
        _valueRows.AddRange(AddRow("Tipo de valor", _valueType));
        _valueRows.AddRange(AddRow("Valor", _value));
        _patchRows.AddRange(AddRow("Patrón", _pattern));
        _patchRows.AddRange(AddHint("Bytes a buscar en hexadecimal; ?? = cualquiera (p. ej. 89 4B 10 48 8B ??)."));
        _patchRows.AddRange(AddRow("Desplazamiento", _patternOffset));
        _patchRows.AddRange(AddHint("Cuántos bytes después del inicio del patrón empieza el parche."));
        _patchRows.AddRange(AddRow("Bytes nuevos", _bytes));
        _patchRows.AddRange(AddHint("Lo que se escribe al activarlo (90 = NOP). Se deshace al desactivarlo."));
    }

    Control BuildLayout()
    {
        var test = Ui.Pill("Comprobar en el juego", Test);
        test.Margin = new Padding(0, 12, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange([Ui.Pill("Cancelar", () => DialogResult = DialogResult.Cancel), Ui.Pill("Guardar", Save, primary: true)]);

        // Todo en una columna que crece con su contenido (la ventana se ajusta sola al cambiar de tipo).
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.AddRange([_grid, test, _result, buttons]);
        return layout;
    }

    static T Input<T>(T control) where T : Control
    {
        Theme.Input(control);
        control.Width = 330;
        control.Margin = new Padding(4, 6, 4, 2);
        return control;
    }

    static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(340, 0),
        Font = Theme.Small,
        ForeColor = Theme.TextDim,
        Margin = new Padding(5, 0, 4, 6),
    };

    Control[] AddRow(string label, Control control)
    {
        var caption = Ui.Label(label, Theme.Text);
        caption.Margin = new Padding(0, 9, 14, 0);
        _grid.Controls.Add(caption);
        _grid.Controls.Add(control);
        return [caption, control];
    }

    Control[] AddHint(string text) => AddHint(Hint(text));

    Control[] AddHint(Label hint)
    {
        var spacer = new Label { AutoSize = true, Margin = Padding.Empty };
        _grid.Controls.Add(spacer);
        _grid.Controls.Add(hint);
        return [spacer, hint];
    }

    void FillModules()
    {
        if (_memory is not { IsAttached: true }) return;
        foreach (var module in _memory.GetModules()) _module.Items.Add(module.Name); // el exe principal va primero
    }

    void UpdateVisibility()
    {
        bool patch = SelectedType == CheatType.Patch;
        SuspendLayout();
        foreach (var c in _valueRows) c.Visible = !patch;
        foreach (var c in _patchRows) c.Visible = patch;
        _moduleHint.Text = patch
            ? "Dónde buscar el patrón. Vacío = el .exe del juego."
            : "Exe o dll donde empieza la dirección. Vacío = dirección absoluta (cambia al reiniciar el juego).";
        ResumeLayout(true);
    }

    void Fill(CheatDefinition definition)
    {
        _name.Text = definition.Name;
        _type.SelectedIndex = Math.Max(0, Array.FindIndex(Types, t => t.Type == definition.Type));
        _hotkey.Text = definition.Hotkey ?? "";
        _module.Text = definition.Module ?? "";
        _address.Text = definition.Address ?? "";
        _offsets.Text = string.Join(", ", definition.Offsets ?? []);
        _valueType.SelectedItem = definition.ValueType.Name();
        _value.Text = definition.ValueType.Format(definition.Value);
        _pattern.Text = definition.Pattern ?? "";
        _patternOffset.Text = definition.PatternOffset.ToString();
        _bytes.Text = definition.Bytes ?? "";
        UpdateVisibility();
    }

    // ================= Lectura y validación =================

    /// <summary>Lee los campos. Lanza FormatException con un mensaje que entiende el usuario.</summary>
    CheatDefinition Build(bool checkHotkey)
    {
        var definition = new CheatDefinition
        {
            Name = _name.Text.Trim(),
            Type = SelectedType,
            Hotkey = NullIfEmpty(_hotkey.Text),
            Module = NullIfEmpty(_module.Text),
        };
        if (definition.Name.Length == 0) throw new FormatException("Ponle un nombre al cheat.");
        if (checkHotkey) ValidateHotkey(definition.Hotkey);
        return definition.Type == CheatType.Patch ? WithPatch(definition) : WithValue(definition);
    }

    void ValidateHotkey(string? hotkey)
    {
        if (hotkey is null) return;
        if (!HotkeyText.TryParse(hotkey, out _)) throw new FormatException($"El atajo «{hotkey}» no es válido.");
        if (_usedHotkeys.Contains(hotkey)) throw new FormatException($"El atajo {hotkey} ya lo usa otro cheat.");
    }

    CheatDefinition WithPatch(CheatDefinition definition)
    {
        Require(() => BytePattern.Parse(_pattern.Text), "Patrón: bytes en hexadecimal separados por espacios (?? = cualquiera).");
        if (!int.TryParse(_patternOffset.Text.Trim(), out int offset)) throw new FormatException("Desplazamiento: escribe un número entero.");
        Require(() => NumberText.ParseHexBytes(_bytes.Text), "Bytes nuevos: bytes en hexadecimal separados por espacios (p. ej. 90 90 90).");
        return definition with { Pattern = _pattern.Text.Trim(), PatternOffset = offset, Bytes = _bytes.Text.Trim() };
    }

    CheatDefinition WithValue(CheatDefinition definition)
    {
        if (!NumberText.TryParseHex(_address.Text, out _)) throw new FormatException("Dirección: escribe un número en hexadecimal (p. ej. 1A2B).");
        var offsets = _offsets.Text.Split([',', ' ', '→', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (offsets.Any(o => !NumberText.TryParseHex(o, out _))) throw new FormatException("Offsets: escribe números en hexadecimal (p. ej. 18, 10).");
        if (!NumberText.TryParseDecimal(_value.Text, out double value)) throw new FormatException("Valor: escribe un número.");
        return definition with
        {
            Address = _address.Text.Trim(),
            Offsets = offsets.Length > 0 ? offsets : null,
            ValueType = ValueKinds.Parse((string?)_valueType.SelectedItem),
            Value = value,
        };
    }

    static void Require(Action parse, string message)
    {
        try { parse(); }
        catch (FormatException) { throw new FormatException(message); }
    }

    static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    // ================= Botones =================

    void ShowResult(string text, Color color)
    {
        _result.Text = text;
        _result.ForeColor = color;
    }

    internal void Save()
    {
        try
        {
            Result = Build(checkHotkey: true);
            DialogResult = DialogResult.OK;
        }
        catch (FormatException ex)
        {
            ShowResult("⚠ " + ex.Message, Theme.Danger);
        }
    }

    /// <summary>Prueba la dirección o el patrón contra el juego conectado, sin activar nada.</summary>
    internal void Test()
    {
        if (_memory is not { IsAttached: true } memory)
        {
            ShowResult("Conecta el juego para comprobarlo.", Theme.TextDim);
            return;
        }
        try
        {
            var definition = Build(checkHotkey: false);
            var (text, ok) = definition.Type == CheatType.Patch ? TestPatch(memory, definition) : TestValue(memory, definition);
            ShowResult(text, ok ? Theme.Success : Theme.Danger);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            ShowResult("⚠ " + ex.Message, Theme.Danger);
        }
    }

    static (string Text, bool Ok) TestPatch(IProcessMemory memory, CheatDefinition definition)
    {
        long found = PatternScanner.Find(memory, BytePattern.Parse(definition.Pattern!), definition.Module);
        if (found == 0) return ("⚠ Patrón no encontrado en el juego.", false);
        long address = found + definition.PatternOffset;
        int length = NumberText.ParseHexBytes(definition.Bytes).Length;
        string current = memory.Read(address, length) is { } bytes ? Convert.ToHexString(bytes) : "??";
        return ($"✓ Encontrado en 0x{address:X}. Bytes actuales: {current}", true);
    }

    static (string Text, bool Ok) TestValue(IProcessMemory memory, CheatDefinition definition)
    {
        long address = new Cheat(definition).ResolveAddress(memory);
        if (address == 0 || memory.ReadValue(address, definition.ValueType) is not { } value)
            return ("⚠ La dirección no es válida o el puntero está roto.", false);
        return ($"✓ Apunta a 0x{address:X}, que ahora vale {definition.ValueType.Format(value)}.", true);
    }
}
