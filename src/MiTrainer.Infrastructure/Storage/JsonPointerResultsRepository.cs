using System.Text.Json.Serialization;
using MiTrainer.Core.Pointers;
using MiTrainer.Core.Values;

namespace MiTrainer.Infrastructure.Storage;

/// <summary>
/// Resultados de escaneos de punteros como JSON. Cada cadena va en una línea compacta ("juego.exe|1A2B|18|10")
/// porque un escaneo puede tener cientos de miles.
/// </summary>
public sealed class JsonPointerResultsRepository(string folder) : IPointerResultsRepository
{
    public string Folder { get; } = folder;

    public string AutoSavePathFor(string processName) => Path.Combine(Folder, JsonStorage.SafeFileName(processName) + ".json");

    public void Save(string path, PointerScanResults results) => JsonStorage.Write(path, new FileFormat
    {
        Process = results.Process,
        ValueType = results.Kind,
        Filters = results.Filters,
        Date = results.Date,
        Paths = [.. results.Paths.Select(p => p.ToCompactString())],
    });

    public PointerScanResults Load(string path)
    {
        var file = JsonStorage.Read<FileFormat>(path);
        return new PointerScanResults(file.Process, file.ValueType, file.Filters, file.Date, [.. file.Paths.Select(PointerPath.ParseCompact)]);
    }

    /// <summary>Forma exacta del archivo en disco (separada del modelo para poder cambiar uno sin el otro).</summary>
    sealed class FileFormat
    {
        [JsonPropertyName("proceso")] public string Process { get; set; } = "";
        [JsonPropertyName("tipoValor")] public ValueKind ValueType { get; set; }
        [JsonPropertyName("filtros")] public int Filters { get; set; }
        [JsonPropertyName("fecha")] public DateTime Date { get; set; }
        [JsonPropertyName("cadenas")] public List<string> Paths { get; set; } = [];
    }
}
