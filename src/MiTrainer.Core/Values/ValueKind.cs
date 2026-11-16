using System.Globalization;
using System.Text.Json.Serialization;

namespace MiTrainer.Core.Values;

/// <summary>Tipo de un valor en la memoria del juego. En JSON se escribe como "int", "long", "float" o "double".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValueKind>))]
public enum ValueKind
{
    [JsonStringEnumMemberName("int")] Int32,
    [JsonStringEnumMemberName("long")] Int64,
    [JsonStringEnumMemberName("float")] Float,
    [JsonStringEnumMemberName("double")] Double,
}

public static class ValueKinds
{
    /// <summary>Los tipos en el orden en que se muestran en la interfaz.</summary>
    public static IReadOnlyList<ValueKind> All { get; } = [ValueKind.Int32, ValueKind.Int64, ValueKind.Float, ValueKind.Double];

    public static int Size(this ValueKind kind) => kind is ValueKind.Int64 or ValueKind.Double ? 8 : 4;

    public static bool IsInteger(this ValueKind kind) => kind is ValueKind.Int32 or ValueKind.Int64;

    /// <summary>Nombre corto ("int", "float"...), el mismo que en los archivos JSON.</summary>
    public static string Name(this ValueKind kind) => kind switch
    {
        ValueKind.Int32 => "int",
        ValueKind.Int64 => "long",
        ValueKind.Float => "float",
        _ => "double",
    };

    public static ValueKind Parse(string? name) =>
        All.FirstOrDefault(k => string.Equals(k.Name(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// ¿Coincide un valor leído con el que busca el usuario? Los decimales casi nunca son exactos
    /// (99.73 se ve como 100 en el juego), así que con float/double basta con estar a menos de 0.5.
    /// </summary>
    public static bool Matches(this ValueKind kind, double value, double target) =>
        kind.IsInteger() ? value == target : Math.Abs(value - target) < 0.5;
}
