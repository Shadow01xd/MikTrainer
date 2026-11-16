using MiTrainer.Core.Memory;

namespace MiTrainer.TestSupport;

/// <summary>
/// Memoria de un proceso inventado: bloques de bytes en direcciones que elegimos. Permite probar escáneres,
/// punteros y cheats sin ningún juego real.
/// </summary>
public sealed class FakeProcessMemory : IProcessMemory
{
    readonly List<Block> _blocks = [];
    readonly List<ModuleInfo> _modules = [];

    sealed record Block(long Start, byte[] Data, bool Writable, bool Executable)
    {
        public long End => Start + Data.Length;
    }

    public int ProcessId { get; init; } = 1234;

    /// <summary>Hora de inicio inventada; dos memorias con el mismo PID y distinta hora son procesos distintos.</summary>
    public DateTime StartTime { get; init; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public ProcessIdentity Identity => new(ProcessId, StartTime);
    public string ProcessName { get; init; } = "juego";
    public bool IsAttached { get; set; } = true;
    public bool Is32Bit { get; init; }
    public bool Disposed { get; private set; }

    /// <summary>Simula que Windows rechaza las escrituras (memoria protegida, proceso bloqueado...).</summary>
    public bool FailWrites { get; set; }

    /// <summary>Se llama antes de cada lectura (desde el hilo que lee): sirve para pausar un escaneo a mitad.</summary>
    public Action? BeforeRead { get; set; }

    /// <summary>Se llama antes de consultar los módulos (desde el hilo que consulta).</summary>
    public Action? BeforeGetModules { get; set; }

    /// <summary>
    /// Simula una implementación cuya consulta de módulos lanza al cerrarse el proceso
    /// (como <c>Process.Modules</c> en Windows).
    /// </summary>
    public bool ModulesThrow { get; set; }

    /// <summary>Añade una región de memoria rellena de ceros.</summary>
    public FakeProcessMemory AddRegion(long start, int size, bool writable = true, bool executable = false)
    {
        _blocks.Add(new Block(start, new byte[size], writable, executable));
        _blocks.Sort((a, b) => a.Start.CompareTo(b.Start));
        return this;
    }

    /// <summary>Añade un módulo (exe/dll) con su propia región. El primero que se añade es el ejecutable principal.</summary>
    public ModuleInfo AddModule(string name, long baseAddress, int size, bool executable = true)
    {
        AddRegion(baseAddress, size, writable: true, executable);
        var module = new ModuleInfo(name, baseAddress, size);
        _modules.Add(module);
        return module;
    }

    public void SetBytes(long address, params byte[] bytes) => Copy(address, bytes);
    public void SetInt32(long address, int value) => Copy(address, BitConverter.GetBytes(value));
    public void SetInt64(long address, long value) => Copy(address, BitConverter.GetBytes(value));
    public void SetFloat(long address, float value) => Copy(address, BitConverter.GetBytes(value));
    public int GetInt32(long address) => BitConverter.ToInt32(Get(address, 4));
    public byte[] Get(long address, int count) => Find(address, count) is { } block ? block.Data[(int)(address - block.Start)..][..count] : throw new ArgumentOutOfRangeException(nameof(address));

    void Copy(long address, byte[] bytes)
    {
        var block = Find(address, bytes.Length) ?? throw new ArgumentOutOfRangeException(nameof(address), $"0x{address:X} no está en ninguna región");
        bytes.CopyTo(block.Data, address - block.Start);
    }

    Block? Find(long address, int count) => _blocks.FirstOrDefault(b => address >= b.Start && address + count <= b.End);

    public bool TryRead(long address, byte[] buffer, int count)
    {
        BeforeRead?.Invoke();
        if (!IsAttached || Find(address, count) is not { } block) return false;
        Array.Copy(block.Data, address - block.Start, buffer, 0, count);
        return true;
    }

    public bool Write(long address, byte[] data)
    {
        if (!IsAttached || FailWrites || Find(address, data.Length) is null) return false;
        Copy(address, data);
        return true;
    }

    public IReadOnlyList<MemoryRegion> GetRegions() =>
        [.. _blocks.Select(b => new MemoryRegion(b.Start, b.Data.Length, b.Writable, b.Executable))];

    public IReadOnlyList<ModuleInfo> GetModules()
    {
        BeforeGetModules?.Invoke();
        return ModulesThrow ? throw new InvalidOperationException("El proceso ya no existe") : _modules;
    }

    public void Dispose() => Disposed = true;
}
