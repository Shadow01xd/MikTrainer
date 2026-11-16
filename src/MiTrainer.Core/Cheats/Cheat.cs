using MiTrainer.Core.Memory;
using MiTrainer.Core.Values;

namespace MiTrainer.Core.Cheats;

/// <summary>Un cheat en ejecución: sabe activarse, desactivarse y mantener su valor.</summary>
public sealed class Cheat(CheatDefinition definition)
{
    long _patchAddress;
    byte[]? _originalBytes;

    public CheatDefinition Definition { get; } = definition;
    public bool Enabled { get; private set; }

    /// <summary>El último intento de activarlo falló (el motivo está en <see cref="Status"/>).</summary>
    public bool Failed { get; private set; }

    /// <summary>Texto para el usuario: qué está haciendo o por qué falló.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Activa el cheat. En "fijar" escribe una vez y queda desactivado. Nunca lanza: los errores van a Status.</summary>
    public bool Enable(IProcessMemory memory)
    {
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

    /// <summary>Desactiva el cheat; un parche vuelve a dejar los bytes originales.</summary>
    public void Disable(IProcessMemory? memory)
    {
        if (Enabled && _originalBytes != null && memory is { IsAttached: true })
            memory.Write(_patchAddress, _originalBytes);
        _originalBytes = null;
        Enabled = false;
        Failed = false;
        Status = "Desactivado";
    }

    /// <summary>El juego se cerró: olvidamos el estado sin tocar la memoria.</summary>
    public void Reset()
    {
        _originalBytes = null;
        Enabled = false;
        Failed = false;
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
        var pattern = BytePattern.Parse(Definition.Pattern ?? "");
        byte[] bytes = NumberText.ParseHexBytes(Definition.Bytes);
        long found = PatternScanner.Find(memory, pattern, Definition.Module);
        if (found == 0) throw new InvalidOperationException("Patrón no encontrado (¿otra versión del juego?)");

        long address = found + Definition.PatternOffset;
        _originalBytes = memory.Read(address, bytes.Length) ?? throw new InvalidOperationException("No se pudo leer la memoria");
        if (!memory.Write(address, bytes)) throw new InvalidOperationException("No se pudo escribir la memoria");
        _patchAddress = address;
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
