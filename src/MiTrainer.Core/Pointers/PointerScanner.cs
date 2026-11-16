using MiTrainer.Core.Memory;

namespace MiTrainer.Core.Pointers;

/// <summary>
/// Escáner de punteros: busca cadenas que empiezan en una dirección estática (dentro del exe o de una dll)
/// y llegan a la dirección objetivo. Así un cheat sigue funcionando cuando el juego se reinicia.
/// </summary>
public static class PointerScanner
{
    public const int MaxResults = 100_000;
    const int MaxNodesPerLevel = 300_000;

    /// <summary>
    /// Búsqueda hacia atrás: ¿quién apunta cerca del objetivo? ¿y quién apunta cerca de esos?...
    /// hasta llegar a una dirección estática o a la profundidad máxima.
    /// </summary>
    public static List<PointerPath> Scan(PointerMap map, long target, int maxDepth, long maxOffset,
        CancellationToken cancel = default, IProgress<string>? progress = null)
    {
        var results = new List<PointerPath>();
        var tree = new SearchTree(target);
        var visited = new HashSet<long> { target };
        var level = new List<int> { SearchTree.Root };

        for (int depth = 1; depth <= maxDepth && level.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (int node in level)
            {
                cancel.ThrowIfCancellationRequested();
                long address = tree.Address(node);
                for (int i = map.LowerBound(address - maxOffset); i < map.Count && map.ValueAt(i) <= address; i++)
                {
                    long offset = address - map.ValueAt(i);
                    if ((offset & 3) != 0) continue; // los campos de las estructuras van alineados
                    long location = map.LocationAt(i);

                    if (map.FindModule(location) is { } module)
                    {
                        results.Add(new PointerPath(module.Name, location - module.BaseAddress, tree.OffsetsFrom(node, offset)));
                        if (results.Count >= MaxResults) return results;
                        continue; // una dirección estática ya es el principio de la cadena
                    }
                    if (depth == maxDepth || next.Count >= MaxNodesPerLevel || !visited.Add(location)) continue;
                    next.Add(tree.Add(location, node, offset));
                }
            }
            progress?.Report($"Nivel {depth}/{maxDepth}: {results.Count:N0} cadenas encontradas, {next.Count:N0} por explorar");
            level = next;
        }
        return results;
    }

    /// <summary>Se queda con las cadenas que siguen cumpliendo la condición (normalmente: "llegan a la nueva dirección").</summary>
    public static List<PointerPath> Filter(IProcessMemory memory, IEnumerable<PointerPath> paths, Func<long, bool> keep,
        CancellationToken cancel = default)
    {
        var moduleBases = memory.ModuleBases();
        var kept = new List<PointerPath>();
        int checkedCount = 0;
        foreach (var path in paths)
        {
            if ((checkedCount++ & 0xFFF) == 0) cancel.ThrowIfCancellationRequested();
            if (path.Resolve(memory, moduleBases) is var address && address != 0 && keep(address)) kept.Add(path);
        }
        return kept;
    }

    /// <summary>
    /// Árbol de la búsqueda hacia atrás. Cada nodo es una dirección que apunta (con un offset) a la de su padre;
    /// la raíz es el objetivo. Guardado en listas paralelas para no crear millones de objetos.
    /// </summary>
    sealed class SearchTree(long target)
    {
        public const int Root = 0;
        readonly List<long> _address = [target];
        readonly List<int> _parent = [-1];
        readonly List<long> _offset = [0]; // Read(dirección) + offset = dirección del padre

        public long Address(int node) => _address[node];

        public int Add(long address, int parent, long offset)
        {
            _address.Add(address);
            _parent.Add(parent);
            _offset.Add(offset);
            return _address.Count - 1;
        }

        /// <summary>Offsets desde una dirección estática que apunta a <paramref name="node"/> hasta el objetivo.</summary>
        public long[] OffsetsFrom(int node, long firstOffset)
        {
            var offsets = new List<long> { firstOffset };
            for (int n = node; _parent[n] >= 0; n = _parent[n]) offsets.Add(_offset[n]);
            return [.. offsets];
        }
    }
}
