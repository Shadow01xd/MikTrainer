using MiTrainer.App.Dialogs;
using MiTrainer.Core.Cheats;
using MiTrainer.Core.Values;
using MiTrainer.TestSupport;

namespace MiTrainer.App.Tests;

public class CheatEditorTests
{
    const long Exe = 0x40_0000, Heap = 0x50_0000;
    readonly FakeProcessMemory _game = new();

    public CheatEditorTests()
    {
        _game.AddModule("juego.exe", Exe, 0x1000);
        _game.AddRegion(Heap, 0x1000);
        _game.SetInt32(Heap + 4, 50); // el oro del juego
        _game.SetBytes(Exe + 0x10, 0x29, 0x4B, 0x10);
    }

    static CheatEditor Editor(CheatDefinition definition, IEnumerable<string>? used = null, FakeProcessMemory? game = null)
    {
        var editor = new CheatEditor(definition, isNew: true, game, used ?? []);
        editor.CreateControl();
        return editor;
    }

    [Fact]
    public void Comprobar_lee_el_valor_actual_y_Guardar_devuelve_el_cheat() => Sta.Run(() =>
    {
        var gold = new CheatDefinition { Name = "Oro infinito", Address = $"{Heap + 4:X}", Value = 99999, Hotkey = "F2" };
        using var editor = Editor(gold, game: _game);

        editor.Test();
        Assert.Contains("vale 50", editor.ResultText);

        editor.Save();
        Assert.Equal(gold with { Offsets = null }, editor.Result);
    });

    [Fact]
    public void Comprobar_un_parche_dice_donde_esta_y_que_bytes_hay() => Sta.Run(() =>
    {
        using var editor = Editor(new CheatDefinition { Name = "Parche", Type = CheatType.Patch, Pattern = "29 4B ??", Bytes = "90 90" }, game: _game);

        editor.Test();

        Assert.Contains($"Encontrado en 0x{Exe + 0x10:X}", editor.ResultText);
        Assert.Contains("294B", editor.ResultText);
    });

    [Theory]
    [InlineData("", "10", "F5", "nombre")]
    [InlineData("Malo", "XYZ", "F5", "Dirección")]
    [InlineData("Repetido", "10", "F1", "ya lo usa")]
    public void Explica_por_que_no_se_puede_guardar(string name, string address, string hotkey, string message) => Sta.Run(() =>
    {
        using var editor = Editor(new CheatDefinition { Name = name, Address = address, Hotkey = hotkey }, used: ["F1"]);

        editor.Save();

        Assert.Null(editor.Result);
        Assert.Contains(message, editor.ResultText);
    });

    [Fact]
    public void Sin_juego_conectado_lo_dice_en_vez_de_fallar() => Sta.Run(() =>
    {
        using var editor = Editor(new CheatDefinition { Name = "Vida", Address = "10" });

        editor.Test();

        Assert.Contains("Conecta el juego", editor.ResultText);
    });

    [Fact]
    public void Graba_atajos_e_ignora_modificadores_sueltos() => Sta.Run(() =>
    {
        using var editor = Editor(new CheatDefinition { Name = "Atajo" });
        var box = editor.HotkeyInput;

        box.Record(Keys.F5, Keys.Control);
        Assert.Equal("Ctrl+F5", box.Text);

        box.Record(Keys.ShiftKey, Keys.Shift);
        Assert.Equal("Ctrl+F5", box.Text);

        box.Record(Keys.Delete, Keys.None);
        Assert.Equal("", box.Text);
    });

    [Fact]
    public void Los_valores_decimales_se_guardan_con_su_tipo() => Sta.Run(() =>
    {
        using var editor = Editor(new CheatDefinition { Name = "Velocidad", Address = "10", ValueType = ValueKind.Float, Value = 2.5 });

        editor.Save();

        Assert.Equal(ValueKind.Float, editor.Result!.ValueType);
        Assert.Equal(2.5, editor.Result.Value);
    });
}
