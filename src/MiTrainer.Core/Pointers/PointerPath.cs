using System.Globalization;
using MiTrainer.Core.Memory;

namespace MiTrainer.Core.Pointers;

/// <summary>Una cadena de punteros que empieza en una dirección estática: "juego.exe"+1A2B → 18 → 10.</summary>
public sealed record PointerPath(string Module, long BaseOffset, IReadOnlyList<long> Offsets)
{
    public string BaseText => $"\"{Module}\"+{BaseOffset:X}";
    public string OffsetsText => string.Join(" → ", Offsets.Select(o => o.ToString("X")));
    public override string ToString() => $"{BaseText} → {OffsetsText}";

    /// <summary>Dirección a la que apunta ahora, o 0 si la cadena está rota o el módulo no está cargado.</summary>
    public long Resolve(IProcessMemory memory, IReadOnlyDictionary<string, long> moduleBases) =>
        moduleBases.TryGetValue(Module, out long moduleBase) ? memory.ResolvePointer(moduleBase + BaseOffset, Offsets) : 0;

    /// <summary>Forma compacta para guardar cientos de miles en un archivo: "juego.exe|1A2B|18|10".</summary>
    public string ToCompactString() =>
        string.Join('|', [Module, BaseOffset.ToString("X"), .. Offsets.Select(o => o.ToString("X"))]);

    public static PointerPath ParseCompact(string text)
    {
        var parts = text.Split('|');
        if (parts.Length < 3) throw new FormatException($"Cadena de punteros no válida: {text}");
        return new PointerPath(parts[0], ParseHex(parts[1]), parts[2..].Select(ParseHex).ToArray());
    }

    static long ParseHex(string text) => long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // Los records comparan listas por referencia: comparamos el contenido.
    public bool Equals(PointerPath? other) =>
        other is not null && Module == other.Module && BaseOffset == other.BaseOffset && Offsets.SequenceEqual(other.Offsets);

    public override int GetHashCode() => HashCode.Combine(Module, BaseOffset, Offsets.Count);
}
