namespace MiTrainer.Core.Memory;

/// <summary>
/// La memoria de un proceso al que estamos conectados. Es la única puerta entre la lógica del trainer
/// y el sistema operativo: la implementación real vive en Infrastructure y las pruebas usan una falsa.
/// </summary>
public interface IProcessMemory : IDisposable
{
    int ProcessId { get; }

    /// <summary>Nombre del proceso sin extensión ("juego").</summary>
    string ProcessName { get; }

    /// <summary>false cuando el juego se ha cerrado.</summary>
    bool IsAttached { get; }

    bool Is32Bit { get; }

    /// <summary>Lee <paramref name="count"/> bytes; false si alguno no se puede leer.</summary>
    bool TryRead(long address, byte[] buffer, int count);

    /// <summary>Escribe los bytes (quitando la protección de solo lectura si hace falta).</summary>
    bool Write(long address, byte[] data);

    /// <summary>Regiones de memoria reservadas y legibles, ordenadas por dirección.</summary>
    IReadOnlyList<MemoryRegion> GetRegions();

    /// <summary>Módulos cargados (exe y dlls). El primero es siempre el ejecutable principal.</summary>
    IReadOnlyList<ModuleInfo> GetModules();
}

public readonly record struct MemoryRegion(long Start, long Size, bool Writable, bool Executable)
{
    public long End => Start + Size;
}

public sealed record ModuleInfo(string Name, long BaseAddress, long Size)
{
    public long End => BaseAddress + Size;
    public bool Contains(long address) => address >= BaseAddress && address < End;
}
