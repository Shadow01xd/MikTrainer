using System.Diagnostics;
using MiTrainer.Infrastructure.Memory;

namespace MiTrainer.Infrastructure.Tests;

/// <summary>
/// El juego de prueba (samples/JuegoPrueba) lanzado como proceso de verdad, con la entrada y salida redirigidas
/// para manejarlo por comandos: "d" recibe daño, "a" dice la dirección real de la vida, "p" muestra el estado.
/// </summary>
public sealed class TestGame : IDisposable
{
    readonly Process _process;

    public TestGame()
    {
        _process = Process.Start(new ProcessStartInfo(FindExecutable())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        Life = ReadLife(_process.StandardOutput.ReadLine()!);
        Memory = WindowsProcessMemory.Open(Process.GetProcessById(_process.Id));
    }

    public WindowsProcessMemory Memory { get; }

    /// <summary>Vida según el propio juego (lo que imprimió la última vez).</summary>
    public int Life { get; private set; }

    /// <summary>Dirección real de la vida, que el juego nos chiva para comprobar los escaneos.</summary>
    public long LifeAddress => Convert.ToInt64(Send("a")[6..], 16);

    /// <summary>Cierra el juego y espera a que termine (la memoria sigue abierta, como cuando el usuario sale del juego).</summary>
    public void Exit()
    {
        _process.StandardInput.WriteLine("q");
        if (!_process.WaitForExit(5000)) _process.Kill();
        _process.WaitForExit();
    }

    public int TakeDamage() => Life = ReadLife(Send("d"));
    public int ReadLife() => Life = ReadLife(Send("p"));

    string Send(string command)
    {
        _process.StandardInput.WriteLine(command);
        return _process.StandardOutput.ReadLine() ?? throw new InvalidOperationException("El juego de prueba se cerró");
    }

    static int ReadLife(string line) => int.Parse(line.Split(' ')[0]["VIDA=".Length..]);

    /// <summary>samples/JuegoPrueba/bin/&lt;Debug|Release&gt;/net10.0/JuegoPrueba.exe, buscando la raíz de la solución.</summary>
    static string FindExecutable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MiTrainer.slnx"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("No se encontró la raíz de la solución (MiTrainer.slnx)");
        string configuration = AppContext.BaseDirectory.Contains(@"\Release\") ? "Release" : "Debug";
        return Path.Combine(dir.FullName, "samples", "JuegoPrueba", "bin", configuration, "net10.0", "JuegoPrueba.exe");
    }

    public void Dispose()
    {
        Memory.Dispose();
        if (!_process.HasExited)
        {
            _process.StandardInput.WriteLine("q");
            if (!_process.WaitForExit(2000)) _process.Kill();
        }
        _process.Dispose();
    }
}
