# Motor de memoria

Cómo funcionan por dentro las piezas que leen y cambian el juego. Todo el código está en `MiTrainer.Core`
salvo el acceso real a Windows (`Infrastructure/Memory`).

## Leer y escribir

`WindowsProcessMemory` abre el proceso con `OpenProcess` y usa `ReadProcessMemory` / `WriteProcessMemory`.
Si una zona es de solo lectura (código del juego), quita la protección un instante con `VirtualProtectEx`,
escribe, la restaura y limpia la caché de instrucciones.

Las regiones vienen de `VirtualQueryEx`: solo se usan las **reservadas** (`MEM_COMMIT`) y legibles, y cada una
dice si es escribible o ejecutable. MiTrainer es de 64 bits y puede leer juegos de 32 y 64 bits; en los de 32
los punteros miden 4 bytes (`IProcessMemory.Is32Bit`).

## Escáner de valores (`ValueScanner`)

Como el de Cheat Engine:

1. **Primer escaneo**: recorre toda la memoria escribible en bloques de 1 MB buscando el valor exacto,
   cada 4 bytes (los valores de los juegos van alineados). Puede dar cientos de miles de resultados.
2. **Siguientes escaneos**: solo vuelve a leer esas direcciones y se queda con las que cumplen la condición
   (*exacto*, *aumentó*, *disminuyó*, *cambió*, *sin cambios*). Lee página a página (4 KB) en vez de dirección
   a dirección, que es muchísimo más rápido.

Con `float`/`double` se compara redondeando (diferencia menor que 0,5), porque el juego enseña "100" aunque
en memoria haya 99,73.

## Cadenas de punteros

La dirección de la vida cambia cada vez que se abre el juego, pero suele haber un camino fijo hasta ella que
empieza en el `.exe` o en una dll (direcciones **estáticas**):

```
"juego.exe"+2000  →  lee puntero, suma 18  →  lee puntero, suma 10  =  vida
```

`ProcessMemoryExtensions.ResolvePointer` sigue ese camino con la misma semántica que Cheat Engine:
en cada paso lee el puntero y le suma el siguiente offset.

### Escáner de punteros (`PointerMap` + `PointerScanner`)

1. **Mapa**: `PointerMap.Build` recorre la memoria escribible y anota cada valor que *parece* un puntero
   (apunta dentro de alguna región válida), junto con dónde está guardado. Se ordena por el valor apuntado.
2. **Búsqueda hacia atrás** desde el objetivo, por niveles:
   - ¿Quién apunta a una dirección entre `objetivo - offsetMáx` y `objetivo`? Búsqueda binaria en el mapa.
   - Si ese "quién" está dentro de un módulo, es una dirección estática: **cadena encontrada**.
   - Si no, pasa al siguiente nivel como nuevo objetivo (sin repetir direcciones ya vistas).
3. El árbol de búsqueda se guarda en listas paralelas (`SearchTree`) para no crear millones de objetos.

Límites: 100 000 cadenas y 300 000 nodos por nivel. Un juego grande puede dar muchísimas cadenas: por eso se
**filtra** tras reiniciar el juego (`PointerScanner.Filter`), quedándose con las que siguen llegando a la nueva
dirección del valor. Tras dos o tres reinicios suelen quedar pocas y fiables; las del `.exe` principal y con
pocos offsets se muestran primero.

## Parches de código (AOB)

Un parche busca una secuencia de bytes del código del juego (`BytePattern`, con `??` como comodín) y la
reemplaza, por ejemplo por `90` (NOP, "no hacer nada") para anular la instrucción que resta munición.

- La búsqueda (`PatternScanner`) recorre el módulo indicado en bloques que se solapan, para no perder
  coincidencias en los bordes. `"*"` busca en todo el código cargado.
- Al activar se guardan los bytes originales; al desactivar (o al cerrar MiTrainer) se restauran.
- Buscar por patrón en vez de por dirección hace que el parche siga funcionando cuando el juego se actualiza,
  mientras esa parte del código no cambie.

## Tipos de cheat

| Tipo | Activar | Mientras está activo | Desactivar |
|---|---|---|---|
| `congelar` | Escribe el valor | Lo reescribe cada 50 ms | Deja de escribir |
| `fijar` | Escribe el valor una vez | — (no queda activo) | — |
| `parche` | Guarda los bytes originales y escribe los nuevos | — | Restaura los originales |

`Cheat.Enable` nunca lanza excepciones: si algo falla (puntero roto, patrón no encontrado...) queda
`Failed = true` con el motivo en `Status`, y la tarjeta lo muestra en rojo.
