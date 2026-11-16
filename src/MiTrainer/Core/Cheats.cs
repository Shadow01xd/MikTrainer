using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiTrainer.Core;

public static class CheatTypes
{
    /// <summary>Escribe el valor continuamente (vida infinita, munición infinita...).</summary>
    public const string Freeze = "congelar";
    /// <summary>Escribe el valor una sola vez (dar dinero, subir de nivel...).</summary>
    public const string Set = "fijar";
    /// <summary>Busca un patrón de bytes en el código y lo reemplaza (NOP, saltos...).</summary>
    public const string Patch = "parche";
}

/// <summary>Un cheat tal como se guarda en el perfil JSON.</summary>
public sealed class CheatDef
{
    [JsonPropertyName("nombre")] public string Name { get; set; } = "Cheat";
    [JsonPropertyName("atajo")] public string? Hotkey { get; set; }
    [JsonPropertyName("tipo")] public string Type { get; set; } = CheatTypes.Freeze;

    // congelar / fijar
    [JsonPropertyName("modulo")] public string? Module { get; set; }
    [JsonPropertyName("direccion")] public string? Address { get; set; }
    [JsonPropertyName("offsets")] public List<string>? Offsets { get; set; }
    [JsonPropertyName("tipoValor")] public string? ValueType { get; set; }
    [JsonPropertyName("valor")] public double Value { get; set; }

    // parche
    [JsonPropertyName("patron")] public string? Pattern { get; set; }
    [JsonPropertyName("desplazamiento")] public int PatternOffset { get; set; }
    [JsonPropertyName("bytes")] public string? Bytes { get; set; }
}

/// <summary>Perfil de un juego: qué proceso y qué cheats tiene.</summary>
public sealed class GameProfile
{
    [JsonPropertyName("juego")] public string Game { get; set; } = "";
    [JsonPropertyName("proceso")] public string Process { get; set; } = "";
    [JsonPropertyName("cheats")] public List<CheatDef> Cheats { get; set; } = [];

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static GameProfile Load(string path) =>
        JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(path), Options) ?? new GameProfile();

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    /// <summary>Copia independiente de un cheat (para duplicarlo).</summary>
    public static CheatDef Clone(CheatDef def) =>
        JsonSerializer.Deserialize<CheatDef>(JsonSerializer.Serialize(def, Options), Options)!;
}

/// <summary>Un cheat en ejecución: sabe activarse, desactivarse y mantenerse.</summary>
public sealed class Cheat(CheatDef def)
{
    nint _patchAddress;
    byte[]? _original;

    public CheatDef Def { get; } = def;
    public bool Enabled { get; private set; }
    public string Status { get; set; } = "";
    /// <summary>El último intento de activarlo dio error (el mensaje está en Status).</summary>
    public bool Failed { get; private set; }

    public bool Enable(MemoryEngine mem)
    {
        Failed = false;
        try
        {
            switch (Def.Type)
            {
                case CheatTypes.Patch:
                    if (string.IsNullOrWhiteSpace(Def.Pattern)) throw new InvalidOperationException("Falta el 'patron'");
                    byte[] bytes = Hex.ParseBytes(Def.Bytes);
                    nint at = mem.AobScan(Def.Pattern, Def.Module);
                    if (at == 0) throw new InvalidOperationException("Patrón no encontrado (¿otra versión del juego?)");
                    at += Def.PatternOffset;
                    _original = mem.Read(at, bytes.Length) ?? throw new InvalidOperationException("No se pudo leer la memoria");
                    if (!mem.Write(at, bytes)) throw new InvalidOperationException("No se pudo escribir la memoria");
                    _patchAddress = at;
                    Enabled = true;
                    Status = $"Activo (parche en 0x{at:X})";
                    break;

                case CheatTypes.Freeze:
                case CheatTypes.Set:
                    nint address = WriteValue(mem);
                    Enabled = Def.Type == CheatTypes.Freeze;
                    Status = Enabled
                        ? $"Congelado en {Def.Value} (0x{address:X})"
                        : $"Valor {Def.Value} escrito en 0x{address:X}";
                    break;

                default:
                    throw new InvalidOperationException($"Tipo desconocido: '{Def.Type}'");
            }
            return true;
        }
        catch (Exception ex)
        {
            Enabled = false;
            Failed = true;
            Status = ex.Message;
            return false;
        }
    }

    public void Disable(MemoryEngine mem)
    {
        if (Enabled && _original != null && mem.IsAttached)
            mem.Write(_patchAddress, _original);
        _original = null;
        Enabled = false;
        Failed = false;
        Status = "Desactivado";
    }

    /// <summary>No se pudo ni intentar activarlo (p. ej. no hay juego conectado).</summary>
    public void Fail(string message)
    {
        Failed = true;
        Status = message;
    }

    /// <summary>El juego se cerró: olvidamos el estado sin tocar memoria.</summary>
    public void Reset()
    {
        _original = null;
        Enabled = false;
        Failed = false;
        Status = "";
    }

    /// <summary>Se llama muchas veces por segundo para mantener los valores congelados.</summary>
    public void Tick(MemoryEngine mem)
    {
        if (!Enabled || Def.Type != CheatTypes.Freeze) return;
        try { WriteValue(mem); }
        catch { /* en pantallas de carga el puntero puede no ser válido un momento */ }
    }

    public nint ResolveAddress(MemoryEngine mem)
    {
        if (string.IsNullOrWhiteSpace(Def.Address)) throw new InvalidOperationException("Falta la 'direccion'");
        nint address = (nint)Hex.ParseNumber(Def.Address);
        if (!string.IsNullOrWhiteSpace(Def.Module)) address += mem.ModuleBase(Def.Module);
        if (Def.Offsets is { Count: > 0 })
            address = mem.ResolvePointer(address, Def.Offsets.Select(Hex.ParseNumber).ToList());
        return address;
    }

    nint WriteValue(MemoryEngine mem)
    {
        nint address = ResolveAddress(mem);
        if (address == 0 || !mem.WriteValue(address, ValueKinds.Parse(Def.ValueType), Def.Value))
            throw new InvalidOperationException("Dirección no válida o puntero roto");
        return address;
    }
}
