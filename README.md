# MiTrainer

Trainer de cheats para juegos **de un jugador** en Windows (C# / .NET 10 / WinForms).
No lo uses en juegos online: el anti-cheat lo detecta y te banean.

## Proyectos

| Carpeta | Qué es |
|---|---|
| `src/MiTrainer` | La app: conexión al juego, cheats con atajos y escáner de memoria |
| `src/MiTrainer/Core` | El motor: leer/escribir memoria, punteros, patrones AOB, escáner |
| `src/MiTrainer/perfiles` | Un `.json` por juego con sus cheats (mira `ejemplo.json`) |
| `src/JuegoPrueba` | Un "juego" de consola para practicar |
| `tests/Pruebas` | Prueba automática del motor contra JuegoPrueba |
| `tests/PruebasUI` | Prueba del editor y las tarjetas (sin mostrar ventanas ni tocar ratón/teclado) |

## Ejecutar

```bash
dotnet run --project src/MiTrainer
```

## Practicar con el juego de prueba

1. `dotnet run --project src/JuegoPrueba` (otra terminal)
2. En MiTrainer: elige `JuegoPrueba.exe` → **Conectar**
3. Pestaña **Escáner**: Valor `100` → **Primer escaneo**
4. Pulsa `D` en el juego (vida 93) → Valor `93` → **Siguiente escaneo**. Repite hasta 1–2 resultados
5. Doble clic en el resultado → se añade como cheat "congelar" con atajo F1
6. Pestaña **Cheats**: márcalo o pulsa F1. Recibe daño: la vida no baja

## Crear y editar cheats

- **+ Nuevo cheat** abre el editor: nombre, tipo, atajo (haz clic y pulsa la combinación), módulo,
  dirección, offsets, valor… o patrón y bytes si es un parche
- **Comprobar en el juego** te dice a qué dirección llega y cuánto vale ahora, sin activar nada
- En cada tarjeta: **✎** para editar, y clic derecho para **Duplicar** o **Eliminar**
- Los cambios se guardan solos en el perfil (si es nuevo, pulsa **Guardar** una vez para darle nombre)

## Tipos de cheat (perfil JSON)

- `congelar`: escribe un valor continuamente (vida, munición)
- `fijar`: escribe un valor una vez (dinero)
- `parche`: busca un patrón de bytes en el código y lo reemplaza (p. ej. por `90` = NOP). Se deshace al desactivar

Las direcciones directas del escáner cambian al reiniciar el juego. Para cheats permanentes usa
**cadenas de punteros** (`modulo` + `direccion` + `offsets`) o **parches por patrón**.

## Escáner de punteros (cheats que sobreviven a reiniciar el juego)

1. En **Escáner**, encuentra el valor, selecciona el resultado y pulsa **Buscar punteros**
2. En **Punteros**, pulsa **Buscar punteros** (profundidad 4 y offset 1000 suelen bastar)
3. Cierra y vuelve a abrir el juego (MiTrainer se reconecta solo y **conserva** las cadenas)
4. Vuelve a encontrar el valor en el Escáner → **Buscar punteros** → en Punteros pulsa **Filtrar**
5. Repite 3–4 un par de veces hasta que queden pocas cadenas. Elige una (mejor si es del `.exe` del juego
   y con pocos offsets) → **Añadir como cheat** → **Guardar** en Cheats

Las cadenas se **guardan solas** tras cada búsqueda o filtro en `punteros/<juego>.json` (junto a MiTrainer.exe)
y se recuperan al conectar con ese juego, aunque hayas cerrado MiTrainer. Con **Guardar como…** / **Abrir…**
puedes guardar copias o compartirlas.

En JuegoPrueba pulsa `A` para ver la dirección real de la vida y comprobar que el escáner acertó.

Si no puede conectarse a un juego, ejecuta MiTrainer como administrador.
