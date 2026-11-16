# Guía de uso

> Solo para juegos **de un jugador**. En juegos online el anti-cheat lo detecta y te banean.
> Si MiTrainer no puede conectarse a un juego, ejecútalo como administrador.

## Mis juegos

La primera página lista los juegos instalados en **Steam**, **Epic** y **GOG**, con su icono y su `.exe`.

- **Jugar**: abre el juego desde su tienda y deja listo su perfil; MiTrainer se conecta solo cuando arranca.
- **Crear perfil** / **Ver cheats**: crea un perfil con el `.exe` ya puesto, o abre el que ya tiene.

En Steam el `.exe` se adivina. Si falla, cambia `"proceso"` en el JSON del perfil.

## Cheats

Cada cheat es una tarjeta. Actívalo con el interruptor, con un clic en la tarjeta o con su **atajo de teclado**,
que funciona aunque estés dentro del juego.

- **+ Nuevo cheat** abre el editor: nombre, tipo, atajo (haz clic y pulsa la combinación), módulo, dirección,
  offsets y valor; o patrón y bytes si es un parche.
- **Comprobar en el juego** dice a qué dirección llega y cuánto vale ahora, sin activar nada.
- **✎** edita; clic derecho para **Duplicar** o **Eliminar**.
- Los cambios se guardan solos en el perfil. Si es nuevo, pulsa **Guardar** una vez para darle nombre.

## Encontrar un valor (Escáner)

Practica con el juego de prueba:

```bash
dotnet run --project samples/JuegoPrueba
```

1. En MiTrainer elige `JuegoPrueba.exe` arriba a la derecha → **Conectar**.
2. **Escáner**: Valor `100` → **Primer escaneo**.
3. Pulsa `D` en el juego (la vida baja a 93) → Valor `93` → **Siguiente**. Repite hasta que queden 1 o 2 resultados.
   (En el juego, `A` te enseña la dirección real para comprobar que acertaste.)
4. Doble clic en el resultado → se añade como cheat «congelar» con un atajo libre.
5. **Cheats**: actívalo y recibe daño: la vida no baja.

## Cheats que sobreviven a reiniciar el juego (Punteros)

Las direcciones del escáner cambian al reiniciar el juego. Para que el cheat dure:

1. En **Escáner**, selecciona el resultado → **Buscar punteros**.
2. En **Punteros** pulsa **Buscar punteros** (profundidad 4 y offset 1000 suelen bastar).
3. Cierra y vuelve a abrir el juego. MiTrainer conserva las cadenas (también si lo cierras: se guardan solas).
4. Vuelve a encontrar el valor → **Buscar punteros** → en Punteros pulsa **Filtrar**.
5. Repite 3–4 hasta que queden pocas cadenas. Elige una (mejor del `.exe` del juego y con pocos offsets)
   → **Añadir como cheat**.

**Guardar como…** / **Abrir…** guardan copias de las cadenas o las cargan de otro archivo.

## Parches de código

Para cosas como "munición infinita" a veces es mejor anular la instrucción que resta. Con Cheat Engine,
clic derecho en la dirección → *Find out what writes to this address* te da la instrucción y sus bytes.
Crea un cheat de tipo **parche** con esos bytes como patrón (pon `??` en los que cambian entre versiones)
y `90 90 90` (NOP) como bytes nuevos. Se deshace al desactivarlo o al cerrar MiTrainer.
