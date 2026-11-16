using MiTrainer.Core.Memory;

namespace MiTrainer.Core.Cheats;

/// <summary>
/// Todo lo necesario para deshacer un parche: en qué proceso, en qué dirección y qué bytes había antes.
/// Se conserva mientras el parche siga aplicado y no se haya podido restaurar.
/// </summary>
public sealed record PendingRestore(string CheatName, int ProcessId, string ProcessName, long Address, byte[] OriginalBytes)
{
    /// <summary>"0x7FF6A15B0010 ← 29 4B 10 (juego.exe, PID 1234)": para que el usuario pueda restaurarlo a mano.</summary>
    public string Describe() =>
        $"0x{Address:X} ← {string.Join(' ', OriginalBytes.Select(b => b.ToString("X2")))} ({ProcessName}.exe, PID {ProcessId})";

    /// <summary>Escribe los bytes originales. Solo en el mismo proceso en el que se aplicó el parche.</summary>
    public bool TryRestore(IProcessMemory memory) =>
        memory.IsAttached && memory.ProcessId == ProcessId && memory.Write(Address, OriginalBytes);
}
