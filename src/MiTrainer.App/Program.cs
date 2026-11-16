using MiTrainer.App.Hotkeys;
using MiTrainer.App.Shell;
using MiTrainer.Infrastructure.Games;
using MiTrainer.Infrastructure.Memory;
using MiTrainer.Infrastructure.Storage;

namespace MiTrainer.App;

static class Program
{
    /// <summary>Raíz de composición: el único sitio donde se eligen las implementaciones concretas.</summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // modo oscuro de WinForms (barra de título, listas, desplegables)
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001

        string data = AppContext.BaseDirectory;
        var profiles = new JsonProfileRepository(Path.Combine(data, "perfiles"));
        var pointerResults = new JsonPointerResultsRepository(Path.Combine(data, "punteros"));
        var connector = new WindowsProcessConnector();
        var games = new GameLibrary([new SteamStore(), new EpicStore(), new GogStore()]);

        using var session = new TrainerSession(connector, profiles);
        using var hotkeys = new HotkeyBinder(session);
        Application.Run(new MainForm(new AppServices(session, connector, profiles, pointerResults, games, hotkeys)));
    }
}
