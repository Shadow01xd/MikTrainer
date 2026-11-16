using MiTrainer.App.Hotkeys;
using MiTrainer.App.Pages;
using MiTrainer.Core.Games;
using MiTrainer.Core.Trainer;
using MiTrainer.Core.Values;
using MiTrainer.TestSupport;

namespace MiTrainer.App.Tests;

/// <summary>
/// Un escaneo pertenece a la sesión que lo inició: si la sesión cambia mientras corre, su resultado se descarta
/// (ni se muestra ni se guarda). Las lecturas de la memoria falsa se pausan para cambiar la sesión justo a mitad.
/// </summary>
public sealed class SessionBoundScanTests : IDisposable
{
    const long Heap = 0x50_0000, Life = Heap + 0x10;
    readonly FakeProcessConnector _connector = new();
    readonly InMemoryPointerResultsRepository _pointerResults = new();
    readonly TrainerSession _session;
    readonly FakeProcessMemory _first;

    public SessionBoundScanTests()
    {
        _first = _connector.Start(Game(1, "juego"));
        _connector.Start(Game(2, "otro"));
        _first.SetInt32(Life, 100);
        _session = new TrainerSession(_connector, new InMemoryProfileRepository());
        _session.Connect(1);
    }

    public void Dispose() => _session.Dispose();

    static FakeProcessMemory Game(int id, string name)
    {
        var game = new FakeProcessMemory { ProcessId = id, ProcessName = name };
        game.AddModule(name + ".exe", 0x40_0000, 0x1000);
        game.AddRegion(Heap, 0x1000);
        return game;
    }

    AppServices Services(HotkeyBinder hotkeys) =>
        new(_session, _connector, new InMemoryProfileRepository(), _pointerResults, new GameLibrary([]), hotkeys);

    /// <summary>Pausa las lecturas de <paramref name="game"/> hasta que <paramref name="release"/> se active.</summary>
    static ManualResetEventSlim PauseReads(FakeProcessMemory game, ManualResetEventSlim release)
    {
        var started = new ManualResetEventSlim();
        game.BeforeRead = () =>
        {
            started.Set();
            release.Wait();
        };
        return started;
    }

    // ================= Escáner =================

    [Fact]
    public void Escaner_sin_cambios_de_sesion_muestra_su_resultado() => Sta.Run(() =>
    {
        using var hotkeys = new HotkeyBinder(_session);
        using var page = new ScannerPage(Services(hotkeys));
        page.CreateControl();
        page.ValueInput.Text = "100";

        var scan = page.RunScan(first: true);
        WaitFor(() => scan.IsCompleted);

        Assert.Equal(1, page.ResultCount);
        Assert.StartsWith("1 resultados", page.StatusText);
    });

    [Fact]
    public void Escaner_descarta_el_resultado_si_la_sesion_cambia_mientras_escanea() => Sta.Run(() =>
    {
        using var hotkeys = new HotkeyBinder(_session);
        using var page = new ScannerPage(Services(hotkeys));
        page.CreateControl();
        page.ValueInput.Text = "100";
        using var release = new ManualResetEventSlim();
        using var started = PauseReads(_first, release);

        var scan = page.RunScan(first: true);
        Assert.True(started.Wait(5000), "el escaneo no empezó a leer");
        _session.Connect(2);        // la sesión cambia a mitad del escaneo
        release.Set();
        WaitFor(() => scan.IsCompleted);

        Assert.Equal(0, page.ResultCount);
        Assert.Equal(ScannerPage.DiscardedMessage, page.StatusText);
    });

    // ================= Punteros =================

    [Fact]
    public void Punteros_sin_cambios_de_sesion_guarda_su_resultado() => Sta.Run(() =>
    {
        using var hotkeys = new HotkeyBinder(_session);
        using var page = new PointersPage(Services(hotkeys));
        page.CreateControl();
        page.Prepare(Life, ValueKind.Int32);

        var scan = page.ScanOrCancel();
        WaitFor(() => scan.IsCompleted);

        Assert.Equal(1, _pointerResults.SaveCount);
        Assert.DoesNotContain("descartado", page.StatusText);
    });

    [Fact]
    public void Punteros_descarta_y_no_guarda_si_la_sesion_cambia_mientras_busca() => Sta.Run(() =>
    {
        using var hotkeys = new HotkeyBinder(_session);
        using var page = new PointersPage(Services(hotkeys));
        page.CreateControl();
        page.Prepare(Life, ValueKind.Int32);
        using var release = new ManualResetEventSlim();
        using var started = PauseReads(_first, release);

        var scan = page.ScanOrCancel();
        Assert.True(started.Wait(5000), "la búsqueda no empezó a leer");
        _session.Connect(2);        // la sesión cambia a mitad de la búsqueda
        release.Set();
        WaitFor(() => scan.IsCompleted);

        Assert.Equal(0, page.ResultCount);
        Assert.Equal(PointersPage.DiscardedMessage, page.StatusText);
        Assert.Equal(0, _pointerResults.SaveCount); // no se guardó nada con el nombre del proceso equivocado
    });

    // ================= Filtro de punteros (revisión de 0120a82: caso 4) =================

    /// <summary>Página con una cadena estática ya encontrada: "juego.exe"+80 → 18 → 10 = vida.</summary>
    PointersPage PageWithOneChain(AppServices services)
    {
        _first.SetInt64(0x40_0080, Heap + 0x100);
        _first.SetInt64(Heap + 0x118, Life - 0x10);
        var page = new PointersPage(services);
        page.CreateControl();
        page.Prepare(Life, ValueKind.Int32);
        var scan = page.ScanOrCancel();
        WaitFor(() => scan.IsCompleted);
        Assert.True(page.ResultCount >= 1, page.StatusText);
        return page;
    }

    [Fact]
    public void Filtrar_mientras_el_juego_anterior_se_cierra_descarta_sin_errores() => Sta.Run(() =>
    {
        using var hotkeys = new HotkeyBinder(_session);
        using var page = PageWithOneChain(Services(hotkeys));
        int chains = page.ResultCount, saves = _pointerResults.SaveCount;
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        _first.BeforeGetModules = () =>
        {
            started.Set();
            release.Wait();
        };

        var filter = page.Filter();
        Assert.True(started.Wait(5000), "el filtro no consultó los módulos");
        _session.Connect(2);          // cambiamos de juego...
        _first.ModulesThrow = true;   // ...y el anterior se cierra mientras se consultaban sus módulos
        release.Set();
        WaitFor(() => filter.IsCompleted);

        Assert.True(filter.IsCompletedSuccessfully, filter.Exception?.ToString()); // la excepción no llegó a la interfaz
        Assert.Equal(PointersPage.DiscardedMessage, page.StatusText);
        Assert.Equal(saves, _pointerResults.SaveCount);
    });

    [Fact]
    public void Si_el_filtro_falla_sin_cambiar_de_sesion_se_muestra_el_error() => Sta.Run(() =>
    {
        using var hotkeys = new HotkeyBinder(_session);
        using var page = PageWithOneChain(Services(hotkeys));
        int chains = page.ResultCount;
        _first.ModulesThrow = true;

        var filter = page.Filter();
        WaitFor(() => filter.IsCompleted);

        Assert.True(filter.IsCompletedSuccessfully, filter.Exception?.ToString());
        Assert.StartsWith("Error al filtrar", page.StatusText);
        Assert.Equal(chains, page.ResultCount); // las cadenas no se tocan
    });

    /// <summary>Procesa mensajes (las continuaciones de los await vuelven al hilo de la interfaz) hasta que se cumpla.</summary>
    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(condition(), "La condición no se cumplió a tiempo");
    }
}
