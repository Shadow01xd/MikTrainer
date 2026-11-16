using MiTrainer.Core.Cheats;
using MiTrainer.Core.Pointers;
using MiTrainer.Core.Values;
using MiTrainer.Infrastructure.Storage;

namespace MiTrainer.Infrastructure.Tests;

public sealed class RepositoryTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("mitrainer-datos-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Un_perfil_guardado_vuelve_igual()
    {
        var repository = new JsonProfileRepository(_folder);
        var profile = new GameProfile
        {
            Game = "Baldur's Gate 3",
            Process = "bg3.exe",
            Cheats = [new CheatDefinition { Name = "Oro ñ", Type = CheatType.Set, Address = "1A2B", Offsets = ["18"], ValueType = ValueKind.Int64, Value = 99999, Hotkey = "Ctrl+F2" }],
        };
        string path = repository.NewPathFor(profile.Game);

        repository.Save(path, profile);
        var loaded = repository.Load(path);

        Assert.Equal(profile.Game, loaded.Game);
        Assert.Equal(profile.Cheats[0] with { Offsets = null }, loaded.Cheats[0] with { Offsets = null });
        Assert.Equal(["18"], loaded.Cheats[0].Offsets);
        Assert.Contains("Oro ñ", File.ReadAllText(path)); // tildes legibles en el archivo
    }

    [Fact]
    public void Lee_el_perfil_de_ejemplo_con_comentarios()
    {
        var profile = new JsonProfileRepository(_folder).Load(Path.Combine(AppContext.BaseDirectory, "Datos", "ejemplo.json"));

        Assert.Equal(3, profile.Cheats.Count);
        Assert.Equal(CheatType.Patch, profile.Cheats[2].Type);
    }

    [Fact]
    public void Los_nombres_de_archivo_nuevos_no_pisan_los_existentes()
    {
        var repository = new JsonProfileRepository(_folder);
        string first = repository.NewPathFor("Stellar Blade™");
        repository.Save(first, new GameProfile());

        string second = repository.NewPathFor("Stellar Blade™");

        Assert.EndsWith("Stellar Blade.json", first);
        Assert.EndsWith("Stellar Blade (2).json", second);
        Assert.Equal([first], repository.List());
    }

    [Fact]
    public void Los_resultados_de_punteros_vuelven_identicos()
    {
        var repository = new JsonPointerResultsRepository(_folder);
        var results = new PointerScanResults("juego", ValueKind.Float, Filters: 2, new DateTime(2026, 11, 16, 12, 30, 0),
            [new PointerPath("juego.exe", 0x2000, [0x18, 0x10]), new PointerPath("motor.dll", 0x40, [0x8])]);
        string path = repository.AutoSavePathFor("juego");

        repository.Save(path, results);
        var loaded = repository.Load(path);

        Assert.Equal(results with { Paths = [] }, loaded with { Paths = [] });
        Assert.Equal(results.Paths, loaded.Paths);
        Assert.False(File.Exists(path + ".tmp"));
    }
}
