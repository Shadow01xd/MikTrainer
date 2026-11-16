using System.Media;

namespace MiTrainer.App.Hotkeys;

/// <summary>
/// Mantiene registrados los atajos de los cheats del perfil abierto: al pulsar uno, activa o desactiva su cheat.
/// </summary>
sealed class HotkeyBinder : IDisposable
{
    readonly TrainerSession _session;
    readonly GlobalHotkeys _hotkeys = new();
    int _suspended;

    public HotkeyBinder(TrainerSession session)
    {
        _session = session;
        _session.ProfileChanged += (_, _) => Rebind();
        _hotkeys.Pressed += (_, id) => OnPressed(id);
        Rebind();
    }

    /// <summary>
    /// Suelta los atajos mientras dura el bloque (p. ej. con el editor abierto, para poder "grabar" F1, F2...).
    /// </summary>
    public IDisposable Suspend()
    {
        _suspended++;
        _hotkeys.UnregisterAll();
        return new Resume(this);
    }

    void Rebind()
    {
        _hotkeys.UnregisterAll();
        if (_suspended > 0) return;
        var cheats = _session.Cheats;
        for (int i = 0; i < cheats.Count; i++)
        {
            string? text = cheats[i].Definition.Hotkey;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!HotkeyText.TryParse(text, out var hotkey)) Warn(cheats[i], $"Atajo no válido: {text}");
            else if (!_hotkeys.Register(i, hotkey)) Warn(cheats[i], $"El atajo {text} lo usa otra aplicación");
        }
    }

    void Warn(Cheat cheat, string message)
    {
        cheat.Note(message);
        _session.NotifyCheatChanged(cheat);
    }

    void OnPressed(int index)
    {
        if (index < 0 || index >= _session.Cheats.Count) return;
        var cheat = _session.Cheats[index];
        _session.Toggle(cheat);
        bool worked = cheat.Enabled || (cheat.Definition.Type == CheatType.Set && !cheat.Failed);
        (worked ? SystemSounds.Asterisk : SystemSounds.Beep).Play();
    }

    public void Dispose() => _hotkeys.Dispose();

    sealed class Resume(HotkeyBinder binder) : IDisposable
    {
        bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            binder._suspended--;
            binder.Rebind();
        }
    }
}
