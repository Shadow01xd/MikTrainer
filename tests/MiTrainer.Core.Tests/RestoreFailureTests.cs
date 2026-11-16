using MiTrainer.Core.Cheats;
using MiTrainer.Core.Trainer;
using MiTrainer.TestSupport;

namespace MiTrainer.Core.Tests;

/// <summary>Si un parche no se puede deshacer, se avisa y no se pierde lo necesario para recuperarlo.</summary>
public class RestoreFailureTests
{
    const long Exe = 0x40_0000;
    const long Code = Exe + 0x200;
    static readonly byte[] Original = [0x29, 0x4B, 0x10];
    readonly FakeProcessMemory _game = new() { ProcessId = 7, ProcessName = "juego" };

    public RestoreFailureTests()
    {
        _game.AddModule("juego.exe", Exe, 0x1000);
        _game.SetBytes(Code, Original);
    }

    static CheatDefinition Patch(string name = "Munición infinita") =>
        new() { Name = name, Type = CheatType.Patch, Pattern = "29 4B 10", Bytes = "90 90 90" };

    Cheat AppliedPatch()
    {
        var cheat = new Cheat(Patch());
        Assert.True(cheat.Enable(_game));
        return cheat;
    }

    [Fact]
    public void Si_no_se_puede_escribir_queda_Restauracion_fallida_con_los_datos_para_recuperarlo()
    {
        var cheat = AppliedPatch();
        _game.FailWrites = true;

        bool restored = cheat.Disable(_game);

        Assert.False(restored);
        Assert.True(cheat.RestoreFailed);
        Assert.True(cheat.Failed);
        Assert.StartsWith("Restauración fallida", cheat.Status);
        var pending = cheat.PendingRestore!;
        Assert.Equal(("Munición infinita", _game.Identity, Code), (pending.CheatName, pending.Process, pending.Address));
        Assert.Equal(Original, pending.OriginalBytes);
        Assert.Contains("29 4B 10", cheat.Status); // el usuario ve qué bytes había
        Assert.Equal([0x90, 0x90, 0x90], _game.Get(Code, 3)); // el juego sigue modificado
    }

    [Fact]
    public void Reintentar_restaura_cuando_se_vuelve_a_poder_escribir()
    {
        var cheat = AppliedPatch();
        _game.FailWrites = true;
        cheat.Disable(_game);

        _game.FailWrites = false;
        bool restored = cheat.RetryRestore(_game);

        Assert.True(restored);
        Assert.False(cheat.RestoreFailed);
        Assert.Null(cheat.PendingRestore);
        Assert.Equal(Original, _game.Get(Code, 3));
    }

    [Fact]
    public void Con_la_restauracion_pendiente_no_se_vuelve_a_aplicar_el_parche()
    {
        var cheat = AppliedPatch();
        _game.FailWrites = true;
        cheat.Disable(_game);
        _game.FailWrites = false;

        Assert.False(cheat.Enable(_game));
        Assert.True(cheat.RestoreFailed);
    }

    [Fact]
    public void No_restaura_en_un_proceso_distinto_del_que_se_parcheo()
    {
        var pending = new PendingRestore("x", _game.Identity, "juego", Code, Original);
        var other = new FakeProcessMemory { ProcessId = 8 };
        other.AddRegion(Exe, 0x1000);

        Assert.False(pending.TryRestore(other));
    }

    // ================= En la sesión =================

    static GameProfile ProfileWithPatch() => new() { Game = "Juego", Process = "juego.exe", Cheats = [Patch()] };

    TrainerSession SessionWithAppliedPatch(FakeProcessConnector connector)
    {
        connector.Start(_game);
        var session = new TrainerSession(connector, new InMemoryProfileRepository());
        session.OpenProfile(ProfileWithPatch(), path: null);
        session.Toggle(session.Cheats[0]);
        Assert.True(session.Cheats[0].Enabled);
        return session;
    }

    [Fact]
    public void Pulsar_el_cheat_con_la_restauracion_fallida_la_reintenta()
    {
        var session = SessionWithAppliedPatch(new FakeProcessConnector());
        _game.FailWrites = true;
        session.Toggle(session.Cheats[0]);
        Assert.True(session.Cheats[0].RestoreFailed);

        _game.FailWrites = false;
        session.Toggle(session.Cheats[0]);

        Assert.False(session.Cheats[0].RestoreFailed);
        Assert.False(session.Cheats[0].Enabled); // reintentar no vuelve a activar el parche
        Assert.Equal(Original, _game.Get(Code, 3));
    }

    [Fact]
    public void Cambiar_de_perfil_conserva_el_parche_pendiente_y_la_sesion_lo_restaura_despues()
    {
        var session = SessionWithAppliedPatch(new FakeProcessConnector());
        var statuses = new List<SessionStatus>();
        session.StatusChanged += (_, s) => statuses.Add(s);
        _game.FailWrites = true;

        session.OpenProfile(new GameProfile { Game = "Otro", Process = "otro.exe" }, path: null);

        var pending = Assert.Single(session.PendingRestores);
        Assert.Equal(Code, pending.Address);
        Assert.Single(session.AllPendingRestores());

        _game.FailWrites = false;
        session.Watch();

        Assert.Empty(session.PendingRestores);
        Assert.Equal(Original, _game.Get(Code, 3));
        Assert.Contains(statuses, s => s.Text.Contains("restaurado") && s.Kind == StatusKind.Success);
    }

    [Fact]
    public void Si_el_juego_se_cierra_no_queda_nada_que_restaurar()
    {
        var connector = new FakeProcessConnector();
        var session = SessionWithAppliedPatch(connector);
        _game.FailWrites = true;
        session.OpenProfile(new GameProfile { Game = "Otro", Process = "otro.exe" }, path: null);
        Assert.Single(session.PendingRestores);

        connector.Exit(_game);
        session.Watch();

        Assert.Empty(session.AllPendingRestores()); // el parche desapareció con el proceso
    }

    // ================= Revisión de 0120a82: casos 2 y 3 =================

    [Fact]
    public void Desactivar_otra_vez_tras_la_restauracion_fallida_no_borra_los_datos()
    {
        var cheat = AppliedPatch();
        _game.FailWrites = true;
        cheat.Disable(_game);

        bool restored = cheat.Disable(_game); // segunda llamada (antes borraba la copia)

        Assert.False(restored);
        Assert.True(cheat.RestoreFailed);
        Assert.Equal(Original, cheat.PendingRestore!.OriginalBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Editar_o_eliminar_un_parche_con_la_restauracion_fallida_conserva_la_copia(bool edit)
    {
        var session = SessionWithAppliedPatch(new FakeProcessConnector());
        _game.FailWrites = true;
        session.Toggle(session.Cheats[0]); // restauración fallida
        var cheat = session.Cheats[0];

        if (edit) session.ReplaceCheat(cheat, cheat.Definition with { Name = "Otro nombre" });
        else session.RemoveCheat(cheat);

        var pending = Assert.Single(session.AllPendingRestores());
        Assert.Equal(Original, pending.OriginalBytes);
        Assert.Equal([0x90, 0x90, 0x90], _game.Get(Code, 3)); // el parche sigue aplicado

        _game.FailWrites = false;
        session.Watch();
        Assert.Equal(Original, _game.Get(Code, 3)); // y se pudo recuperar después
    }

    [Fact]
    public void No_restaura_en_otro_proceso_que_reutiliza_el_PID()
    {
        var cheat = AppliedPatch();
        _game.FailWrites = true;
        cheat.Disable(_game);

        // El juego se cerró y Windows le dio el mismo PID a otro proceso (que arrancó después)
        var reused = new FakeProcessMemory { ProcessId = _game.ProcessId, ProcessName = "juego", StartTime = _game.StartTime.AddMinutes(5) };
        reused.AddModule("juego.exe", Exe, 0x1000);
        reused.SetBytes(Code, 0xAA, 0xBB, 0xCC);

        Assert.False(cheat.RetryRestore(reused));
        Assert.Equal([0xAA, 0xBB, 0xCC], reused.Get(Code, 3));
        Assert.True(cheat.RestoreFailed);
    }

    [Fact]
    public void La_sesion_no_restaura_en_un_proceso_que_reutiliza_el_PID()
    {
        var connector = new FakeProcessConnector();
        var session = SessionWithAppliedPatch(connector);
        _game.FailWrites = true;
        session.OpenProfile(new GameProfile { Game = "Otro", Process = "otro.exe" }, path: null); // el pendiente pasa a la sesión
        connector.Start(new FakeProcessMemory { ProcessId = 99, ProcessName = "otro" });
        session.Watch();                     // la sesión se va a otro.exe sin ver cerrarse el juego original
        connector.Exit(_game);

        var reused = connector.Start(new FakeProcessMemory { ProcessId = _game.ProcessId, ProcessName = "juego", StartTime = _game.StartTime.AddMinutes(5) });
        reused.AddModule("juego.exe", Exe, 0x1000);
        reused.SetBytes(Code, 0xAA, 0xBB, 0xCC);
        session.Connect(reused.ProcessId);
        session.Watch();

        Assert.Equal([0xAA, 0xBB, 0xCC], reused.Get(Code, 3));
        Assert.Single(session.PendingRestores);
    }

    [Fact]
    public void Sin_hora_de_inicio_la_identidad_nunca_se_da_por_buena()
    {
        var unknown = new Memory.ProcessIdentity(7, default);

        Assert.False(unknown.IsSameProcessAs(unknown));
        Assert.True(_game.Identity.IsSameProcessAs(_game.Identity));
    }
}
