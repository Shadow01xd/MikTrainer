// Juego de prueba para practicar con MiTrainer: busca la vida o el oro con el escáner y modifícalos.
// Teclas: D = recibir daño, G = ganar oro, A = ver la dirección de la vida (pista), Q = salir.
// Con la entrada redirigida (pruebas automáticas) lee comandos por línea: d, g, a, p (mostrar), q.
using System.Runtime.CompilerServices;

var jugador = new Jugador();
bool automatico = Console.IsInputRedirected;
Mostrar();

while (true)
{
    char tecla = automatico
        ? (Console.ReadLine() ?? "q").TrimStart('﻿').FirstOrDefault() // algunos programas mandan un BOM al principio
        : Console.ReadKey(intercept: true).KeyChar;

    switch (char.ToLowerInvariant(tecla))
    {
        case 'd':
            jugador.Vida -= 7;
            if (jugador.Vida <= 0)
            {
                jugador.Vida = 100;
                if (!automatico) Console.WriteLine("¡Has muerto! Vuelves a empezar...");
            }
            break;
        case 'g':
            jugador.Oro += 25;
            break;
        case 'a':
            // Pista para comprobar que el escáner encontró la dirección correcta.
            unsafe { Console.WriteLine($"DIR=0x{(nint)Unsafe.AsPointer(ref jugador.Vida):X}"); }
            continue;
        case 'q':
            return;
    }
    Mostrar();
}

void Mostrar()
{
    if (automatico)
    {
        Console.WriteLine($"VIDA={jugador.Vida} ORO={jugador.Oro}");
        return;
    }
    Console.WriteLine();
    Console.WriteLine($"  Vida: {jugador.Vida,4}    Oro: {jugador.Oro,6}");
    Console.WriteLine("  [D] recibir daño   [G] ganar oro   [A] pista: dirección de la vida   [Q] salir");
}

class Jugador
{
    public int Vida = 100;
    public int Oro = 50;
}
