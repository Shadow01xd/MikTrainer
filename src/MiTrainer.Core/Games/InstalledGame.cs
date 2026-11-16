namespace MiTrainer.Core.Games;

/// <summary>Un juego instalado en el equipo.</summary>
/// <param name="Store">Tienda de la que viene ("Steam", "Epic", "GOG").</param>
/// <param name="Executable">Ruta del .exe del juego, o null si no se sabe.</param>
/// <param name="LaunchUri">Cómo abrirlo a través de su tienda (steam://...), o null para abrir el .exe.</param>
public sealed record InstalledGame(string Name, string Store, string InstallDir, string? Executable, string? LaunchUri)
{
    /// <summary>Nombre del ejecutable tal como aparece en los perfiles ("juego.exe").</summary>
    public string? ProcessName => Executable is null ? null : Path.GetFileName(Executable);

    public bool CanLaunch => LaunchUri != null || Executable != null;
}

/// <summary>Una tienda de juegos (Steam, Epic...) capaz de decir qué juegos tiene instalados.</summary>
public interface IGameStore
{
    string Name { get; }
    IEnumerable<InstalledGame> FindInstalled();
}

/// <summary>Junta los juegos de todas las tiendas, sin duplicados y ordenados por nombre.</summary>
public sealed class GameLibrary(IEnumerable<IGameStore> stores)
{
    public List<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        foreach (var store in stores)
        {
            // Si una tienda tiene archivos raros o no está instalada, seguimos con las demás.
            try { games.AddRange(store.FindInstalled()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException) { }
        }
        return [.. games
            .DistinctBy(g => g.InstallDir.TrimEnd('\\', '/').ToLowerInvariant())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
