# Convenciones

## Idioma

- **Código en inglés**: clases, métodos, variables (`TrainerSession`, `FindInstalled`).
- **Todo lo que lee una persona en español**: textos de la interfaz, mensajes de error, comentarios,
  documentación, nombres de las pruebas y campos de los archivos JSON (`"nombre"`, `"direccion"`).

## Código

- **Avisos = errores** (`TreatWarningsAsErrors` en `Directory.Build.props`) y nullable activado en toda la solución.
- Un archivo por clase pública importante; las piezas pequeñas muy ligadas pueden compartir archivo
  (p. ej. `CheatType` con `CheatDefinition`).
- **Modelos inmutables** cuando se puede (`record` con `init`); para cambiar uno se crea una copia con `with`.
- **Sin números mágicos**: constantes con nombre (`MaxResults`, `PageSize`, `AnimationStep`).
- Los errores esperables (puntero roto, archivo mal escrito) se capturan **por tipo** (`catch (IOException)`,
  `when (ex is ...)`) y se convierten en un mensaje para el usuario. Nunca `catch` vacío sin explicar por qué.
- Métodos cortos con un nombre que diga qué hacen; si un bloque necesita un comentario para entenderse,
  suele ser mejor un método con ese nombre.
- Los comentarios explican el **porqué** ("los decimales casi nunca son exactos..."), no el qué.
- Las dependencias entre capas siguen [la arquitectura](arquitectura.md): nada en Core puede usar Windows ni WinForms.

## Interfaz

- Colores y fuentes solo en `Theming/Theme.cs`.
- Piezas repetidas (filas, listas, botones) con `Controls/Ui.cs`.
- Las páginas no se conocen entre sí: se comunican por eventos de la sesión o a través de `MainForm`.

## Git

- Rama principal: `main`.
- Mensajes de commit en español, con una primera línea corta que diga qué cambia y una lista de detalles debajo.
- **Sin líneas de atribución** (`Co-Authored-By`, "Generated with...") en commits ni pull requests.
- Nunca se suben `bin/`, `obj/` ni los datos del usuario (`perfiles/` y `punteros/` de la carpeta de salida).
