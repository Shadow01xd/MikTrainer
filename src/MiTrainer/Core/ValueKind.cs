using System.Globalization;

namespace MiTrainer.Core;

public enum ValueKind { Int32, Int64, Float, Double }

public static class ValueKinds
{
    public static int Size(this ValueKind kind) => kind is ValueKind.Int64 or ValueKind.Double ? 8 : 4;

    public static bool IsInteger(this ValueKind kind) => kind is ValueKind.Int32 or ValueKind.Int64;

    public static double FromBytes(this ValueKind kind, byte[] bytes, int offset = 0) => kind switch
    {
        ValueKind.Int32 => BitConverter.ToInt32(bytes, offset),
        ValueKind.Int64 => BitConverter.ToInt64(bytes, offset),
        ValueKind.Float => BitConverter.ToSingle(bytes, offset),
        _ => BitConverter.ToDouble(bytes, offset),
    };

    public static byte[] ToBytes(this ValueKind kind, double value) => kind switch
    {
        ValueKind.Int32 => BitConverter.GetBytes((int)value),
        ValueKind.Int64 => BitConverter.GetBytes((long)value),
        ValueKind.Float => BitConverter.GetBytes((float)value),
        _ => BitConverter.GetBytes(value),
    };

    public static string Format(this ValueKind kind, double value) =>
        kind.IsInteger() ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Nombre usado en los perfiles JSON: int, long, float, double.</summary>
    public static string Name(this ValueKind kind) => kind switch
    {
        ValueKind.Int32 => "int",
        ValueKind.Int64 => "long",
        ValueKind.Float => "float",
        _ => "double",
    };

    public static ValueKind Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "long" or "int64" => ValueKind.Int64,
        "float" => ValueKind.Float,
        "double" => ValueKind.Double,
        _ => ValueKind.Int32,
    };

    /// <summary>Acepta tanto "5.5" como "5,5".</summary>
    public static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

public static class Hex
{
    /// <summary>"1A2B", "0x1A2B" o "-10" (en hexadecimal, como en Cheat Engine).</summary>
    public static long ParseNumber(string text)
    {
        string s = text.Trim();
        bool negative = s.StartsWith('-');
        if (negative) s = s[1..];
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        long value = long.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    /// <summary>"90 90 90" → { 0x90, 0x90, 0x90 }.</summary>
    public static byte[] ParseBytes(string? text)
    {
        var bytes = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b, 16)).ToArray();
        return bytes.Length > 0 ? bytes : throw new FormatException("Faltan los 'bytes' del parche");
    }
}
