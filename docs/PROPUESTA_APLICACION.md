# Propuesta — Aplicación CTEditor: mapas, NPC, eventos, recursos y editor de píxeles

> Estado: **EN CURSO** (fase 0-1). Decisiones del autor: el editor será una **aplicación propia** (no solo
> herramientas dentro de Unity); las generaciones quedan en pausa; eventos **por nodos y grafos** además de lista;
> **editar y jugar al instante**; **editor de píxeles** incorporado (tipo Aseprite) para retocar; **lectura de carpetas**
> y **corte de tilesets** con el tamaño de tile que se elija. La protección del código (DLL) no interesa por ahora. Más
> adelante se retomarán las herramientas de Unity para profesionales.

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

## 8. Fases

| Fase | Qué | Resultado visible |
|---|---|---|
| **0** | Carpeta de proyecto, lector PNG puro, catálogo de recursos | Tests (sin Unity) |
| **1** | Corte de tilesets y hojas de personaje (tamaño, desplazamiento, separación, vacíos, repetidos, sugerencia) | Tests |
| **2** | Esqueleto de la aplicación (UI Toolkit): abrir proyecto, panel de recursos, **asistente de corte** | Primera ventana de la aplicación |
| **3** | Modelo de mapa + **editor de mapas** (capas, pincel, paso, terreno) + **▶ Jugar / Probar aquí** | Pintar y pasear |
| **4** | **Editor de píxeles** (lápiz, relleno, colores, modo tile, retocar desde el mapa) | Retocar y ver al momento |
| **5** | Conexiones, teletransportes, zonas de encuentro, vista del mundo | Un mundo recorrible con combates |
| **6** | NPC, movimiento, entrenadores con visión | Mapas con vida |
| **7** | Eventos: modelo de grafo + intérprete + editor (lista y nodos) | Historias |
| **8** | Recetas, mapa de la historia, depurador, validador | Lo que nadie más tiene |
| **9** | Importadores (RPG Maker XP / Essentials, Tiled) | Atraer proyectos existentes |
| **10** | Pasar los editores antiguos a la aplicación, uno a uno | Todo en la aplicación |

## 9. Hecho hasta ahora

- **Fase 0-1 (dominio)**: `CTEditor.Art.Domain` (`PixelImage`, `Rgba32`, `SliceSettings`, `TileSlicer`,
  `TileSizeSuggester`, `CharacterSheetLayout`) y `CTEditor.Project` (`PngReader`, `AssetCatalog`), con tests.
