# 17 · Historial

Resumen de lo construido, en orden (ver `git log` para el detalle).

## Base (PR #1-#16 en GitHub)

SharedKernel → definiciones (stats, tipos, movimientos, catálogo, reglas, especies) → fichas de Unity + mappers →
Party (instancias, fábrica, niveles) → Battle (fórmula de daño, acciones, eventos, turno) → crecimiento clásico →
tests EditMode → Eventing y contratos → bootstrap y escena de prueba → IA básica → primeras ventanas de editor
(movimientos, estados, habilidades).

## Lotes (antes de la serie de pasos)

- **Lote 1-4**: XP, curvas (6 clásicas + personalizadas), EVs, IVs, naturalezas, fórmula clásica verificada.
- **Lote A**: condiciones genéricas, potencia con modificadores y fórmula, estadísticas del daño, críticos
  configurables, curar a otros, clima, amistad, dado compartido.
- **Lote B**: estados volátiles (atrapar, drenadoras, protección, aguante) apilables con el principal.
- **Cierre del combate**: efectos de lado, Sustituto, Niebla, Foco Energía, Anulación, Otra Vez, Contraataque, Manto
  Espejo, Venganza, Saña, Furia, Metrónomo, Espejo, Mimético, Transformación, Conversión, trampas y cambios forzados.
- **Lote D**: Pokédex, movimientos con requisitos, planificador de movimientos, tablas de tipos por época, zoom/filtros.
- **Lote E**: mecánicas de 3.ª-4.ª gen., niveles de IA, curación inteligente, «Preparar un reto», MT/tutor/huevo, grupos huevo.
- **Lote F**: IAs Maestro e Injusto, memoria y predicción, género, 5.ª-6.ª gen. (objetos de competición, campos, Zona
  Mágica, Escudo Real, Alas Vendaval, Piel Feérica, Rivalidad, Baba...).
- Interfaz: menús con gráficos propios, mapa de menús, controles y mando, caja de texto.
- Excel robusto (un fallo no bloquea), ids de respaldo y reenlazado, papelera.
- Packs Gen1…Gen6 fieles; «✨ Sugerir según la IA»; plantillas de entrenadores; objetos de cada generación; torneo de IAs.

## Plan de 8 pasos

| Paso | Qué | Estado |
|---|---|---|
| 1 | Reglas de generación y fichas de mecánica en el Ruleset | ✅ |
| 2 | Formas de combate, variantes enlazadas y Árbol de familia | ✅ |
| 3 | Megaevolución (motor, jugador, IA por nivel, 48 megas de la 6.ª) | ✅ |
| 4 | EVs, IVs y habilidad elegida por miembro de equipo | ✅ |
| 5 | Importar/exportar en formato Showdown + nombres en inglés | ✅ |
| 6 | Sets de competición de Smogon (IA por nivel, fijo o cambiante, selector) | ✅ |
| 7 | Asistente «Cambiar de generación» (papelera por grupos, copia, Adaptar a las reglas) | ✅ |
| 8 | 7.ª generación (variantes de Alola, movimientos Z, pack Gen7) | ✅ |

## Después del paso 7

- **Arreglo crítico**: `Handles.DrawDashedLine` (no existe en Unity) impedía compilar el editor desde el paso 2; Unity
  seguía con el código viejo. Cambiado a `DrawDottedLine` y creada `Tools/compilar_unity` (compila y prueba TODO sin
  Unity). La detección de CSV por cabeceras ya no adivina si la mayoría de columnas no encajan.
- **Objetos por EFECTOS** (bloques «cuándo / si / qué»): dominio, motor, datos, editor con tarjetas y desplegables,
  plantillas por piezas, Excel `efectos`, regla «las MT se gastan». Ver [09](09-efectos-por-bloques.md).
- **Sistema antiguo de objetos eliminado**: sin campos sueltos, sin `ItemExtras`, sin botón de conversión. Las
  plantillas de objetos pasan a ser datos (`Plantillas/objetos.csv`, 135 objetos, +16 bayas con efecto) y los packs
  traen todos sus objetos con todos sus efectos; el verificador revisa los efectos.
- **Editor más rápido**: validación en caché, búsquedas por id indexadas, referencias en una sola pasada, listas
  virtuales, sin repintar al mover el ratón, papelera y recuentos en caché ([11](11-editor.md)).
- **Sin carpeta de Plantillas**: las plantillas del editor de objetos son los objetos del pack/Excel elegido; la fuente de
  efectos para generar packs pasa a `Tools/datos_fuente/objetos.csv`.
- **Árbol de familia rápido**: grafo (padres, bases, variantes, cadenas) en caché por cambio de contenido y lista virtual.
- **Habilidades por bloques**: `AbilityData` = nombre + efectos; momentos, acciones y comportamientos especiales nuevos;
  las 190 habilidades convertidas y comparadas propiedad a propiedad con las antiguas (idénticas); bloques libres
  ejecutados como los de un objeto; editor con tarjetas y plantillas del pack; `efectos` en Excel y en los packs;
  «¿quién usa esto?» mira dentro de los efectos.
- **Paso 8 — 7.ª generación**: pack Gen7 (807 especies, variantes de Alola, 709 movimientos, 231 habilidades, 195
  entrenadores, 1301 sets); `completar_fuente.py` para completar `datos_fuente` de una generación nueva; momentos y
  acciones nuevos para las habilidades de la 7.ª (campos al entrar `poner_lado`, Búnker, Campo Psíquico bloquea la
  prioridad); **movimientos Z** (mecánica con usos, Pulsera Z, tabla de potencias y 25 % a través de Protección;
  35 cristales Z como objetos con bloques `movimiento_z`; efectos Z de los movimientos de estado; botón Z; IA;
  `usa_z` en entrenadores); condiciones `mov.id` y `especie`; reglas de la 7.ª activan los movimientos Z.
- **Aplicación CTEditor — fase 0-1**: propuesta ([PROPUESTA_APLICACION](PROPUESTA_APLICACION.md)); ensamblados
  `CTEditor.Art.Domain` (imagen de píxeles, corte con tamaño/desplazamiento/separación, tiles vacíos y repetidos,
  sugerencia de tamaño, hojas de personaje XP y VX/MV) y `CTEditor.Project` (PNG sin Unity, lectura y escritura;
  catálogo de «graficos/»), con `TileSlicingTests`.
- **Aplicación — base del entorno**: decisiones (base RPG Maker XP / Essentials, tile de 32 px elegible al crear,
  tema oscuro, entorno editable); JSON propio, `proyecto.json`, archivo de corte con propiedades de tile de RPG Maker XP
  y etiquetas de Essentials; `CTEditor.Workspace` (temas, paneles acoplables, distribuciones guardadas, atajos).
- **Aplicación — fase 2** ([20](20-aplicacion.md)): decisiones de exportación (motor listo + datos, Windows primero,
  datos empaquetados con cifrado opcional; después proyecto Unity). `Assets/CTEditor/App` con UI Toolkit: ventana
  única a pantalla completa, inicio, menús, paneles acoplables, Recursos, asistente de corte, explorador,
  personalización; `Tools/compilar_app` para compilarla sin Unity.
- **Revisión DDD + fases 3 y 4**: `World.Domain`, `Editing` (casos de uso), repositorios, deshacer común y registros
  extensibles ([03](03-arquitectura.md)); editor de mapas (árbol, herramientas, capas, propiedades de tile, inicio,
  autoguardado), ▶ Jugar / Probar aquí con el jugador andando, editor de píxeles Retoque. Tests `MapEditingTests`,
  `PixelEditingTests`.
- **Fase 4.5 — consolidación**: tramos en el mundo continuo (panel Mundo: mover, ver/bloquear/solo, nuevo tramo al
  lado, mapa de la región), encuentros como en los juegos (panel Encuentros), varios tilesets por mapa, capas
  automáticas, selección/copiar/pegar, vista previa del sello, ids fijos de tilesets (con conversión de mapas
  antiguos), importar y cortar en un paso, datos de un pack. `WorldTests`.
- **Documentación** completa por capítulos (esta carpeta).
