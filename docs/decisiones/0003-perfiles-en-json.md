# 0003 — Perfiles en JSON legibles

**Estado:** aceptada

## Contexto

Los cheats de cada juego se tienen que guardar entre sesiones, poder compartirse y, a veces, editarse a mano
(por ejemplo, corregir el `.exe` de un juego o pegar una dirección de Cheat Engine).

## Decisión

Un archivo JSON por juego en `perfiles/`, con nombres de campo en español, comentarios `//` permitidos y
direcciones/offsets como **texto hexadecimal**. Los enums se guardan con nombres legibles
(`"tipo": "parche"`, `"tipoValor": "float"`) mediante `JsonStringEnumMemberName`.

Los cambios hechos desde la app se guardan solos si el perfil ya tiene archivo. La escritura es segura:
primero a un temporal y luego se reemplaza el archivo.

## Alternativas descartadas

- **Base de datos (SQLite)**: no se podría editar ni compartir un perfil con un editor de texto.
- **Direcciones como números en el JSON**: JSON no tiene hexadecimal y nadie reconoce `1715004` como `1A2B3C`.

## Consecuencias

- Los perfiles de versiones anteriores de la app siguen funcionando (hay una prueba que lo comprueba).
- Los resultados de punteros usan el mismo enfoque, pero cada cadena en una línea compacta
  (`juego.exe|1A2B|18|10`) porque puede haber cientos de miles.
