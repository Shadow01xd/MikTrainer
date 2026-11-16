using System.Diagnostics;
using MiTrainer.App.Dialogs;

namespace MiTrainer.App.Pages;

/// <summary>Página «Cheats»: el perfil abierto, sus cheats como tarjetas y las acciones para crearlos y editarlos.</summary>
sealed class CheatsPage : Page
{
    readonly ComboBox _profileBox = Theme.Input(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 });
    readonly Label _profileInfo = Ui.Label("");
    readonly FlowLayoutPanel _cards = Ui.CardList();
    readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = Theme.Body,
        ForeColor = Theme.TextDim,
        Visible = false,
        Text = "Este perfil todavía no tiene cheats.\n\nPulsa «+ Nuevo cheat», o ve al Escáner, encuentra un valor y pulsa «Añadir como cheat».",
    };
    bool _fillingProfiles;

    record ProfileItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    public CheatsPage(AppServices services) : base(services)
    {
        _profileBox.SelectedIndexChanged += (_, _) => OnProfileSelected();
        _cards.ClientSizeChanged += (_, _) => Ui.FitCards(_cards);

        var toolbar = Ui.Row(DockStyle.Top, Ui.Label("Perfil"), _profileBox,
            Ui.Pill("+ Nuevo cheat", NewCheat, primary: true),
            Ui.Pill("Recargar", () => OpenProfile(Session.ProfilePath)),
            Ui.Pill("Guardar", SaveProfile),
            Ui.Pill("Abrir carpeta", OpenFolder),
            _profileInfo);

        Controls.Add(_cards);
        Controls.Add(_empty);
        Controls.Add(toolbar);

        Session.ProfileChanged += (_, _) => ShowProfile();
        Session.CheatChanged += (_, cheat) => CardFor(cheat)?.UpdateState();
    }

    /// <summary>Abre el perfil indicado, o el primero de la carpeta (o uno vacío si no hay ninguno).</summary>
    public void OpenProfile(string? path)
    {
        path ??= Services.Profiles.List().FirstOrDefault();
        if (path is null)
        {
            Session.OpenProfile(new GameProfile(), null);
            return;
        }
        try
        {
            Session.OpenProfile(Services.Profiles.Load(path), path);
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, $"No se pudo leer el perfil:\n{ex.Message}", "MiTrainer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= Perfil =================

    void ShowProfile()
    {
        FillProfileList();
        RebuildCards();
        _profileInfo.Text = Session.SaveState switch
        {
            ProfileSaveState.Error => "No se pudo guardar: " + Session.SaveError,
            ProfileSaveState.Unsaved when Session.Cheats.Count > 0 => $"{CheatCount()} · sin guardar: pulsa «Guardar»",
            ProfileSaveState.Unsaved => "",
            _ => $"{CheatCount()} · ✓ guardado",
        };
    }

    string CheatCount() => Session.Cheats.Count == 1 ? "1 cheat" : $"{Session.Cheats.Count} cheats";

    void FillProfileList()
    {
        _fillingProfiles = true;
        _profileBox.Items.Clear();
        foreach (var path in Services.Profiles.List()) _profileBox.Items.Add(new ProfileItem(path));
        _profileBox.SelectedItem = _profileBox.Items.Cast<ProfileItem>()
            .FirstOrDefault(p => string.Equals(p.Path, Session.ProfilePath, StringComparison.OrdinalIgnoreCase));
        _fillingProfiles = false;
    }

    void OnProfileSelected()
    {
        if (_fillingProfiles || _profileBox.SelectedItem is not ProfileItem item || item.Path == Session.ProfilePath) return;
        OpenProfile(item.Path);
    }

    void SaveProfile()
    {
        string? path = Session.ProfilePath;
        if (path is null)
        {
            using var dialog = new SaveFileDialog
            {
                InitialDirectory = Services.Profiles.Folder,
                Filter = "Perfil (*.json)|*.json",
                FileName = string.IsNullOrWhiteSpace(Session.Profile.Game) ? "mi-juego.json" : Session.Profile.Game + ".json",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            path = dialog.FileName;
        }
        Session.SaveProfileAs(path);
    }

    void OpenFolder()
    {
        Directory.CreateDirectory(Services.Profiles.Folder);
        Process.Start("explorer.exe", Services.Profiles.Folder);
    }

    // ================= Tarjetas =================

    void RebuildCards()
    {
        _cards.SuspendLayout();
        foreach (Control card in _cards.Controls) card.Dispose();
        _cards.Controls.Clear();
        foreach (var cheat in Session.Cheats) _cards.Controls.Add(CreateCard(cheat));
        _cards.ResumeLayout();
        _empty.Visible = Session.Cheats.Count == 0;
        _cards.Visible = !_empty.Visible;
    }

    CheatCard CreateCard(Cheat cheat)
    {
        var card = new CheatCard(cheat) { Width = Ui.CardWidth(_cards) };
        card.ToggleRequested += (_, _) => Session.Toggle(cheat);
        card.EditRequested += (_, _) => EditCheat(cheat);
        card.DuplicateRequested += (_, _) => DuplicateCheat(cheat);
        card.DeleteRequested += (_, _) => DeleteCheat(cheat);
        return card;
    }

    CheatCard? CardFor(Cheat cheat) => _cards.Controls.OfType<CheatCard>().FirstOrDefault(c => c.Cheat == cheat);

    // ================= Crear, editar, duplicar y eliminar =================

    void NewCheat()
    {
        var template = new CheatDefinition { Hotkey = Session.NextFreeHotkey() };
        if (ShowEditor(template, isNew: true, editing: null) is { } definition) Session.AddCheat(definition);
    }

    void EditCheat(Cheat cheat)
    {
        if (ShowEditor(cheat.Definition, isNew: false, editing: cheat) is { } definition) Session.ReplaceCheat(cheat, definition);
    }

    void DuplicateCheat(Cheat cheat) =>
        Session.AddCheat(cheat.Definition with { Name = cheat.Definition.Name + " (copia)", Hotkey = Session.NextFreeHotkey() });

    void DeleteCheat(Cheat cheat)
    {
        var answer = MessageBox.Show(this, $"¿Eliminar «{cheat.Definition.Name}»?", "MiTrainer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) Session.RemoveCheat(cheat);
    }

    CheatDefinition? ShowEditor(CheatDefinition definition, bool isNew, Cheat? editing)
    {
        // Con el editor abierto soltamos los atajos globales para poder "grabar" F1, F2...
        using var _ = Services.Hotkeys.Suspend();
        var used = Session.Cheats.Where(c => c != editing).Select(c => c.Definition.Hotkey).OfType<string>();
        using var editor = new CheatEditor(definition, isNew, Session.Memory, used);
        return editor.ShowDialog(this) == DialogResult.OK ? editor.Result : null;
    }
}
