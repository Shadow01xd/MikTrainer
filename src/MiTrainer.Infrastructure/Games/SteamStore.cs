using System.Text.RegularExpressions;
using Microsoft.Win32;
using MiTrainer.Core.Games;

namespace MiTrainer.Infrastructure.Games;

/// <summary>
/// Juegos de Steam: libraryfolders.vdf dice dónde están las bibliotecas y cada appmanifest_*.acf describe un juego.
/// </summary>
/// <param name="steamPath">Carpeta de Steam; null = buscarla en el registro.</param>
public sealed partial class SteamStore(string? steamPath = null) : IGameStore
{
    // Herramientas que Steam instala como si fueran juegos.
    static readonly string[] NotGames = ["Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "SteamVR", "Steam Audio"];

    public string Name => "Steam";

    public IEnumerable<InstalledGame> FindInstalled()
    {
        string? root = (steamPath ?? FindSteamPath())?.Replace('/', '\\');
        if (root is null) yield break;

        foreach (var library in Libraries(root))
        {
            string apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                if (ReadManifest(apps, File.ReadAllText(manifest)) is { } game) yield return game;
        }
    }

    static IEnumerable<string> Libraries(string root)
    {
        string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        var libraries = new List<string> { root };
        if (File.Exists(vdf)) libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    static InstalledGame? ReadManifest(string appsFolder, string text)
    {
        var fields = ParseKeyValues(text);
        if (!fields.TryGetValue("name", out var name) || !fields.TryGetValue("installdir", out var dir)) return null;
        if (NotGames.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase))) return null;

        string installDir = Path.Combine(appsFolder, "common", dir);
        if (!Directory.Exists(installDir)) return null;
        string? launch = fields.TryGetValue("appid", out var appId) ? $"steam://rungameid/{appId}" : null;
        return new InstalledGame(name, "Steam", installDir, ExecutableGuesser.Guess(installDir), launch);
    }

    static string? FindSteamPath() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
        ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

    /// <summary>Rutas de las bibliotecas: líneas como <c>"path"  "D:\\SteamLibrary"</c>.</summary>
    public static IEnumerable<string> ParseLibraryFolders(string vdf) =>
        LibraryPathRegex().Matches(vdf).Select(m => Unescape(m.Groups[1].Value));

    /// <summary>Pares <c>"clave" "valor"</c> de un .acf/.vdf (el primero de cada clave).</summary>
    public static Dictionary<string, string> ParseKeyValues(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in KeyValueRegex().Matches(text)) values.TryAdd(m.Groups[1].Value, Unescape(m.Groups[2].Value));
        return values;
    }

    static string Unescape(string value) => value.Replace(@"\\", @"\");

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("^\\s*\"([^\"]+)\"\\s+\"([^\"]*)\"", RegexOptions.Multiline)]
    private static partial Regex KeyValueRegex();
}
