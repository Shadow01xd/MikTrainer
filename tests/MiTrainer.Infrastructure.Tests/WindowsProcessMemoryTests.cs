using MiTrainer.Core.Cheats;
using MiTrainer.Core.Memory;
using MiTrainer.Core.Pointers;
using MiTrainer.Core.Scanning;
using MiTrainer.Core.Values;

namespace MiTrainer.Infrastructure.Tests;

/// <summary>De punta a punta contra un proceso real: el juego de prueba.</summary>
public sealed class WindowsProcessMemoryTests : IDisposable
{
    readonly TestGame _game = new();

    public void Dispose() => _game.Dispose();

    [Fact]
    public void El_escaner_encuentra_la_vida_real_del_juego()
    {
        var scan = ValueScanner.First(_game.Memory, ValueKind.Int32, _game.Life);
        for (int i = 0; i < 6 && scan.Count > 1; i++) scan = ValueScanner.Next(scan, _game.Memory, ScanMode.Exact, _game.TakeDamage());

        Assert.Contains(_game.LifeAddress, scan.Addresses);
    }

    [Fact]
    public void Un_cheat_congelado_aguanta_el_dano()
    {
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Freeze, Address = NumberText.ToHex(_game.LifeAddress), Value = 500 });

        Assert.True(cheat.Enable(_game.Memory), cheat.Status);
        _game.TakeDamage();
        cheat.Tick(_game.Memory);

        Assert.Equal(500, _game.ReadLife());
    }

    [Fact]
    public void Un_parche_en_memoria_de_solo_lectura_se_aplica_y_se_revierte()
    {
        // La cabecera del .exe ("MZ") está en memoria de solo lectura: hay que quitar la protección para escribir.
        long exe = _game.Memory.FindModule(null).BaseAddress;
        var cheat = new Cheat(new CheatDefinition { Type = CheatType.Patch, Pattern = "4D 5A ?? 00", Bytes = "AA BB" });

        Assert.True(cheat.Enable(_game.Memory), cheat.Status);
        Assert.Equal([0xAA, 0xBB], _game.Memory.Read(exe, 2));

        cheat.Disable(_game.Memory);
        Assert.Equal([0x4D, 0x5A], _game.Memory.Read(exe, 2));
    }

    [Fact]
    public void Encuentra_una_cadena_estatica_y_la_conserva_al_filtrar()
    {
        // .NET no guarda sus variables dentro del .exe, así que fabricamos una cadena como la de un juego en C++:
        // hueco en la sección de datos del exe → bloque de memoria → +18 → (vida - 10)
        var memory = _game.Memory;
        var exe = memory.FindModule(null);
        long slot = FreeStaticSlot(memory, exe);
        long block = memory.Allocate(0x100);
        long life = _game.LifeAddress;
        try
        {
            memory.Write(block + 0x18, BitConverter.GetBytes(life - 0x10));
            memory.Write(slot, BitConverter.GetBytes(block));

            var paths = PointerScanner.Scan(PointerMap.Build(memory), life, maxDepth: 3, maxOffset: 0x400);
            var good = new PointerPath(exe.Name, slot - exe.BaseAddress, [0x18, 0x10]);
            Assert.Contains(good, paths);

            long newLife = memory.Allocate(0x100) + 0x40; // "reinicio": la vida se muda
            memory.Write(block + 0x18, BitConverter.GetBytes(newLife - 0x10));
            Assert.Contains(good, PointerScanner.Filter(memory, paths, address => address == newLife));
        }
        finally
        {
            memory.Write(slot, new byte[8]); // dejamos el exe como estaba
        }
    }

    [Fact]
    public void Lista_modulos_con_el_exe_primero_y_regiones_ordenadas()
    {
        var modules = _game.Memory.GetModules();
        var regions = _game.Memory.GetRegions();

        Assert.Equal("JuegoPrueba.exe", modules[0].Name);
        Assert.Contains(modules, m => m.Name.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
        Assert.True(regions.Zip(regions.Skip(1)).All(pair => pair.First.Start < pair.Second.Start));
    }

    [Fact]
    public void La_identidad_incluye_la_hora_de_inicio_del_proceso()
    {
        var identity = _game.Memory.Identity;

        Assert.Equal(_game.Memory.ProcessId, identity.Id);
        Assert.NotEqual(default, identity.StartTime);
        Assert.True(identity.IsSameProcessAs(identity));
    }

    [Fact]
    public void Con_el_juego_cerrado_los_modulos_vienen_vacios_en_vez_de_lanzar()
    {
        _game.Exit();

        Assert.False(_game.Memory.IsAttached);
        Assert.Empty(_game.Memory.GetModules());
    }

    /// <summary>8 bytes a cero al final de una sección escribible del exe (sitio libre para un puntero "estático").</summary>
    static long FreeStaticSlot(IProcessMemory memory, ModuleInfo exe)
    {
        foreach (var region in memory.GetRegions().Where(r => r.Writable && exe.Contains(r.Start)))
            for (long p = region.End - 8; p >= region.Start; p -= 8)
                if (memory.Read(p, 8) is { } bytes && BitConverter.ToInt64(bytes) == 0) return p;
        throw new InvalidOperationException("No hay hueco libre en el exe");
    }
}
