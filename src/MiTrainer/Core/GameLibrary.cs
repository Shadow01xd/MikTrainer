using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MiTrainer.Core;

/// <param name="Executable">Ruta del .exe del juego (adivinada en Steam), o null si no se sabe.</param>
/// <param name="LaunchUri">Cómo abrirlo a través de su tienda (steam://...), o null para abrir el .exe.</param>
public sealed record InstalledGame(string Name, string Store, string InstallDir, string? Executable, string? LaunchUri)
{
    /// <summary>Nombre del proceso tal como aparece en los perfiles ("juego.exe").</summary>
    public string? ProcessName => Executable is null ? null : Path.GetFileName(Executable);
}

/// <summary>
/// Detecta los juegos instalados leyendo los archivos que dejan las tiendas (como hace Wand):
/// Steam (libraryfolders.vdf + appmanifest_*.acf), Epic (manifiestos .item) y GOG (registro).
/// </summary>
public static partial class GameLibrary
{
    public static List<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        games.AddRange(Safe(() => Steam(FindSteam())));
        games.AddRange(Safe(() => Epic(EpicManifestsDir)));
        games.AddRange(Safe(Gog));
        return games
            .DistinctBy(g => g.InstallDir.TrimEnd('\\').ToLowerInvariant())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // Si una tienda tiene archivos raros, seguimos con las demás.
    static IEnumerable<InstalledGame> Safe(Func<IEnumerable<InstalledGame>> scan)
    {
        try { return scan().ToList(); }
        catch { return []; }
    }

    // ---------------- Steam ----------------

    static string? FindSteam() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
        ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

    // Herramientas que Steam instala como si fueran juegos.
    static readonly string[] SteamNotGames = ["Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "SteamVR", "Steam Audio"];

    public static IEnumerable<InstalledGame> Steam(string? steamPath)
    {
        if (steamPath is null) yield break;
        steamPath = steamPath.Replace('/', '\\');
        var libraries = new List<string> { steamPath };
        string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libraries.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var fields = ParseKeyValues(File.ReadAllText(manifest));
                if (!fields.TryGetValue("name", out var name) || !fields.TryGetValue("installdir", out var dir)) continue;
                if (SteamNotGames.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase))) continue;
                string installDir = Path.Combine(apps, "common", dir);
                if (!Directory.Exists(installDir)) continue;
                fields.TryGetValue("appid", out var appId);
                yield return new InstalledGame(name, "Steam", installDir, GuessExecutable(installDir),
                    appId is null ? null : $"steam://rungameid/{appId}");
            }
        }
    }

    /// <summary>Rutas de las bibliotecas de Steam: líneas "path"  "D:\\SteamLibrary".</summary>
    public static IEnumerable<string> ParseLibraryFolders(string vdf) =>
        LibraryPathRegex().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    /// <summary>Pares "clave" "valor" de un archivo .acf/.vdf (solo los de primer nivel nos importan).</summary>
    public static Dictionary<string, string> ParseKeyValues(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in KeyValueRegex().Matches(text))
            values.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Replace(@"\\", @"\"));
        return values;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("^\\s*\"([^\"]+)\"\\s+\"([^\"]*)\"", RegexOptions.Multiline)]
    private static partial Regex KeyValueRegex();

    // Ejecutables que nunca son el juego.
    static readonly string[] NotTheGame =
        ["unins", "setup", "install", "redist", "vcredist", "dxsetup", "crash", "report", "launcher", "helper", "update",
         "easyanticheat", "battleye", "ue4prereq", "uninstall", "dotnet", "directx", "physx", "cef", "webhelper",
         "bootstrap", "ffmpeg", "minidump", "stackwalk", "translator", "python", "node", "7z", "dumper", "benchmark"];

    /// <summary>
    /// Steam no dice cuál es el .exe: puntuamos cada candidato por tamaño y damos ventaja a los que se parecen
    /// al nombre de la carpeta del juego o son el "-Shipping" de Unreal. Se busca hasta 3 carpetas de profundidad.
    /// </summary>
    public static string? GuessExecutable(string installDir)
    {
        var candidates = new List<FileInfo>();
        void Walk(DirectoryInfo dir, int depth)
        {
            try
            {
                candidates.AddRange(dir.EnumerateFiles("*.exe")
                    .Where(f => !NotTheGame.Any(n => f.Name.Contains(n, StringComparison.OrdinalIgnoreCase))));
                if (depth < 3)
                    foreach (var sub in dir.EnumerateDirectories())
                        if (!sub.Name.StartsWith('_') && !sub.Name.Equals("redist", StringComparison.OrdinalIgnoreCase))
                            Walk(sub, depth + 1);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        Walk(new DirectoryInfo(installDir), 0);

        string folder = Letters(Path.GetFileName(installDir.TrimEnd('\\')));
        double Score(FileInfo exe)
        {
            double score = exe.Length;
            string name = Letters(Path.GetFileNameWithoutExtension(exe.Name));
            if (exe.Name.Contains("-Shipping", StringComparison.OrdinalIgnoreCase)) score *= 4;
            if (name.Length >= 3 && folder.Length >= 3 && (folder.StartsWith(name[..Math.Min(4, name.Length)]) || name.StartsWith(folder[..Math.Min(4, folder.Length)])))
                score *= 4; // "NoRestForTheWicked.exe" en la carpeta "No Rest for the Wicked"
            return score;
        }
        return candidates.MaxBy(Score)?.FullName;
    }

    static string Letters(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // ---------------- Epic Games ----------------

    static string EpicManifestsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");

    public static IEnumerable<InstalledGame> Epic(string manifestsDir)
    {
        if (!Directory.Exists(manifestsDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(manifestsDir, "*.item"))
        {
            JsonElement item;
            try { item = JsonDocument.Parse(File.ReadAllText(file)).RootElement; }
            catch (JsonException) { continue; }

            string? Get(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string? name = Get("DisplayName"), dir = Get("InstallLocation");
            if (name is null || dir is null || !Directory.Exists(dir)) continue;
            if (item.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) continue;
            if (item.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
                && !categories.EnumerateArray().Any(c => c.GetString() == "games")) continue;

            string? exe = Get("LaunchExecutable") is { Length: > 0 } relative ? Path.Combine(dir, relative) : GuessExecutable(dir);
            string? uri = Get("CatalogNamespace") is { } ns && Get("CatalogItemId") is { } id && Get("AppName") is { } app
                ? $"com.epicgames.launcher://apps/{ns}%3A{id}%3A{app}?action=launch&silent=true"
                : null;
            yield return new InstalledGame(name, "Epic", dir, exe, uri);
        }
    }

    // ---------------- GOG ----------------

    static IEnumerable<InstalledGame> Gog()
    {
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (games is null) yield break;
        foreach (var id in games.GetSubKeyNames())
        {
            using var key = games.OpenSubKey(id);
            if (key?.GetValue("gameName") is not string name || key.GetValue("path") is not string dir || !Directory.Exists(dir)) continue;
            string? exe = key.GetValue("exe") as string;
            // Los juegos de GOG no tienen DRM: se abren directamente con su .exe.
            yield return new InstalledGame(name, "GOG", dir, File.Exists(exe) ? exe : GuessExecutable(dir), null);
        }
    }
}
