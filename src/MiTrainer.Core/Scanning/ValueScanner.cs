using MiTrainer.Core.Memory;
using MiTrainer.Core.Values;

namespace MiTrainer.Core.Scanning;

public enum ScanMode { Exact, Increased, Decreased, Changed, Unchanged }

/// <summary>
/// Escáner de valores como el de Cheat Engine: un primer escaneo por valor exacto
/// y escaneos siguientes que van descartando direcciones.
/// </summary>
public sealed class ValueScanner
{
    const int MaxResults = 20_000_000;
    const int PageSize = 4096;
    const int Alignment = 4; // los valores de los juegos casi siempre están alineados a 4 bytes

    long[] _addresses = [];
    double[] _values = [];

    public ValueKind Kind { get; private set; }
    public bool HasResults { get; private set; }
    public int Count => _addresses.Length;
    public IReadOnlyList<long> Addresses => _addresses;

    /// <summary>Valor de cada dirección en el último escaneo.</summary>
    public IReadOnlyList<double> Values => _values;

    public void Reset()
    {
        _addresses = [];
        _values = [];
        HasResults = false;
    }

    /// <summary>Recorre toda la memoria escribible buscando <paramref name="target"/>.</summary>
    public void FirstScan(IProcessMemory memory, ValueKind kind, double target, CancellationToken cancel = default)
    {
        Kind = kind;
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
                    if (addresses.Count >= MaxResults) goto done;
                }
            }
        }
    done:
        _addresses = [.. addresses];
        _values = [.. values];
        HasResults = true;
    }

    /// <summary>Vuelve a leer solo las direcciones que quedan y se queda con las que cumplen <paramref name="mode"/>.</summary>
    public void NextScan(IProcessMemory memory, ScanMode mode, double target, CancellationToken cancel = default)
    {
        int size = Kind.Size();
        var addresses = new List<long>();
        var values = new List<double>();
        var page = new byte[PageSize];
        long cachedPage = -1;
        bool pageOk = false;

        for (int i = 0; i < _addresses.Length; i++)
        {
            if ((i & 0xFFFF) == 0) cancel.ThrowIfCancellationRequested();
            long address = _addresses[i];
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
                value = Kind.FromBytes(page, offset);
            }
            else if (memory.Read(address, size) is { } bytes) value = Kind.FromBytes(bytes);
            else continue;

            if (!Keeps(mode, value, _values[i], target)) continue;
            addresses.Add(address);
            values.Add(value);
        }
        _addresses = [.. addresses];
        _values = [.. values];
    }

    bool Keeps(ScanMode mode, double value, double previous, double target) => mode switch
    {
        ScanMode.Exact => Kind.Matches(value, target),
        ScanMode.Increased => value > previous,
        ScanMode.Decreased => value < previous,
        ScanMode.Changed => value != previous,
        ScanMode.Unchanged => value == previous,
        _ => false,
    };
}
