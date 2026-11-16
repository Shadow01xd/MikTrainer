using System.Diagnostics;
using MiTrainer.Core;
using MiTrainer.UI;

namespace MiTrainer;

// Página «Mis juegos»: juegos instalados (Steam, Epic, GOG), con su perfil de cheats y un botón para jugar.
public sealed partial class MainForm
{
    readonly TextBox _gameSearch = Theme.Input(new TextBox { Width = 260, PlaceholderText = "Buscar juego…" });
    readonly Label _gamesInfo = Theme.Label("Buscando juegos…");
    readonly FlowLayoutPanel _gamesPanel = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(0, 10, 0, 0),
    };
    readonly List<GameCard> _gameCards = [];
    bool _scanningGames;

    void BuildGamesPage()
    {
        _gameSearch.TextChanged += (_, _) => FilterGames();
        _gamesPanel.ClientSizeChanged += (_, _) => ResizeGameCards();

        var toolbar = Row(_gameSearch, Pill("Actualizar", () => _ = LoadGamesAsync()), _gamesInfo);
        toolbar.Dock = DockStyle.Top;

        _gamesPage.Controls.Add(_gamesPanel);
        _gamesPage.Controls.Add(toolbar);
    }

    async Task LoadGamesAsync()
    {
        if (_scanningGames) return;
        _scanningGames = true;
        _gamesInfo.Text = "Buscando juegos en Steam, Epic y GOG…";
        try
        {
            // Leer las tiendas y sacar los iconos puede tardar un poco: fuera del hilo de la interfaz.
            var found = await Task.Run(() => GameLibrary.Scan().Select(g => (Game: g, Icon: GameCard.LoadIcon(g.Executable))).ToList());

            _gamesPanel.SuspendLayout();
            foreach (var card in _gameCards) card.Dispose();
            _gameCards.Clear();
            _gamesPanel.Controls.Clear();
            foreach (var (game, icon) in found)
            {
                var card = new GameCard(game, icon) { Width = GameCardWidth() };
                card.PlayRequested += (_, _) => PlayGame(card);
                card.ProfileRequested += (_, _) => OpenGameProfile(card);
                _gameCards.Add(card);
                _gamesPanel.Controls.Add(card);
            }
            _gamesPanel.ResumeLayout();
            RefreshGameProfiles();
            FilterGames();
        }
        catch (Exception ex)
        {
            _gamesInfo.Text = "No se pudieron leer los juegos: " + ex.Message;
        }
        finally
        {
            _scanningGames = false;
        }
    }

    int GameCardWidth() => Math.Max(300, _gamesPanel.ClientSize.Width - _gamesPanel.Padding.Horizontal - 4);

    void ResizeGameCards()
    {
        int width = GameCardWidth();
        _gamesPanel.SuspendLayout();
        foreach (var card in _gameCards) card.Width = width;
        _gamesPanel.ResumeLayout();
    }

    void FilterGames()
    {
        string search = _gameSearch.Text.Trim();
        _gamesPanel.SuspendLayout();
        foreach (var card in _gameCards)
            card.Visible = search.Length == 0 || card.Game.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        _gamesPanel.ResumeLayout();

        int withProfile = _gameCards.Count(c => c.ProfilePath != null);
        int shown = _gameCards.Count(c => c.Visible);
        _gamesInfo.Text = _gameCards.Count == 0 ? "No se encontraron juegos de Steam, Epic ni GOG"
            : (shown < _gameCards.Count ? $"{shown} de {_gameCards.Count} juegos" : $"{_gameCards.Count} juegos")
              + $" · {withProfile} con perfil";
    }

    // ---------- Juego ↔ perfil ----------

    /// <summary>Vuelve a leer los perfiles y marca en cada juego si tiene uno (por su .exe o por su nombre).</summary>
    void RefreshGameProfiles()
    {
        if (_gameCards.Count == 0) return;
        var profiles = new List<(string Path, GameProfile Profile)>();
        if (Directory.Exists(_profilesDir))
            foreach (var file in Directory.GetFiles(_profilesDir, "*.json"))
            {
                try { profiles.Add((file, GameProfile.Load(file))); }
                catch { /* un perfil roto no debe romper la lista de juegos */ }
            }

        foreach (var card in _gameCards)
        {
            var match = profiles.FirstOrDefault(p =>
                (card.Game.ProcessName != null && string.Equals(p.Profile.Process, card.Game.ProcessName, StringComparison.OrdinalIgnoreCase))
                || string.Equals(p.Profile.Game, card.Game.Name, StringComparison.CurrentCultureIgnoreCase));
            card.SetProfile(match.Path, match.Profile?.Cheats.Count ?? 0);
        }
        FilterGames();
    }

    /// <summary>Abre el perfil del juego en Cheats, o le crea uno vacío con su .exe ya puesto.</summary>
    void OpenGameProfile(GameCard card)
    {
        string? path = card.ProfilePath;
        if (path == null)
        {
            Directory.CreateDirectory(_profilesDir);
            string name = new(card.Game.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c is not ('™' or '®')).ToArray());
            path = Path.Combine(_profilesDir, name.Trim() + ".json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(_profilesDir, $"{name.Trim()} ({n}).json");
            new GameProfile { Game = card.Game.Name, Process = card.Game.ProcessName ?? "" }.Save(path);
        }
        LoadProfileList(path);
        RefreshGameProfiles();
        ShowPage(_cheatsPage);
    }

    /// <summary>
    /// Abre el juego desde su tienda y deja preparado su perfil: en cuanto arranca el .exe,
    /// MiTrainer se conecta solo (gracias al "proceso" del perfil).
    /// </summary>
    void PlayGame(GameCard card)
    {
        var game = card.Game;
        if (card.ProfilePath != null) LoadProfileList(card.ProfilePath);
        else SetProfile(new GameProfile { Game = game.Name, Process = game.ProcessName ?? "" }, null);

        try
        {
            var start = game.LaunchUri != null
                ? new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true }
                : new ProcessStartInfo(game.Executable!) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.Executable) };
            Process.Start(start);
            SetStatus(game.ProcessName != null
                ? $"Abriendo {game.Name}… me conectaré solo a {game.ProcessName}"
                : $"Abriendo {game.Name}… no sé su .exe: conéctate con la lista de arriba", Theme.TextDim);
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir {game.Name}: {ex.Message}", Theme.Danger);
        }
    }
}
