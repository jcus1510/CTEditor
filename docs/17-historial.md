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
- **Pulido 1 (primera prueba en Unity)**: rejilla alineada con los tiles, la interfaz ya no desaparece al seleccionarla
  en la Hierarchy, sin solapamientos de texto, controles a la misma altura, iconos en las herramientas, ayudas
  emergentes, inicio de tamaño fijo, importar BMP/DIB, tamaños habituales en el corte. Nace [`DISENO.md`](DISENO.md).
  `ImageFormatTests`.
- **Pulido 2**: proyección de la cámara del mapa fijada a mano (los tiles salían ~3 % más estrechos que la rejilla al
  hacer zoom); ventana Tiles compacta con iconos; Capas con iconos, asa para reordenar arrastrando y deslizador propio
  con %; barras de desplazamiento finas; importar con vista previa y tipo sugerido; cambiar el tipo después; menú
  Ver → Ventanas / Distribuciones (submenús); Ctrl + 0.
- **Pulido 3**: la causa real del descuadre del mapa (los renderizadores estaban en x = 3 100 000, donde un float solo
  tiene precisión de ¼ de casilla) — ahora en el origen; tilesets de Propiedades compactos; escala por ventana
  (Alt + rueda, Alt + 0) recordada; atajos ampliados por categorías (inspirados en Tiled / Pokémon Studio, GB Studio,
  Photoshop y Unity), grabables pulsándolos y con buscador; Espacio + arrastrar.
- **Pulido 4**: barra del mapa colocable (clic derecho: arriba / izquierda / derecha / abajo; por defecto a la
  izquierda); elegir una capa = pintar en ella (apaga las capas automáticas y lo avisa); quitar cualquier tileset no
  usado del mapa; etiquetas de terreno pequeñas; ayudas más claras de los modos de Tiles; etiquetas de las cajas de
  número junto a su caja.
- **Pulido 5**: rejilla dibujada como una malla ajustada al píxel y con sombra (ya no desaparece a trozos); ayudas
  emergentes con el elemento realmente bajo el ratón; botones de la barra del mapa más grandes y botón para colocarla;
  «Nuevo mapa» rediseñado (formulario en dos columnas, tamaños habituales, brújula para el lado y ficha resumen con el
  tileset). Lista de prueba en Unity en el capítulo 20.
- **Pulido 6 — Encuentros rediseñada**: zonas como lista compacta, la zona elegida con sus métodos en pestañas,
  especies en tarjetas con barra de %, horas con las cuatro encendidas = siempre, «Todas las horas», interruptor con
  icono, «Dejar de pintar» (y Esc), resaltar todas las zonas en el mapa o solo la elegida. Ctrl+Z en cada ventana (el
  editor activo es la ventana en la que haces clic).
- **Consola limpia**: el XmlException venía de «My project.slnx» con marcas de conflicto de un git stash antiguo
  (también en SampleScene.unity); arreglados además dos avisos (GridOverlay.Clear, PreventDefault).
- **Fase 5a**: historial de versiones (`ProjectBackups`: .zip cada 10 minutos solo si hubo cambios, máximo 5, a mano
  con Ctrl+Mayús+S, restaurar guardando antes cómo estaba); botón Guardar; iconos en las pestañas; paleta de órdenes
  Ctrl+P (`CommandSearch`); ventana Problemas (`ProblemFinder`). `BackupTests`.
- **Encuentros como tabla** (con referencias de Porymap, Pokémon Studio y Essentials): filas espaciadas con
  miniatura, niveles y peso en cajas pequeñas (rueda del ratón), % real con barra y pasos de media para encontrarlo,
  horas con iconos (amanecer, sol, atardecer, luna), menú «⋯» (condición explicada, forma, objeto equipado,
  variocolor, subir/bajar, duplicar, quitar) y combates dobles por método.
- **Encuentros, ronda 2**: columnas alineadas con anchos compartidos; % con barra corta y pasos debajo; horas de
  vista con iconos; color de zona con selector de degradado (`ColorPicker`); sin aviso de «dejar de pintar» (el icono
  de pintar alterna); variocolor global en Proyecto → Ajustes del juego; selector de especies con todas, en orden de
  Pokédex y con filtros (tipos exactos, grupo huevo, generación, formas, legendarios: `SpeciesFilter`); botón «i» de
  ayuda en las ventanas (`PanelHelp`).
- **Sugerencias de diseño 1-6 y 8-10**: la «i» parpadea tres veces la primera vez; tabla de especies más estrecha
  con asa para arrastrar; **simulador de encuentros** (`EncounterSimulator`) en su ventana; **plantillas de zona**
  (`EncounterTemplate`); aviso de casillas sin su terreno (`EncounterChecks`, apagado por defecto); títulos de
  sección iguales (`Ui.SectionTitle`); densidad cómoda/compacta; animaciones rápidas y desactivables
  (`Ui.Appear`); color de acento sin cambiar el tema. Nace [`HOJA_DE_RUTA.md`](HOJA_DE_RUTA.md). `EncounterToolsTests`.
- **Bloque A (fase 5b)**: jugar dentro de la ventana Juego con cambios en vivo; perfiles de prueba (equipo,
  medallas, dinero, objetos, interruptores, hora) con su editor y selector; selector de especies común; estados
  vacíos (`Ui.EmptyState`). `TestProfileTests`.
- **B2 edición rápida**: Mayús + clic = línea; voltear (X / Y) y girar (R, Mayús+R) el sello, con el giro guardado en
  cada casilla; sellos guardados en Ctrl+Alt+1-9 / Alt+1-9. Pruebas en `MapEditingTests`.
- **B1 corte libre**: piezas a mano con lupa y precisión de píxel, detección de objetos sueltos; se colocan como
  bloques bajo la rejilla sin cambiar los números de los tiles (`FreePieceSet`). `FreeSliceTests`.
- **B3 puertas enlazadas**: herramienta Puerta; interior nuevo con su salida en un paso; enlace por id; ir a la otra con
  doble clic; salto al jugar; avisos en Problemas; deshacer en los dos mapas a la vez. `DoorTests`.
- **B4 autotiles**: XP, VX/MV y 47 piezas detectados por las medidas y escalados; 47 piezas en la paleta; bordes que se
  recalculan al pintar, rellenar, pegar y borrar (`AutotileResolver`). `AutotileTests`.
- **B5 pinceles aleatorios** con pesos para lápiz, rectángulo y relleno, guardados en `datos/pinceles.json`. `BrushTests`.
- **B6 piezas reutilizables**: guardar una selección con sus capas y colocarla en otros mapas como copia
  (`datos/piezas.json`); pegar conserva el giro de los tiles. `PieceTests`. **Fase 6 completa.**
- **Bloque C, base (C0-C3)**: inventario de las 36 ventanas; ensamblado `CTEditor.Content` con esquemas, CSV,
  comprobaciones con fila y columna, índice de referencias y operaciones seguras con papelera y deshacer. `ContentTests`.
- **Documentación** completa por capítulos (esta carpeta).
