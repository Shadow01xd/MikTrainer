using MiTrainer.Core.Scanning;
using MiTrainer.Core.Values;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

public class ValueScannerTests
{
    const long Heap = 0x10_0000;
    readonly FakeProcessMemory _game = new FakeProcessMemory().AddRegion(Heap, 0x1000);
    readonly ValueScanner _scanner = new();

    [Fact]
    public void Primer_escaneo_encuentra_todas_las_direcciones_con_el_valor()
    {
        _game.SetInt32(Heap + 0x10, 100);
        _game.SetInt32(Heap + 0x80, 100);
        _game.SetInt32(Heap + 0x90, 99);

        _scanner.FirstScan(_game, ValueKind.Int32, 100);

        Assert.Equal([Heap + 0x10, Heap + 0x80], _scanner.Addresses);
    }

    [Fact]
    public void Siguiente_escaneo_descarta_las_que_no_cambiaron_como_el_juego()
    {
        long vida = Heap + 0x10, otro = Heap + 0x80;
        _game.SetInt32(vida, 100);
        _game.SetInt32(otro, 100);
        _scanner.FirstScan(_game, ValueKind.Int32, 100);

        _game.SetInt32(vida, 93); // recibimos daño
        _scanner.NextScan(_game, ScanMode.Decreased, 0);

        Assert.Equal([vida], _scanner.Addresses);
        Assert.Equal(93, _scanner.Values[0]);
    }

    [Theory]
    [InlineData(ScanMode.Exact, 50, true)]
    [InlineData(ScanMode.Increased, 0, true)]
    [InlineData(ScanMode.Decreased, 0, false)]
    [InlineData(ScanMode.Changed, 0, true)]
    [InlineData(ScanMode.Unchanged, 0, false)]
    public void Cada_modo_compara_con_el_valor_anterior(ScanMode mode, double target, bool kept)
    {
        _game.SetInt32(Heap, 10);
        _scanner.FirstScan(_game, ValueKind.Int32, 10);
        _game.SetInt32(Heap, 50);

        _scanner.NextScan(_game, mode, target);

        Assert.Equal(kept, _scanner.Addresses.Contains(Heap));
    }

    [Fact]
    public void Los_decimales_se_comparan_redondeando()
    {
        _game.SetFloat(Heap + 4, 99.7f); // en el juego se ve "100"

        _scanner.FirstScan(_game, ValueKind.Float, 100);

        Assert.Contains(Heap + 4, _scanner.Addresses);
    }

    [Fact]
    public void Solo_busca_en_memoria_escribible()
    {
        _game.AddRegion(0x20_0000, 0x100, writable: false);
        _game.SetInt32(0x20_0000, 777);

        _scanner.FirstScan(_game, ValueKind.Int32, 777);

        Assert.Empty(_scanner.Addresses);
    }
}
