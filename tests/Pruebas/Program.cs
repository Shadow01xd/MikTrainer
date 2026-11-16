// Prueba automática del motor: abre JuegoPrueba, encuentra la vida con el escáner
// y comprueba escribir, congelar y parchear (y que todo se deshace al desactivar).
using System.Diagnostics;
using System.Text.Json;
using MiTrainer.Core;

if (args.Length == 0)
{
    Console.WriteLine("Uso: Pruebas <ruta a JuegoPrueba.exe>");
    return 2;
}

int fallos = 0;
void Comprobar(bool ok, string que)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALLO")} {que}");
    if (!ok) fallos++;
}

using var juego = Process.Start(new ProcessStartInfo(args[0])
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
})!;

// Envía un comando al juego y devuelve la vida que imprime.
int Enviar(string comando)
{
    if (comando.Length > 0) juego.StandardInput.WriteLine(comando);
    string? linea;
    while ((linea = juego.StandardOutput.ReadLine()) != null)
        if (linea.StartsWith("VIDA="))
            return int.Parse(linea.Split(' ')[0][5..]);
    throw new Exception("El juego se cerró");
}

try
{
    int vida = Enviar("");
    using var mem = new MemoryEngine();
    mem.Attach(juego);
    Comprobar(mem.IsAttached, $"conectado a JuegoPrueba (PID {juego.Id}, {(mem.Is32Bit ? 32 : 64)} bits)");

    var escaner = new ValueScanner(mem);
    escaner.FirstScan(ValueKind.Int32, vida);
    Console.WriteLine($"      primer escaneo ({vida}): {escaner.Count:N0} resultados");
    vida = Enviar("d");
    escaner.NextScan(ScanMode.Decreased, 0);
    Console.WriteLine($"      'disminuyó' ({vida}): {escaner.Count:N0} resultados");
    for (int i = 0; i < 5 && escaner.Count > 1; i++)
    {
        vida = Enviar("d");
        escaner.NextScan(ScanMode.Exact, vida);
        Console.WriteLine($"      exacto ({vida}): {escaner.Count:N0} resultados");
    }
    Comprobar(escaner.Count is >= 1 and <= 3, "el escáner encontró la vida");

    foreach (long direccion in escaner.Addresses)
        mem.WriteValue((nint)direccion, ValueKind.Int32, 999);
    Comprobar(Enviar("p") == 999, "escribir vida = 999");

    var congelar = new Cheat(new CheatDef
    {
        Type = CheatTypes.Freeze,
        Address = $"{escaner.Addresses[0]:X}",
        ValueType = "int",
        Value = 500,
    });
    Comprobar(congelar.Enable(mem), "activar 'congelar': " + congelar.Status);
    Enviar("d");
    congelar.Tick(mem);
    Comprobar(Enviar("p") == 500, "congelado: la vida sigue en 500 tras recibir daño");
    congelar.Disable(mem);
    Comprobar(Enviar("d") == 493, "desactivado: el daño vuelve a funcionar");

    var parche = new Cheat(new CheatDef { Type = CheatTypes.Patch, Pattern = "4D 5A ?? 00", Bytes = "AA BB" });
    nint inicio = mem.ModuleBase(null);
    Comprobar(parche.Enable(mem) && mem.Read(inicio, 2) is [0xAA, 0xBB], "parche por patrón AOB aplicado: " + parche.Status);
    parche.Disable(mem);
    Comprobar(mem.Read(inicio, 2) is [0x4D, 0x5A], "parche revertido a los bytes originales");

    var punteroRoto = new Cheat(new CheatDef { Type = CheatTypes.Freeze, Address = "10", Offsets = ["8"], Value = 1 });
    Comprobar(!punteroRoto.Enable(mem), "un puntero inválido da error en vez de romper: " + punteroRoto.Status);

    // ---------- Escáner de punteros: mapa hecho a mano ----------
    // "juego.exe"+2000 → [A]+18 → [B]+10 = objetivo, con algo de ruido alrededor
    {
        long a = 0x2_0000_0000, b = 0x3_0000_0000, objetivo = b + 0x10;
        var mapa = PointerMap.FromEntries(
            [(0x1_4000_2000, a), (a + 0x18, b), (0x5_0000_0000, objetivo + 0x5000), (0x5_0000_0008, a)],
            [("juego.exe", 0x1_4000_0000, 0x1_4001_0000)]);
        var cadenas = PointerScanner.Scan(mapa, objetivo, maxDepth: 3, maxOffset: 0x100);
        Comprobar(cadenas.Any(c => c.ToString() == "\"juego.exe\"+2000 → 18 → 10"),
            $"mapa de prueba: encuentra \"juego.exe\"+2000 → 18 → 10  [{string.Join("; ", cadenas)}]");
        Comprobar(PointerScanner.Scan(mapa, objetivo, maxDepth: 1, maxOffset: 0x100).Count == 0, "mapa de prueba: respeta la profundidad máxima");
    }

    // ---------- Escáner de punteros: juego real ----------
    // .NET no guarda sus variables dentro del .exe, así que fabricamos una cadena estática como la de un juego en C++:
    // un hueco en la sección de datos del exe → bloque de memoria → +18 → (vida - 10)
    long vidaDir = escaner.Addresses[0];
    var exe = mem.FindModule(null);
    long exeInicio = exe.BaseAddress, exeFin = exeInicio + exe.ModuleMemorySize;
    nint hueco = 0;
    foreach (var region in mem.Regions().Where(r => r.Writable && r.Start >= exeInicio && r.Start < exeFin))
        for (long p = region.Start + region.Size - 8; p >= region.Start && hueco == 0; p -= 8)
            if (mem.Read((nint)p, 8) is { } b && BitConverter.ToInt64(b) == 0) hueco = (nint)p;
    Comprobar(hueco != 0, $"hay un hueco estático libre en {exe.ModuleName}+{hueco - exeInicio:X}");

    nint bloque = mem.Allocate(0x100);
    mem.Write(bloque + 0x18, BitConverter.GetBytes(vidaDir - 0x10));
    mem.Write(hueco, BitConverter.GetBytes((long)bloque));

    var reloj = Stopwatch.StartNew();
    var mapaReal = PointerMap.Build(mem);
    var cadenasReales = PointerScanner.Scan(mapaReal, vidaDir, maxDepth: 3, maxOffset: 0x400);
    var bases = mem.ModuleBases();
    string esperada = $"\"{exe.ModuleName}\"+{hueco - exeInicio:X} → 18 → 10";
    Comprobar(cadenasReales.Any(c => c.ToString() == esperada),
        $"juego real: encuentra {esperada}  ({cadenasReales.Count:N0} cadenas, {mapaReal.Count:N0} punteros en memoria, {reloj.Elapsed.TotalSeconds:0.0} s)");
    Comprobar(cadenasReales.All(c => c.Resolve(mem, bases) == (nint)vidaDir), "todas las cadenas encontradas apuntan de verdad a la vida");

    // Simulamos un reinicio: la vida "se muda" a otra dirección y solo la cadena buena la sigue.
    nint nuevaVida = mem.Allocate(0x100) + 0x40;
    mem.Write(bloque + 0x18, BitConverter.GetBytes((long)nuevaVida - 0x10));
    var filtradas = PointerScanner.Filter(mem, cadenasReales, bases, a => a == nuevaVida);
    Comprobar(filtradas.Any(c => c.ToString() == esperada), $"filtrar tras el 'reinicio' conserva la cadena buena ({filtradas.Count} de {cadenasReales.Count})");

    var porPuntero = new Cheat(new CheatDef
    {
        Type = CheatTypes.Freeze,
        Module = exe.ModuleName,
        Address = $"{hueco - exeInicio:X}",
        Offsets = ["18", "10"],
        ValueType = "int",
        Value = 777,
    });
    Comprobar(porPuntero.Enable(mem) && mem.ReadValue(nuevaVida, ValueKind.Int32) == 777, "cheat por puntero escribe en la dirección nueva: " + porPuntero.Status);
    // Guardar y volver a cargar las cadenas (como al cerrar y abrir MiTrainer)
    string archivo = Path.Combine(Path.GetTempPath(), $"mitrainer-prueba-{Environment.ProcessId}.json");
    var guardado = new PointerSave("JuegoPrueba", ValueKind.Float, 2, new DateTime(2026, 11, 16, 12, 30, 0), cadenasReales);
    reloj.Restart();
    PointerFile.Save(archivo, guardado);
    var cargado = PointerFile.Load(archivo);
    long bytes = new FileInfo(archivo).Length;
    File.Delete(archivo);
    Comprobar(cargado.Process == "JuegoPrueba" && cargado.Kind == ValueKind.Float && cargado.Filters == 2 && cargado.Date == guardado.Date,
        "archivo de punteros: conserva juego, tipo de valor, filtros y fecha");
    Comprobar(cargado.Paths.Select(c => c.ToString()).SequenceEqual(cadenasReales.Select(c => c.ToString())),
        $"archivo de punteros: las {cadenasReales.Count} cadenas vuelven idénticas ({bytes / 1024.0:0.0} KB, {reloj.Elapsed.TotalMilliseconds:0} ms)");
    var cadenaCargada = cargado.Paths.First(c => c.ToString() == esperada);
    Comprobar(cadenaCargada.Resolve(mem, bases) == nuevaVida, "una cadena cargada del archivo sigue llegando a la vida");

    mem.Write(hueco, new byte[8]); // dejamos el exe como estaba
}
finally
{
    if (!juego.HasExited)
    {
        juego.StandardInput.WriteLine("q");
        if (!juego.WaitForExit(2000)) juego.Kill();
    }
}

// ---------- Detección de juegos instalados ----------
string falso = Path.Combine(Path.GetTempPath(), $"mitrainer-tiendas-{Environment.ProcessId}");
try
{
    // Steam: biblioteca principal + una segunda en otro "disco"
    string steam = Path.Combine(falso, "Steam"), otraBiblioteca = Path.Combine(falso, "D", "SteamLibrary");
    Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
    Directory.CreateDirectory(Path.Combine(otraBiblioteca, "steamapps"));
    File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
        $"\"libraryfolders\"\n{{\n\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{steam.Replace(@"\", @"\\")}\"\n\t}}\n" +
        $"\t\"1\"\n\t{{\n\t\t\"path\"\t\t\"{otraBiblioteca.Replace(@"\", @"\\")}\"\n\t}}\n}}");
    void JuegoSteam(string biblioteca, string id, string nombre, string carpeta)
    {
        File.WriteAllText(Path.Combine(biblioteca, "steamapps", $"appmanifest_{id}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{id}\"\n\t\"name\"\t\t\"{nombre}\"\n\t\"installdir\"\t\t\"{carpeta}\"\n}}");
        Directory.CreateDirectory(Path.Combine(biblioteca, "steamapps", "common", carpeta));
    }
    JuegoSteam(steam, "1086940", "Baldur's Gate 3", "Baldurs Gate 3");
    JuegoSteam(otraBiblioteca, "105600", "Terraria", "Terraria");
    JuegoSteam(steam, "228980", "Steamworks Common Redistributables", "Steamworks Shared");
    // El .exe del juego (el más grande) frente a otros que no lo son
    string bg3 = Path.Combine(steam, "steamapps", "common", "Baldurs Gate 3");
    Directory.CreateDirectory(Path.Combine(bg3, "bin"));
    File.WriteAllBytes(Path.Combine(bg3, "bin", "bg3.exe"), new byte[50_000]);
    File.WriteAllBytes(Path.Combine(bg3, "bin", "CrashReporter.exe"), new byte[90_000]);
    File.WriteAllBytes(Path.Combine(bg3, "unins000.exe"), new byte[90_000]);
    File.WriteAllBytes(Path.Combine(bg3, "bin", "bg3_dx11.exe"), new byte[40_000]);

    var steamJuegos = GameLibrary.Steam(steam).ToList();
    var bg3Juego = steamJuegos.FirstOrDefault(j => j.Name == "Baldur's Gate 3");
    Comprobar(steamJuegos.Count == 2 && steamJuegos.Any(j => j.Name == "Terraria"),
        $"Steam: encuentra los juegos de las dos bibliotecas y descarta las herramientas [{string.Join(", ", steamJuegos.Select(j => j.Name))}]");
    Comprobar(bg3Juego is { ProcessName: "bg3.exe", LaunchUri: "steam://rungameid/1086940" },
        $"Steam: adivina el .exe (bg3.exe, no el instalador ni el CrashReporter) y el enlace para abrirlo: {bg3Juego?.ProcessName}");

    // Epic: un juego y una aplicación que no es juego
    string epic = Path.Combine(falso, "Epic");
    string sb = Path.Combine(falso, "Games", "StellarBlade");
    Directory.CreateDirectory(epic);
    Directory.CreateDirectory(sb);
    File.WriteAllText(Path.Combine(epic, "a.item"), JsonSerializer.Serialize(new
    {
        DisplayName = "Stellar Blade", InstallLocation = sb, LaunchExecutable = "SB.exe",
        CatalogNamespace = "ns", CatalogItemId = "item", AppName = "Stellar", AppCategories = new[] { "public", "games" },
    }));
    File.WriteAllText(Path.Combine(epic, "b.item"), JsonSerializer.Serialize(new
    {
        DisplayName = "Unreal Engine", InstallLocation = sb, AppCategories = new[] { "engines" },
    }));
    var epicJuegos = GameLibrary.Epic(epic).ToList();
    Comprobar(epicJuegos is [{ Name: "Stellar Blade", ProcessName: "SB.exe", LaunchUri: var uri }] && uri!.StartsWith("com.epicgames.launcher://apps/ns%3Aitem%3AStellar"),
        $"Epic: detecta el juego con su .exe y enlace, e ignora lo que no es juego [{string.Join(", ", epicJuegos.Select(j => j.Name))}]");
}
finally
{
    Directory.Delete(falso, recursive: true);
}

// Escaneo real de este equipo (solo lectura): informativo, no falla si no hay tiendas.
var relojJuegos = Stopwatch.StartNew();
var instalados = GameLibrary.Scan();
Console.WriteLine($"      en este equipo: {instalados.Count} juegos ({string.Join(", ", instalados.GroupBy(j => j.Store).Select(g => $"{g.Key} {g.Count()}"))}) en {relojJuegos.Elapsed.TotalSeconds:0.0} s");
foreach (var instalado in instalados.Take(8))
    Console.WriteLine($"        {instalado.Store,-6} {instalado.Name}  →  {instalado.ProcessName ?? "(.exe desconocido)"}");

Console.WriteLine(fallos == 0 ? "\nTodo correcto." : $"\n{fallos} prueba(s) fallaron.");
return fallos == 0 ? 0 : 1;
