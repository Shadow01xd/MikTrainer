# Formatos de archivo

Los dos archivos se guardan en JSON con nombres de campo en español, junto a `MiTrainer.exe`.
Se escriben primero a un temporal y luego se mueven encima, así un fallo a medias nunca deja un archivo roto.

## Perfil de un juego — `perfiles/<juego>.json`

Admite comentarios `//` y comas finales. Ejemplo completo: [`src/MiTrainer.App/perfiles/ejemplo.json`](../src/MiTrainer.App/perfiles/ejemplo.json).

```jsonc
{
  "juego": "Mi Juego",          // nombre que se muestra
  "proceso": "MiJuego.exe",     // MiTrainer se conecta solo cuando aparece este .exe
  "cheats": [ /* ... */ ]
}
```

### Campos de cada cheat

| Campo | Tipos | Descripción |
|---|---|---|
| `nombre` | todos | Nombre en la tarjeta |
| `atajo` | todos | `F1`, `Ctrl+F2`, `Alt+Shift+NumPad1`... (opcional) |
| `tipo` | todos | `congelar`, `fijar` o `parche` |
| `modulo` | todos | `congelar`/`fijar`: módulo donde empieza la dirección (vacío = dirección absoluta). `parche`: dónde buscar (vacío = el `.exe`) |
| `direccion` | congelar, fijar | Hexadecimal. Con módulo, distancia desde su inicio |
| `offsets` | congelar, fijar | Lista de offsets hexadecimales de la cadena de punteros (`["18", "10"]`) |
| `tipoValor` | congelar, fijar | `int` (por defecto), `long`, `float` o `double` |
| `valor` | congelar, fijar | Número a escribir |
| `patron` | parche | Bytes hexadecimales separados por espacios; `??` = cualquiera |
| `desplazamiento` | parche | Bytes desde el inicio del patrón hasta donde empieza el parche (por defecto 0) |
| `bytes` | parche | Bytes nuevos (`90` = NOP) |

Las direcciones y offsets se guardan como **texto hexadecimal** porque así los escribe el usuario y así
aparecen en Cheat Engine.

## Resultados de punteros — `punteros/<proceso>.json`

Se guardan solos tras cada búsqueda o filtro, y se cargan al conectar con ese juego.

```json
{
  "proceso": "JuegoPrueba",
  "tipoValor": "int",
  "filtros": 2,
  "fecha": "2026-11-16T12:30:00",
  "cadenas": [
    "JuegoPrueba.exe|28FF8|18|10",
    "coreclr.dll|4327E0|A70|D80"
  ]
}
```

Cada cadena va en una línea compacta (`módulo|base|offset|offset...`, en hexadecimal) porque un escaneo puede
tener cientos de miles: 42 000 cadenas ocupan unos 2 MB. `filtros` dice cuántas veces se han filtrado
(cuantas más, más fiables las que quedan).
