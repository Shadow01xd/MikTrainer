namespace MiTrainer.Core;

public enum ScanMode { Exact, Increased, Decreased, Changed, Unchanged }

/// <summary>
/// Escáner de valores como el de Cheat Engine: primer escaneo por valor exacto
/// y luego escaneos siguientes para ir descartando direcciones.
/// </summary>
public sealed class ValueScanner(MemoryEngine mem)
{
    const int MaxResults = 20_000_000;

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

    public void FirstScan(ValueKind kind, double target)
    {
        Kind = kind;
        int size = kind.Size();
        var addresses = new List<long>();
        var values = new List<double>();
        var buffer = new byte[1 << 20];

        foreach (var region in mem.Regions())
        {
            if (!region.Writable) continue;
            long end = region.Start + region.Size;
            for (long pos = region.Start; pos < end; pos += buffer.Length)
            {
                int length = (int)Math.Min(buffer.Length, end - pos);
                if (!mem.ReadInto((nint)pos, buffer, length)) continue;
                for (int i = 0; i + size <= length; i += 4)
                {
                    double value = kind.FromBytes(buffer, i);
                    if (!IsEqual(value, target)) continue;
                    addresses.Add(pos + i);
                    values.Add(value);
                    if (addresses.Count >= MaxResults) goto done;
                }
            }
        }
    done:
        _addresses = addresses.ToArray();
        _values = values.ToArray();
        HasResults = true;
    }

    public void NextScan(ScanMode mode, double target)
    {
        int size = Kind.Size();
        var addresses = new List<long>();
        var values = new List<double>();
        var page = new byte[4096];
        long cachedPage = -1;
        bool pageOk = false;

        for (int i = 0; i < _addresses.Length; i++)
        {
            long address = _addresses[i];
            long pageStart = address & ~0xFFFL;
            int offset = (int)(address - pageStart);
            double value;

            // Leemos página a página (4 KB) en vez de dirección a dirección: mucho más rápido.
            if (offset + size <= page.Length)
            {
                if (pageStart != cachedPage)
                {
                    cachedPage = pageStart;
                    pageOk = mem.ReadInto((nint)pageStart, page, page.Length);
                }
                if (!pageOk) continue;
                value = Kind.FromBytes(page, offset);
            }
            else
            {
                if (mem.Read((nint)address, size) is not { } bytes) continue;
                value = Kind.FromBytes(bytes);
            }

            double previous = _values[i];
            bool keep = mode switch
            {
                ScanMode.Exact => IsEqual(value, target),
                ScanMode.Increased => value > previous,
                ScanMode.Decreased => value < previous,
                ScanMode.Changed => value != previous,
                ScanMode.Unchanged => value == previous,
                _ => false,
            };
            if (keep)
            {
                addresses.Add(address);
                values.Add(value);
            }
        }
        _addresses = addresses.ToArray();
        _values = values.ToArray();
    }

    // Los decimales casi nunca son exactos (99.73 se ve como 100 en el juego), así que redondeamos.
    bool IsEqual(double value, double target) =>
        Kind.IsInteger() ? value == target : Math.Abs(value - target) < 0.5;
}
