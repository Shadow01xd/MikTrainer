using MiTrainer.Core.Memory;
using MiTrainer.Core.Pointers;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

public class PointerScannerTests
{
    static readonly ModuleInfo Exe = new("juego.exe", 0x1_4000_0000, 0x1_0000);

    [Fact]
    public void Encuentra_la_cadena_desde_una_direccion_estatica()
    {
        // "juego.exe"+2000 → [A]+18 → [B]+10 = objetivo, con algo de ruido alrededor
        long a = 0x2_0000_0000, b = 0x3_0000_0000, target = b + 0x10;
        var map = PointerMap.FromEntries(
            [(Exe.BaseAddress + 0x2000, a), (a + 0x18, b), (0x5_0000_0000, target + 0x5000), (0x5_0000_0008, a)],
            [Exe]);

        var paths = PointerScanner.Scan(map, target, maxDepth: 3, maxOffset: 0x100);

        Assert.Contains(new PointerPath("juego.exe", 0x2000, [0x18, 0x10]), paths);
    }

    [Fact]
    public void Respeta_la_profundidad_maxima()
    {
        long a = 0x2_0000_0000, target = 0x3_0000_0010;
        var map = PointerMap.FromEntries([(Exe.BaseAddress + 0x2000, a), (a + 0x18, 0x3_0000_0000)], [Exe]);

        Assert.Empty(PointerScanner.Scan(map, target, maxDepth: 1, maxOffset: 0x100));
    }

    [Fact]
    public void Respeta_el_offset_maximo()
    {
        long target = 0x3_0000_0800;
        var map = PointerMap.FromEntries([(Exe.BaseAddress + 0x100, 0x3_0000_0000)], [Exe]);

        Assert.Empty(PointerScanner.Scan(map, target, maxDepth: 2, maxOffset: 0x400));
        Assert.Single(PointerScanner.Scan(map, target, maxDepth: 2, maxOffset: 0x1000));
    }

    [Fact]
    public void Construye_el_mapa_desde_la_memoria_y_filtra_tras_un_reinicio()
    {
        const long Heap = 0x50_0000;
        var game = new FakeProcessMemory();
        var exe = game.AddModule("juego.exe", 0x40_0000, 0x1000);
        game.AddRegion(Heap, 0x1000);
        long block = Heap + 0x100, life = Heap + 0x400;
        game.SetInt64(exe.BaseAddress + 0x80, block);   // estático → bloque
        game.SetInt64(block + 0x18, life - 0x10);       // bloque+18 → (vida - 10)

        var paths = PointerScanner.Scan(PointerMap.Build(game), life, maxDepth: 3, maxOffset: 0x100);
        var good = new PointerPath("juego.exe", 0x80, [0x18, 0x10]);
        Assert.Contains(good, paths);

        // "Reinicio": la vida se muda y solo la cadena buena la sigue
        long newLife = Heap + 0x800;
        game.SetInt64(block + 0x18, newLife - 0x10);
        var kept = PointerScanner.Filter(game, paths, address => address == newLife);

        Assert.Equal([good], kept);
    }

    [Fact]
    public void El_filtro_se_puede_cancelar()
    {
        var game = new FakeProcessMemory();
        game.AddModule("juego.exe", 0x40_0000, 0x1000);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PointerScanner.Filter(game, [new PointerPath("juego.exe", 0x80, [0x10])], _ => true, cancel.Token));
    }

    [Fact]
    public void La_forma_compacta_vuelve_identica()
    {
        var path = new PointerPath("motor.dll", 0x1A2B, [0x18, 0x10, 0x8]);

        Assert.Equal("motor.dll|1A2B|18|10|8", path.ToCompactString());
        Assert.Equal(path, PointerPath.ParseCompact(path.ToCompactString()));
    }
}
