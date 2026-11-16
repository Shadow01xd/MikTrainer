using System.Text.Json.Serialization;
using MiTrainer.Core.Values;

namespace MiTrainer.Core.Cheats;

/// <summary>Qué hace un cheat. En JSON: "congelar", "fijar" o "parche".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CheatType>))]
public enum CheatType
{
    /// <summary>Escribe el valor continuamente (vida infinita, munición infinita...).</summary>
    [JsonStringEnumMemberName("congelar")] Freeze,

    /// <summary>Escribe el valor una sola vez (dar dinero, subir de nivel...).</summary>
    [JsonStringEnumMemberName("fijar")] Set,

    /// <summary>Busca un patrón de bytes en el código y lo reemplaza (NOP, saltos...). Se deshace al desactivarlo.</summary>
    [JsonStringEnumMemberName("parche")] Patch,
}

/// <summary>
/// Un cheat tal como se guarda en el perfil. Es inmutable: para cambiarlo se crea una copia con <c>with</c>.
/// Direcciones y offsets van en hexadecimal (texto), igual que los escribe el usuario y Cheat Engine.
/// </summary>
public sealed record CheatDefinition
{
    [JsonPropertyName("nombre")] public string Name { get; init; } = "";
    [JsonPropertyName("atajo")] public string? Hotkey { get; init; }
    [JsonPropertyName("tipo")] public CheatType Type { get; init; }

    // ---- congelar / fijar ----

    /// <summary>Exe o dll donde empieza la dirección. Vacío = dirección absoluta (en "parche": el exe principal).</summary>
    [JsonPropertyName("modulo")] public string? Module { get; init; }

    [JsonPropertyName("direccion")] public string? Address { get; init; }

    /// <summary>Cadena de punteros a seguir desde la dirección (vacía si no hay).</summary>
    [JsonPropertyName("offsets")] public IReadOnlyList<string>? Offsets { get; init; }

    [JsonPropertyName("tipoValor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ValueKind ValueType { get; init; }

    [JsonPropertyName("valor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Value { get; init; }

    // ---- parche ----

    [JsonPropertyName("patron")] public string? Pattern { get; init; }

    [JsonPropertyName("desplazamiento"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PatternOffset { get; init; }

    [JsonPropertyName("bytes")] public string? Bytes { get; init; }

    public string Describe() => Type switch
    {
        CheatType.Freeze => $"Mantiene el valor en {ValueType.Format(Value)}",
        CheatType.Set => $"Pone el valor a {ValueType.Format(Value)}",
        _ => "Modifica el código del juego",
    };
}

/// <summary>Perfil de un juego: cómo se llama su proceso y qué cheats tiene.</summary>
public sealed class GameProfile
{
    [JsonPropertyName("juego")] public string Game { get; set; } = "";

    /// <summary>Ejecutable del juego ("juego.exe"): MiTrainer se conecta solo cuando aparece.</summary>
    [JsonPropertyName("proceso")] public string Process { get; set; } = "";

    [JsonPropertyName("cheats")] public List<CheatDefinition> Cheats { get; set; } = [];
}

/// <summary>Dónde se guardan los perfiles de los juegos.</summary>
public interface IProfileRepository
{
    string Folder { get; }

    /// <summary>Rutas de todos los perfiles, ordenadas por nombre.</summary>
    IReadOnlyList<string> List();

    GameProfile Load(string path);
    void Save(string path, GameProfile profile);

    /// <summary>Ruta libre para un perfil nuevo con ese nombre ("Mi juego.json", "Mi juego (2).json"...).</summary>
    string NewPathFor(string name);
}
