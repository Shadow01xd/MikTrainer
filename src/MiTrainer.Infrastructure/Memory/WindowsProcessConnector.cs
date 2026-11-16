using System.Diagnostics;
using MiTrainer.Core.Memory;
using MiTrainer.Core.Trainer;

namespace MiTrainer.Infrastructure.Memory;

/// <summary><see cref="IProcessConnector"/> real: procesos de Windows de la sesión del usuario.</summary>
public sealed class WindowsProcessConnector : IProcessConnector
{
    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        int session = Process.GetCurrentProcess().SessionId;
        var processes = Process.GetProcesses();
        try
        {
            return [.. processes
                .Where(p => p.SessionId == session && p.Id != Environment.ProcessId)
                .Select(p => new ProcessInfo(p.Id, p.ProcessName))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    public int? FindProcess(string executableName)
    {
        var matches = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executableName));
        try { return matches.FirstOrDefault()?.Id; }
        finally
        {
            foreach (var p in matches) p.Dispose();
        }
    }

    public IProcessMemory Open(int processId)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { throw new InvalidOperationException("Ese proceso ya no existe"); }
        return WindowsProcessMemory.Open(process);
    }
}
