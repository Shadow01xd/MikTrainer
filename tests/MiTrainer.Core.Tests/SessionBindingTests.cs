using MiTrainer.Core.Cheats;
using MiTrainer.Core.Trainer;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

/// <summary>El perfil activo solo actúa sobre el proceso al que está vinculado.</summary>
public class SessionBindingTests
{
    const long Life = 0x50_0010;
    readonly FakeProcessConnector _connector = new();
    readonly InMemoryProfileRepository _profiles = new();
    readonly TrainerSession _session;

    public SessionBindingTests()
    {
        _session = new TrainerSession(_connector, _profiles);
    }

    static FakeProcessMemory Game(int id, string name)
    {
        var game = new FakeProcessMemory { ProcessId = id, ProcessName = name };
        game.AddModule(name + ".exe", 0x40_0000 + id * 0x10_0000, 0x1000);
        game.AddRegion(0x50_0000, 0x1000);
        return game;
    }

    static GameProfile ProfileFor(string? executable) => new()
    {
        Game = executable ?? "Sin ejecutable",
        Process = executable ?? "",
        Cheats = [new CheatDefinition { Name = "Vida infinita", Type = CheatType.Freeze, Address = $"{Life:X}", Value = 100 }],
    };

    [Fact]
    public void El_perfil_del_juego_conectado_queda_vinculado_y_se_puede_usar()
    {
        var game = _connector.Start(Game(1, "juego"));
        _session.Connect(1);

        _session.OpenProfile(ProfileFor("juego.exe"), path: null);
        _session.Toggle(_session.Cheats[0]);

        Assert.Equal(SessionState.Ready, _session.State);
        Assert.True(_session.Cheats[0].Enabled);
        Assert.Equal(100, game.GetInt32(Life));
    }

    [Fact]
    public void Abrir_el_perfil_de_otro_juego_bloquea_sus_opciones_sobre_el_proceso_conectado()
    {
        var game = _connector.Start(Game(1, "juego"));
        _session.Connect(1);

        _session.OpenProfile(ProfileFor("otro.exe"), path: null);
        _session.Toggle(_session.Cheats[0]);

        Assert.Equal(SessionState.AwaitingConfirmation, _session.State);
        Assert.Contains("otro.exe", _session.BlockReason);
        Assert.Contains("juego.exe", _session.BlockReason);
        Assert.True(_session.Cheats[0].Failed);
        Assert.Equal(0, game.GetInt32(Life)); // no se escribió nada en el juego equivocado
    }

    [Fact]
    public void Al_cambiar_de_perfil_los_cheats_del_anterior_dejan_de_actuar()
    {
        var game = _connector.Start(Game(1, "juego"));
        _session.Connect(1);
        _session.OpenProfile(ProfileFor("juego.exe"), path: null);
        var previous = _session.Cheats[0];
        _session.Toggle(previous);

        _session.OpenProfile(ProfileFor("otro.exe"), path: null);
        game.SetInt32(Life, 1);
        _session.Tick();

        Assert.False(previous.Enabled);
        Assert.Equal(1, game.GetInt32(Life));
    }

    [Fact]
    public void Cuando_aparece_el_proceso_del_perfil_la_sesion_se_pasa_a_el()
    {
        _connector.Start(Game(1, "juego"));
        _session.Connect(1);
        _session.OpenProfile(ProfileFor("otro.exe"), path: null);

        var other = _connector.Start(Game(2, "otro"));
        _session.Watch();

        Assert.Same(other, _session.Memory);
        Assert.Equal(SessionState.Ready, _session.State);
    }

    [Fact]
    public void Un_perfil_sin_ejecutable_espera_confirmacion_y_al_confirmar_lo_guarda()
    {
        _connector.Start(Game(1, "juego"));
        const string path = "perfiles/nuevo.json";
        _profiles.Save(path, ProfileFor(null));
        _session.OpenProfile(_profiles.Load(path), path);
        _session.Connect(1);

        _session.Toggle(_session.Cheats[0]);
        Assert.True(_session.Cheats[0].Failed);
        Assert.Equal(SessionState.AwaitingConfirmation, _session.State);

        _session.ConfirmSession();

        Assert.Equal(SessionState.Ready, _session.State);
        Assert.Equal("juego.exe", _session.Profile.Process);
        Assert.Equal("juego.exe", _profiles.Load(path).Process);
    }

    [Fact]
    public void Confirmar_corrige_un_ejecutable_equivocado_del_perfil()
    {
        _connector.Start(Game(1, "juego"));
        _session.OpenProfile(ProfileFor("Launcher.exe"), path: null); // p. ej. se adivinó el lanzador
        _session.Connect(1);

        _session.ConfirmSession();

        Assert.Equal(SessionState.Ready, _session.State);
        Assert.Equal("juego.exe", _session.Profile.Process);
    }

    [Fact]
    public void Conectarse_a_mano_a_otro_proceso_rompe_el_vinculo()
    {
        _connector.Start(Game(1, "juego"));
        _connector.Start(Game(2, "otro"));
        _session.OpenProfile(ProfileFor("juego.exe"), path: null);
        _session.Connect(1);
        Assert.Equal(SessionState.Ready, _session.State);

        _session.Connect(2);

        Assert.Equal(SessionState.AwaitingConfirmation, _session.State);
    }

    [Fact]
    public void Avisa_de_cada_cambio_de_estado()
    {
        _connector.Start(Game(1, "juego"));
        var states = new List<SessionState>();
        _session.StateChanged += (_, _) => states.Add(_session.State);

        _session.OpenProfile(ProfileFor(null), path: null);
        _session.Connect(1);
        _session.ConfirmSession();

        Assert.Equal([SessionState.AwaitingConfirmation, SessionState.Ready], states);
    }

    // ================= Tareas atadas a la conexión =================

    [Fact]
    public void Una_tarea_deja_de_valer_y_se_cancela_si_cambia_la_conexion()
    {
        _connector.Start(Game(1, "juego"));
        _connector.Start(Game(2, "otro"));
        _session.Connect(1);
        var operation = _session.BeginOperation()!;
        Assert.True(_session.IsCurrent(operation));

        _session.Connect(2);

        Assert.False(_session.IsCurrent(operation));
        Assert.True(operation.Cancellation.IsCancellationRequested);
        Assert.True(_session.IsCurrent(_session.BeginOperation()!));
    }

    [Fact]
    public void Una_tarea_deja_de_valer_si_el_juego_se_cierra()
    {
        var game = _connector.Start(Game(1, "juego"));
        _session.Connect(1);
        var operation = _session.BeginOperation()!;

        _connector.Exit(game);
        _session.Watch();

        Assert.False(_session.IsCurrent(operation));
        Assert.True(operation.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void Cambiar_de_perfil_no_invalida_las_tareas_de_la_misma_conexion()
    {
        // Las direcciones de un escaneo dependen del proceso, no del perfil abierto.
        _connector.Start(Game(1, "juego"));
        _session.Connect(1);
        var operation = _session.BeginOperation()!;

        _session.OpenProfile(ProfileFor("otro.exe"), path: null);

        Assert.True(_session.IsCurrent(operation));
    }

    [Fact]
    public void Sin_juego_conectado_no_se_puede_empezar_una_tarea()
    {
        Assert.Null(_session.BeginOperation());
    }
}
