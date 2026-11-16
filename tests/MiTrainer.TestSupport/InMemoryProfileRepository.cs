using System.Text.Json;
using MiTrainer.Core.Cheats;

namespace MiTrainer.TestSupport;

/// <summary>Repositorio de perfiles en memoria. Guarda copias (vía JSON) para comportarse como un archivo de verdad.</summary>
public sealed class InMemoryProfileRepository : IProfileRepository
{
    readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public string Folder => "perfiles";
    public int SaveCount { get; private set; }

    public IReadOnlyList<string> List() => [.. _files.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    public GameProfile Load(string path) => JsonSerializer.Deserialize<GameProfile>(_files[path])!;

    public void Save(string path, GameProfile profile)
    {
        _files[path] = JsonSerializer.Serialize(profile);
        SaveCount++;
    }

    public string NewPathFor(string name)
    {
        string path = Path.Combine(Folder, name + ".json");
        for (int n = 2; _files.ContainsKey(path); n++) path = Path.Combine(Folder, $"{name} ({n}).json");
        return path;
    }
}
