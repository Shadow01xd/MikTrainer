# 0001 — WinForms sobre .NET 10

**Estado:** aceptada

## Contexto

Un trainer necesita llamar a la API de Windows (`OpenProcess`, `ReadProcessMemory`, `RegisterHotKey`...),
ser una aplicación de escritorio ligera y tener una interfaz cuidada (estilo Wand: oscura, con tarjetas e interruptores).

## Decisión

C# con .NET 10 y WinForms, con los controles importantes dibujados a mano (`PaintedControl`) y el modo oscuro
de WinForms (`Application.SetColorMode`) para barra de título, listas y desplegables.

## Alternativas descartadas

- **C++**: máximo control, pero mucho más lento de escribir y probar para lo que necesitamos.
- **Electron / Tauri** (como Wand): interfaz web muy flexible, pero añade un proceso y una capa más entre la
  interfaz y la memoria del juego, y mucho más peso.
- **WPF / WinUI**: más potentes para interfaces, pero más complejos; con WinForms y controles dibujados se
  consigue el aspecto buscado con menos piezas.

## Consecuencias

- La API de Windows se llama con `LibraryImport` (código generado, sin reflexión).
- La app es de 64 bits (`PlatformTarget x64`) para poder leer juegos de 64 bits.
- Toda la lógica vive fuera de WinForms (ver [0002](0002-arquitectura-en-capas.md)), así cambiar de tecnología de
  interfaz en el futuro solo afectaría a `MiTrainer.App`.
