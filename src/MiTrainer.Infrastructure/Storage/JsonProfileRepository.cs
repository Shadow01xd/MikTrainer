using MiTrainer.Core.Cheats;

namespace MiTrainer.Infrastructure.Storage;

/// <summary>Perfiles de juegos como archivos .json dentro de una carpeta.</summary>
public sealed class JsonProfileRepository(string folder) : IProfileRepository
{
    public string Folder { get; } = folder;

    public IReadOnlyList<string> List() =>
        Directory.Exists(Folder) ? [.. Directory.GetFiles(Folder, "*.json").Order(StringComparer.OrdinalIgnoreCase)] : [];

    public GameProfile Load(string path) => JsonStorage.Read<GameProfile>(path);

    public void Save(string path, GameProfile profile) => JsonStorage.Write(path, profile);

    public string NewPathFor(string name)
    {
        string baseName = JsonStorage.SafeFileName(name);
        string path = Path.Combine(Folder, baseName + ".json");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"{baseName} ({n}).json");
        return path;
    }
}
