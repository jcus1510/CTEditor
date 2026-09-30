# Propuesta — Aplicación CTEditor: mapas, NPC, eventos, recursos y editor de píxeles

> Estado: **EN CURSO** (fases 0 a 4 hechas; la aplicación se describe en [20](20-aplicacion.md)). Decisiones del autor: el editor será una **aplicación propia** (no solo
> herramientas dentro de Unity); las generaciones quedan en pausa; eventos **por nodos y grafos** además de lista;
> **editar y jugar al instante**; **editor de píxeles** incorporado (tipo Aseprite) para retocar; **lectura de carpetas**
> y **corte de tilesets** con el tamaño de tile que se elija. La protección del código (DLL) no interesa por ahora. Más
> adelante se retomarán las herramientas de Unity para profesionales.
>
> Segunda ronda de decisiones: **RPG Maker XP / Pokémon Essentials como base** (tile de 32 px, pantalla 512 × 384,
> propiedades de tile y etiquetas de terreno con sus mismos valores, para importar sin pérdidas); el tamaño del tile se
> **elige al crear el proyecto**; **tema oscuro** por defecto; **entorno de trabajo editable** (paneles, tema, atajos).

## 1. Qué hace mejor la competencia y cómo superarla

| Maker Studio / RPG Maker hacen bien | CTEditor lo hará así |
|---|---|
| Interfaz clásica: árbol de mapas a la izquierda, mapa al centro, paleta de tiles, capas | La misma distribución (lo que la gente ya conoce), con tema oscuro, zoom y paneles que se pueden mover |
| Capas ilimitadas (Maker Studio) | Capas ilimitadas con nombre, visibilidad, bloqueo y opacidad |
| Pasar al juego rápido | **Mismo programa = mismo motor**: ▶ arranca el juego sin compilar, en la casilla que elijas, y los cambios se ven **mientras juegas** |
| Eventos por comandos en lista | Lista **y** grafo de nodos del mismo evento; recetas; «mapa de la historia» |
| Tienda de módulos (Maker Studio) | Más adelante: packs de contenido (ya existen) y de recetas |
| — | Combate por bloques, IA por niveles, packs fieles Gen1-7 (ya hecho) |
| — | Editor de píxeles incorporado: retocar un tile desde el mapa y verlo al momento |
| — | Validador y «¿quién usa esto?» también para mapas y eventos |

## 2. Arquitectura

La aplicación es **un programa hecho con Unity** (compilado como juego de escritorio) que contiene el editor **y** el
motor del juego. Los datos viven en una **carpeta de proyecto** (JSON + CSV + PNG), que también podrá abrir Unity.

```
                 Núcleo puro (C# sin Unity, probado con dotnet test)
   SharedKernel · GameDefinition · Battle · Party · Adventure · Art · World · Project
        ▲                                ▲                                 ▲
  Aplicación CTEditor             Motor del juego                 Herramientas de Unity
  (Unity + UI Toolkit)            (el mismo, en ▶ o exportado)    (las ventanas actuales)
        └──────────────── carpeta de proyecto: JSON + CSV + PNG ───────────────┘
```

Ensamblados nuevos (todos sin Unity, `noEngineReferences`):

| asmdef | Carpeta | Contenido |
|---|---|---|
| `CTEditor.Art.Domain` | `Runtime/Art/Domain` | Imágenes de píxeles, corte de tilesets y hojas de personaje, herramientas de dibujo, paletas. |
| `CTEditor.Project` | `Runtime/Project` | Carpeta de proyecto: lectura/escritura, catálogo de recursos, lector PNG puro, vigilancia de cambios. |
| `CTEditor.World.Domain` | `Runtime/World/Domain` | Mapas, capas, tilesets, terreno, conexiones, NPC, eventos (grafo) y su intérprete. |
| `CTEditor.App` | `App/` | La aplicación: UI Toolkit en tiempo de ejecución, ventanas, modo edición ↔ juego. |

**Por qué así**: todo lo que decide (cortar, pintar, qué hace un evento) se prueba sin abrir Unity, igual que el
combate. Unity solo dibuja y lee el ratón.

## 3. Carpeta de proyecto

```
MiJuego/
  proyecto.json                 nombre, versión, tamaño de tile, resolución, mapa inicial
  datos/                        los CSV de siempre (especies.csv, movimientos.csv, objetos.csv...)
  mapas/<id>.mapa.json          capas, tiles, terreno, zonas, conexiones, NPC y eventos del mapa
  eventos/<id>.evento.json      eventos comunes (reutilizables, recetas)
  graficos/
    tilesets/<nombre>.png       + <nombre>.corte.json (cómo se corta y qué es cada tile)
    personajes/<nombre>.png     + <nombre>.corte.json (filas = direcciones, columnas = pasos)
    combate/  iconos/  interfaz/  retratos/
  audio/musica/  audio/efectos/
```

- **Todo son archivos normales**: se pueden editar con Excel, Aseprite o cualquier programa. La aplicación **vigila la
  carpeta**: si guardas un PNG desde fuera, se recarga al momento (también durante el juego).
- Los datos actuales (fichas de Unity) siguen funcionando; el paso a la carpeta se hace por los CSV que ya existen.
  Pendiente técnico: los esquemas CSV actuales escriben en fichas de Unity; hace falta un lector que construya el
  dominio **directamente** desde el CSV (para la aplicación).

## 3b. Entorno de trabajo (de cada usuario, no del proyecto)

Se guarda en `entorno.json`, en la carpeta del usuario (`WorkspaceSettings`):
- **Tema**: Oscuro (por defecto), Claro o Alto contraste; **cualquier color** se puede cambiar (fondo, paneles, texto,
  acento, selección, rejilla, avisos...).
- **Paneles** (`DockLayout`): se arrastran a otro grupo (pestaña) o a un lado (divide el espacio), se cierran, se abren
  y se redimensionan con los separadores. Distribuciones de fábrica: **Clásico (RPG Maker)**, Mapa grande, Arte,
  Historia; el usuario puede **guardar las suyas con nombre**.
- **Escala de la interfaz** (75 %–200 %), **tamaño de letra**, **atajos de teclado** cambiables (avisa y resuelve
  choques) y **proyectos recientes**.
- Si el archivo se estropea, la aplicación arranca con el de fábrica.

## 3c. Base RPG Maker XP / Essentials

- `proyecto.json`: nombre, **tamaño de tile (32 por defecto; 16, 48, 24 o cualquiera al crear)**, pantalla 512 × 384,
  mapa inicial.
- Propiedades de tile (`TileProperties` en `imagen.corte.json`): **bloqueo por dirección con los bits de RPG Maker XP**
  (abajo 1, izquierda 2, derecha 4, arriba 8), **prioridad 0-5**, **etiqueta de terreno con los números de Essentials**
  (1 saliente, 2 hierba, 7 agua, 10 hierba alta, 12 hielo...), **arbusto** y **mostrador**.
- El importador (fase 9) leerá `Data/*.rxdata` (formato Marshal de Ruby: `MapInfos`, `Map###`, `Tilesets`), los PBS de
  Essentials y las carpetas `Graphics/Tilesets`, `Autotiles`, `Characters`, `Battlers`... hacia la carpeta del proyecto.

## 3d. Ventana y exportación (decisiones de la tercera ronda)

- **Una sola ventana a pantalla completa con la resolución del monitor**; las secciones se redimensionan arrastrando
  los separadores (como el Inspector y el Hierarchy de Unity); los diálogos y paneles flotantes viven dentro.
- **Juego final**: sigue siendo un juego hecho con Unity, pero el usuario **no necesita Unity**:
  1. **Motor listo + datos** (vía principal): la aplicación lleva el motor del juego ya compilado y al exportar lo
     copia con los datos del proyecto empaquetados (imágenes agrupadas, **un archivo con cifrado opcional**), el
     nombre y el icono. **Windows primero**; luego Web, Mac/Linux y Android.
  2. **Exportar a proyecto Unity** (después, para profesionales): genera un proyecto de Unity con el motor y los datos
     para añadir lo que quieran y compilar para cualquier plataforma, consolas incluidas.
- El juego se dibuja a su resolución (512 × 384 por defecto) y se amplía en múltiplos exactos; partidas en la carpeta
  del usuario; la pantalla «Made with Unity» depende de la licencia con la que se compile el motor.

## 4. Editar y jugar al instante

- **▶ Jugar** (F5): arranca el juego desde el mapa inicial. **▶ Probar aquí** (Ctrl+F5 o clic derecho): en la casilla
  elegida, con un equipo e interruptores de prueba.
- Sin compilar ni guardar: el juego lee el proyecto **en memoria**.
- **Cambios en caliente** mientras se juega:
  - tiles, capas y terreno: al instante;
  - un evento: la próxima vez que empiece;
  - datos (especies, movimientos, objetos...): en el próximo combate;
  - un PNG retocado (en la aplicación o fuera): al instante.
- **Pausa y depurador** (F9): interruptores y variables editables, qué evento se está ejecutando (resaltado en su
  grafo), teletransportarse, curar, dar objetos.
- **Volver** (Esc largo o botón): regresa al editor exactamente donde estaba.

## 5. Recursos: lectura de carpetas y corte

**Lectura de carpetas.** La aplicación recorre `graficos/` y clasifica cada imagen por su carpeta (tileset,
personaje, combate, icono...). Arrastrar un PNG o una carpeta a la ventana lo copia a su sitio. Cada imagen muestra su
tamaño y si ya está cortada.

**Asistente de corte** (tilesets y hojas de sprites):
- **Tamaño del tile** (ancho × alto, por defecto el del proyecto; 8, 16, 24, 32, 48, 64 o cualquiera).
- **Desplazamiento** (margen del borde) y **separación** entre tiles.
- Vista previa con la rejilla encima; número de columnas y filas; avisos si sobran píxeles.
- **Sugerencia automática** del tamaño (tamaños que encajan exactos; formatos conocidos: tileset de RPG Maker XP de 8
  columnas de 32 px, hojas de personaje 4×4 de XP y 3×4 de VX/MV).
- **Tiles vacíos** (transparentes) marcados y omitidos; **tiles repetidos** detectados (se pintan como uno).
- **Hojas de personaje**: el corte asigna solo las direcciones (abajo, izquierda, derecha, arriba) y los pasos.
- El corte se guarda en `<nombre>.corte.json`: si cambia la imagen, se vuelve a cortar igual.

**Propiedades de cada tile** (se pintan sobre el tileset, como en RPG Maker pero en capas visibles):
paso (libre / bloqueado / por dirección), terreno (hierba alta, agua, saliente, hielo, arena, cueva... definidos como
fichas editables), capa por defecto (suelo / encima del jugador), animación (fotogramas) y autotile.

## 6. Editor de píxeles («Retoque»)

Pequeño, pensado para **corregir**, no para sustituir a Aseprite:
- Lienzo con zoom y rejilla de píxeles y de tiles; deshacer/rehacer.
- Lápiz, goma, cubo de relleno, cuentagotas, línea, rectángulo, selección y mover, **reemplazar un color en toda la
  imagen**.
- Paleta sacada de la imagen (y paletas guardadas).
- **Modo tile**: al dibujar un tile se ve repetido alrededor para comprobar que encaja sin costuras.
- **Fotogramas** con papel cebolla para tiles animados y pasos de personajes (fase posterior).
- **Desde el mapa**: clic derecho en un tile → «Retocar» → se abre en el editor → al guardar, el mapa y el juego se
  actualizan al momento.
- Guarda siempre en PNG (sin formato propio).

## 7. Mapas, NPC y eventos

**Mapas**: capas ilimitadas; pincel, relleno, rectángulo, cuentagotas, sellos (grupos de tiles), autotiles; capa de
paso y de terreno; **zonas de encuentro pintadas** (enlazadas a las zonas salvajes); conexiones por los bordes y
teletransportes con flecha; propiedades (música, clima, día/noche, bici, vuelo); **vista del mundo** con todos los
mapas conectados.

**NPC**: gráfico (hoja cortada), movimiento (quieto, al azar, ruta dibujada, seguir), **entrenador** enlazado a su
ficha con **línea de visión** dibujada, plantillas (enfermera, tendero, profesor, rival...).

**Eventos**:
- **Páginas con condiciones** (la página activa cambia según interruptores, variables, medallas, objetos, hora).
- Disparadores: al hablar, al pisar, al tocar, al entrar al mapa, automático, en paralelo, al ver al jugador.
- **Dos vistas del mismo evento**: lista de tarjetas (eventos sencillos) y **grafo de nodos** (escenas):
  - nodo diálogo con opciones → una salida por opción; combate → «gana» / «pierde»; condición → «sí» / «no»;
  - mover personajes, cámara, esperar, en paralelo, teletransportar, dar/quitar, curar, tienda, música, pantalla,
    interruptores y variables;
  - **subgrafos con parámetros** = recetas (líder de gimnasio, tienda, puerta, objeto oculto...) que se abren y cambian.
- **Mapa de la historia**: grafo automático de qué evento activa cada interruptor y cuál lo necesita; marca
  interruptores que nunca se activan.
- Validador y «¿quién usa esto?» para mapas, NPC, interruptores y eventos.
- Diálogos exportables a Excel (traducción).

## 8. Fases (replanteadas tras la fase 4)

**Decisiones de la cuarta ronda:** capas **automáticas + manuales**; **mundo continuo** para exteriores + interiores con
puertas; eventos con **las cuatro formas** (recetas arrastrables, bloques en lista, grafo de nodos y guion de
diálogos, todas sobre el mismo modelo); orden **Mundo → Juego jugable → Eventos**.

**Principio:** el usuario dice QUÉ quiere que pase; el editor se encarga de CÓMO. Se copia de RPG Maker lo que la gente
ya conoce y se mejora donde algo cuesta.

| Fase | Qué | Mejora sobre RPG Maker / Maker Studio |
|---|---|---|
| 0-4 ✅ | Proyecto, corte, aplicación, mapas, jugar, retoque | — |
| **5 · Comodidad base** | Buscador de órdenes (Ctrl+P), panel **Problemas** siempre al día (clic = ir al sitio), **historial de versiones** local («volver a como estaba ayer»), **jugar en un panel** con cambios en vivo, **perfiles de prueba** (equipo, medallas, objetos) | Nadie lo tiene junto; es la red de seguridad de quien no programa |
| **6 · El mundo** | **Mundo continuo** (exteriores en un lienzo, sin cargas) + interiores; **puertas que se enlazan solas**; zonas de encuentro pintadas; **capas automáticas** (según la prioridad y el tipo de tile) con capas manuales opcionales; **pinceles de terreno** (orillas solas), **pincel aleatorio** con pesos; **piezas reutilizables** (casa, árbol grande) | Construir un mapa es colocar cosas, no pelear con capas y bordes |
| **7 · Juego jugable** | Combates dentro de la aplicación (motor existente), menús (equipo, mochila, Pokédex, guardar), Centro, tienda, pantalla de título, partida guardada | Una partida de principio a fin sin salir del editor |
| **8 · Personajes y eventos** | NPC con rutas dibujadas, entrenadores con visión; eventos con **un solo modelo** y cuatro vistas: **recetas arrastrables** (puerta, cartel, objeto oculto, entrenador, enfermera, tienda, líder), **bloques «cuándo / si / qué»** (los de objetos y habilidades), **grafo de nodos**, **guion de diálogos** (`Profesor: ¡Hola!`) que se convierte en nodos y al revés; interruptores con nombre creados solos; **mapa de la historia** | El mismo sistema en todo el editor; escribir historia como un guion |
| **9 · Cinemáticas y depurador** | Línea de tiempo (cámara, movimientos, esperas), pausar en un nodo mientras se juega, paso a paso, ver y cambiar interruptores | Depurar sin mensajes de prueba |
| **10 · Importador Essentials** | Mapas, tilesets, autotiles, eventos básicos y PBS | Traer los proyectos que ya existen |
| **11 · Exportar Windows** | Motor listo + datos empaquetados con cifrado opcional, nombre e icono | Publicar sin Unity |
| **12 · Base de datos en la aplicación** | Especies, movimientos, objetos, habilidades, entrenadores… | Todo en un solo programa |
| Después | Idiomas del juego, Web y Android, exportar a proyecto Unity, proyecto de ejemplo y guía de primeros pasos, tienda de módulos | — |

**Cada fase, por capas (DDD):** dominio puro con tests → caso de uso en `Editing` → repositorio en `Project` → panel
registrado en la aplicación ([03](03-arquitectura.md)).

## 9. Hecho hasta ahora

- **Fase 0-1 (dominio)**: `CTEditor.Art.Domain` (`PixelImage`, `Rgba32`, `SliceSettings`, `TileSlicer`,
  `TileSizeSuggester`, `CharacterSheetLayout`) y `CTEditor.Project` (`Png`, `ProjectLayout`, `AssetCatalog`), con
  `TileSlicingTests`.
- **Base del entorno**: `Json` propio (sin dependencias, conserva el orden, errores con línea y columna),
  `ProjectFile`/`ProjectSettings`, `SliceFile`/`TileProperties` (base RPG Maker XP / Essentials) y
  `CTEditor.Workspace` (`Theme`, `DockLayout`, `ShortcutMap`, `WorkspaceSettings`), con `WorkspaceTests`.
- **Fase 2 (aplicación)**: `Assets/CTEditor/App` — ventana única, pantalla de inicio, menús, paneles acoplables con
  separadores y pestañas arrastrables, panel Recursos, asistente de corte visual (con vista previa andando para
  personajes), explorador de carpetas y personalización del entorno. Ver [20](20-aplicacion.md).
- **Revisión DDD** ([03](03-arquitectura.md)): `World.Domain` (mundo), `Editing` (casos de uso), repositorios en el
  dominio implementados en `Project`, deshacer común en `SharedKernel`, registros extensibles.
- **Fase 3**: árbol de mapas, editor de mapas con herramientas y deshacer, propiedades de tile pintadas sobre el tileset,
  capas, propiedades del mapa, inicio del jugador, autoguardado y **▶ Jugar / Probar aquí** con el jugador andando.
- **Fase 4**: editor de píxeles Retoque con modo tile y retoque desde el mapa, Tiles y Recursos.
- **Siguiente**: fase 5 (comodidad base), según el plan replanteado de la sección 8.
