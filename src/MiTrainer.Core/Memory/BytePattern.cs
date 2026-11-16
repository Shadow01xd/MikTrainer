namespace MiTrainer.Core.Memory;

/// <summary>Patrón de bytes con comodines: "48 8B ?? 10" (?? = cualquier byte). También llamado AOB.</summary>
public sealed class BytePattern
{
    readonly byte[] _bytes;
    readonly bool[] _mask; // true = el byte tiene que coincidir

    BytePattern(byte[] bytes, bool[] mask)
    {
        _bytes = bytes;
        _mask = mask;
    }

    public int Length => _bytes.Length;

    public static BytePattern Parse(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("El patrón está vacío");
        var bytes = new byte[parts.Length];
        var mask = new bool[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Trim('?').Length == 0) continue;
            try { bytes[i] = Convert.ToByte(parts[i], 16); }
            catch (Exception ex) { throw new FormatException($"«{parts[i]}» no es un byte hexadecimal", ex); }
            mask[i] = true;
        }
        return new BytePattern(bytes, mask);
    }

    /// <summary>Primera posición de <paramref name="data"/> (hasta <paramref name="length"/>) donde aparece el patrón, o -1.</summary>
    public int IndexOf(byte[] data, int length)
    {
        for (int i = 0; i <= length - _bytes.Length; i++)
        {
            int j = 0;
            while (j < _bytes.Length && (!_mask[j] || data[i + j] == _bytes[j])) j++;
            if (j == _bytes.Length) return i;
        }
        return -1;
    }
}

/// <summary>Busca un <see cref="BytePattern"/> en la memoria de un proceso.</summary>
public static class PatternScanner
{
    /// <summary>Nombre de "módulo" que significa: todo el código ejecutable cargado.</summary>
    public const string AllExecutableMemory = "*";

    const int ChunkSize = 1 << 20;

    /// <summary>
    /// Dirección de la primera coincidencia, o 0 si no hay.
    /// <paramref name="module"/>: vacío = el ejecutable principal, "*" = todo el código, o el nombre de un módulo.
    /// </summary>
    public static long Find(IProcessMemory memory, BytePattern pattern, string? module = null)
    {
        ModuleInfo? range = module == AllExecutableMemory ? null : memory.FindModule(module);
        var buffer = new byte[ChunkSize + pattern.Length];

        foreach (var region in memory.GetRegions())
        {
            long start = region.Start, end = region.End;
            if (range is not null)
            {
                start = Math.Max(start, range.BaseAddress);
                end = Math.Min(end, range.End);
                if (start >= end) continue;
            }
            else if (!region.Executable) continue;

            for (long position = start; position < end; position += ChunkSize)
            {
                // Cada bloque se solapa con el siguiente para no perder coincidencias en el borde.
                int length = (int)Math.Min(ChunkSize + pattern.Length - 1, end - position);
                if (!memory.TryRead(position, buffer, length)) continue;
                int index = pattern.IndexOf(buffer, length);
                if (index >= 0) return position + index;
            }
        }
        return 0;
    }
}
