# 0005 — Detección de juegos leyendo las tiendas

**Estado:** aceptada

## Contexto

La página «Mis juegos» tiene que mostrar los juegos instalados, como hace Wand, sin pedir al usuario que los añada a mano.

## Decisión

Leer los archivos que dejan las propias tiendas, sin conectarse a internet:

| Tienda | Origen |
|---|---|
| Steam | `steamapps/libraryfolders.vdf` (bibliotecas) y `appmanifest_<id>.acf` (un juego cada uno) |
| Epic | `ProgramData/Epic/EpicGamesLauncher/Data/Manifests/*.item` (JSON) |
| GOG | Registro: `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games` |

Cada tienda es un `IGameStore`; `GameLibrary` las junta, quita duplicados y aguanta que alguna falle.

Steam no dice cuál es el `.exe` del juego, así que `ExecutableGuesser` lo adivina: el más grande que no sea un
instalador, lanzador o herramienta, con ventaja para el que se parece al nombre de la carpeta o es el
`-Shipping` de Unreal. En un equipo real con 46 juegos acertó en 45 (el otro es el que indica la propia Epic).

## Alternativas descartadas

- **APIs web de las tiendas**: requieren cuenta, claves y conexión, y no dicen dónde está instalado nada.
- **Pedir al usuario la carpeta de cada juego**: justo lo que la página quiere evitar.

## Consecuencias

- Añadir otra tienda es una clase nueva en `Infrastructure/Games` y una línea en `Program.cs`.
- Si el `.exe` adivinado falla, se corrige en el `"proceso"` del perfil del juego.
- También aparecen programas que Steam vende (Wallpaper Engine, Aseprite); se pueden ignorar.
