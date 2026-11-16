using MiTrainer.Core.Cheats;
using MiTrainer.Core.Trainer;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

public class TrainerSessionTests
{
    const long Heap = 0x50_0000;
    readonly FakeProcessConnector _connector = new();
    readonly InMemoryProfileRepository _profiles = new();
    readonly TrainerSession _session;

    public TrainerSessionTests()
    {
        _session = new TrainerSession(_connector, _profiles);
    }

    static FakeProcessMemory NewGame(int id = 1)
    {
        var game = new FakeProcessMemory { ProcessId = id, ProcessName = "juego" };
        game.AddModule("juego.exe", 0x40_0000, 0x1000);
        game.AddRegion(Heap, 0x1000);
        return game;
    }

    static GameProfile ProfileWithLife() => new()
    {
        Game = "Juego",
        Process = "juego.exe",
        Cheats = [new CheatDefinition { Name = "Vida infinita", Type = CheatType.Freeze, Address = $"{Heap:X}", Value = 100, Hotkey = "F1" }],
    };

    [Fact]
    public void Se_conecta_sola_cuando_aparece_el_proceso_del_perfil()
    {
        _session.OpenProfile(ProfileWithLife(), path: null);
        Assert.False(_session.IsConnected);

        var game = _connector.Start(NewGame());
        _session.Watch();

        Assert.True(_session.IsConnected);
        Assert.Same(game, _session.Memory);
    }

    [Fact]
    public void Activar_sin_juego_marca_el_cheat_como_fallido()
    {
        _session.OpenProfile(ProfileWithLife(), path: null);
        var cheat = _session.Cheats[0];

        _session.Toggle(cheat);

        Assert.True(cheat.Failed);
        Assert.Equal("Conecta el juego primero", cheat.Status);
    }

    [Fact]
    public void Tick_mantiene_el_valor_congelado()
    {
        var game = _connector.Start(NewGame());
        _session.OpenProfile(ProfileWithLife(), path: null);
        _session.Toggle(_session.Cheats[0]);

        game.SetInt32(Heap, 1);
        _session.Tick();

        Assert.Equal(100, game.GetInt32(Heap));
    }

    [Fact]
    public void Cuando_el_juego_se_cierra_los_cheats_se_reinician_y_se_avisa()
    {
        var game = _connector.Start(NewGame());
        _session.OpenProfile(ProfileWithLife(), path: null);
        _session.Toggle(_session.Cheats[0]);
        var statuses = new List<SessionStatus>();
        _session.StatusChanged += (_, s) => statuses.Add(s);

        _connector.Exit(game);
        _session.Watch();

        Assert.False(_session.IsConnected);
        Assert.False(_session.Cheats[0].Enabled);
        Assert.Contains(statuses, s => s.Text == "El juego se cerró" && s.Kind == StatusKind.Error);
        Assert.True(game.Disposed);
    }

    [Fact]
    public void Los_cambios_se_guardan_solos_si_el_perfil_tiene_archivo()
    {
        _session.OpenProfile(ProfileWithLife(), "perfiles/juego.json");

        _session.AddCheat(new CheatDefinition { Name = "Oro", Type = CheatType.Set, Address = "20", Value = 999 });

        Assert.Equal(ProfileSaveState.Saved, _session.SaveState);
        Assert.Equal(2, _profiles.Load("perfiles/juego.json").Cheats.Count);
    }

    [Fact]
    public void Sin_archivo_los_cambios_quedan_pendientes_hasta_guardar()
    {
        _session.OpenProfile(ProfileWithLife(), path: null);
        _session.AddCheat(new CheatDefinition { Name = "Oro" });
        Assert.Equal(ProfileSaveState.Unsaved, _session.SaveState);
        Assert.Equal(0, _profiles.SaveCount);

        _session.SaveProfileAs("perfiles/nuevo.json");

        Assert.Equal(ProfileSaveState.Saved, _session.SaveState);
        Assert.Equal("perfiles/nuevo.json", _session.ProfilePath);
    }

    [Fact]
    public void Editar_un_parche_activo_lo_deshace_primero()
    {
        var game = _connector.Start(NewGame());
        game.SetBytes(0x40_0100, 0x29, 0x4B, 0x10);
        _session.Connect(game.ProcessId);
        _session.OpenProfile(new GameProfile { Cheats = [new CheatDefinition { Name = "Parche", Type = CheatType.Patch, Pattern = "29 4B 10", Bytes = "90 90 90" }] }, null);
        _session.Toggle(_session.Cheats[0]);

        _session.ReplaceCheat(_session.Cheats[0], _session.Cheats[0].Definition with { Name = "Otro nombre" });

        Assert.Equal([0x29, 0x4B, 0x10], game.Get(0x40_0100, 3));
        Assert.Equal("Otro nombre", _session.Profile.Cheats[0].Name);
    }

    [Fact]
    public void Propone_la_primera_tecla_F_libre()
    {
        _session.OpenProfile(ProfileWithLife(), path: null); // usa F1

        Assert.Equal("F2", _session.NextFreeHotkey());
    }
}
