using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MiTrainer.Core.Memory;
using static MiTrainer.Infrastructure.Memory.NativeMethods;

namespace MiTrainer.Infrastructure.Memory;

/// <summary><see cref="IProcessMemory"/> real: lee y escribe la memoria de un proceso de Windows.</summary>
public sealed class WindowsProcessMemory : IProcessMemory
{
    readonly Process _process;
    nint _handle;

    WindowsProcessMemory(Process process, nint handle)
    {
        _process = process;
        _handle = handle;
        Is32Bit = IsWow64Process(handle, out bool wow64) && wow64;
        Identity = new ProcessIdentity(process.Id, ReadStartTime(process));
    }

    // Sin hora de inicio la identidad queda "desconocida" y nunca se da por buena (ver ProcessIdentity).
    static DateTime ReadStartTime(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { return default; }
    }

    /// <summary>Abre el proceso para leer y escribir su memoria.</summary>
    /// <exception cref="InvalidOperationException">Sin permisos (juego como administrador) o el proceso ya no existe.</exception>
    public static WindowsProcessMemory Open(Process process)
    {
        nint handle = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, process.Id);
        if (handle == 0)
        {
            var error = new Win32Exception(Marshal.GetLastPInvokeError());
            throw new InvalidOperationException($"No se pudo abrir el proceso ({error.Message}). Prueba a ejecutar MiTrainer como administrador.", error);
        }
        return new WindowsProcessMemory(process, handle);
    }

    public int ProcessId => _process.Id;
    public ProcessIdentity Identity { get; }
    public string ProcessName => _process.ProcessName;
    public bool Is32Bit { get; }

    public bool IsAttached
    {
        get
        {
            try { return _handle != 0 && !_process.HasExited; }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
        }
    }

    public bool TryRead(long address, byte[] buffer, int count) =>
        ReadProcessMemory(_handle, (nint)address, buffer, count, out nint read) && read == count;

    public bool Write(long address, byte[] data)
    {
        if (WriteProcessMemory(_handle, (nint)address, data, data.Length, out _)) return true;

        // Memoria de solo lectura (p. ej. código): quitamos la protección un momento.
        if (!VirtualProtectEx(_handle, (nint)address, data.Length, PAGE_EXECUTE_READWRITE, out uint old)) return false;
        bool ok = WriteProcessMemory(_handle, (nint)address, data, data.Length, out _);
        VirtualProtectEx(_handle, (nint)address, data.Length, old, out _);
        FlushInstructionCache(_handle, (nint)address, data.Length);
        return ok;
    }

    public IReadOnlyList<MemoryRegion> GetRegions()
    {
        var regions = new List<MemoryRegion>();
        long max = Is32Bit ? 0xFFFF_FFFFL : 0x7FFF_FFFF_FFFFL;
        int infoSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        for (long address = 0; address < max;)
        {
            if (VirtualQueryEx(_handle, (nint)address, out var info, infoSize) == 0) break;
            if (info.State == MEM_COMMIT && (info.Protect & (PAGE_GUARD | PAGE_NOACCESS)) == 0)
                regions.Add(new MemoryRegion(info.BaseAddress, info.RegionSize,
                    Writable: (info.Protect & PAGE_WRITABLE) != 0,
                    Executable: (info.Protect & PAGE_EXECUTE_ANY) != 0));
            long next = (long)info.BaseAddress + (long)info.RegionSize;
            if (next <= address) break;
            address = next;
        }
        return regions;
    }

    public IReadOnlyList<ModuleInfo> GetModules()
    {
        try
        {
            _process.Refresh(); // por si el juego cargó dlls nuevas desde la última vez
            var main = _process.MainModule;
            var modules = new List<ModuleInfo>();
            if (main != null) modules.Add(ToInfo(main));
            foreach (ProcessModule module in _process.Modules)
                if (main == null || module.BaseAddress != main.BaseAddress) modules.Add(ToInfo(module));
            return modules;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return []; // el proceso se cerró (o está cerrándose) mientras se consultaban sus módulos
        }
    }

    static ModuleInfo ToInfo(ProcessModule module) => new(module.ModuleName, module.BaseAddress, module.ModuleMemorySize);

    /// <summary>Reserva memoria nueva dentro del juego (para inyectar código o datos). Devuelve 0 si falla.</summary>
    public long Allocate(int size, bool executable = false) =>
        VirtualAllocEx(_handle, 0, size, MEM_COMMIT | MEM_RESERVE, executable ? PAGE_EXECUTE_READWRITE : PAGE_READWRITE);

    public void Dispose()
    {
        if (_handle != 0) CloseHandle(_handle);
        _handle = 0;
        _process.Dispose();
    }
}
