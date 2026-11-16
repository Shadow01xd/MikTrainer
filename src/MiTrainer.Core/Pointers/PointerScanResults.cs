using MiTrainer.Core.Values;

namespace MiTrainer.Core.Pointers;

/// <summary>Resultado de un escaneo de punteros, tal como se guarda entre sesiones.</summary>
/// <param name="Process">Juego al que pertenecen las cadenas (nombre del proceso sin .exe).</param>
/// <param name="Kind">Tipo del valor al que apuntan (para mostrarlo en la lista).</param>
/// <param name="Filters">Cuántas veces se han filtrado: cuantas más, más fiables son las que quedan.</param>
public sealed record PointerScanResults(string Process, ValueKind Kind, int Filters, DateTime Date, IReadOnlyList<PointerPath> Paths);

/// <summary>Dónde se guardan los resultados de los escaneos de punteros.</summary>
public interface IPointerResultsRepository
{
    string Folder { get; }

    /// <summary>Archivo donde se guardan solos los resultados de un juego.</summary>
    string AutoSavePathFor(string processName);

    void Save(string path, PointerScanResults results);
    PointerScanResults Load(string path);
}
