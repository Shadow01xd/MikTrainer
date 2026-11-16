using MiTrainer.Core.Memory;
using MiTrainer.Core.Values;

namespace MiTrainer.Core.Cheats;

/// <summary>Un cheat en ejecución: sabe activarse, desactivarse y mantener su valor.</summary>
public sealed class Cheat(CheatDefinition definition)
{
    PendingRestore? _patch; // parche aplicado: dónde y qué bytes había antes

    public CheatDefinition Definition { get; } = definition;
    public bool Enabled { get; private set; }

    /// <summary>El último intento de activarlo falló (el motivo está en <see cref="Status"/>).</summary>
    public bool Failed { get; private set; }

    /// <summary>
    /// Se intentó deshacer el parche y no se pudo: el juego sigue modificado.
    /// <see cref="PendingRestore"/> guarda lo necesario para deshacerlo.
    /// </summary>
    public bool RestoreFailed { get; private set; }

    /// <summary>Datos para deshacer el parche cuya restauración falló (null si no hay nada pendiente).</summary>
    public PendingRestore? PendingRestore => RestoreFailed ? _patch : null;

    /// <summary>Texto para el usuario: qué está haciendo o por qué falló.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Activa el cheat. En "fijar" escribe una vez y queda desactivado. Nunca lanza: los errores van a Status.</summary>
    public bool Enable(IProcessMemory memory)
    {
        if (RestoreFailed) return false; // primero hay que deshacer el parche anterior (RetryRestore)
        Failed = false;
        try
        {
            switch (Definition.Type)
            {
                case CheatType.Patch:
                    ApplyPatch(memory);
                    break;
                case CheatType.Freeze:
                    long address = WriteValue(memory);
                    Enabled = true;
                    Status = $"Congelado en {Definition.ValueType.Format(Definition.Value)} (0x{address:X})";
                    break;
                case CheatType.Set:
                    address = WriteValue(memory);
                    Status = $"Valor {Definition.ValueType.Format(Definition.Value)} escrito en 0x{address:X}";
                    break;
            }
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            Fail(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Desactiva el cheat; un parche vuelve a dejar los bytes originales. Si no se pueden escribir, queda en
    /// «Restauración fallida» conservando los datos para reintentarlo, y devuelve false.
    /// </summary>
    public bool Disable(IProcessMemory? memory)
    {
        // Con la restauración ya fallida el parche sigue aplicado: nunca se descartan sus datos, solo se reintenta.
        if (RestoreFailed) return RetryRestore(memory);

        // Sin proceso conectado el juego se cerró: con él desapareció el parche y no hay nada que deshacer.
        if (Enabled && _patch is { } patch && memory is { IsAttached: true } && !patch.TryRestore(memory))
        {
            MarkRestoreFailed(patch);
            return false;
        }
        _patch = null;
        Enabled = false;
        Failed = false;
        Status = "Desactivado";
        return true;
    }

    /// <summary>Vuelve a intentar deshacer un parche cuya restauración falló.</summary>
    public bool RetryRestore(IProcessMemory? memory)
    {
        if (!RestoreFailed || _patch is not { } patch) return true;
        if (memory is null || !patch.TryRestore(memory))
        {
            MarkRestoreFailed(patch);
            return false;
        }
        _patch = null;
        RestoreFailed = false;
        Failed = false;
        Status = "Restaurado: el juego vuelve a estar como antes";
        return true;
    }

    void MarkRestoreFailed(PendingRestore patch)
    {
        Enabled = false;
        Failed = true;
        RestoreFailed = true;
        Status = $"Restauración fallida: el juego sigue modificado en {patch.Describe()}. Pulsa para reintentar";
    }

    /// <summary>
    /// Se cerró el proceso <paramref name="closed"/>: se olvida lo que era de él (el cheat activo y, si su parche
    /// estaba ahí, la restauración pendiente, que se fue con el proceso). Una restauración pendiente de otro proceso
    /// que sigue abierto se conserva.
    /// </summary>
    public void OnProcessExited(ProcessIdentity closed)
    {
        if (RestoreFailed && _patch is { } patch && patch.Process != closed) return;
        Reset();
    }

    /// <summary>Olvida todo el estado sin tocar la memoria.</summary>
    public void Reset()
    {
        _patch = null;
        Enabled = false;
        Failed = false;
        RestoreFailed = false;
        Status = "";
    }

    /// <summary>Se llama muchas veces por segundo para mantener los valores congelados.</summary>
    public void Tick(IProcessMemory memory)
    {
        if (!Enabled || Definition.Type != CheatType.Freeze) return;
        try { WriteValue(memory); }
        catch (InvalidOperationException) { /* en pantallas de carga el puntero puede no ser válido un momento */ }
    }

    /// <summary>No se pudo ni intentar activarlo (p. ej. no hay juego conectado).</summary>
    public void Fail(string message)
    {
        Enabled = false;
        Failed = true;
        Status = message;
    }

    /// <summary>Un aviso que no impide usar el cheat (p. ej. su atajo de teclado está ocupado).</summary>
    public void Note(string message) => Status = message;

    /// <summary>Dirección final: módulo + dirección y, si hay offsets, siguiendo la cadena de punteros.</summary>
    public long ResolveAddress(IProcessMemory memory)
    {
        if (!NumberText.TryParseHex(Definition.Address, out long address))
            throw new InvalidOperationException("Falta la dirección o no está en hexadecimal");
        if (!string.IsNullOrWhiteSpace(Definition.Module)) address += memory.FindModule(Definition.Module).BaseAddress;
        if (Definition.Offsets is { Count: > 0 } offsets)
            address = memory.ResolvePointer(address, offsets.Select(NumberText.ParseHex));
        return address;
    }

    void ApplyPatch(IProcessMemory memory)
    {
        // Sin identidad completa no podríamos reconocer luego este proceso para deshacer el parche con seguridad:
        // mejor no aplicarlo que dejar el juego modificado sin vuelta atrás.
        if (!memory.Identity.IsKnown)
            throw new InvalidOperationException("No se pudo identificar el proceso (hora de inicio desconocida): el parche no se aplica porque luego no se podría deshacer con seguridad");

        var pattern = BytePattern.Parse(Definition.Pattern ?? "");
        byte[] bytes = NumberText.ParseHexBytes(Definition.Bytes);
        long found = PatternScanner.Find(memory, pattern, Definition.Module);
        if (found == 0) throw new InvalidOperationException("Patrón no encontrado (¿otra versión del juego?)");

        long address = found + Definition.PatternOffset;
        byte[] original = memory.Read(address, bytes.Length) ?? throw new InvalidOperationException("No se pudo leer la memoria");
        if (!memory.Write(address, bytes)) throw new InvalidOperationException("No se pudo escribir la memoria");
        _patch = new PendingRestore(Definition.Name, memory.Identity, memory.ProcessName, address, original);
        Enabled = true;
        Status = $"Activo (parche en 0x{address:X})";
    }

    long WriteValue(IProcessMemory memory)
    {
        long address = ResolveAddress(memory);
        if (address == 0 || !memory.WriteValue(address, Definition.ValueType, Definition.Value))
            throw new InvalidOperationException("Dirección no válida o puntero roto");
        return address;
    }
}
