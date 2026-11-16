using MiTrainer.Core;

namespace MiTrainer.UI;

/// <summary>Ventana para crear o editar un cheat sin tocar el JSON.</summary>
sealed class CheatEditor : Form
{
    static readonly (string Type, string Text)[] Types =
    [
        (CheatTypes.Freeze, "Congelar un valor (vida, munición…)"),
        (CheatTypes.Set, "Fijar un valor una vez (dinero, nivel…)"),
        (CheatTypes.Patch, "Parche de código (NOP, saltos…)"),
    ];

    readonly MemoryEngine _mem;
    readonly HashSet<string> _usedHotkeys;
    readonly TableLayoutPanel _grid = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2,
    };

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
    readonly TextBox _patternOffset = Input(new TextBox { Text = "0" });
    readonly TextBox _bytes = Input(new TextBox());
    readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(460, 0), Font = Theme.Small, ForeColor = Theme.TextDim, Margin = new Padding(4, 10, 4, 4) };
    readonly List<Control> _valueRows = [];
    readonly List<Control> _patchRows = [];

    public CheatEditor(CheatDef? def, MemoryEngine mem, IEnumerable<string> usedHotkeys)
    {
        _mem = mem;
        _usedHotkeys = usedHotkeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Text = def == null ? "Nuevo cheat" : "Editar cheat";
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

        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _type.Items.AddRange(Types.Select(t => (object)t.Text).ToArray());
        _type.SelectedIndexChanged += (_, _) => UpdateVisibility();
        _valueType.Items.AddRange(["int", "long", "float", "double"]);
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

        var test = new PillButton("Comprobar en el juego");
        test.Click += (_, _) => Test();
        var save = new PillButton("Guardar", primary: true);
        save.Click += (_, _) => Save();
        var cancel = new PillButton("Cancelar");
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        test.Margin = new Padding(0, 12, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange([cancel, save]);

        // Todo en una columna que crece con su contenido (la ventana se ajusta sola al cambiar de tipo).
        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.AddRange([_grid, test, _result, buttons]);
        Controls.Add(layout);

        Fill(def ?? new CheatDef { Name = "", Type = CheatTypes.Freeze, ValueType = "int" });
    }

    /// <summary>El cheat tal como quedó al pulsar Guardar.</summary>
    public CheatDef? Result { get; private set; }

    // Para las pruebas automáticas
    internal string ResultText => _result.Text;
    internal HotkeyBox HotkeyInput => _hotkey;
    internal ComboBox TypeInput => _type;

    string SelectedType => Types[Math.Max(0, _type.SelectedIndex)].Type;

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
        var caption = Theme.Label(label, Theme.Text);
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
        if (!_mem.IsAttached) return;
        try
        {
            string main = _mem.FindModule(null).ModuleName;
            _module.Items.Add(main);
            foreach (var name in _mem.ModuleBases().Keys.Where(n => n != main).Order(StringComparer.OrdinalIgnoreCase))
                _module.Items.Add(name);
        }
        catch { /* sin lista de módulos: se puede escribir a mano */ }
    }

    void UpdateVisibility()
    {
        bool patch = SelectedType == CheatTypes.Patch;
        SuspendLayout();
        foreach (var c in _valueRows) c.Visible = !patch;
        foreach (var c in _patchRows) c.Visible = patch;
        _moduleHint.Text = patch
            ? "Dónde buscar el patrón. Vacío = el .exe del juego."
            : "Exe o dll donde empieza la dirección. Vacío = dirección absoluta (cambia al reiniciar el juego).";
        ResumeLayout(true);
    }

    void Fill(CheatDef def)
    {
        _name.Text = def.Name;
        _type.SelectedIndex = Math.Max(0, Array.FindIndex(Types, t => t.Type == def.Type));
        _hotkey.Text = def.Hotkey ?? "";
        _module.Text = def.Module ?? "";
        _address.Text = def.Address ?? "";
        _offsets.Text = string.Join(", ", def.Offsets ?? []);
        _valueType.SelectedItem = ValueKinds.Parse(def.ValueType).Name();
        _value.Text = ValueKinds.Parse(def.ValueType).Format(def.Value);
        _pattern.Text = def.Pattern ?? "";
        _patternOffset.Text = def.PatternOffset.ToString();
        _bytes.Text = def.Bytes ?? "";
        UpdateVisibility();
    }

    /// <summary>Lee los campos y los valida. Lanza FormatException con un mensaje que entiende el usuario.</summary>
    CheatDef Build(bool checkHotkey)
    {
        var def = new CheatDef
        {
            Name = _name.Text.Trim(),
            Type = SelectedType,
            Hotkey = string.IsNullOrWhiteSpace(_hotkey.Text) ? null : _hotkey.Text.Trim(),
            Module = string.IsNullOrWhiteSpace(_module.Text) ? null : _module.Text.Trim(),
        };
        if (def.Name.Length == 0) throw new FormatException("Ponle un nombre al cheat.");
        if (checkHotkey && def.Hotkey != null)
        {
            if (!Hotkeys.TryParse(def.Hotkey, out _, out _)) throw new FormatException($"El atajo «{def.Hotkey}» no es válido.");
            if (_usedHotkeys.Contains(def.Hotkey)) throw new FormatException($"El atajo {def.Hotkey} ya lo usa otro cheat.");
        }

        if (def.Type == CheatTypes.Patch)
        {
            try { MemoryEngine.ParsePattern(_pattern.Text); }
            catch { throw new FormatException("Patrón: bytes en hexadecimal separados por espacios (?? = cualquiera)."); }
            if (!int.TryParse(_patternOffset.Text.Trim(), out int offset)) throw new FormatException("Desplazamiento: escribe un número entero.");
            try { Hex.ParseBytes(_bytes.Text); }
            catch { throw new FormatException("Bytes nuevos: bytes en hexadecimal separados por espacios (p. ej. 90 90 90)."); }
            def.Pattern = _pattern.Text.Trim();
            def.PatternOffset = offset;
            def.Bytes = _bytes.Text.Trim();
        }
        else
        {
            ParseHex(_address.Text, "Dirección");
            def.Address = _address.Text.Trim();
            var offsets = _offsets.Text.Split([',', ' ', '→', ';'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var o in offsets) ParseHex(o, "Offsets");
            def.Offsets = offsets.Length > 0 ? [.. offsets] : null;
            def.ValueType = (string)_valueType.SelectedItem!;
            if (!ValueKinds.TryParseNumber(_value.Text, out double value)) throw new FormatException("Valor: escribe un número.");
            def.Value = value;
        }
        return def;
    }

    static void ParseHex(string text, string field)
    {
        try { Hex.ParseNumber(text); }
        catch { throw new FormatException($"{field}: escribe un número en hexadecimal (p. ej. 1A2B)."); }
    }

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
        if (!_mem.IsAttached)
        {
            ShowResult("Conecta el juego para comprobarlo.", Theme.TextDim);
            return;
        }
        try
        {
            var def = Build(checkHotkey: false);
            if (def.Type == CheatTypes.Patch)
            {
                nint at = _mem.AobScan(def.Pattern!, def.Module);
                if (at == 0)
                {
                    ShowResult("⚠ Patrón no encontrado en el juego.", Theme.Danger);
                    return;
                }
                at += def.PatternOffset;
                int length = Hex.ParseBytes(def.Bytes).Length;
                string current = _mem.Read(at, length) is { } bytes ? Convert.ToHexString(bytes) : "??";
                ShowResult($"✓ Encontrado en 0x{at:X}. Bytes actuales: {current}", Theme.Success);
            }
            else
            {
                nint address = new Cheat(def).ResolveAddress(_mem);
                var kind = ValueKinds.Parse(def.ValueType);
                if (address == 0 || _mem.ReadValue(address, kind) is not { } value)
                {
                    ShowResult("⚠ La dirección no es válida o el puntero está roto.", Theme.Danger);
                    return;
                }
                ShowResult($"✓ Apunta a 0x{address:X}, que ahora vale {kind.Format(value)}.", Theme.Success);
            }
        }
        catch (FormatException ex)
        {
            ShowResult("⚠ " + ex.Message, Theme.Danger);
        }
        catch (Exception ex)
        {
            ShowResult("⚠ " + ex.Message, Theme.Danger);
        }
    }
}
