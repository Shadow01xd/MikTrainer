using MiTrainer.App.Controls;
using MiTrainer.App.Hotkeys;
using MiTrainer.App.Shell;
using MiTrainer.Core.Cheats;
using MiTrainer.Core.Games;
using MiTrainer.Core.Trainer;
using MiTrainer.TestSupport;

namespace MiTrainer.App.Tests;

/// <summary>La ventana principal entera, montada con servicios falsos (sin juegos ni archivos de verdad).</summary>
public class MainFormTests
{
    [Fact]
    public void Se_monta_con_todas_las_paginas_y_muestra_los_juegos() => Sta.Run(() =>
    {
        var connector = new FakeProcessConnector();
        var profiles = new InMemoryProfileRepository();
        // Sin atajos: así la prueba no registra teclas globales en el equipo.
        profiles.Save(profiles.NewPathFor("Terraria"), new GameProfile
        {
            Game = "Terraria",
            Process = "Terraria.exe",
            Cheats = [new CheatDefinition { Name = "Vida infinita", Type = CheatType.Freeze, Address = "10", Value = 100 }],
        });
        var games = new GameLibrary([new FakeGameStore(
            new InstalledGame("Terraria", "Steam", @"C:\Juegos\Terraria", @"C:\Juegos\Terraria\Terraria.exe", "steam://rungameid/105600"),
            new InstalledGame("Baldur's Gate 3", "Steam", @"C:\Juegos\BG3", @"C:\Juegos\BG3\bin\bg3.exe", "steam://rungameid/1086940"))]);

        using var session = new TrainerSession(connector, profiles);
        using var hotkeys = new HotkeyBinder(session);
        using var form = new MainForm(new AppServices(session, connector, profiles, new InMemoryPointerResultsRepository(), games, hotkeys));

        // Sin mostrar la ventana no hay evento Load: inicializamos a mano, igual que haría OnLoad.
        form.CreateControl();
        form.Initialize();
        WaitFor(() => FindAll<GameCard>(form).Count() == 2);

        Assert.Equal("Terraria", session.Profile.Game);
        Assert.Single(session.Cheats);
        Assert.Contains(FindAll<GameCard>(form), card => card.Game.Name == "Terraria" && card.ProfilePath != null);
        Assert.Contains(FindAll<PillButton>(form), b => b.Text == "Punteros");
        Assert.Contains(FindAll<Label>(form), l => l.Text == "2 juegos · 1 con perfil");

        if (Environment.GetEnvironmentVariable("MITRAINER_CAPTURAS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            using var final = Sta.Capture(form);
            final.Save(Path.Combine(folder, "ventana.png"));
        }
    });

    static IEnumerable<T> FindAll<T>(Control root) where T : Control =>
        root.Controls.Cast<Control>().SelectMany(c => (c is T t ? [t] : Enumerable.Empty<T>()).Concat(FindAll<T>(c)));

    /// <summary>Procesa mensajes (la carga de juegos es asíncrona) hasta que se cumpla la condición.</summary>
    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
        Assert.True(condition(), "La condición no se cumplió a tiempo");
    }
}
