using System.Runtime.InteropServices;

namespace MiTrainer.App.Hotkeys;

/// <summary>
/// Atajos de teclado globales (funcionan aunque el juego tenga el foco). Usa su propia ventana invisible
/// para recibir WM_HOTKEY, así ninguna ventana de la app tiene que ocuparse de ello.
/// </summary>
sealed partial class GlobalHotkeys : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;
    static readonly nint HWND_MESSAGE = -3; // ventana "solo mensajes": no se ve ni aparece en ningún sitio

    readonly HashSet<int> _registered = [];

    public GlobalHotkeys()
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
    }

    /// <summary>Se pulsó el atajo registrado con ese id.</summary>
    public event EventHandler<int>? Pressed;

    /// <summary>Registra un atajo. false si otra aplicación ya lo usa.</summary>
    public bool Register(int id, Hotkey hotkey)
    {
        if (!RegisterHotKey(Handle, id, hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key)) return false;
        _registered.Add(id);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _registered) UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) Pressed?.Invoke(this, (int)m.WParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
