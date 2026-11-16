# 0004 — Pruebas que no tocan el escritorio

**Estado:** aceptada

## Contexto

Al principio, la interfaz se probaba con scripts que enviaban clics y teclas al escritorio. Un día había otro
programa a pantalla completa (un juego) y Windows no dejó pasar MiTrainer al frente: los clics y una pulsación
de F2 acabaron en el juego del usuario.

## Decisión

Ninguna prueba automática puede mostrar ventanas al frente, mover el ratón ni pulsar teclas.

- Los formularios se prueban creados pero ocultos, llamando a sus métodos internos (`Save`, `Test`, `Record`),
  expuestos con `InternalsVisibleTo`.
- Para dibujar una ventana se muestra fuera de la pantalla y sin activarla (`SW_SHOWNOACTIVATE`).
- La lógica se prueba en Core con dobles de prueba; la integración real usa el juego de prueba con su entrada
  redirigida, nunca el escritorio.

## Consecuencias

- Las pruebas se pueden ejecutar en cualquier momento, aunque el usuario esté jugando.
- Los clics reales en la ventana (abrir menús, arrastrar) no tienen pruebas automáticas: se comprueban a mano.
