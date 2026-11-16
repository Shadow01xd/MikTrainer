using MiTrainer.Core.Values;

namespace MiTrainer.Core.Memory;

/// <summary>Operaciones de alto nivel construidas sobre <see cref="IProcessMemory"/>.</summary>
public static class ProcessMemoryExtensions
{
    public static int PointerSize(this IProcessMemory memory) => memory.Is32Bit ? 4 : 8;

    public static byte[]? Read(this IProcessMemory memory, long address, int size)
    {
        var buffer = new byte[size];
        return memory.TryRead(address, buffer, size) ? buffer : null;
    }

    public static double? ReadValue(this IProcessMemory memory, long address, ValueKind kind) =>
        memory.Read(address, kind.Size()) is { } bytes ? kind.FromBytes(bytes) : null;

    public static bool WriteValue(this IProcessMemory memory, long address, ValueKind kind, double value) =>
        memory.Write(address, kind.ToBytes(value));

    /// <summary>Lee un puntero (4 u 8 bytes según el juego). Devuelve 0 si no se puede.</summary>
    public static long ReadPointer(this IProcessMemory memory, long address)
    {
        if (memory.Read(address, memory.PointerSize()) is not { } bytes) return 0;
        return memory.Is32Bit ? BitConverter.ToUInt32(bytes) : BitConverter.ToInt64(bytes);
    }

    /// <summary>
    /// Cadena de punteros al estilo Cheat Engine: [[base]+off1]+off2 ...
    /// En cada paso se lee el puntero y se le suma el siguiente offset. Devuelve 0 si la cadena se rompe.
    /// </summary>
    public static long ResolvePointer(this IProcessMemory memory, long baseAddress, IEnumerable<long> offsets)
    {
        long address = baseAddress;
        foreach (long offset in offsets)
        {
            address = memory.ReadPointer(address);
            if (address == 0) return 0;
            address += offset;
        }
        return address;
    }

    /// <summary>Módulo por nombre ("juego.exe", "motor.dll"); vacío = el ejecutable principal.</summary>
    public static ModuleInfo FindModule(this IProcessMemory memory, string? name)
    {
        var modules = memory.GetModules();
        if (string.IsNullOrWhiteSpace(name)) return modules.Count > 0 ? modules[0] : throw new InvalidOperationException("El proceso no tiene módulos");
        return modules.FirstOrDefault(m => string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Módulo '{name}' no encontrado");
    }

    /// <summary>Nombre → dirección base de cada módulo (para resolver muchas cadenas de punteros seguidas).</summary>
    public static IReadOnlyDictionary<string, long> ModuleBases(this IProcessMemory memory)
    {
        var bases = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in memory.GetModules()) bases.TryAdd(module.Name, module.BaseAddress);
        return bases;
    }
}
