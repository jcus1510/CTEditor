# 20 · La aplicación CTEditor

`Assets/CTEditor/App` — el editor como **programa propio** (hecho con Unity, UI Toolkit en tiempo de ejecución), no
como ventanas del editor de Unity. Plan completo y decisiones: [PROPUESTA_APLICACION](PROPUESTA_APLICACION.md).

## Cómo abrirla

- En Unity: **CTEditor → Aplicación → Abrir la aplicación (Play)**. La primera vez crea la escena
  `Assets/CTEditor/App/Scenes/Aplicacion.unity` (un objeto con `AppRoot`; lo demás se crea por código) y la pone la
  primera en Build Settings.
- Compilada para Windows (Build Settings → la escena de la aplicación): arranca **a pantalla completa con la resolución
  del monitor**; F11 cambia a ventana.
- El entorno de trabajo de cada usuario se guarda en `Application.persistentDataPath/entorno.json` (menú CTEditor →
  Aplicación → Abrir la carpeta del entorno de trabajo).

## Qué hay (fases 2, 3 y 4)

| Pieza | Archivo | Qué hace |
|---|---|---|
| Entrada | `AppRoot.cs` | Pantalla completa a resolución nativa, panel de UI Toolkit con la escala del usuario × la de Windows (ppp), cámara y EventSystem (Input System) si faltan, F11/F5/Ctrl+S aunque nada tenga el foco. |
| Ventana | `AppShell.cs` | UNA ventana con capas: pantalla (inicio o mesa de trabajo), diálogos, menús, arrastre y avisos emergentes. Proyecto abierto, entorno, registro de avisos, atajos (`ShortcutMap`), Ctrl + rueda = escala, vigilancia de «graficos/». |
| Inicio | `StartScreen.cs` | Nuevo proyecto (nombre, carpeta, **tamaño de tile: 32 px por defecto**, 16, 48, 24 u otro; pantalla 512 × 384) y abrir (buscar o recientes). Avisa si la carpeta es un proyecto de RPG Maker XP / Essentials (importación: fase 9). |
| Menús | `MenuBar.cs` | Proyecto, Ver (paneles, distribuciones), Entorno (temas, escala, pantalla completa, personalizar), Jugar, Ayuda. Con un menú abierto, pasar por otro título lo abre. |
| Paneles | `DockView.cs` | Dibuja `DockLayout`: **separadores que se arrastran** (doble clic = mitad y mitad), **pestañas que se arrastran** a otro grupo o a un lado (marca azul de dónde caerá), × y clic central cierran, «+» abre un panel cerrado. El contenido de cada panel se conserva al reordenar. |
| Recursos | `Panels/AssetsPanel.cs` | Imágenes de «graficos/» por tipo, miniatura, tamaño, cortada o no; buscar, filtrar por tipo, importar un PNG (elige el tipo y abre el corte), recargar, abrir la carpeta. Se actualiza sola si cambias un archivo desde fuera. |
| Corte | `SliceWizard.cs` | Imagen con rejilla, zoom (Alt + rueda), tamaño (cuadrado o no), desplazamiento, separación, sugerencias, vacíos (azul) y repetidos (naranja), información del tile bajo el ratón, resumen y avisos. Personajes: plantilla XP 4×4 o VX/MV 3×4, nombre de cada fila y **vista previa andando** en las 4 direcciones. Guarda `imagen.corte.json` conservando las propiedades de tile. |
| Explorador | `FolderBrowser.cs` | Elegir carpeta o archivo dentro de la aplicación (no hay diálogo nativo): accesos, unidades, subir, ruta a mano, nueva carpeta; marca los proyectos de CTEditor. |
| Entorno | `SettingsDialog.cs` | Tema y cada color, escala y letra, pantalla completa, distribuciones (de fábrica y propias) y atajos. |
| Estilo | `Ui.cs`, `Textures.cs` | Controles con el tema aplicado en código (sin hojas de estilo aparte); texturas nítidas, tiras para imágenes muy altas, miniaturas reducidas en memoria. |
| Registro de paneles | `Panels/PanelRegistry.cs` | `PanelRegistry.Register(id, fábrica)`: cada panel se registra; un módulo nuevo añade el suyo sin tocar nada. Eventos (fase 7) y Base de datos (fase 10) aún explican cuándo llegan. |
| Mapas | `Panels/MapTreePanel.cs` | Árbol de mapas como RPG Maker: nuevo mapa (nombre, 20 × 15 por defecto, tileset, dónde), abrir, cambiar el nombre (doble clic), mover dentro de otro o a la raíz, borrar; marca el mapa de inicio y los que no se han guardado (*). |
| Mapa | `Panels/MapPanel.cs` | El mapa con el **mismo renderizador que el juego** (Tilemap de Unity en una textura). Herramientas: lápiz (con sello de varios tiles), rectángulo, relleno, goma, cuentagotas, inicio del jugador. Clic derecho = coger tiles del mapa; botón central o Alt = mover; rueda = zoom al ratón; Ctrl + clic = retocar ese tile. Rejilla, capa activa, atenuar las demás, «Probar aquí». |
| Tiles | `Panels/TilesPanel.cs` | Paleta del tileset (clic o arrastrar = sello) y **propiedades pintadas encima** como en RPG Maker XP: paso (centro = todo, borde = un lado), prioridad 0-5, terreno (hierba, agua…), arbusto, mostrador. Arrastrar aplica a varios; se deshace con Ctrl+Z. |
| Capas | `Panels/MapTreePanel.cs` (`LayersPanel`) | Capas de arriba abajo: ver, bloquear, opacidad, añadir, quitar, subir, bajar, cambiar el nombre. |
| Propiedades | `Panels/InspectorPanel.cs` | Nombre, tamaño con ancla (por dónde crece), tileset, música, bici, exterior; inicio del jugador y su hoja de personaje. |
| Retoque | `Panels/RetouchPanel.cs` | Editor de píxeles: lápiz, goma, relleno, línea, rectángulo (borde o relleno), cuentagotas, reemplazar color; grosor; colores principal/secundario, código, paleta de la imagen o del tile, recientes; rejilla de píxeles y de tiles; **modo tile** con vista 3 × 3; guardar (el mapa y el juego se actualizan solos). |
| Juego | `Play/PlayScreen.cs` | **▶ Jugar (F5) / Probar aquí (Ctrl+F5)**: el jugador anda con las reglas de paso de RPG Maker XP, animado con su hoja de personaje; cámara que le sigue; resolución del proyecto ampliada en múltiplos exactos; recarga los gráficos si cambian; F9 depurador (casilla, terreno, lados libres, atravesar paredes); Esc vuelve al editor tal como estaba. |
| Dibujo | `Rendering/MapRenderer.cs`, `Rendering/TilesetAtlas.cs` | Cada capa = dos tilemaps (debajo / encima del jugador según la prioridad del tile); cámara propia en su capa de Unity; tiras para tilesets muy altos. |

## Fase 4.5 — consolidación (el mundo, los encuentros, comodidad)

| Pieza | Dónde | Qué hace |
|---|---|---|
| **Tramos** | `World.Domain/Maps.cs`, `WorldLayout.cs` | Cada pueblo, ruta o cueva es su propio mapa (fácil de delimitar). Exterior = colocado en el **mundo continuo** (se pasa de uno a otro andando, sin cargas); interior = aparte (puertas). Categoría (pueblo, ruta, cueva...), clima, sale en el mapa de la región. |
| **Mundo** | `Panels/WorldPanel.cs` | Todos los tramos en un lienzo, dibujados como en el juego. **Arrastrar = mover** (casilla a casilla; en rojo si se pisa con otro). Lista con **Ver / Bloq. / Solo** (como las capas) para centrarse en un tramo. «Nuevo tramo…» lo pega al elegido (arriba, abajo, izquierda, derecha). Doble clic = editarlo. |
| **Mapa de la región** | `RegionMapBuilder`, «Mapa de la región…» | Imagen generada del mundo, esquemática (colores por tipo) o reducida (colores reales), con escala elegible. Se guarda en `graficos/interfaz/mapa_region.png` (retocable) + `datos/mapa_region.json` (dónde queda cada tramo). |
| **Encuentros** | `World.Domain/Encounters.cs`, `Panels/EncountersPanel.cs` | Como en los juegos: zona de **todo el tramo** o **zonas pintadas** en el mapa (mandan sobre la general); una tabla por **método** (hierba, hierba alta, cueva, surf, buceo, caña vieja/buena/súper, golpe cabeza, golpe roca... y **métodos propios** con su id: «volando»...); **probabilidad por paso**; especies con niveles, **peso y % real**; **horas del día** e **interruptor**; botón «Pesos clásicos» (20/20/10/10/10/10/5/5/4/4/1/1, agua 60/30/5/4/1, cañas 70/30). Las especies salen de `datos/especies.csv`. En el juego salen al andar (aviso, los combates llegan en la fase 7). |
| **Varios tilesets por mapa** | `MapTile`, panel Tiles | Pestañas con los tilesets del mapa y «+ Tileset». Cada casilla recuerda el suyo. |
| **Capas automáticas** | `MapTools.*Auto`, panel Mapa | Cada tile va solo a su capa (suelo, detalles, encima) según su pieza: se deduce de la transparencia y la prioridad, o se corrige en Tiles → «Pieza». La goma borra lo de más arriba. Se pueden desactivar (capas a mano, como RPG Maker). Clic derecho en Capas: para qué es cada capa. |
| **Selección** | herramienta Selección | Ctrl+C / Ctrl+X / Ctrl+V / Supr, con todas las capas; pegar no borra con casillas vacías. |
| **Vista previa del sello** | panel Mapa | Los tiles se ven semitransparentes bajo el ratón antes de pintar. Los tramos vecinos se ven atenuados alrededor (doble clic = abrir). |
| **Ids fijos** | `.corte.json` («id») | Renombrar o mover una imagen no rompe los mapas. Los mapas antiguos (con la ruta) se convierten solos. |
| **Importar y cortar** | Recursos | Importar una imagen o una **carpeta entera**: las que encajan (8 columnas de 32 px, hojas 4 × 4 o 3 × 4) se cortan solas. El asistente de corte ya no pierde el foco al escribir. |
| **Datos de un pack** | Inicio y Proyecto → «Copiar datos de un pack…» | Copia a `datos/` las hojas de Gen1…Gen7 (especies, movimientos, objetos...). |

## Pulido 1 — tras la primera prueba en Unity

Detalle y reglas visuales en [`DISENO.md`](DISENO.md) (documento vivo del diseño).

| Arreglo / novedad | Qué cambia |
|---|---|
| **Rejilla y tiles alineados** | La cámara del mapa no tomaba la proporción de su textura: con zoom o al moverse los tiles se salían de la rejilla. Ahora se fija la proporción y el centro se ajusta al píxel (el mapa, la rejilla y el cursor usan el mismo centro). Vale para Mapa, Mundo y el juego. |
| **La interfaz ya no desaparece** | Al seleccionar «Interfaz» en la Hierarchy, Unity reconstruía el `UIDocument` y borraba todo. La aplicación vive en su propio elemento y se vuelve a enganchar sola. |
| **Sin solapamientos** | Textos de una línea terminan en «…»; botones, chips y cajas de número no se encogen; las filas estrechas se parten en varias líneas. |
| **Controles alineados** | Una sola altura para botones, campos y cajas de número (`Ui.ControlHeight`); etiqueta y campo en la misma línea; caja de número [−][valor][+] unida. |
| **Iconos** | Herramientas del mapa y de Retoque, zoom, ajustar, rejilla, vecinos, cerrar... con iconos nítidos a cualquier escala (`IconArt`). |
| **Ayudas emergentes** | Unity no las muestra en una aplicación: ahora aparecen al dejar el ratón quieto (nombre, qué hace y atajo). |
| **Inicio fijo** | La pantalla de inicio tiene un tamaño fijo, centrada; no se escala con Ctrl + rueda. |
| **BMP y DIB** | Se pueden importar (`Bmp`, `ImageFile`): se convierten a PNG al entrar. Si ya hay un BMP en `graficos/`, Recursos ofrece «Convertir a PNG». |
| **Tamaños habituales al cortar** | El asistente ofrece siempre 16, 32 y 48 px (y el del proyecto), encajen o no, y dice cuántos píxeles sobran; luego se ajustan desplazamiento y separación. Un tileset de 16 px se dibuja al tamaño de tile del proyecto. |

## Pulido 2

| Arreglo / novedad | Qué cambia |
|---|---|
| **Zoom del mapa** | Midiendo la captura, en vertical los tiles coincidían con la rejilla y en horizontal salían ~3 % más estrechos: la cámara no respetaba la proporción. Ahora la proyección se fija a mano (exactamente ancho × alto píxeles de la textura). |
| **Tiles compacta** | Pestañas de tileset numeradas (1, 2… con el nombre en la ayuda), nombre pequeño con «…», zoom −/+ con %, retocar y los modos (pintar, paso, prioridad, terreno, arbusto, mostrador, pieza) como iconos. Queda más sitio para los tiles. |
| **Capas** | Iconos (añadir, subir, bajar, quitar; ver/oculta; bloqueada/desbloqueada), **asa para arrastrar** y cambiar el orden, el papel (suelo, detalles, encima) debajo del nombre, y un **deslizador propio** que se arrastra y muestra el % de opacidad. |
| **Barras de desplazamiento** | Finas, sin flechas, con una «píldora» redondeada que se ilumina al pasar el ratón. |
| **Importar** | Vista previa de las imágenes (hasta 8) con medidas y **tipo sugerido** (por el nombre o las medidas, `AssetKindGuesser`). |
| **Cambiar de tipo** | En Recursos, cada imagen muestra su tipo; clic = moverla a otro tipo con su corte (`AssetMover`). Si es un tileset usado, avisa. |
| **Menú Ver** | Ver → **Ventanas** → (cada ventana) y Ver → **Distribuciones** → (las de fábrica, las tuyas, guardar). Los menús admiten submenús. En los textos, «ventana» en vez de «panel». |
| **Escala** | Ctrl + 0 vuelve la interfaz al 100 %; al cambiarla con Ctrl + rueda sale un aviso con el % actual. |

## Cómo se hace un mapa (flujo)

1. **Recursos** → Importar (o copiar) el tileset en `graficos/tilesets` → **Cortar** (32 px).
2. **Mapas** o **Mundo** → Nuevo mapa: pueblo, ruta... (exterior, pegado a otro tramo) o interior; elige el tileset.
3. **Tiles** → elige un tile o un bloque → pinta en **Mapa** (B lápiz, U rectángulo, G relleno, E goma, I cuentagotas).
4. **Tiles** → modo Paso / Prioridad / Terreno para decir por dónde se pasa, qué va encima del jugador y dónde hay hierba.
5. **Encuentros** → «Todo el tramo» → «+ Hierba» → «+ Especie» (o una zona pintada para un trozo concreto).
6. **Propiedades** → Colocar el inicio → **Jugar (F5)**: se pasa de tramo en tramo andando. Esc para volver. Todo se guarda solo.

## Reglas

- **Una sola ventana**: una aplicación de Unity en Windows no abre ventanas nativas sueltas; los paneles se acoplan,
  se redimensionan y los diálogos flotan dentro de la ventana.
- **Sin emoji ni símbolos raros** en los textos: la fuente de UI Toolkit puede no tenerlos (se verían cuadrados).
  Usar letras, `×`, `·`, `«»`, `—`, `•`.
- La lógica (corte, JSON, proyecto, paneles, atajos) vive en los dominios puros (`Art`, `Project`, `Workspace`) con
  tests; la aplicación solo dibuja y reenvía clics.
- **Herramientas con icono** y ayuda emergente; textos que no caben, con «…». Ver [`DISENO.md`](DISENO.md).
- Solo APIs de UI Toolkit que ya existían en Unity 2021.3 (sin `IntegerField` ni `Painter2D`): así se puede compilar sin
  Unity. Para números, `Ui.NumberBox`.

## Verificar sin Unity

`dotnet build Tools/compilar_app` compila la aplicación y sus dominios contra las DLL de Unity 2021.3 (NuGet
`UnityEngine.Modules`). El código del Input System va entre `#if CTEDITOR_INPUT_SYSTEM` y `App/Editor` no se incluye
(usa UnityEditor). **Lo visual hay que probarlo en Unity.**

## Exportar el juego (decidido, pendiente)

Vía principal **motor listo + datos**: el motor del juego se compila con Unity una vez por plataforma y la aplicación
lo copia junto a los datos empaquetados (**un archivo, cifrado opcional**), con el nombre y el icono del juego.
**Windows primero**. Después, **Exportar a proyecto Unity** para profesionales (consolas, código propio). Ver la propuesta.
