using MiTrainer.Core.Memory;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

public class PatternScannerTests
{
    const long Exe = 0x14000_0000, Dll = 0x7FF0_0000;
    readonly FakeProcessMemory _game = new();

    public PatternScannerTests()
    {
        _game.AddModule("juego.exe", Exe, 0x2000);
        _game.AddModule("motor.dll", Dll, 0x2000);
        _game.SetBytes(Exe + 0x500, 0x48, 0x8B, 0x05, 0x10, 0x89, 0x4B);
        _game.SetBytes(Dll + 0x700, 0x48, 0x8B, 0x05, 0x10, 0x89, 0x4B);
    }

    [Fact]
    public void Encuentra_el_patron_con_comodines_en_el_exe_principal()
    {
        long found = PatternScanner.Find(_game, BytePattern.Parse("48 8B ?? 10 89"));

        Assert.Equal(Exe + 0x500, found);
    }

    [Fact]
    public void Busca_solo_en_el_modulo_indicado()
    {
        long found = PatternScanner.Find(_game, BytePattern.Parse("48 8B ?? 10 89"), "motor.dll");

        Assert.Equal(Dll + 0x700, found);
    }

    [Fact]
    public void Devuelve_cero_si_no_esta()
    {
        Assert.Equal(0, PatternScanner.Find(_game, BytePattern.Parse("DE AD BE EF")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("48 ZZ")]
    [InlineData("123")]
    public void Un_patron_mal_escrito_da_FormatException(string text)
    {
        Assert.Throws<FormatException>(() => BytePattern.Parse(text));
    }

    [Fact]
    public void Un_modulo_que_no_existe_da_InvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => PatternScanner.Find(_game, BytePattern.Parse("48"), "otro.dll"));
    }
}
