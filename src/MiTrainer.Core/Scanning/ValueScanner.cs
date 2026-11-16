using MiTrainer.Core.Memory;
using MiTrainer.Core.Values;

namespace MiTrainer.Core.Scanning;

public enum ScanMode { Exact, Increased, Decreased, Changed, Unchanged }

/// <summary>
/// Resultado de un escaneo: direcciones candidatas y su valor en ese momento. Es inmutable: cada escaneo devuelve
/// uno nuevo, así quien lo lanzó decide si lo adopta o lo descarta (p. ej. si la sesión cambió mientras tanto).
/// </summary>
public sealed class ScanResults
{
    readonly long[] _addresses;
    readonly double[] _values;

    internal ScanResults(ValueKind kind, long[] addresses, double[] values)
    {
        Kind = kind;
        _addresses = addresses;
        _values = values;
    }

    public ValueKind Kind { get; }
    public int Count => _addresses.Length;
    public IReadOnlyList<long> Addresses => _addresses;

    /// <summary>Valor de cada dirección en el momento del escaneo.</summary>
    public IReadOnlyList<double> Values => _values;
}

/// <summary>
/// Escáner de valores como el de Cheat Engine: un primer escaneo por valor exacto
/// y escaneos siguientes que van descartando direcciones.
/// </summary>
public static class ValueScanner
{
    const int MaxResults = 20_000_000;
    const int PageSize = 4096;
    const int Alignment = 4; // los valores de los juegos casi siempre están alineados a 4 bytes

    /// <summary>Recorre toda la memoria escribible buscando <paramref name="target"/>.</summary>
    public static ScanResults First(IProcessMemory memory, ValueKind kind, double target, CancellationToken cancel = default)
    {
        int size = kind.Size();
        var addresses = new List<long>();
        var values = new List<double>();
        var buffer = new byte[1 << 20];

        foreach (var region in memory.GetRegions().Where(r => r.Writable))
        {
            for (long position = region.Start; position < region.End; position += buffer.Length)
            {
                cancel.ThrowIfCancellationRequested();
                int length = (int)Math.Min(buffer.Length, region.End - position);
                if (!memory.TryRead(position, buffer, length)) continue;
                for (int i = 0; i + size <= length; i += Alignment)
                {
                    double value = kind.FromBytes(buffer, i);
                    if (!kind.Matches(value, target)) continue;
                    addresses.Add(position + i);
                    values.Add(value);
                    if (addresses.Count >= MaxResults) return new ScanResults(kind, [.. addresses], [.. values]);
                }
            }
        }
        return new ScanResults(kind, [.. addresses], [.. values]);
    }

    /// <summary>Vuelve a leer solo las direcciones de <paramref name="previous"/> y se queda con las que cumplen <paramref name="mode"/>.</summary>
    public static ScanResults Next(ScanResults previous, IProcessMemory memory, ScanMode mode, double target, CancellationToken cancel = default)
    {
        var kind = previous.Kind;
        int size = kind.Size();
        var addresses = new List<long>();
        var values = new List<double>();
        var page = new byte[PageSize];
        long cachedPage = -1;
        bool pageOk = false;

        for (int i = 0; i < previous.Count; i++)
        {
            if ((i & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
            long address = previous.Addresses[i];
            long pageStart = address & ~(long)(PageSize - 1);
            int offset = (int)(address - pageStart);
            double value;

            // Leemos página a página en vez de dirección a dirección: muchísimo más rápido.
            if (offset + size <= PageSize)
            {
                if (pageStart != cachedPage)
                {
                    cachedPage = pageStart;
                    pageOk = memory.TryRead(pageStart, page, PageSize);
                }
                if (!pageOk) continue;
                value = kind.FromBytes(page, offset);
            }
            else if (memory.Read(address, size) is { } bytes) value = kind.FromBytes(bytes);
            else continue;

            if (!Keeps(kind, mode, value, previous.Values[i], target)) continue;
            addresses.Add(address);
            values.Add(value);
        }
        return new ScanResults(kind, [.. addresses], [.. values]);
    }

    static bool Keeps(ValueKind kind, ScanMode mode, double value, double previous, double target) => mode switch
    {
        ScanMode.Exact => kind.Matches(value, target),
        ScanMode.Increased => value > previous,
        ScanMode.Decreased => value < previous,
        ScanMode.Changed => value != previous,
        ScanMode.Unchanged => value == previous,
        _ => false,
    };
}
