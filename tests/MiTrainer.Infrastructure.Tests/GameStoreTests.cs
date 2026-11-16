using System.Text.Json;
using MiTrainer.Infrastructure.Games;

namespace MiTrainer.Infrastructure.Tests;

/// <summary>Las tiendas, contra carpetas falsas creadas en un directorio temporal.</summary>
public sealed class GameStoreTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("mitrainer-tiendas-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    string Folder(params string[] parts)
    {
        string path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    static void Exe(string path, int size) => File.WriteAllBytes(path, new byte[size]);

    [Fact]
    public void Steam_lee_todas_las_bibliotecas_y_descarta_las_herramientas()
    {
        string steam = Folder("Steam"), other = Folder("D", "SteamLibrary");
        Folder("Steam", "steamapps");
        Folder("D", "SteamLibrary", "steamapps");
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            $"\"libraryfolders\"\n{{\n\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{Escape(steam)}\"\n\t}}\n\t\"1\"\n\t{{\n\t\t\"path\"\t\t\"{Escape(other)}\"\n\t}}\n}}");
        SteamGame(steam, "1086940", "Baldur's Gate 3", "Baldurs Gate 3");
        SteamGame(other, "105600", "Terraria", "Terraria");
        SteamGame(steam, "228980", "Steamworks Common Redistributables", "Steamworks Shared");

        var games = new SteamStore(steam).FindInstalled().ToList();

        Assert.Equal(["Baldur's Gate 3", "Terraria"], games.Select(g => g.Name).Order());
        Assert.Equal("steam://rungameid/1086940", games.Single(g => g.Name == "Baldur's Gate 3").LaunchUri);
    }

    void SteamGame(string library, string id, string name, string folder)
    {
        File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{id}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{id}\"\n\t\"name\"\t\t\"{name}\"\n\t\"installdir\"\t\t\"{folder}\"\n}}");
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", folder));
    }

    static string Escape(string path) => path.Replace(@"\", @"\\");

    [Fact]
    public void Epic_detecta_juegos_con_su_exe_e_ignora_lo_que_no_es_juego()
    {
        string manifests = Folder("Epic"), game = Folder("Games", "StellarBlade");
        File.WriteAllText(Path.Combine(manifests, "a.item"), JsonSerializer.Serialize(new
        {
            DisplayName = "Stellar Blade", InstallLocation = game, LaunchExecutable = "SB.exe",
            CatalogNamespace = "ns", CatalogItemId = "item", AppName = "Stellar", AppCategories = new[] { "public", "games" },
        }));
        File.WriteAllText(Path.Combine(manifests, "b.item"), JsonSerializer.Serialize(new
        {
            DisplayName = "Unreal Engine", InstallLocation = game, AppCategories = new[] { "engines" },
        }));
        File.WriteAllText(Path.Combine(manifests, "roto.item"), "{ esto no es json");

        var games = new EpicStore(manifests).FindInstalled().ToList();

        var sb = Assert.Single(games);
        Assert.Equal("SB.exe", sb.ProcessName);
        Assert.StartsWith("com.epicgames.launcher://apps/ns%3Aitem%3AStellar", sb.LaunchUri);
    }

    [Fact]
    public void El_exe_adivinado_no_es_el_instalador_ni_el_reporte_de_errores()
    {
        string dir = Folder("Baldurs Gate 3");
        Folder("Baldurs Gate 3", "bin");
        Exe(Path.Combine(dir, "bin", "bg3.exe"), 50_000);
        Exe(Path.Combine(dir, "bin", "CrashReporter.exe"), 90_000);
        Exe(Path.Combine(dir, "unins000.exe"), 90_000);

        Assert.EndsWith(@"bin\bg3.exe", ExecutableGuesser.Guess(dir));
    }

    [Fact]
    public void El_exe_que_se_parece_al_nombre_del_juego_gana_aunque_sea_pequeno()
    {
        string dir = Folder("No Rest for the Wicked");
        Exe(Path.Combine(dir, "NoRestForTheWicked.exe"), 10_000);
        Exe(Path.Combine(dir, "otra_herramienta.exe"), 30_000);

        Assert.EndsWith("NoRestForTheWicked.exe", ExecutableGuesser.Guess(dir));
    }

    [Fact]
    public void En_Unreal_prefiere_el_ejecutable_Shipping()
    {
        string dir = Folder("Juego");
        Folder("Juego", "SB", "Binaries", "Win64");
        Exe(Path.Combine(dir, "SB", "Binaries", "Win64", "SB-Win64-Shipping.exe"), 20_000);
        Exe(Path.Combine(dir, "Tool.exe"), 60_000);

        Assert.EndsWith("SB-Win64-Shipping.exe", ExecutableGuesser.Guess(dir));
    }
}
