using MiTrainer.Core.Memory;

namespace MiTrainer.Core.Trainer;

public sealed record ProcessInfo(int Id, string Name)
{
    public override string ToString() => $"{Name}.exe  ({Id})";
}

/// <summary>Encuentra procesos y abre su memoria. La implementación real usa la API de Windows.</summary>
public interface IProcessConnector
{
    /// <summary>Procesos de la sesión del usuario, ordenados por nombre.</summary>
    IReadOnlyList<ProcessInfo> ListProcesses();

    /// <summary>Id del primer proceso con ese ejecutable ("juego.exe" o "juego"), o null si no está abierto.</summary>
    int? FindProcess(string executableName);

    /// <summary>Abre la memoria del proceso. Lanza <see cref="InvalidOperationException"/> si no se puede.</summary>
    IProcessMemory Open(int processId);
}
