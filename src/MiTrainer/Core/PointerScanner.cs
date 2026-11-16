namespace MiTrainer.Core;

/// <summary>Una cadena de punteros: "juego.exe"+1A2B → 18 → 10.</summary>
public sealed record PointerPath(string Module, long BaseOffset, long[] Offsets)
{
    public string BaseText => $"\"{Module}\"+{BaseOffset:X}";
    public string OffsetsText => string.Join(" → ", Offsets.Select(o => o.ToString("X")));
    public override string ToString() => $"{BaseText} → {OffsetsText}";

    /// <summary>Dirección final a la que apunta ahora, o 0 si la cadena está rota.</summary>
    public nint Resolve(MemoryEngine mem, IReadOnlyDictionary<string, nint> moduleBases) =>
        moduleBases.TryGetValue(Module, out nint moduleBase)
            ? mem.ResolvePointer(moduleBase + (nint)BaseOffset, Offsets)
            : 0;
}

/// <summary>
/// Foto de la memoria: todas las posiciones de memoria escribible que contienen algo que parece un puntero
/// (un valor que cae dentro de memoria válida), ordenadas por ese valor para poder buscar "quién apunta cerca de X".
/// </summary>
public sealed class PointerMap
{
    const int MaxPointers = 30_000_000;

    readonly long[] _values;    // a dónde apunta, ordenado
    readonly long[] _locations; // dónde está guardado ese puntero
    readonly (string Name, long Start, long End)[] _modules;

    PointerMap(long[] values, long[] locations, IEnumerable<(string Name, long Start, long End)> modules)
    {
        Array.Sort(values, locations);
        _values = values;
        _locations = locations;
        _modules = modules.OrderBy(m => m.Start).ToArray();
    }

    public int Count => _values.Length;
    public bool Truncated { get; private init; }
    internal long[] Values => _values;
    internal long[] Locations => _locations;

    /// <summary>Para pruebas: un mapa hecho a mano.</summary>
    public static PointerMap FromEntries(IEnumerable<(long Location, long Value)> entries, IEnumerable<(string Name, long Start, long End)> modules)
    {
        var list = entries.ToArray();
        return new PointerMap(list.Select(e => e.Value).ToArray(), list.Select(e => e.Location).ToArray(), modules);
    }

    public static PointerMap Build(MemoryEngine mem, CancellationToken cancel = default, IProgress<string>? progress = null)
    {
        var regions = mem.Regions().ToArray();
        var starts = regions.Select(r => r.Start).ToArray();
        long min = regions.Length > 0 ? regions[0].Start : 0;
        long max = regions.Length > 0 ? regions[^1].Start + regions[^1].Size : 0;

        bool IsValidAddress(long value)
        {
            if (value < min || value >= max) return false;
            int i = Array.BinarySearch(starts, value);
            if (i < 0) i = ~i - 1;
            return i >= 0 && value < regions[i].Start + regions[i].Size;
        }

        mem.Process!.Refresh(); // por si el juego cargó dlls nuevas desde que nos conectamos
        var modules = mem.Process.Modules.Cast<System.Diagnostics.ProcessModule>()
            .Select(m => (m.ModuleName, (long)m.BaseAddress, (long)m.BaseAddress + m.ModuleMemorySize))
            .ToArray();

        int pointerSize = mem.PointerSize;
        var values = new List<long>();
        var locations = new List<long>();
        var buffer = new byte[1 << 20];
        long total = regions.Where(r => r.Writable).Sum(r => r.Size), done = 0;
        bool truncated = false;

        foreach (var region in regions)
        {
            if (!region.Writable) continue;
            long end = region.Start + region.Size;
            for (long pos = region.Start; pos < end; pos += buffer.Length)
            {
                cancel.ThrowIfCancellationRequested();
                int length = (int)Math.Min(buffer.Length, end - pos);
                done += length;
                if (!mem.ReadInto((nint)pos, buffer, length)) continue;
                for (int i = 0; i + pointerSize <= length; i += pointerSize)
                {
                    long value = pointerSize == 8 ? BitConverter.ToInt64(buffer, i) : BitConverter.ToUInt32(buffer, i);
                    if (!IsValidAddress(value)) continue;
                    values.Add(value);
                    locations.Add(pos + i);
                }
                if (values.Count >= MaxPointers) { truncated = true; goto done; }
            }
            progress?.Report($"Leyendo memoria... {done * 100 / Math.Max(1, total)}%  ({values.Count:N0} punteros)");
        }
    done:
        progress?.Report($"Ordenando {values.Count:N0} punteros...");
        return new PointerMap(values.ToArray(), locations.ToArray(), modules) { Truncated = truncated };
    }

    /// <summary>Si la dirección está dentro de un módulo (exe/dll) es "estática": no cambia de sitio al reiniciar.</summary>
    internal (string Name, long Start)? FindModule(long address)
    {
        int lo = 0, hi = _modules.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (address < _modules[mid].Start) hi = mid - 1;
            else if (address >= _modules[mid].End) lo = mid + 1;
            else return (_modules[mid].Name, _modules[mid].Start);
        }
        return null;
    }

    /// <summary>Primer índice cuyo valor es >= value.</summary>
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

/// <summary>
/// Escáner de punteros: busca cadenas que empiezan en una dirección estática (dentro del exe o una dll)
/// y llegan a la dirección objetivo. Así el cheat sigue funcionando cuando el juego se reinicia.
/// </summary>
public static class PointerScanner
{
    public const int MaxResults = 100_000;
    const int MaxNodesPerLevel = 300_000;

    /// <summary>
    /// Búsqueda hacia atrás: ¿quién apunta cerca del objetivo? ¿y quién apunta cerca de esos? ...
    /// hasta llegar a una dirección estática o a la profundidad máxima.
    /// </summary>
    public static List<PointerPath> Scan(PointerMap map, long target, int maxDepth, long maxOffset,
        CancellationToken cancel = default, IProgress<string>? progress = null)
    {
        var results = new List<PointerPath>();
        // Nodos del árbol de búsqueda: dirección, nodo padre y offset (Read(dirección) + offset = dirección del padre).
        var address = new List<long> { target };
        var parent = new List<int> { -1 };
        var offset = new List<long> { 0 };
        var visited = new HashSet<long> { target };
        var level = new List<int> { 0 };

        for (int depth = 1; depth <= maxDepth && level.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (int node in level)
            {
                cancel.ThrowIfCancellationRequested();
                long a = address[node];
                for (int i = map.LowerBound(a - maxOffset); i < map.Count && map.Values[i] <= a; i++)
                {
                    long off = a - map.Values[i];
                    if ((off & 3) != 0) continue; // los campos de las estructuras van alineados
                    long location = map.Locations[i];

                    if (map.FindModule(location) is { } module)
                    {
                        results.Add(BuildPath(module.Name, location - module.Start, off, node, parent, offset));
                        if (results.Count >= MaxResults) return results;
                        continue;
                    }
                    if (depth == maxDepth || next.Count >= MaxNodesPerLevel || !visited.Add(location)) continue;
                    next.Add(address.Count);
                    address.Add(location);
                    parent.Add(node);
                    offset.Add(off);
                }
            }
            progress?.Report($"Nivel {depth}/{maxDepth}: {results.Count:N0} cadenas encontradas, {next.Count:N0} por explorar");
            level = next;
        }
        return results;
    }

    static PointerPath BuildPath(string module, long baseOffset, long firstOffset, int node, List<int> parent, List<long> offset)
    {
        var offsets = new List<long> { firstOffset };
        for (int n = node; parent[n] >= 0; n = parent[n]) offsets.Add(offset[n]);
        return new PointerPath(module, baseOffset, offsets.ToArray());
    }

    /// <summary>Se queda con las cadenas que cumplen la condición (normalmente: "apunta a la nueva dirección").</summary>
    public static List<PointerPath> Filter(MemoryEngine mem, IEnumerable<PointerPath> paths,
        IReadOnlyDictionary<string, nint> moduleBases, Func<nint, bool> keep) =>
        paths.Where(p => p.Resolve(mem, moduleBases) is var a && a != 0 && keep(a)).ToList();
}
