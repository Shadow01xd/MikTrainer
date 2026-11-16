using MiTrainer.App.Hotkeys;

namespace MiTrainer.App;

/// <summary>Los servicios que la interfaz necesita. Se crean una sola vez en <see cref="Program"/>.</summary>
sealed record AppServices(
    TrainerSession Session,
    IProcessConnector Connector,
    IProfileRepository Profiles,
    IPointerResultsRepository PointerResults,
    GameLibrary Games,
    HotkeyBinder Hotkeys);
