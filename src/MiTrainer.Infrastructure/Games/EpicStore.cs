using System.Text.Json;
using MiTrainer.Core.Games;

namespace MiTrainer.Infrastructure.Games;

/// <summary>Juegos de Epic Games: un manifiesto JSON (.item) por juego instalado.</summary>
/// <param name="manifestsFolder">Carpeta de manifiestos; null = la de siempre en ProgramData.</param>
public sealed class EpicStore(string? manifestsFolder = null) : IGameStore
{
    static string DefaultManifestsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");

    public string Name => "Epic";

    public IEnumerable<InstalledGame> FindInstalled()
    {
        string folder = manifestsFolder ?? DefaultManifestsFolder;
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*.item"))
            if (Read(file) is { } game) yield return game;
    }

    static InstalledGame? Read(string file)
    {
        using var document = TryParse(file);
        if (document is null) return null;
        var item = document.RootElement;

        string? name = Text(item, "DisplayName"), dir = Text(item, "InstallLocation");
        if (name is null || dir is null || !Directory.Exists(dir)) return null;
        if (item.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;
        if (!IsGame(item)) return null;

        string? exe = Text(item, "LaunchExecutable") is { Length: > 0 } relative ? Path.Combine(dir, relative) : ExecutableGuesser.Guess(dir);
        string? launch = Text(item, "CatalogNamespace") is { } ns && Text(item, "CatalogItemId") is { } id && Text(item, "AppName") is { } app
            ? $"com.epicgames.launcher://apps/{ns}%3A{id}%3A{app}?action=launch&silent=true"
            : null;
        return new InstalledGame(name, "Epic", dir, exe, launch);
    }

    static JsonDocument? TryParse(string file)
    {
        try { return JsonDocument.Parse(File.ReadAllText(file)); }
        catch (JsonException) { return null; }
    }

    // Epic también instala motores y herramientas: solo nos interesan los que dicen ser "games".
    static bool IsGame(JsonElement item) =>
        !item.TryGetProperty("AppCategories", out var categories) || categories.ValueKind != JsonValueKind.Array
        || categories.EnumerateArray().Any(c => c.GetString() == "games");

    static string? Text(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
