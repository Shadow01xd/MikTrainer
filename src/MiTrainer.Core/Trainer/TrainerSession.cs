using MiTrainer.Core.Cheats;
using MiTrainer.Core.Memory;

namespace MiTrainer.Core.Trainer;

public enum StatusKind { Neutral, Success, Error }

/// <summary>Mensaje de estado para la cabecera ("Conectado a juego.exe", "El juego se cerró"...).</summary>
public sealed record SessionStatus(string Text, StatusKind Kind);

public enum ProfileSaveState { Unsaved, Saved, Error }

/// <summary>Si el perfil abierto puede usarse con el juego conectado.</summary>
public enum SessionState
{
    /// <summary>No hay ningún juego conectado.</summary>
    Disconnected,

    /// <summary>Hay un juego conectado, pero el perfil no está vinculado a él: las opciones están bloqueadas.</summary>
    AwaitingConfirmation,

    /// <summary>Perfil y proceso vinculados: se pueden activar opciones.</summary>
    Ready,
}

/// <summary>
/// Una tarea (escaneo, búsqueda de punteros...) atada a la conexión que la inició. Si la conexión cambia,
/// <see cref="Cancellation"/> se cancela y <see cref="TrainerSession.IsCurrent"/> deja de ser cierto:
/// su resultado ya no vale y hay que descartarlo.
/// </summary>
public sealed record SessionOperation(int ConnectionId, IProcessMemory Memory, CancellationToken Cancellation);

/// <summary>
/// El trainer en marcha: a qué juego estamos conectados, qué perfil está abierto y el estado de sus cheats.
/// No sabe nada de ventanas: la interfaz llama a sus métodos y escucha sus eventos.
/// </summary>
/// <remarks>
/// Regla de seguridad: los cheats de un perfil solo se activan sobre el proceso al que ese perfil está vinculado
/// (<see cref="SessionState.Ready"/>). El vínculo se confirma solo si el ejecutable del perfil coincide con el del
/// proceso conectado, o a mano con <see cref="ConfirmSession"/>. Cambiar de perfil o de proceso lo rompe.
/// </remarks>
public sealed class TrainerSession(IProcessConnector connector, IProfileRepository profiles) : IDisposable
{
    readonly List<Cheat> _cheats = [];
    readonly List<PendingRestore> _pendingRestores = [];
    CancellationTokenSource _connectionCancel = new();
    int _connectionId;
    int? _boundConnectionId;
    SessionState _lastState;

    public IProcessMemory? Memory { get; private set; }
    public bool IsConnected => Memory is { IsAttached: true };

    public GameProfile Profile { get; private set; } = new();

    /// <summary>Archivo del perfil abierto, o null si todavía no se ha guardado.</summary>
    public string? ProfilePath { get; private set; }

    public ProfileSaveState SaveState { get; private set; } = ProfileSaveState.Unsaved;
    public string? SaveError { get; private set; }

    public IReadOnlyList<Cheat> Cheats => _cheats;

    public SessionState State =>
        !IsConnected ? SessionState.Disconnected
        : _boundConnectionId == _connectionId ? SessionState.Ready
        : SessionState.AwaitingConfirmation;

    /// <summary>Por qué no se pueden activar opciones ahora (null si se puede).</summary>
    public string? BlockReason => State switch
    {
        SessionState.Ready => null,
        SessionState.Disconnected when !string.IsNullOrWhiteSpace(Profile.Process) => $"Abre {Profile.Process} para usar este perfil",
        SessionState.Disconnected => "Conecta el juego primero",
        _ when string.IsNullOrWhiteSpace(Profile.Process) => $"Confirma que este perfil es para {Memory!.ProcessName}.exe",
        _ => $"Este perfil es para {Profile.Process} y estás conectado a {Memory!.ProcessName}.exe",
    };

    /// <summary>Parches que no se pudieron deshacer y ya no pertenecen a ningún cheat del perfil abierto.</summary>
    public IReadOnlyList<PendingRestore> PendingRestores => _pendingRestores;

    /// <summary>Todos los parches pendientes de deshacer: los del perfil abierto y los de perfiles anteriores.</summary>
    public IEnumerable<PendingRestore> AllPendingRestores() =>
        _pendingRestores.Concat(_cheats.Select(c => c.PendingRestore).OfType<PendingRestore>());

    /// <summary>Nos conectamos a otro proceso o nos desconectamos.</summary>
    public event EventHandler? ConnectionChanged;

    /// <summary>Cambió <see cref="State"/> (p. ej. el perfil quedó vinculado o bloqueado).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Cambió el perfil abierto o su lista de cheats.</summary>
    public event EventHandler? ProfileChanged;

    /// <summary>Cambió el estado de un cheat (activado, desactivado, error...).</summary>
    public event EventHandler<Cheat>? CheatChanged;

    public event EventHandler<SessionStatus>? StatusChanged;

    // ================= Conexión =================

    public void Connect(int processId)
    {
        DisableAll(); // con la memoria vieja: los parches se deshacen en su proceso
        IProcessMemory? opened = null;
        try
        {
            opened = connector.Open(processId);
        }
        catch (InvalidOperationException ex)
        {
            Report("Error: " + ex.Message, StatusKind.Error);
        }
        ReplaceMemory(opened);
    }

    /// <summary>
    /// Se llama cada poco: detecta que el juego se cerró, reintenta los parches pendientes de deshacer y,
    /// si el perfil indica su proceso, se conecta solo a él en cuanto aparece.
    /// </summary>
    public void Watch()
    {
        if (Memory is { IsAttached: false } closed)
        {
            foreach (var cheat in _cheats)
            {
                cheat.Reset();
                CheatChanged?.Invoke(this, cheat);
            }
            _pendingRestores.RemoveAll(p => p.ProcessId == closed.ProcessId); // el proceso ya no existe: no queda nada que deshacer
            Report("El juego se cerró", StatusKind.Error);
            ReplaceMemory(null);
        }
        RetryPendingRestores();
        TryAutoConnect();
    }

    /// <summary>Mantiene los valores congelados. Se llama muchas veces por segundo.</summary>
    public void Tick()
    {
        if (State != SessionState.Ready) return;
        foreach (var cheat in _cheats) cheat.Tick(Memory!);
    }

    /// <summary>Empieza una tarea atada a la conexión actual, o null si no hay juego conectado.</summary>
    public SessionOperation? BeginOperation() =>
        Memory is { IsAttached: true } memory ? new SessionOperation(_connectionId, memory, _connectionCancel.Token) : null;

    /// <summary>¿Sigue siendo válida la conexión con la que empezó la tarea?</summary>
    public bool IsCurrent(SessionOperation operation) => operation.ConnectionId == _connectionId && IsConnected;

    void ReplaceMemory(IProcessMemory? memory)
    {
        if (!ReferenceEquals(Memory, memory)) Memory?.Dispose();
        Memory = memory;
        _connectionId++;
        _connectionCancel.Cancel(); // las tareas de la conexión anterior se cancelan
        _connectionCancel.Dispose();
        _connectionCancel = new CancellationTokenSource();
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        Rebind();
    }

    /// <summary>
    /// Si el perfil indica un proceso y no estamos vinculados a él, nos conectamos en cuanto aparece
    /// (también si ahora mismo estamos conectados a otro juego).
    /// </summary>
    void TryAutoConnect()
    {
        if (State == SessionState.Ready || string.IsNullOrWhiteSpace(Profile.Process)) return;
        if (connector.FindProcess(Profile.Process) is { } id && id != Memory?.ProcessId) Connect(id);
        else if (Memory is null) Report($"Esperando a {Profile.Process}...", StatusKind.Neutral);
    }

    // ================= Vínculo perfil ↔ proceso =================

    /// <summary>Vincula el perfil al proceso conectado si su ejecutable coincide; si no, las opciones quedan bloqueadas.</summary>
    void Rebind()
    {
        _boundConnectionId = Memory is { IsAttached: true } memory && SameExecutable(Profile.Process, memory.ProcessName)
            ? _connectionId
            : null;
        ReportStateIfChanged();
    }

    /// <summary>
    /// El usuario confirma que el perfil abierto es para el juego conectado. Si el perfil indicaba otro ejecutable
    /// (p. ej. el lanzador en vez del juego), se corrige y se guarda.
    /// </summary>
    public void ConfirmSession()
    {
        if (State != SessionState.AwaitingConfirmation) return;
        Profile.Process = Memory!.ProcessName + ".exe";
        if (ProfilePath is not null) Persist();
        _boundConnectionId = _connectionId;
        ProfileChanged?.Invoke(this, EventArgs.Empty);
        ReportStateIfChanged();
    }

    void ReportStateIfChanged()
    {
        var state = State;
        if (state == _lastState) return;
        _lastState = state;
        if (state == SessionState.Ready)
            Report($"Perfil vinculado a {Memory!.ProcessName}.exe · PID {Memory.ProcessId} · {(Memory.Is32Bit ? 32 : 64)} bits", StatusKind.Success);
        else if (state == SessionState.AwaitingConfirmation)
            Report(BlockReason!, StatusKind.Error);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    static bool SameExecutable(string? profileProcess, string processName) =>
        !string.IsNullOrWhiteSpace(profileProcess)
        && string.Equals(Path.GetFileNameWithoutExtension(profileProcess.Trim()), processName, StringComparison.OrdinalIgnoreCase);

    // ================= Perfil =================

    /// <summary>
    /// Abre un perfil (o uno temporal sin archivo si <paramref name="path"/> es null). Las opciones quedan bloqueadas
    /// hasta que el perfil se vincule al proceso que le corresponde.
    /// </summary>
    public void OpenProfile(GameProfile profile, string? path)
    {
        DisableAll();
        KeepPendingRestores(_cheats);
        Profile = profile;
        ProfilePath = path;
        SaveState = path is null ? ProfileSaveState.Unsaved : ProfileSaveState.Saved;
        _cheats.Clear();
        _cheats.AddRange(profile.Cheats.Select(d => new Cheat(d)));
        ProfileChanged?.Invoke(this, EventArgs.Empty);
        Rebind();
        TryAutoConnect();
    }

    /// <summary>Guarda el perfil en <paramref name="path"/> (o en su archivo actual) y completa juego/proceso si faltan.</summary>
    public void SaveProfileAs(string path)
    {
        if (string.IsNullOrWhiteSpace(Profile.Process) && State == SessionState.Ready) Profile.Process = Memory!.ProcessName + ".exe";
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
        KeepPendingRestores([cheat]);
        _cheats[_cheats.IndexOf(cheat)] = new Cheat(definition);
        Profile.Cheats[Profile.Cheats.IndexOf(cheat.Definition)] = definition;
        CheatsChanged();
    }

    public void RemoveCheat(Cheat cheat)
    {
        cheat.Disable(Memory);
        KeepPendingRestores([cheat]);
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
        if (cheat.RestoreFailed) cheat.RetryRestore(Memory);   // pulsar un cheat con la restauración fallida la reintenta
        else if (!enabled) cheat.Disable(Memory);
        else if (BlockReason is { } reason) cheat.Fail(reason); // perfil no vinculado a este proceso
        else cheat.Enable(Memory!);
        CheatChanged?.Invoke(this, cheat);
    }

    /// <summary>Avisa de que algo externo cambió el estado de un cheat (p. ej. su atajo no se pudo registrar).</summary>
    public void NotifyCheatChanged(Cheat cheat) => CheatChanged?.Invoke(this, cheat);

    /// <summary>Desactiva todo; los parches devuelven el juego a como estaba (o quedan como «Restauración fallida»).</summary>
    public void DisableAll()
    {
        foreach (var cheat in _cheats.Where(c => c.Enabled))
        {
            cheat.Disable(Memory);
            CheatChanged?.Invoke(this, cheat);
        }
    }

    // Un cheat que se va del perfil no se lleva consigo la información para deshacer su parche.
    void KeepPendingRestores(IEnumerable<Cheat> leaving) =>
        _pendingRestores.AddRange(leaving.Select(c => c.PendingRestore).OfType<PendingRestore>());

    void RetryPendingRestores()
    {
        if (Memory is not { IsAttached: true } memory) return;
        foreach (var pending in _pendingRestores.Where(p => p.ProcessId == memory.ProcessId).ToList())
        {
            if (!pending.TryRestore(memory)) continue;
            _pendingRestores.Remove(pending);
            Report($"Parche de «{pending.CheatName}» restaurado en 0x{pending.Address:X}", StatusKind.Success);
        }
    }

    public void Report(string text, StatusKind kind) => StatusChanged?.Invoke(this, new SessionStatus(text, kind));

    public void Dispose()
    {
        DisableAll();
        Memory?.Dispose();
        Memory = null;
        _connectionCancel.Cancel();
        _connectionCancel.Dispose();
    }
}
