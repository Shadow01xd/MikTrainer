using MiTrainer.Core.Games;
using MiTrainer.Core.Pointers;

namespace MiTrainer.TestSupport;

/// <summary>Tienda de juegos con una lista fija.</summary>
public sealed class FakeGameStore(params InstalledGame[] games) : IGameStore
{
    public string Name => "Falsa";
    public IEnumerable<InstalledGame> FindInstalled() => games;
}

/// <summary>Resultados de punteros en memoria.</summary>
public sealed class InMemoryPointerResultsRepository : IPointerResultsRepository
{
    readonly Dictionary<string, PointerScanResults> _files = new(StringComparer.OrdinalIgnoreCase);

    public string Folder => "punteros";
    public int SaveCount { get; private set; }
    public string AutoSavePathFor(string processName) => Path.Combine(Folder, processName + ".json");

    public void Save(string path, PointerScanResults results)
    {
        _files[path] = results;
        SaveCount++;
    }
    public PointerScanResults Load(string path) => _files[path];
}
