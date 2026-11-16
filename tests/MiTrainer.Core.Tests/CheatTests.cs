using MiTrainer.Core.Cheats;
using MiTrainer.Core.Values;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

public class CheatTests
{
    const long Exe = 0x40_0000, Heap = 0x50_0000;
    readonly FakeProcessMemory _game = new();

    public CheatTests()
    {
        _game.AddModule("juego.exe", Exe, 0x1000);
        _game.AddRegion(Heap, 0x1000);
    }

    [Fact]
    public void Congelar_escribe_el_valor_y_lo_mantiene_en_cada_tick()
    {
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Freeze, Address = $"{Heap:X}", Value = 100 });

        Assert.True(cheat.Enable(_game));
        _game.SetInt32(Heap, 93); // el juego resta vida
        cheat.Tick(_game);

        Assert.True(cheat.Enabled);
        Assert.Equal(100, _game.GetInt32(Heap));
    }

    [Fact]
    public void Fijar_escribe_una_vez_y_no_queda_activado()
    {
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Set, Address = $"{Heap:X}", Value = 99999 });

        Assert.True(cheat.Enable(_game));

        Assert.False(cheat.Enabled);
        Assert.Equal(99999, _game.GetInt32(Heap));
    }

    [Fact]
    public void Un_parche_se_deshace_al_desactivarlo()
    {
        _game.SetBytes(Exe + 0x200, 0x29, 0x4B, 0x10, 0xC3);
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Patch, Pattern = "29 4B ?? C3", Bytes = "90 90 90" });

        Assert.True(cheat.Enable(_game));
        Assert.Equal([0x90, 0x90, 0x90, 0xC3], _game.Get(Exe + 0x200, 4));

        cheat.Disable(_game);
        Assert.Equal([0x29, 0x4B, 0x10, 0xC3], _game.Get(Exe + 0x200, 4));
    }

    [Fact]
    public void Sigue_modulo_mas_direccion_y_cadena_de_punteros()
    {
        _game.SetInt64(Exe + 0x80, Heap + 0x100);
        _game.SetInt64(Heap + 0x118, Heap + 0x300);
        var cheat = new Cheat(new CheatDefinition { Module = "juego.exe", Address = "80", Offsets = ["18", "10"] });

        Assert.Equal(Heap + 0x310, cheat.ResolveAddress(_game));
    }

    [Fact]
    public void Un_puntero_roto_falla_con_un_mensaje_en_vez_de_lanzar()
    {
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Freeze, Address = "10", Offsets = ["8"], Value = 1 });

        Assert.False(cheat.Enable(_game));
        Assert.True(cheat.Failed);
        Assert.Contains("puntero roto", cheat.Status);
    }

    [Fact]
    public void Un_patron_que_no_esta_avisa_de_otra_version_del_juego()
    {
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Patch, Pattern = "DE AD BE EF", Bytes = "90" });

        Assert.False(cheat.Enable(_game));
        Assert.Contains("otra versión", cheat.Status);
    }

    [Fact]
    public void Funciona_con_valores_decimales()
    {
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Set, Address = $"{Heap:X}", ValueType = ValueKind.Float, Value = 5.5 });

        cheat.Enable(_game);

        Assert.Equal(5.5f, BitConverter.ToSingle(_game.Get(Heap, 4)));
    }
}
