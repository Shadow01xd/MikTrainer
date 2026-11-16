using Microsoft.Win32;
using MiTrainer.Core.Games;

namespace MiTrainer.Infrastructure.Games;

/// <summary>Juegos de GOG: cada uno deja una clave en el registro con su nombre, carpeta y ejecutable.</summary>
public sealed class GogStore : IGameStore
{
    const string GamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    public string Name => "GOG";

    public IEnumerable<InstalledGame> FindInstalled()
    {
        using var games = Registry.LocalMachine.OpenSubKey(GamesKey);
        if (games is null) yield break;
        foreach (var id in games.GetSubKeyNames())
        {
            using var key = games.OpenSubKey(id);
            if (key?.GetValue("gameName") is not string name || key.GetValue("path") is not string dir || !Directory.Exists(dir)) continue;
            string? exe = key.GetValue("exe") as string;
            // Los juegos de GOG no tienen DRM: se abren directamente con su .exe.
            yield return new InstalledGame(name, "GOG", dir, File.Exists(exe) ? exe : ExecutableGuesser.Guess(dir), LaunchUri: null);
        }
    }
}
