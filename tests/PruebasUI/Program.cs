// Pruebas de la interfaz sin mostrar ventanas ni tocar el ratón/teclado:
// se crean el editor y las tarjetas ocultos, se rellenan desde código y se "fotografían" con DrawToBitmap.
using System.Diagnostics;
using MiTrainer.Core;
using MiTrainer.UI;

if (args.Length < 2)
{
    Console.WriteLine("Uso: PruebasUI <ruta a JuegoPrueba.exe> <carpeta para las capturas>");
    return 2;
}
string capturas = args[1];
Directory.CreateDirectory(capturas);

int fallos = 0;
void Comprobar(bool ok, string que)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALLO")} {que}");
    if (!ok) fallos++;
}

#pragma warning disable WFO5001
Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001

var thread = new Thread(Ejecutar);
thread.SetApartmentState(ApartmentState.STA); // WinForms necesita STA
thread.Start();
thread.Join();
return fallos == 0 ? 0 : 1;

void Ejecutar()
{
    using var juego = Process.Start(new ProcessStartInfo(args[0]) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false })!;
    string Enviar(string comando)
    {
        juego.StandardInput.WriteLine(comando);
        return juego.StandardOutput.ReadLine()!;
    }
    try
    {
        juego.StandardOutput.ReadLine();
        long vida = Convert.ToInt64(Enviar("a")[6..], 16);
        using var mem = new MemoryEngine();
        mem.Attach(juego);

        // ---------- Editor: un cheat válido ----------
        var oro = new CheatDef { Name = "Oro infinito", Type = CheatTypes.Freeze, Address = $"{vida + 4:X}", ValueType = "int", Value = 99999, Hotkey = "F2" };
        using (var editor = Editor(oro, mem, ["F1"]))
        {
            editor.Test();
            Comprobar(editor.ResultText.Contains("vale 50"), $"«Comprobar en el juego» lee el oro actual: {editor.ResultText}");
            Captura(editor, "editor.png");
            editor.Save();
            Comprobar(editor.Result is { Name: "Oro infinito", Hotkey: "F2", Value: 99999 }, "«Guardar» devuelve el cheat con sus datos");
            var cheat = new Cheat(editor.Result!);
            Comprobar(cheat.Enable(mem) && Enviar("p").EndsWith("ORO=99999"), "el cheat creado en el editor funciona en el juego");
            cheat.Disable(mem);
        }

        // ---------- Editor: errores que debe explicar ----------
        using (var editor = Editor(new CheatDef { Name = "Repetido", Address = "10", Hotkey = "F1" }, mem, ["F1"]))
        {
            editor.Save();
            Comprobar(editor.Result == null && editor.ResultText.Contains("ya lo usa"), $"atajo repetido: {editor.ResultText}");
        }
        using (var editor = Editor(new CheatDef { Name = "Malo", Address = "XYZ" }, mem, []))
        {
            editor.Save();
            Comprobar(editor.Result == null && editor.ResultText.Contains("Dirección"), $"dirección no válida: {editor.ResultText}");
        }
        using (var editor = Editor(new CheatDef { Name = "" , Address = "10" }, mem, []))
        {
            editor.Save();
            Comprobar(editor.Result == null && editor.ResultText.Contains("nombre"), $"sin nombre: {editor.ResultText}");
        }

        // ---------- Editor: parche ----------
        using (var editor = Editor(new CheatDef { Name = "Parche", Type = CheatTypes.Patch, Pattern = "4D 5A ?? 00", Bytes = "90 90" }, mem, []))
        {
            editor.Test();
            Comprobar(editor.ResultText.Contains("Encontrado") && editor.ResultText.Contains("4D5A"), $"parche: encuentra el patrón: {editor.ResultText}");
            Captura(editor, "editor-parche.png");
            editor.Save();
            Comprobar(editor.Result is { Type: CheatTypes.Patch, Bytes: "90 90" }, "parche: se guarda con sus bytes");
        }

        // ---------- Grabar atajos ----------
        using (var editor = Editor(new CheatDef { Name = "Atajo" }, mem, []))
        {
            var box = editor.HotkeyInput;
            box.Record(Keys.F5, Keys.Control);
            bool ctrl = box.Text == "Ctrl+F5";
            box.Record(Keys.ShiftKey, Keys.Shift); // pulsar solo Shift no cambia nada
            bool soloShift = box.Text == "Ctrl+F5";
            box.Record(Keys.Delete, Keys.None);
            Comprobar(ctrl && soloShift && box.Text == "" && Hotkeys.TryParse("Ctrl+F5", out _, out _), "grabar atajo: Ctrl+F5, ignora Shift solo, Supr lo borra");
        }

        // ---------- Duplicar ----------
        var copia = GameProfile.Clone(oro);
        copia.Name += " (copia)";
        copia.Offsets = ["10"];
        Comprobar(oro.Name == "Oro infinito" && oro.Offsets == null && copia.Address == oro.Address, "duplicar crea una copia independiente");

        // ---------- Tarjetas con el botón ✎ ----------
        var cheats = new[]
        {
            new Cheat(new CheatDef { Name = "Vida infinita", Type = CheatTypes.Freeze, Address = $"{vida:X}", Value = 100, Hotkey = "F1" }),
            new Cheat(oro),
            new Cheat(new CheatDef { Name = "Oro = 99999", Type = CheatTypes.Set, Address = $"{vida + 4:X}", Value = 99999, Hotkey = "F3" }),
        };
        cheats[0].Enable(mem);
        using var lienzo = new Bitmap(780, 3 * 82);
        using (var g = Graphics.FromImage(lienzo)) g.Clear(Theme.Background);
        for (int i = 0; i < cheats.Length; i++)
        {
            using var card = new CheatCard(cheats[i]) { Width = 780 };
            card.CreateControl();
            card.PerformLayout();
            card.UpdateState();
            using var bmp = new Bitmap(card.Width, card.Height);
            card.DrawToBitmap(bmp, card.ClientRectangle);
            using var g = Graphics.FromImage(lienzo);
            g.DrawImage(bmp, 0, i * 82);
        }
        lienzo.Save(Path.Combine(capturas, "tarjetas.png"));
        cheats[0].Disable(mem);

        // ---------- Mis juegos: tarjetas de los juegos instalados de verdad ----------
        var juegos = GameLibrary.Scan();
        var conIcono = juegos.Select(j => (Juego: j, Icono: GameCard.LoadIcon(j.Executable))).ToList();
        Comprobar(juegos.Count == 0 || conIcono.Count(j => j.Icono != null) >= juegos.Count * 0.8,
            $"Mis juegos: {juegos.Count} juegos, {conIcono.Count(j => j.Icono != null)} con icono");
        if (juegos.Count > 0)
        {
            var muestra = conIcono.Take(7).ToList();
            using var lista = new Bitmap(780, muestra.Count * 72);
            using (var g = Graphics.FromImage(lista)) g.Clear(Theme.Background);
            for (int i = 0; i < muestra.Count; i++)
            {
                using var card = new GameCard(muestra[i].Juego, muestra[i].Icono) { Width = 780 };
                if (i == 2) card.SetProfile("perfil.json", 3); // así se ve un juego con perfil
                if (i == 4) card.SetProfile("perfil.json", 0);
                card.CreateControl();
                card.PerformLayout();
                using var bmp = new Bitmap(card.Width, card.Height);
                card.DrawToBitmap(bmp, card.ClientRectangle);
                using var g = Graphics.FromImage(lista);
                g.DrawImage(bmp, 0, i * 72);
            }
            lista.Save(Path.Combine(capturas, "mis-juegos.png"));
        }
        Comprobar(true, "capturas guardadas en " + capturas);
    }
    catch (Exception ex)
    {
        Comprobar(false, "excepción: " + ex);
    }
    finally
    {
        if (!juego.HasExited) juego.Kill();
    }
}

static CheatEditor Editor(CheatDef def, MemoryEngine mem, string[] usados)
{
    var editor = new CheatEditor(def, mem, usados);
    editor.CreateControl(); // crea la ventana sin mostrarla
    editor.PerformLayout();
    return editor;
}

void Captura(Form form, string nombre)
{
    // Windows solo pinta los controles de una ventana visible: la mostramos muy fuera de la pantalla
    // y SIN activarla, para no quitarle el foco a lo que el usuario tenga abierto.
    form.StartPosition = FormStartPosition.Manual;
    form.Location = new Point(-32000, -32000);
    ShowWindow(form.Handle, 4 /* SW_SHOWNOACTIVATE */);
    form.Refresh();
    using var bmp = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
    ShowWindow(form.Handle, 0 /* SW_HIDE */);
    bmp.Save(Path.Combine(capturas, nombre));
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern bool ShowWindow(nint hWnd, int command);
