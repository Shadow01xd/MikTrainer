using MiTrainer.Core.Cheats;
using MiTrainer.Core.Trainer;
using MiTrainer.Core.Values;
using MiTrainer.Infrastructure.Memory;
using MiTrainer.TestSupport;

namespace MiTrainer.Infrastructure.Tests;

/// <summary>El flujo normal de la sesión contra el juego de prueba real (procesos y memoria de Windows).</summary>
public sealed class SessionWithTestGameTests : IDisposable
{
    readonly TestGame _game = new();
    readonly TrainerSession _session = new(new WindowsProcessConnector(), new InMemoryProfileRepository());

    public void Dispose()
    {
        _session.Dispose();
        _game.Dispose();
    }

    GameProfile Profile(string process) => new()
    {
        Game = "Juego de prueba",
        Process = process,
        Cheats = [new CheatDefinition { Name = "Vida infinita", Type = CheatType.Freeze, Address = NumberText.ToHex(_game.LifeAddress), Value = 500 }],
    };

    [Fact]
    public void Perfil_vinculado_al_juego_congela_la_vida()
    {
        _session.OpenProfile(Profile("JuegoPrueba.exe"), path: null);
        _session.Connect(_game.Memory.ProcessId); // a este proceso en concreto (puede haber otros juegos de prueba abiertos)

        Assert.Equal(SessionState.Ready, _session.State);
        _session.Toggle(_session.Cheats[0]);
        _game.TakeDamage();
        _session.Tick();

        Assert.Equal(500, _game.ReadLife());
    }

    [Fact]
    public void Con_el_perfil_de_otro_juego_las_opciones_no_tocan_este_proceso()
    {
        _session.OpenProfile(Profile("JuegoPrueba.exe"), path: null);
        _session.Connect(_game.Memory.ProcessId);
        int before = _game.ReadLife();

        _session.OpenProfile(Profile("OtroJuego.exe"), path: null);
        _session.Toggle(_session.Cheats[0]);
        _session.Tick();

        Assert.Equal(SessionState.AwaitingConfirmation, _session.State);
        Assert.True(_session.Cheats[0].Failed);
        Assert.Equal(before - 7, _game.TakeDamage()); // la vida sigue bajando: no se congeló nada
    }
}
