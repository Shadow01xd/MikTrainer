# Documentación de MiTrainer

| Documento | Para qué sirve |
|---|---|
| [Arquitectura](arquitectura.md) | Las capas, qué hace cada proyecto, cómo fluyen los datos y cómo ampliar la app |
| [Motor de memoria](motor-de-memoria.md) | Cómo funcionan por dentro el escáner, los punteros y los parches |
| [Formatos de archivo](formatos.md) | Perfiles de juegos y resultados de punteros (JSON) |
| [Guía de uso](guia-de-uso.md) | Cómo usar la app paso a paso |
| [Pruebas](pruebas.md) | Qué se prueba, cómo ejecutarlo y reglas de las pruebas |
| [Convenciones](convenciones.md) | Estilo de código, nombres, idioma y commits |
| [Decisiones](decisiones/) | Registro de decisiones de arquitectura (ADR) y su porqué |

## Decisiones registradas

1. [WinForms sobre .NET 10](decisiones/0001-winforms-y-net.md)
2. [Arquitectura en capas](decisiones/0002-arquitectura-en-capas.md)
3. [Perfiles en JSON legibles](decisiones/0003-perfiles-en-json.md)
4. [Pruebas que no tocan el escritorio](decisiones/0004-pruebas-sin-escritorio.md)
5. [Detección de juegos leyendo las tiendas](decisiones/0005-deteccion-de-juegos.md)
