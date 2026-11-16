using System.Diagnostics;

namespace MiTrainer.App.Pages;

/// <summary>Página «Mis juegos»: juegos instalados (Steam, Epic, GOG) con su perfil de cheats y un botón para jugar.</summary>
sealed class GamesPage : Page
{
    readonly TextBox _search = Theme.Input(new TextBox { Width = 260, PlaceholderText = "Buscar juego…" });
    readonly Label _info = Ui.Label("Buscando juegos…");
    readonly FlowLayoutPanel _cards = Ui.CardList();
    bool _scanning;

    public GamesPage(AppServices services) : base(services)
    {
        _search.TextChanged += (_, _) => ApplyFilter();
        _cards.ClientSizeChanged += (_, _) => Ui.FitCards(_cards);

        Controls.Add(_cards);
        Controls.Add(Ui.Row(DockStyle.Top, _search, Ui.Pill("Actualizar", () => _ = LoadAsync()), _info));

        // Al guardar o crear perfiles cambia qué juegos "tienen cheats".
        Session.ProfileChanged += (_, _) => RefreshProfiles();
    }

    /// <summary>Se abrió el perfil de un juego: la ventana principal muestra la página de cheats.</summary>
    public event EventHandler? ProfileOpened;

    IEnumerable<GameCard> Cards => _cards.Controls.OfType<GameCard>();

    public async Task LoadAsync()
    {
        if (_scanning) return;
        _scanning = true;
        _info.Text = "Buscando juegos en Steam, Epic y GOG…";
        try
        {
            // Leer las tiendas y sacar los iconos puede tardar: fuera del hilo de la interfaz.
            var found = await Task.Run(() => Services.Games.Scan().Select(g => (Game: g, Icon: GameCard.LoadIcon(g.Executable))).ToList());
            ShowGames(found);
        }
        finally
        {
            _scanning = false;
        }
    }

    void ShowGames(IEnumerable<(InstalledGame Game, Image? Icon)> games)
    {
        _cards.SuspendLayout();
        foreach (Control card in _cards.Controls) card.Dispose();
        _cards.Controls.Clear();
        foreach (var (game, icon) in games)
        {
            var card = new GameCard(game, icon) { Width = Ui.CardWidth(_cards) };
            card.PlayRequested += (_, _) => Play(card);
            card.ProfileRequested += (_, _) => OpenProfile(card);
            _cards.Controls.Add(card);
        }
        _cards.ResumeLayout();
        RefreshProfiles();
    }

    bool MatchesSearch(GameCard card)
    {
        string search = _search.Text.Trim();
        return search.Length == 0 || card.Game.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    void ApplyFilter()
    {
        _cards.SuspendLayout();
        foreach (var card in Cards) card.Visible = MatchesSearch(card);
        _cards.ResumeLayout();

        // Se cuenta con el filtro, no con Visible: si la página está oculta, WinForms dice que ninguna tarjeta es visible.
        int total = Cards.Count(), shown = Cards.Count(MatchesSearch), withProfile = Cards.Count(c => c.ProfilePath != null);
        _info.Text = total == 0 ? "No se encontraron juegos de Steam, Epic ni GOG"
            : (shown < total ? $"{shown} de {total} juegos" : $"{total} juegos") + $" · {withProfile} con perfil";
    }

    // ================= Juego ↔ perfil =================

    /// <summary>Marca en cada juego si tiene perfil: uno que use su .exe o su nombre.</summary>
    void RefreshProfiles()
    {
        if (!Cards.Any()) return;
        var profiles = LoadAllProfiles();
        foreach (var card in Cards)
        {
            var match = profiles.FirstOrDefault(p => Belongs(p.Profile, card.Game));
            card.SetProfile(match.Path, match.Profile?.Cheats.Count ?? 0);
        }
        ApplyFilter();
    }

    List<(string Path, GameProfile Profile)> LoadAllProfiles()
    {
        var profiles = new List<(string, GameProfile)>();
        foreach (var path in Services.Profiles.List())
        {
            try { profiles.Add((path, Services.Profiles.Load(path))); }
            catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException) { /* un perfil roto no rompe la lista */ }
        }
        return profiles;
    }

    static bool Belongs(GameProfile profile, InstalledGame game) =>
        (game.ProcessName != null && string.Equals(profile.Process, game.ProcessName, StringComparison.OrdinalIgnoreCase))
        || string.Equals(profile.Game, game.Name, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Abre el perfil del juego, o le crea uno vacío con su .exe ya puesto.</summary>
    void OpenProfile(GameCard card)
    {
        string path = card.ProfilePath ?? CreateProfile(card.Game);
        Session.OpenProfile(Services.Profiles.Load(path), path);
        ProfileOpened?.Invoke(this, EventArgs.Empty);
    }

    string CreateProfile(InstalledGame game)
    {
        string path = Services.Profiles.NewPathFor(game.Name);
        Services.Profiles.Save(path, new GameProfile { Game = game.Name, Process = game.ProcessName ?? "" });
        return path;
    }

    /// <summary>
    /// Abre el juego desde su tienda y deja preparado su perfil: en cuanto arranca su .exe,
    /// la sesión se conecta sola (gracias al "proceso" del perfil).
    /// </summary>
    void Play(GameCard card)
    {
        var game = card.Game;
        if (card.ProfilePath != null) Session.OpenProfile(Services.Profiles.Load(card.ProfilePath), card.ProfilePath);
        else Session.OpenProfile(new GameProfile { Game = game.Name, Process = game.ProcessName ?? "" }, path: null);

        try
        {
            Process.Start(game.LaunchUri != null
                ? new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true }
                : new ProcessStartInfo(game.Executable!) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.Executable) });
            Session.Report(game.ProcessName != null
                ? $"Abriendo {game.Name}… me conectaré solo a {game.ProcessName}"
                : $"Abriendo {game.Name}… no sé su .exe: conéctate con la lista de arriba", StatusKind.Neutral);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Session.Report($"No se pudo abrir {game.Name}: {ex.Message}", StatusKind.Error);
        }
    }
}
