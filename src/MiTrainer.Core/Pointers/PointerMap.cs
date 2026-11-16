using MiTrainer.Core.Memory;

namespace MiTrainer.Core.Pointers;

/// <summary>
/// Foto de la memoria: cada posición escribible que contiene algo que parece un puntero (un valor que cae
/// dentro de memoria válida), ordenada por ese valor para responder rápido a "¿quién apunta cerca de X?".
/// </summary>
public sealed class PointerMap
{
    const int MaxPointers = 30_000_000;

    readonly long[] _values;    // a dónde apunta, ordenado
    readonly long[] _locations; // dónde está guardado ese puntero
    readonly ModuleInfo[] _modules;

    PointerMap(long[] values, long[] locations, IEnumerable<ModuleInfo> modules, bool truncated)
    {
        Array.Sort(values, locations);
        _values = values;
        _locations = locations;
        _modules = [.. modules.OrderBy(m => m.BaseAddress)];
        Truncated = truncated;
    }

    public int Count => _values.Length;

    /// <summary>Se llegó al máximo de punteros: el juego es enorme y puede faltar alguna cadena.</summary>
    public bool Truncated { get; }

    internal long ValueAt(int index) => _values[index];
    internal long LocationAt(int index) => _locations[index];

    /// <summary>Un mapa hecho a mano (para pruebas): pares (dónde está, a dónde apunta).</summary>
    public static PointerMap FromEntries(IEnumerable<(long Location, long Value)> entries, IEnumerable<ModuleInfo> modules)
    {
        var list = entries.ToArray();
        return new PointerMap([.. list.Select(e => e.Value)], [.. list.Select(e => e.Location)], modules, truncated: false);
    }

    public static PointerMap Build(IProcessMemory memory, CancellationToken cancel = default, IProgress<string>? progress = null)
    {
        var regions = memory.GetRegions();
        var starts = regions.Select(r => r.Start).ToArray();
        long min = regions.Count > 0 ? regions[0].Start : 0;
        long max = regions.Count > 0 ? regions[^1].End : 0;

        bool IsValidAddress(long value)
        {
            if (value < min || value >= max) return false;
            int i = Array.BinarySearch(starts, value);
            if (i < 0) i = ~i - 1;
            return i >= 0 && value < regions[i].End;
        }

        int pointerSize = memory.PointerSize();
        var values = new List<long>();
        var locations = new List<long>();
        var buffer = new byte[1 << 20];
        long total = regions.Where(r => r.Writable).Sum(r => r.Size), done = 0;
        bool truncated = false;

        foreach (var region in regions.Where(r => r.Writable))
        {
            for (long position = region.Start; position < region.End; position += buffer.Length)
            {
                cancel.ThrowIfCancellationRequested();
                int length = (int)Math.Min(buffer.Length, region.End - position);
                done += length;
                if (!memory.TryRead(position, buffer, length)) continue;
                for (int i = 0; i + pointerSize <= length; i += pointerSize)
                {
                    long value = pointerSize == 8 ? BitConverter.ToInt64(buffer, i) : BitConverter.ToUInt32(buffer, i);
                    if (!IsValidAddress(value)) continue;
                    values.Add(value);
                    locations.Add(position + i);
                }
                if (values.Count >= MaxPointers)
                {
                    truncated = true;
                    goto done;
                }
            }
            progress?.Report($"Leyendo memoria... {done * 100 / Math.Max(1, total)}%  ({values.Count:N0} punteros)");
        }
    done:
        progress?.Report($"Ordenando {values.Count:N0} punteros...");
        return new PointerMap([.. values], [.. locations], memory.GetModules(), truncated);
    }

    /// <summary>Si la dirección está dentro de un módulo (exe/dll) es "estática": no cambia de sitio al reiniciar.</summary>
    internal ModuleInfo? FindModule(long address)
    {
        int lo = 0, hi = _modules.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (address < _modules[mid].BaseAddress) hi = mid - 1;
            else if (address >= _modules[mid].End) lo = mid + 1;
            else return _modules[mid];
        }
        return null;
    }

    /// <summary>Primer índice cuyo valor es mayor o igual que <paramref name="value"/>.</summary>
    internal int LowerBound(long value)
    {
        int lo = 0, hi = _values.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_values[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
