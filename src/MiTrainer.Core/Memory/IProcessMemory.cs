namespace MiTrainer.Core.Memory;

/// <summary>
/// La memoria de un proceso al que estamos conectados. Es la única puerta entre la lógica del trainer
/// y el sistema operativo: la implementación real vive en Infrastructure y las pruebas usan una falsa.
/// </summary>
public interface IProcessMemory : IDisposable
{
    int ProcessId { get; }

    /// <summary>
    /// Qué proceso concreto es: PID y hora de inicio. El PID solo no basta, porque Windows lo reutiliza
    /// para otro proceso cuando el original termina.
    /// </summary>
    ProcessIdentity Identity { get; }

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

    /// <summary>
    /// Módulos cargados (exe y dlls). El primero es siempre el ejecutable principal.
    /// No lanza: si el proceso ya no existe, devuelve una lista vacía.
    /// </summary>
    IReadOnlyList<ModuleInfo> GetModules();
}

/// <summary>Un proceso concreto: PID más hora de inicio (el PID se reutiliza; la pareja no).</summary>
public readonly record struct ProcessIdentity(int Id, DateTime StartTime)
{
    /// <summary>
    /// ¿Es seguro el mismo proceso? Si no se conoce la hora de inicio no se puede asegurar, y se responde que no:
    /// es preferible no restaurar a escribir en un proceso ajeno.
    /// </summary>
    public bool IsSameProcessAs(ProcessIdentity other) => IsKnown && this == other;

    /// <summary>Se conoce la hora de inicio: el proceso se puede reconocer más tarde sin confundirlo con otro.</summary>
    public bool IsKnown => StartTime != default;
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
