using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiTrainer.Infrastructure.Storage;

/// <summary>Opciones JSON y escritura segura compartidas por los repositorios.</summary>
internal static class JsonStorage
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip, // los perfiles pueden llevar // comentarios
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // tildes y ñ legibles en el archivo
    };

    public static T Read<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options) ?? throw new FormatException($"{Path.GetFileName(path)} está vacío");
    }

    /// <summary>Escribe a un temporal y lo mueve encima: si algo falla a medias, el archivo anterior sigue intacto.</summary>
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, value, Options);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Nombre de archivo válido a partir de un nombre de juego ("Stellar Blade™" → "Stellar Blade").</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string clean = new([.. name.Where(c => !invalid.Contains(c) && c is not ('™' or '®'))]);
        return string.IsNullOrWhiteSpace(clean) ? "perfil" : clean.Trim();
    }
}
