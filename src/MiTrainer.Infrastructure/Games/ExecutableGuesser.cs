namespace MiTrainer.Infrastructure.Games;

/// <summary>
/// Adivina cuál es el .exe de un juego dentro de su carpeta (Steam no lo dice). Se queda con el más grande que no sea
/// un instalador, lanzador o similar, y da ventaja a los que se parecen al nombre de la carpeta o son el
/// "-Shipping" de Unreal. Busca hasta 3 carpetas de profundidad, que es donde suelen estar.
/// </summary>
public static class ExecutableGuesser
{
    const int MaxDepth = 3;
    const double PreferredBonus = 4;

    // Ejecutables que nunca son el juego.
    static readonly string[] NotTheGame =
    [
        "unins", "setup", "install", "redist", "vcredist", "dxsetup", "crash", "report", "launcher", "helper", "update",
        "easyanticheat", "battleye", "ue4prereq", "uninstall", "dotnet", "directx", "physx", "cef", "webhelper",
        "bootstrap", "ffmpeg", "minidump", "stackwalk", "translator", "python", "node", "7z", "dumper", "benchmark",
    ];

    public static string? Guess(string installDir)
    {
        var candidates = new List<FileInfo>();
        Collect(new DirectoryInfo(installDir), 0, candidates);

        string folder = Letters(Path.GetFileName(installDir.TrimEnd('\\', '/')));
        return candidates.MaxBy(exe => Score(exe, folder))?.FullName;
    }

    static void Collect(DirectoryInfo dir, int depth, List<FileInfo> candidates)
    {
        try
        {
            candidates.AddRange(dir.EnumerateFiles("*.exe").Where(f => !NotTheGame.Any(n => f.Name.Contains(n, StringComparison.OrdinalIgnoreCase))));
            if (depth == MaxDepth) return;
            foreach (var sub in dir.EnumerateDirectories())
                if (!sub.Name.StartsWith('_') && !sub.Name.Equals("redist", StringComparison.OrdinalIgnoreCase))
                    Collect(sub, depth + 1, candidates);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    static double Score(FileInfo exe, string folder)
    {
        double score = exe.Length;
        if (exe.Name.Contains("-Shipping", StringComparison.OrdinalIgnoreCase)) score *= PreferredBonus;
        if (ResemblesFolder(Letters(Path.GetFileNameWithoutExtension(exe.Name)), folder)) score *= PreferredBonus;
        return score;
    }

    // "NoRestForTheWicked.exe" en la carpeta "No Rest for the Wicked": comparten el principio.
    static bool ResemblesFolder(string name, string folder) =>
        name.Length >= 3 && folder.Length >= 3
        && (folder.StartsWith(name[..Math.Min(4, name.Length)]) || name.StartsWith(folder[..Math.Min(4, folder.Length)]));

    static string Letters(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
