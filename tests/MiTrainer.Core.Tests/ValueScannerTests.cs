using MiTrainer.Core.Scanning;
using MiTrainer.Core.Values;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

public class ValueScannerTests
{
    const long Heap = 0x10_0000;
    readonly FakeProcessMemory _game = new FakeProcessMemory().AddRegion(Heap, 0x1000);

    [Fact]
    public void Primer_escaneo_encuentra_todas_las_direcciones_con_el_valor()
    {
        _game.SetInt32(Heap + 0x10, 100);
        _game.SetInt32(Heap + 0x80, 100);
        _game.SetInt32(Heap + 0x90, 99);

        var scan = ValueScanner.First(_game, ValueKind.Int32, 100);

        Assert.Equal([Heap + 0x10, Heap + 0x80], scan.Addresses);
    }

    [Fact]
    public void Siguiente_escaneo_descarta_las_que_no_cambiaron_como_el_juego()
    {
        long vida = Heap + 0x10, otro = Heap + 0x80;
        _game.SetInt32(vida, 100);
        _game.SetInt32(otro, 100);
        var first = ValueScanner.First(_game, ValueKind.Int32, 100);

        _game.SetInt32(vida, 93); // recibimos daño
        var next = ValueScanner.Next(first, _game, ScanMode.Decreased, 0);

        Assert.Equal([vida], next.Addresses);
        Assert.Equal(93, next.Values[0]);
        Assert.Equal(2, first.Count); // el escaneo anterior no cambia
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
        var first = ValueScanner.First(_game, ValueKind.Int32, 10);
        _game.SetInt32(Heap, 50);

        var next = ValueScanner.Next(first, _game, mode, target);

        Assert.Equal(kept, next.Addresses.Contains(Heap));
    }

    [Fact]
    public void Los_decimales_se_comparan_redondeando()
    {
        _game.SetFloat(Heap + 4, 99.7f); // en el juego se ve "100"

        var scan = ValueScanner.First(_game, ValueKind.Float, 100);

        Assert.Contains(Heap + 4, scan.Addresses);
    }

    [Fact]
    public void Solo_busca_en_memoria_escribible()
    {
        _game.AddRegion(0x20_0000, 0x100, writable: false);
        _game.SetInt32(0x20_0000, 777);

        var scan = ValueScanner.First(_game, ValueKind.Int32, 777);

        Assert.Empty(scan.Addresses);
    }
}
