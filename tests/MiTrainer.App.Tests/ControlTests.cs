using MiTrainer.App.Controls;
using MiTrainer.App.Dialogs;
using MiTrainer.App.Hotkeys;
using MiTrainer.App.Theming;
using MiTrainer.Core.Cheats;
using MiTrainer.Core.Games;

namespace MiTrainer.App.Tests;

public class ControlTests
{
    [Theory]
    [InlineData("F1", 0u, (uint)Keys.F1)]
    [InlineData("Ctrl+F2", HotkeyText.ModCtrl, (uint)Keys.F2)]
    [InlineData("alt + shift + NumPad1", HotkeyText.ModAlt | HotkeyText.ModShift, (uint)Keys.NumPad1)]
    public void Interpreta_atajos_escritos_a_mano(string text, uint modifiers, uint key)
    {
        Assert.True(HotkeyText.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("F99")]
    public void Rechaza_atajos_sin_tecla_valida(string text)
    {
        Assert.False(HotkeyText.TryParse(text, out _));
    }

    [Fact]
    public void Las_tarjetas_se_dibujan_en_todos_sus_estados() => Sta.Run(() =>
    {
        var failed = new Cheat(new CheatDefinition { Name = "Roto", Type = CheatType.Freeze, Address = "10" });
        failed.Fail("Dirección no válida o puntero roto");
        Cheat[] cheats =
        [
            new(new CheatDefinition { Name = "Vida infinita", Type = CheatType.Freeze, Hotkey = "F1" }),
            new(new CheatDefinition { Name = "Oro = 99999", Type = CheatType.Set, Hotkey = "Ctrl+F2" }),
            new(new CheatDefinition { Name = "Munición", Type = CheatType.Patch }),
            failed,
        ];

        foreach (var cheat in cheats)
        {
            using var card = new CheatCard(cheat) { Width = 700 };
            using var image = Sta.Capture(card);
            Assert.Equal(card.Size, image.Size);
        }
    });

    [Fact]
    public void La_tarjeta_de_juego_muestra_si_tiene_perfil() => Sta.Run(() =>
    {
        using var card = new GameCard(new InstalledGame("Terraria", "Steam", @"C:\Juegos\Terraria", @"C:\Juegos\Terraria\Terraria.exe", "steam://rungameid/105600"), icon: null) { Width = 700 };
        Assert.Null(card.ProfilePath);

        card.SetProfile(@"perfiles\Terraria.json", cheatCount: 3);

        Assert.Equal(@"perfiles\Terraria.json", card.ProfilePath);
        using var image = Sta.Capture(card);
    });

    /// <summary>
    /// Regenera las capturas del README y de docs/ (solo si se pide con MITRAINER_CAPTURAS=&lt;carpeta&gt;).
    /// Usa ventanas fuera de la pantalla y sin activar: no molesta a lo que tengas abierto.
    /// </summary>
    [Fact]
    public void Capturas_para_la_documentacion() => Sta.Run(() =>
    {
        string? folder = Environment.GetEnvironmentVariable("MITRAINER_CAPTURAS");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);

        using var editor = new CheatEditor(
            new CheatDefinition { Name = "Vida infinita", Type = CheatType.Freeze, Module = "juego.exe", Address = "1A2B3C", Offsets = ["18", "10"], Value = 100, Hotkey = "F1" },
            isNew: false, memory: null, usedHotkeys: []);
        using (var image = Sta.Capture(editor)) image.Save(Path.Combine(folder, "editor.png"));

        var active = new Cheat(new CheatDefinition { Name = "Vida infinita", Type = CheatType.Freeze, Value = 100, Hotkey = "F1" });
        Cheat[] cheats = [active, new(new CheatDefinition { Name = "Oro = 99999", Type = CheatType.Set, Value = 99999, Hotkey = "F2" }),
                          new(new CheatDefinition { Name = "Munición infinita", Type = CheatType.Patch, Hotkey = "F3" })];
        using var sheet = new Bitmap(780, cheats.Length * 82);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Theme.Background);
            for (int i = 0; i < cheats.Length; i++)
            {
                using var card = new CheatCard(cheats[i]) { Width = 780 };
                using var image = Sta.Capture(card);
                g.DrawImage(image, 0, i * 82);
            }
        }
        sheet.Save(Path.Combine(folder, "tarjetas.png"));
    });
}
