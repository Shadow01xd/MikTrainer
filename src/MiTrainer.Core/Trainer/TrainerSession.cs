using MiTrainer.Core.Cheats;
using MiTrainer.Core.Memory;

namespace MiTrainer.Core.Trainer;

public enum StatusKind { Neutral, Success, Error }

/// <summary>Mensaje de estado para la cabecera ("Conectado a juego.exe", "El juego se cerró"...).</summary>
public sealed record SessionStatus(string Text, StatusKind Kind);

public enum ProfileSaveState { Unsaved, Saved, Error }

/// <summary>
/// El trainer en marcha: a qué juego estamos conectados, qué perfil está abierto y el estado de sus cheats.
/// No sabe nada de ventanas: la interfaz llama a sus métodos y escucha sus eventos.
/// </summary>
public sealed class TrainerSession(IProcessConnector connector, IProfileRepository profiles) : IDisposable
{
    readonly List<Cheat> _cheats = [];

    public IProcessMemory? Memory { get; private set; }
    public bool IsConnected => Memory is { IsAttached: true };

    public GameProfile Profile { get; private set; } = new();

    /// <summary>Archivo del perfil abierto, o null si todavía no se ha guardado.</summary>
    public string? ProfilePath { get; private set; }

    public ProfileSaveState SaveState { get; private set; } = ProfileSaveState.Unsaved;
    public string? SaveError { get; private set; }

    public IReadOnlyList<Cheat> Cheats => _cheats;

    /// <summary>Nos conectamos a un juego o nos desconectamos de él.</summary>
    public event EventHandler? ConnectionChanged;

    /// <summary>Cambió el perfil abierto o su lista de cheats.</summary>
    public event EventHandler? ProfileChanged;

    /// <summary>Cambió el estado de un cheat (activado, desactivado, error...).</summary>
    public event EventHandler<Cheat>? CheatChanged;

    public event EventHandler<SessionStatus>? StatusChanged;

    // ================= Conexión =================

    public void Connect(int processId)
    {
        DisableAll();
        Memory?.Dispose();
        Memory = null;
        try
        {
            Memory = connector.Open(processId);
            Report($"Conectado a {Memory.ProcessName}.exe · PID {Memory.ProcessId} · {(Memory.Is32Bit ? 32 : 64)} bits", StatusKind.Success);
        }
        catch (InvalidOperationException ex)
        {
            Report("Error: " + ex.Message, StatusKind.Error);
        }
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Se llama cada poco: detecta que el juego se cerró y, si el perfil indica su proceso,
    /// se conecta solo en cuanto el juego se abre.
    /// </summary>
    public void Watch()
    {
        if (Memory is { IsAttached: false })
        {
            foreach (var cheat in _cheats)
            {
                cheat.Reset();
                CheatChanged?.Invoke(this, cheat);
            }
            Memory.Dispose();
            Memory = null;
            Report("El juego se cerró", StatusKind.Error);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
        TryAutoConnect();
    }

    /// <summary>Mantiene los valores congelados. Se llama muchas veces por segundo.</summary>
    public void Tick()
    {
        if (Memory is not { IsAttached: true } memory) return;
        foreach (var cheat in _cheats) cheat.Tick(memory);
    }

    void TryAutoConnect()
    {
        if (IsConnected || string.IsNullOrWhiteSpace(Profile.Process)) return;
        if (connector.FindProcess(Profile.Process) is { } id) Connect(id);
        else if (Memory is null) Report($"Esperando a {Profile.Process}...", StatusKind.Neutral);
    }

    // ================= Perfil =================

    /// <summary>Abre un perfil (o uno temporal sin archivo si <paramref name="path"/> es null).</summary>
    public void OpenProfile(GameProfile profile, string? path)
    {
        DisableAll();
        Profile = profile;
        ProfilePath = path;
        SaveState = path is null ? ProfileSaveState.Unsaved : ProfileSaveState.Saved;
        _cheats.Clear();
        _cheats.AddRange(profile.Cheats.Select(d => new Cheat(d)));
        ProfileChanged?.Invoke(this, EventArgs.Empty);
        TryAutoConnect();
    }

    /// <summary>Guarda el perfil en <paramref name="path"/> (o en su archivo actual) y completa juego/proceso si faltan.</summary>
    public void SaveProfileAs(string path)
    {
        if (string.IsNullOrWhiteSpace(Profile.Process) && Memory is not null) Profile.Process = Memory.ProcessName + ".exe";
        if (string.IsNullOrWhiteSpace(Profile.Game)) Profile.Game = Path.GetFileNameWithoutExtension(path);
        ProfilePath = path;
        Persist();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddCheat(CheatDefinition definition)
    {
        Profile.Cheats.Add(definition);
        _cheats.Add(new Cheat(definition));
        CheatsChanged();
    }

    public void ReplaceCheat(Cheat cheat, CheatDefinition definition)
    {
        cheat.Disable(Memory); // un parche activo se deshace con los datos viejos antes de cambiarlos
        _cheats[_cheats.IndexOf(cheat)] = new Cheat(definition);
        Profile.Cheats[Profile.Cheats.IndexOf(cheat.Definition)] = definition;
        CheatsChanged();
    }

    public void RemoveCheat(Cheat cheat)
    {
        cheat.Disable(Memory);
        _cheats.Remove(cheat);
        Profile.Cheats.Remove(cheat.Definition);
        CheatsChanged();
    }

    /// <summary>Primera tecla F1..F12 que ningún cheat usa todavía.</summary>
    public string? NextFreeHotkey()
    {
        var used = _cheats.Select(c => c.Definition.Hotkey).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, 12).Select(n => $"F{n}").FirstOrDefault(k => !used.Contains(k));
    }

    // Si el perfil ya tiene archivo, cada cambio se guarda solo.
    void CheatsChanged()
    {
        if (ProfilePath is null) SaveState = ProfileSaveState.Unsaved;
        else Persist();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    void Persist()
    {
        try
        {
            profiles.Save(ProfilePath!, Profile);
            SaveState = ProfileSaveState.Saved;
            SaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveState = ProfileSaveState.Error;
            SaveError = ex.Message;
        }
    }

    // ================= Cheats =================

    public void Toggle(Cheat cheat) => SetEnabled(cheat, !cheat.Enabled);

    public void SetEnabled(Cheat cheat, bool enabled)
    {
        if (!enabled) cheat.Disable(Memory);
        else if (Memory is { IsAttached: true } memory) cheat.Enable(memory);
        else cheat.Fail("Conecta el juego primero");
        CheatChanged?.Invoke(this, cheat);
    }

    /// <summary>Avisa de que algo externo cambió el estado de un cheat (p. ej. su atajo no se pudo registrar).</summary>
    public void NotifyCheatChanged(Cheat cheat) => CheatChanged?.Invoke(this, cheat);

    /// <summary>Desactiva todo; los parches devuelven el juego a como estaba.</summary>
    public void DisableAll()
    {
        foreach (var cheat in _cheats.Where(c => c.Enabled))
        {
            cheat.Disable(Memory);
            CheatChanged?.Invoke(this, cheat);
        }
    }

    public void Report(string text, StatusKind kind) => StatusChanged?.Invoke(this, new SessionStatus(text, kind));

    public void Dispose()
    {
        DisableAll();
        Memory?.Dispose();
        Memory = null;
    }
}
