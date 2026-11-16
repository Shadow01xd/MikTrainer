# 0002 — Arquitectura en capas (Core / Infrastructure / App)

**Estado:** aceptada (sustituye a la primera versión, con todo dentro de un único proyecto)

## Contexto

La primera versión tenía la lógica, el acceso a Windows y la interfaz en un solo proyecto, con una `MainForm`
de más de 1 100 líneas que hacía de todo: conexión, perfiles, cheats, escáner, punteros y atajos. Las pruebas eran
programas de consola que necesitaban el juego de prueba para casi todo.

## Decisión

Tres proyectos con dependencias que solo apuntan hacia dentro:

- **`MiTrainer.Core`**: modelos, algoritmos e interfaces. Sin Windows ni WinForms (`net10.0`).
- **`MiTrainer.Infrastructure`**: implementa las interfaces de Core para Windows.
- **`MiTrainer.App`**: la interfaz. `Program.cs` es la **raíz de composición**, el único sitio que elige las
  implementaciones concretas.

Además:

- La memoria del juego se abstrae en `IProcessMemory`; las operaciones de alto nivel son métodos de extensión.
- `TrainerSession` concentra el estado del trainer y lo comunica por eventos; la interfaz solo reacciona.
- `MainForm` se divide en páginas independientes (`Pages/`) que no se conocen entre sí.
- Los atajos globales usan su propia ventana invisible (`GlobalHotkeys`) en vez de la ventana principal.

## Alternativas descartadas

- **MVVM completo / contenedor de inyección de dependencias**: más infraestructura de la que necesita una app
  de este tamaño. Pasar `AppServices` por constructor es suficiente y explícito.
- **Más capas (Domain / Application separadas)**: Core ya es pequeño y cohesionado; partirlo más solo añadiría proyectos.

## Consecuencias

- La lógica se prueba con una memoria falsa en milisegundos (45 pruebas de Core en ~0,2 s).
- Añadir una tienda, un repositorio o una página no obliga a tocar el resto (ver «Cómo ampliar» en [arquitectura](../arquitectura.md)).
- Hay que respetar la regla de dependencias: nada en Core puede usar Windows. Core apunta a `net10.0`
  (no `-windows`), así que el compilador lo impide.
