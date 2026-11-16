using System.Globalization;

namespace MiTrainer.Core.Values;

/// <summary>Conversión entre texto escrito por el usuario y números (decimales y hexadecimales).</summary>
public static class NumberText
{
    /// <summary>Acepta "5.5" y también "5,5" (teclados en español).</summary>
    public static bool TryParseDecimal(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>"1A2B", "0x1A2B" o "-10", siempre en hexadecimal (como en Cheat Engine).</summary>
    public static bool TryParseHex(string? text, out long value)
    {
        value = 0;
        string s = text?.Trim() ?? "";
        bool negative = s.StartsWith('-');
        if (negative) s = s[1..];
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return false;
        if (negative) value = -value;
        return true;
    }

    public static long ParseHex(string? text) =>
        TryParseHex(text, out long value) ? value : throw new FormatException($"«{text}» no es un número hexadecimal");

    /// <summary>"90 90 90" → { 0x90, 0x90, 0x90 }.</summary>
    public static byte[] ParseHexBytes(string? text)
    {
        try
        {
            var bytes = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b, 16)).ToArray();
            return bytes.Length > 0 ? bytes : throw new FormatException("No hay bytes");
        }
        catch (Exception ex) when (ex is not FormatException)
        {
            throw new FormatException($"«{text}» no son bytes hexadecimales", ex);
        }
    }

    public static string ToHex(long value) => value.ToString("X", CultureInfo.InvariantCulture);
}
