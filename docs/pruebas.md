# Pruebas

```bash
dotnet test MiTrainer.slnx
```

Hay un proyecto de pruebas por capa, todos con xUnit:

| Proyecto | Qué prueba | Cómo | Tiempo |
|---|---|---|---|
| `MiTrainer.Core.Tests` | Escáner, punteros, patrones, cheats, `TrainerSession`, modelos y JSON | Con dobles de prueba: sin juegos ni Windows | < 1 s |
| `MiTrainer.Infrastructure.Tests` | Tiendas (Steam, Epic), adivinar el `.exe`, repositorios JSON y la **memoria real** de Windows | Carpetas falsas en un directorio temporal y el juego de prueba como proceso de verdad | ~1 s |
| `MiTrainer.App.Tests` | Editor de cheats (validación, «Comprobar»), atajos, tarjetas | Ventanas ocultas, sin mostrar nada | ~1 s |

## Dobles de prueba (`tests/MiTrainer.TestSupport`)

- **`FakeProcessMemory`**: un proceso inventado con regiones y módulos en las direcciones que elija la prueba.
  Permite fabricar exactamente la situación a probar (una cadena de punteros, un patrón en el código...).
- **`FakeProcessConnector`**: "abre" y "cierra" esas memorias falsas para probar la conexión automática.
- **`InMemoryProfileRepository`**: perfiles en memoria, guardados como JSON para comportarse como archivos.

## El juego de prueba (`samples/JuegoPrueba`)

Un programa de consola con vida y oro. Con la entrada redirigida acepta comandos por línea (`d` = daño,
`a` = dirección real de la vida, `p` = mostrar, `q` = salir). `TestGame` lo lanza y lo maneja así en las
pruebas de integración.

Como .NET no guarda sus variables dentro del `.exe`, la prueba de punteros **fabrica** una cadena estática
(un hueco libre en la sección de datos del exe → un bloque reservado con `Allocate` → la vida), igual que la
tendría un juego en C++.

## Regla: las pruebas nunca tocan el escritorio

Ninguna prueba muestra ventanas, mueve el ratón ni pulsa teclas: el usuario puede estar usando el equipo
(o jugando). Ver [la decisión 0004](decisiones/0004-pruebas-sin-escritorio.md).

- Los formularios se crean sin mostrarlos (`CreateControl`).
- Para hacer capturas, `Sta.Capture` muestra la ventana a 32 000 píxeles fuera de la pantalla y **sin activarla**
  (`SW_SHOWNOACTIVATE`), la dibuja y la vuelve a ocultar.
- Los atajos se prueban llamando a `HotkeyBox.Record` directamente, no pulsando teclas.

## Regenerar las capturas de la documentación

```bash
set MITRAINER_CAPTURAS=capturas
dotnet test tests/MiTrainer.App.Tests --filter Capturas_para_la_documentacion
```
