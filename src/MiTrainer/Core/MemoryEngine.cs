using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static MiTrainer.Core.Native;

namespace MiTrainer.Core;

public readonly record struct MemoryRegion(long Start, long Size, uint Protect)
{
    public bool Writable => (Protect & (PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
    public bool Executable => (Protect & PAGE_EXECUTE_ANY) != 0;
}

/// <summary>Se conecta a un proceso y lee/escribe su memoria.</summary>
public sealed class MemoryEngine : IDisposable
{
    nint _handle;

    public Process? Process { get; private set; }
    public bool Is32Bit { get; private set; }
    public int PointerSize => Is32Bit ? 4 : 8;

    public bool IsAttached
    {
        get
        {
            try { return _handle != 0 && Process is { HasExited: false }; }
            catch { return false; }
        }
    }

    public void Attach(Process process)
    {
        Detach();
        nint handle = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, process.Id);
        if (handle == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo abrir el proceso (prueba a ejecutar como administrador)");
        _handle = handle;
        Process = process;
        Is32Bit = IsWow64Process(handle, out bool wow64) && wow64;
    }

    public void Detach()
    {
        if (_handle != 0) CloseHandle(_handle);
        _handle = 0;
        Process = null;
    }

    public void Dispose() => Detach();

    // ---------- Lectura y escritura ----------

    public byte[]? Read(nint address, int size)
    {
        var buffer = new byte[size];
        return ReadInto(address, buffer, size) ? buffer : null;
    }

    public bool ReadInto(nint address, byte[] buffer, int size) =>
        ReadProcessMemory(_handle, address, buffer, size, out nint read) && read == size;

    public bool Write(nint address, byte[] data)
    {
        if (WriteProcessMemory(_handle, address, data, data.Length, out _)) return true;

        // Memoria de solo lectura (p. ej. código): quitamos la protección un momento.
        if (!VirtualProtectEx(_handle, address, data.Length, PAGE_EXECUTE_READWRITE, out uint old)) return false;
        bool ok = WriteProcessMemory(_handle, address, data, data.Length, out _);
        VirtualProtectEx(_handle, address, data.Length, old, out _);
        FlushInstructionCache(_handle, address, data.Length);
        return ok;
    }

    /// <summary>Reserva memoria nueva dentro del juego (para inyectar código o datos). Devuelve 0 si falla.</summary>
    public nint Allocate(int size, bool executable = false) =>
        VirtualAllocEx(_handle, 0, size, MEM_COMMIT | MEM_RESERVE, executable ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE);

    public double? ReadValue(nint address, ValueKind kind) =>
        Read(address, kind.Size()) is { } bytes ? kind.FromBytes(bytes) : null;

    public bool WriteValue(nint address, ValueKind kind, double value) => Write(address, kind.ToBytes(value));

    // ---------- Módulos y punteros ----------

    /// <summary>Módulo por nombre ("juego.exe", "engine.dll"); vacío = el ejecutable principal.</summary>
    public ProcessModule FindModule(string? name)
    {
        var process = Process ?? throw new InvalidOperationException("No hay proceso conectado");
        if (string.IsNullOrWhiteSpace(name)) return process.MainModule!;
        foreach (ProcessModule module in process.Modules)
            if (string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase))
                return module;
        throw new InvalidOperationException($"Módulo '{name}' no encontrado");
    }

    public nint ModuleBase(string? name) => FindModule(name).BaseAddress;

    /// <summary>Nombre → dirección base de todos los módulos cargados (relee la lista por si el juego cargó dlls nuevas).</summary>
    public Dictionary<string, nint> ModuleBases()
    {
        var bases = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
        if (Process is not { } process) return bases;
        process.Refresh();
        foreach (ProcessModule module in process.Modules) bases.TryAdd(module.ModuleName, module.BaseAddress);
        return bases;
    }

    public nint ReadPointer(nint address)
    {
        var bytes = Read(address, PointerSize);
        if (bytes is null) return 0;
        return Is32Bit ? (nint)BitConverter.ToUInt32(bytes) : (nint)BitConverter.ToInt64(bytes);
    }

    /// <summary>
    /// Cadena de punteros al estilo Cheat Engine: [[base]+off1]+off2 ...
    /// En cada paso se lee el puntero y se le suma el siguiente offset. Devuelve 0 si se rompe.
    /// </summary>
    public nint ResolvePointer(nint baseAddress, IReadOnlyList<long> offsets)
    {
        nint address = baseAddress;
        foreach (long offset in offsets)
        {
            address = ReadPointer(address);
            if (address == 0) return 0;
            address += (nint)offset;
        }
        return address;
    }

    // ---------- Regiones de memoria y búsqueda de patrones ----------

    public IEnumerable<MemoryRegion> Regions()
    {
        long max = Is32Bit ? 0xFFFF_FFFFL : 0x7FFF_FFFF_FFFFL;
        long address = 0;
        int infoSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        while (address < max)
        {
            if (VirtualQueryEx(_handle, (nint)address, out var info, infoSize) == 0) yield break;
            if (info.State == MEM_COMMIT && (info.Protect & (PAGE_GUARD | PAGE_NOACCESS)) == 0)
                yield return new MemoryRegion(info.BaseAddress, info.RegionSize, info.Protect);
            long next = (long)info.BaseAddress + (long)info.RegionSize;
            if (next <= address) yield break;
            address = next;
        }
    }

    /// <summary>
    /// Busca un patrón de bytes ("48 8B ?? 10", ?? = cualquiera) y devuelve la primera dirección, o 0.
    /// module: vacío = ejecutable principal, "*" = todo el código cargado, o el nombre de un módulo.
    /// </summary>
    public nint AobScan(string pattern, string? module = null)
    {
        var (bytes, mask) = ParsePattern(pattern);
        (long Start, long End)? range = null;
        if (module != "*")
        {
            var m = FindModule(module);
            range = ((long)m.BaseAddress, (long)m.BaseAddress + m.ModuleMemorySize);
        }

        const int Chunk = 1 << 20;
        var buffer = new byte[Chunk + bytes.Length];
        foreach (var region in Regions())
        {
            long start = region.Start, end = region.Start + region.Size;
            if (range is var (rangeStart, rangeEnd))
            {
                start = Math.Max(start, rangeStart);
                end = Math.Min(end, rangeEnd);
                if (start >= end) continue;
            }
            else if (!region.Executable) continue;

            for (long pos = start; pos < end; pos += Chunk)
            {
                // Cada bloque se solapa con el siguiente para no perder coincidencias en el borde.
                int length = (int)Math.Min(Chunk + bytes.Length - 1, end - pos);
                if (!ReadInto((nint)pos, buffer, length)) continue;
                int index = IndexOf(buffer, length, bytes, mask);
                if (index >= 0) return (nint)(pos + index);
            }
        }
        return 0;
    }

    public static (byte[] Bytes, bool[] Mask) ParsePattern(string pattern)
    {
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("Patrón vacío");
        var bytes = new byte[parts.Length];
        var mask = new bool[parts.Length]; // true = byte que debe coincidir
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Trim('?').Length == 0) continue;
            bytes[i] = Convert.ToByte(parts[i], 16);
            mask[i] = true;
        }
        return (bytes, mask);
    }

    static int IndexOf(byte[] data, int length, byte[] pattern, bool[] mask)
    {
        for (int i = 0; i <= length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && (!mask[j] || data[i + j] == pattern[j])) j++;
            if (j == pattern.Length) return i;
        }
        return -1;
    }
}
