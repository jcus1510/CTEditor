# Documentación de CTEditor

Documentación completa del proyecto, capítulo por capítulo, para **entenderlo desde cero, retomarlo o migrarlo**
(a otro repositorio, otra máquina, otra versión de Unity u otra persona / sesión de IA). Está en español, como la
interfaz del editor. El manual de uso para el AUTOR (qué hace cada botón) está en
[`Assets/GameContent/README.md`](../Assets/GameContent/README.md); el de las herramientas Python en
[`Tools/README.md`](../Tools/README.md).

## Índice

| # | Capítulo | De qué trata |
|---|---|---|
| 01 | [Visión y premisas](01-vision-y-premisas.md) | Qué es CTEditor, la premisa «todo personalizable» y las decisiones que se derivan. |
| 02 | [Estructura del repositorio](02-estructura-del-repositorio.md) | Carpetas, qué hay en cada una y qué NO se versiona. |
| 03 | [Arquitectura (DDD)](03-arquitectura.md) | Contextos, asmdefs y dependencias, flujo de datos, patrones (ACL, catálogos, estrategias, eventos). |
| 04 | [Núcleo compartido](04-nucleo-compartido.md) | SharedKernel: Id, Chance, Percentage, Result, IRng, IClock, eventos. |
| 05 | [Definición del juego](05-definicion-del-juego.md) | El contenido puro: especies, movimientos, tipos, estados, habilidades, objetos, reglas, condiciones, climas... |
| 06 | [El combate](06-combate.md) | Battle, Combatant, TurnResolver y sus fases, eventos, fórmulas, IA de combate. |
| 07 | [Equipo y progresión](07-equipo-y-progresion.md) | Party: individuos, fábrica, XP, EVs/IVs, naturalezas, evoluciones, mochila, usar objetos. |
| 08 | [La partida (Aventura)](08-aventura.md) | GameData, BattleSession, TeamBuilder, TrainerBrain, FieldActions, guardado, torneo de IAs, RulesReview. |
| 09 | [Efectos por bloques](09-efectos-por-bloques.md) | El sistema «cuándo / si / qué» de los objetos (y, pronto, habilidades): modelo, motor, editor y Excel. |
| 10 | [Infraestructura Unity](10-infraestructura-unity.md) | ScriptableObjects, mappers (ACL), catálogos, carpetas de contenido, carga en el juego. |
| 11 | [El editor](11-editor.md) | Centro de Contenido, ventanas, inspector en español, papelera, validador, asistentes. |
| 12 | [Excel (CSV)](12-excel-csv.md) | Esquemas, importación en dos fases, formatos de celda, copias de seguridad. |
| 13 | [Packs y herramientas Python](13-packs-y-herramientas.md) | Packs Gen1…Gen7, generadores (PokeAPI, Smogon), verificador. |
| 14 | [Escena, interfaz y controles](14-escena-e-interfaz.md) | Bootstrap: composition root, pantalla de combate, laboratorio, menús, caja de texto, input. |
| 15 | [Pruebas y verificación](15-pruebas-y-verificacion.md) | Tests, cómo compilar y probar sin Unity, qué se revisa antes de cada commit. |
| 16 | [Convenciones](16-convenciones.md) | Idiomas, nombres, enums, etiquetas, .meta, commits, cómo añadir cosas nuevas. |
| 17 | [Historial](17-historial.md) | Qué se construyó y en qué orden (lotes, pasos 1-7, efectos por bloques). |
| 18 | [Pendientes y hoja de ruta](18-pendientes.md) | Lo que falta (paso 8 Gen 7, efectos «Al usarlo»...) y problemas conocidos. |
| 19 | [Cómo migrar el proyecto](19-migrar.md) | Lista paso a paso para llevar el proyecto a otro sitio sin perder nada. |
| 20 | [La aplicación CTEditor](20-aplicacion.md) | El editor como programa propio: ventana única, paneles acoplables, recursos, asistente de corte, entorno. |
| — | [Hoja de ruta](HOJA_DE_RUTA.md) | Lo que queda paso a paso (fase 5b, fase 6, editores de Unity a la aplicación, packs públicos) con la comprobación de cada tarea. |
| — | [Diseño visual](DISENO.md) | Documento vivo: cómo está hecha la interfaz (UI Toolkit), reglas visuales (colores, medidas, iconos) y lo que falta por pulir. |

**Plan actual**: [`PROPUESTA_APLICACION.md`](PROPUESTA_APLICACION.md) — la aplicación CTEditor (mapas, NPC, eventos por
nodos, recursos y corte de tilesets, editor de píxeles, jugar al instante).

Documentos anteriores: [`PROPUESTA_EDITOR_OBJETOS.md`](PROPUESTA_EDITOR_OBJETOS.md) (la propuesta que dio lugar al
capítulo 09; ya implementada) y [`CONTEXTO.md`](CONTEXTO.md) (resumen de una página).

## Lectura rápida (15 minutos)

1. [01](01-vision-y-premisas.md) → qué y por qué.
2. [03](03-arquitectura.md) → cómo está montado.
3. [15](15-pruebas-y-verificacion.md) → cómo comprobar que todo sigue bien.
4. [18](18-pendientes.md) → qué toca después.
