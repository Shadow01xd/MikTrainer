using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiTrainer.Core;

/// <summary>Resultados de un escaneo de punteros guardados en disco.</summary>
/// <param name="Filters">Cuántas veces se han filtrado (cuantas más, más fiables las que quedan).</param>
public sealed record PointerSave(string Process, ValueKind Kind, int Filters, DateTime Date, List<PointerPath> Paths);

/// <summary>
/// Guarda y carga cadenas de punteros en JSON. Cada cadena va en una línea compacta
/// ("juego.exe|1A2B|18|10") porque un escaneo puede tener cientos de miles.
/// </summary>
public static class PointerFile
{
    sealed class Dto
    {
        [JsonPropertyName("proceso")] public string Process { get; set; } = "";
        [JsonPropertyName("tipoValor")] public string ValueType { get; set; } = "int";
        [JsonPropertyName("filtros")] public int Filters { get; set; }
        [JsonPropertyName("fecha")] public DateTime Date { get; set; }
        [JsonPropertyName("cadenas")] public List<string> Paths { get; set; } = [];
    }

    public static void Save(string file, PointerSave save)
    {
        var dto = new Dto
        {
            Process = save.Process,
            ValueType = save.Kind.Name(),
            Filters = save.Filters,
            Date = save.Date,
            Paths = save.Paths.Select(Encode).ToList(),
        };
        // Escribimos a un temporal y luego lo movemos: si algo falla a medias, el archivo anterior sigue intacto.
        string temp = file + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, dto, new JsonSerializerOptions { WriteIndented = true });
        File.Move(temp, file, overwrite: true);
    }

    public static PointerSave Load(string file)
    {
        using var stream = File.OpenRead(file);
        var dto = JsonSerializer.Deserialize<Dto>(stream) ?? throw new FormatException("Archivo vacío");
        return new PointerSave(dto.Process, ValueKinds.Parse(dto.ValueType), dto.Filters, dto.Date,
            dto.Paths.Select(Decode).ToList());
    }

    public static string Encode(PointerPath path) =>
        string.Join('|', [path.Module, path.BaseOffset.ToString("X"), .. path.Offsets.Select(o => o.ToString("X"))]);

    public static PointerPath Decode(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 3) throw new FormatException($"Cadena no válida: {line}");
        return new PointerPath(parts[0],
            long.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            parts[2..].Select(p => long.Parse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray());
    }
}
