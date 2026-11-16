using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace MiTrainer.App.Tests;

/// <summary>Utilidades para probar WinForms sin mostrar nada en pantalla.</summary>
static class Sta
{
    /// <summary>Ejecuta el código en un hilo STA (WinForms lo necesita) y relanza sus excepciones.</summary>
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>
    /// Dibuja una ventana en un bitmap. Windows solo pinta los controles de una ventana visible, así que la mostramos
    /// muy lejos de la pantalla y SIN activarla: no le quita el foco a nada de lo que el usuario tenga abierto.
    /// </summary>
    public static Bitmap Capture(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.CreateControl();
        ShowWindow(form.Handle, SW_SHOWNOACTIVATE);
        try
        {
            form.PerformLayout();
            form.Refresh();
            var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            return bitmap;
        }
        finally
        {
            ShowWindow(form.Handle, SW_HIDE);
        }
    }

    /// <summary>Dibuja un control suelto (tarjetas, botones) sin ventana.</summary>
    public static Bitmap Capture(Control control)
    {
        control.CreateControl();
        control.PerformLayout();
        var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }

    const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll")]
    static extern bool ShowWindow(nint hWnd, int command);
}
