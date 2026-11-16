using MiTrainer.Core.Memory;
using MiTrainer.Core.Trainer;

namespace MiTrainer.TestSupport;

/// <summary>Conector falso: los "procesos" son <see cref="FakeProcessMemory"/> que la prueba abre y cierra a voluntad.</summary>
public sealed class FakeProcessConnector : IProcessConnector
{
    readonly Dictionary<int, FakeProcessMemory> _running = [];

    /// <summary>Simula que el juego se abre.</summary>
    public FakeProcessMemory Start(FakeProcessMemory process)
    {
        _running[process.ProcessId] = process;
        return process;
    }

    /// <summary>Simula que el juego se cierra.</summary>
    public void Exit(FakeProcessMemory process)
    {
        process.IsAttached = false;
        _running.Remove(process.ProcessId);
    }

    public IReadOnlyList<ProcessInfo> ListProcesses() => [.. _running.Values.Select(p => new ProcessInfo(p.ProcessId, p.ProcessName))];

    public int? FindProcess(string executableName) =>
        _running.Values.FirstOrDefault(p => string.Equals(p.ProcessName, Path.GetFileNameWithoutExtension(executableName), StringComparison.OrdinalIgnoreCase))?.ProcessId;

    public IProcessMemory Open(int processId) =>
        _running.TryGetValue(processId, out var process) ? process : throw new InvalidOperationException("Ese proceso ya no existe");
}
