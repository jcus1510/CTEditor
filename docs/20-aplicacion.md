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

## Pulido 3 — atajos y escala por ventana

| Arreglo / novedad | Qué cambia |
|---|---|
| **El mapa ya no se descuadra** | Causa real: cada renderizador se colocaba en x = capa × 100 000 (3 100 000 unidades). Ahí un `float` solo distingue ~0,25: los tiles «saltaban» a cuartos de casilla en horizontal. Ahora están en el origen (se separan solo por la capa de Unity). |
| **Tilesets en Propiedades** | Una fila compacta por tileset (número + nombre con «…»; el nombre entero en la ayuda) y «Cambiar el principal…» como menú. Los botones con texto largo terminan en «…». |
| **Escala por ventana** | **Alt + rueda** sobre una ventana la amplía o reduce sola (y se recuerda); **Alt + 0** la devuelve al 100 %. **Ctrl + rueda** / **Ctrl + 0**: toda la interfaz. |
| **Atajos** | Por categorías y todos cambiables (Entorno → Atajos, o **F1**): clic en el atajo y pulsar la nueva combinación; buscador; volver al de fábrica por acción. **Espacio + arrastrar** mueve el mapa. |

### Atajos de fábrica

| Categoría | Atajos |
|---|---|
| General | Ctrl+S guardar · Ctrl+Z / Ctrl+Y deshacer / rehacer · Ctrl+N nuevo mapa · Ctrl+P buscar · F1 atajos · Ctrl+, entorno · F11 pantalla completa |
| Jugar | F5 jugar · Ctrl+F5 probar desde el ratón · F9 depurador |
| Herramientas | B lápiz · U rectángulo · G relleno · E goma · I cuentagotas · M selección · P inicio · H zona de encuentros · L línea (retoque) |
| Selección | Ctrl+A todo · Ctrl+D quitar · Ctrl+C / Ctrl+X / Ctrl+V · Supr borrar |
| Vista | Ctrl+G rejilla · N vecinos · Z acercar · Mayús+Z alejar · F encuadrar · Espacio + arrastrar mover · Ctrl / Alt + rueda escala |
| Capas | Av Pág / Re Pág siguiente / anterior · Ctrl+Mayús+N nueva · Ctrl+H ver · Ctrl+Mayús+K bloquear · Ctrl+L capas automáticas |
| Sello | X / Y voltear · R girar a la derecha · Mayús+R a la izquierda · Ctrl+Alt+1-9 guardar el sello · Alt+1-9 recuperarlo |
| Tiles | 1 pintar · 2 paso · 3 prioridad · 4 terreno · 5 arbusto · 6 mostrador · 7 pieza |
| Ventanas | Ctrl+1 Mapa · Ctrl+2 Tiles · Ctrl+3 Capas · Ctrl+4 Mundo · Ctrl+5 Encuentros · Ctrl+6 Recursos · Ctrl+7 Retoque · Ctrl+8 Propiedades · Ctrl+9 Mapas |

Inspirados en: herramientas de una letra de Photoshop / Aseprite y de [Tiled](https://doc.mapeditor.org/en/stable/manual/keyboard-shortcuts/)
(el editor de mapas que usa Pokémon Studio), Ctrl + número para cambiar de vista y números para los modos como en
[GB Studio](https://www.gbstudio.dev/docs/getting-started/keyboard-shortcuts/), y F para encuadrar como en Unity.

## Pulido 4

| Arreglo / novedad | Qué cambia |
|---|---|
| **Barra del mapa colocable** | Clic derecho en la barra: arriba, **a la izquierda (por defecto, recomendado)**, a la derecha o abajo; se recuerda (`WorkspaceSettings.Pref`). En vertical, todo son iconos (capas automáticas incluidas) y se gana alto para el mapa. |
| **Capas: se pinta donde eliges** | Con capas automáticas, cada tile iba a la capa de su tipo aunque eligieras otra. Ahora, elegir una capa en Capas apaga las automáticas y lo dice («Ahora pintas en la capa…», Ctrl+L para volver); con las automáticas puestas, Capas muestra un aviso y no resalta ninguna fila. |
| **Quitar tilesets** | Se puede quitar cualquier tileset que no use ningún tile (antes solo el último); los tiles de los siguientes conservan el suyo. |
| **Tiles** | Etiquetas de terreno más pequeñas (punto de color + texto corto). Cada modo explica qué es al pasar el ratón (en la ayuda y en la línea de abajo): p. ej. «Arbusto: el jugador se ve medio hundido, como en la hierba alta». |
| **Cajas de número** | La etiqueta va pegada a su caja («Ancho [12]   Alto [14]»), con más espacio entre parejas; las apiladas (asistente de corte) siguen alineadas en columna. |

## Pulido 5

| Arreglo / novedad | Qué cambia |
|---|---|
| **Rejilla** | Era un elemento de 1 punto por línea: en posiciones con decimales algunas se perdían a trozos al hacer zoom o mover el mapa. Ahora es una sola malla (`GridOverlay`) con cada línea en un píxel entero y una sombra oscura al lado: se ve sobre tiles claros y oscuros. |
| **Ayudas emergentes** | Se busca el elemento que de verdad está bajo el ratón (`panel.Pick`) y se sube hasta el primero con ayuda; la barra ya no tiene ayuda propia que tapara la de sus botones. |
| **Barra del mapa** | Botones más grandes (con iconos más grandes) y un botón al final para colocarla (además del clic derecho). |
| **Nuevo mapa** | Formulario en dos columnas (etiquetas a la izquierda, controles a la derecha), tamaños habituales con un clic (20 × 15, 40 × 30, 60 × 40, 12 × 10), **brújula** para elegir por qué lado se pega al tramo, y una **ficha resumen** a la derecha con la miniatura del tileset y lo que se va a crear. |

## Pulido 6 — ventana Encuentros

| Antes | Ahora |
|---|---|
| Todas las zonas abiertas a la vez, con todos los métodos como botones | **Zonas** en una lista compacta (color, nombre, casillas y métodos; pintar y papelera); «+» añade «Todo el tramo» o una zona pintada; doble clic = renombrar. Debajo, solo la **zona elegida**: sus métodos en **pestañas** («Hierba · 10 %») y «+» para añadir otro. |
| No había forma clara de dejar de pintar | Mientras pintas sale un aviso con **«Dejar de pintar»**; también Esc, B o el mismo icono. |
| Horas: ninguna marcada = todas (contraintuitivo) | **Las cuatro encendidas = sale siempre**; se apagan las que no. Al menos una queda encendida. Para ver los %: **«Todas las horas»** (según los pesos) o una hora concreta. |
| Filas largas de cajas | Cada especie es una **tarjeta**: nombre, **barra con el % real**, niveles «Nv. 2 – 4», peso, horas M D T N y el **interruptor** (icono; encendido si hace falta uno). |
| Solo se veía la zona elegida | Botón para **resaltar todas las zonas** en el mapa (con su nombre) o volver a resaltar solo la elegida. |
| Ctrl+Z no deshacía aquí (si antes usaste Retoque) | El editor activo es **la ventana en la que haces clic**: Ctrl+Z deshace en Retoque si estás en Retoque, y en el mapa (encuentros incluidos) en las demás. |

## Fase 5a — copias, Ctrl+P y Problemas

| Pieza | Dónde | Qué hace |
|---|---|---|
| **Historial de versiones** | `Project/Backups.cs` (`ProjectBackups`, `BackupSchedule`), Proyecto → Historial de versiones… (Ctrl+Mayús+H) | Una copia comprimida (.zip) del proyecto en `copias/` **cada 10 minutos, solo si cambiaste algo** (si no tocas nada, no guarda nada). **Como mucho 5**: al hacer otra se borra la más vieja. Se hace en segundo plano. A mano: **Ctrl+Mayús+S**. Restaurar: antes guarda una copia de cómo está ahora («antes de restaurar») y luego reabre el proyecto. |
| **Guardar** | Botón con disquete en la barra de menús, **Ctrl+S** | Todo se guarda solo, pero el botón lo asegura y lo dice. Ctrl+S ya era «Guardar» (no estaba ocupado por otra cosa); Ctrl+G sigue siendo la rejilla. |
| **Iconos en las pestañas** | `PanelInfo.Icon`, `DockView` | Cada ventana lleva su icono delante del nombre (el nombre se queda). |
| **Paleta de órdenes** | **Ctrl+P**, `CommandPalette` + `Workspace/CommandSearch.cs` | Escribe lo que quieres hacer: todas las acciones (con su atajo), ventanas, mapas del proyecto, distribuciones y temas. Sin tildes ni mayúsculas, con iniciales («pp» → «Pueblo Paleta»). ↑ ↓ e Intro. |
| **Ventana Problemas** | `Editing/ProblemFinder.cs`, `ProblemsPanel` (Ctrl+Mayús+M) | Revisa el proyecto sola tras cada cambio: inicio del jugador, tilesets que faltan, tiles fuera de su tileset, tramos que se pisan, exteriores sin colocar, zonas sin casillas o sin especies, especies que no están en los datos, métodos borrados. Errores primero; clic = abrir el mapa. Un módulo añade comprobaciones con `ProblemFinder.Register`. |

## Encuentros como tabla (referencias de otros editores)

Se miró cómo lo hacen otros:
- **Porymap** ([manual](https://huderlem.github.io/porymap/manual/editing-wild-encounters.html)): una pestaña por
  método (hierba, agua, golpe roca, pesca) con una tabla de huecos (especie, nivel mínimo y máximo, % por hueco) y la
  probabilidad del método arriba; los grupos sirven para variantes por hora.
- **Pokémon Studio** ([ayuda](https://pokemonworkshop.com/en/help/pokemon-sdk/creating-wild-encounters/)): «grupos»
  con activación por interruptor (hora o uno propio), combate simple o **doble**, entorno y variación (etiquetas de
  terreno); cada Pokémon con nivel, probabilidad, habilidad, naturaleza, **variocolor**, sexo, movimientos...
- **Essentials**: tipos de encuentro por terreno y por hora (LandMorning, LandNight...) con su densidad.

| Qué | Cómo queda en CTEditor |
|---|---|
| Tabla (Porymap) | Filas con aire (8 px arriba y abajo, filas alternas, separador): **miniatura** (sprite de `graficos/iconos` o `graficos/combate` con el id de la especie, o su inicial), nombre, **Nivel 2 – 4** y **Peso** en cajas pequeñas (escribir, rueda del ratón o ↑ ↓), **% real con barra** y **≈ pasos de media para encontrarla** (lo que ninguno enseña), **horas** con iconos pequeños y menú **⋯**. |
| Horas (Essentials) | Cuatro iconos: amanecer, sol, atardecer, luna. Encendido = sale a esa hora. Los cuatro encendidos = siempre. |
| Activación (Pokémon Studio) | **Condición**: un interruptor que encienden los eventos («liga_vencida»...). Con él, la especie solo sale tras ese momento de la historia. Se explica en su propia ventana y se ve bajo el nombre («solo si «liga_vencida»»). |
| Extras por especie (Pokémon Studio) | En «⋯»: **forma**, **objeto equipado**, **variocolor 1 de N**; también subir, bajar, **duplicar** (para variantes) y quitar. |
| Combate doble (Pokémon Studio) | Por método y zona: **Dobles %**. |
| Probabilidad del método | Arriba de la tabla, con la explicación: «De media, un encuentro cada 10 pasos». |

## Encuentros, ronda 2 · ayuda en las ventanas · ajustes del juego

| Qué | Cómo |
|---|---|
| **Tabla alineada** | Cabecera y filas comparten los anchos de columna; el % lleva una barra corta al lado y «≈ N pasos» debajo, en pequeño. |
| **Horas de vista** | Iconos: reloj (todas las horas), amanecer, sol, atardecer, luna. |
| **Color de la zona** | Clic en su cuadrito: **selector con degradado** (cuadro de saturación y brillo, barra de tono), antes y ahora, hexadecimal, R G B y colores rápidos (`ColorPicker`, reutilizable). |
| **Dejar de pintar** | Sin aviso aparte: el icono de pintar de la zona alterna (y Esc). |
| **Variocolor global** | **Proyecto → Ajustes del juego…**: 1/8192, 1/4096 o el que quieras, para todo el juego (`ProjectSettings.ShinyOdds`). En una especie solo cuenta si le pones uno propio. |
| **Selector de especies** | Todas (lista virtual, sin límite), **en orden de Pokédex** con número, miniatura y tipos de color; buscar por nombre, id o «#25»; filtros: **hasta dos tipos (exactos o no)**, **grupo huevo**, **generación**, **formas** (con, sin, solo alternativas) y **legendarios**. Datos de `datos/especies.csv`, `tipos.csv` y `grupos_huevo.csv` (`SpeciesFilter`, `CsvSpeciesDirectory.Entries`). |
| **Botón «i»** | En la cabecera de cada ventana: qué es, cómo se usa y trucos (`PanelHelp`; un módulo añade la suya con `PanelHelp.Register`). |

## Sugerencias de diseño aplicadas

| # | Qué | Dónde |
|---|---|---|
| 1 | La «i» de una ventana **parpadea tres veces** la primera vez que la ves (una vez por ventana) | `DockView`, preferencia `ayuda_vista_<ventana>` |
| 2 | Tabla de especies **más estrecha** y **asa para arrastrar** y ordenar | Encuentros |
| 3 | **Simulador de encuentros** en su propia ventana (icono de dado junto a «Zonas»): método, hora, 100/1000/10000 pasos, interruptores; qué sale, %, cuántas veces y niveles | `EncounterSimulator` |
| 4 | **Plantillas** al crear una zona: vacía, ruta temprana, bosque, cueva, agua (se saltan las especies que no estén en tus datos) | `EncounterTemplate`, `MapEditorSession.ApplyTemplate` |
| 5 | Aviso de **casillas pintadas sin su terreno** (p. ej. hierba de encuentros sobre un camino): en la fila de la zona y en rojo en el mapa. **Apagado por defecto**: engranaje de Encuentros → «Avisar de casillas pintadas sin su terreno» | `EncounterChecks.CellsOffTerrain` |
| 6 | **Títulos de sección** iguales en todas las ventanas (mayúsculas pequeñas grises) | `Ui.SectionTitle` |
| 8 | **Densidad**: cómoda (por defecto, la de siempre) o compacta | Entorno → Interfaz |
| 9 | **Animaciones** de 0,12 s al abrir menús, ventanas y ayudas; desactivables | Entorno → Interfaz, `Ui.Appear` |
| 10 | **Color de acento** sin cambiar el tema (azul, verde, morado... u otro con el selector); los colores admiten transparencia (#RRGGBBAA) para temas propios | Entorno → Tema |

## Bloque A — probar sin salir (fase 5b)

| Pieza | Qué hace |
|---|---|
| **Ventana Juego** (`GameWindow`) | Se juega **dentro de la ventana** mientras se edita al lado: con la ventana Juego abierta, F5 juega ahí. Lo que pintas, el paso, las capas y los tilesets cambian **al momento** en el juego. Clic en el juego para jugar; si haces clic en el editor, el juego se pone en pausa («haz clic en el juego para seguir»). Botones: Jugar aquí, pantalla completa, desde el ratón, Parar. |
| **Perfiles de prueba** (`TestProfile`, `datos/perfiles_prueba.json`) | Con qué se empieza: equipo (hasta 6, con el selector de especies), medallas, dinero, objetos, interruptores encendidos y hora fija o la del reloj. Los encuentros usan su hora y sus interruptores; el depurador (F9) lo muestra. Se eligen en la ventana Juego, en Jugar → Perfil de prueba y con Ctrl+P; se editan en «Editar perfiles…». |
| **Selector de especies común** (`SpeciesPicker`) | El mismo en encuentros, perfiles y (pronto) entrenadores. |
| **Estados vacíos** (`Ui.EmptyState`) | Una ventana sin nada muestra un icono, qué hacer y el botón principal (Encuentros, Mapas, Capas, Recursos, Problemas). |

## Lista de prueba en Unity (antes de seguir)

Marca lo que funcione y mándame captura de lo que no:

1. **Inicio**: crear un proyecto con un pack; abrir uno reciente; la ventana no se estira.
2. **Recursos**: importar un PNG y un BMP (vista previa y tipo sugerido); cambiar el tipo de una imagen; cortar a 16 px con desplazamiento y separación.
3. **Mapa**: pintar con cada herramienta (B, U, G, E, I, M), zoom con la rueda / Z / Mayús+Z / F, Espacio + arrastrar; la rejilla siempre entera y alineada.
4. **Capas**: elegir capa y pintar; arrastrar el asa para reordenar; ver / bloquear; opacidad arrastrando.
5. **Tiles**: los modos 1-7 (paso, prioridad, terreno...) y que se guarden; varios tilesets por mapa; quitar uno no usado.
6. **Selección**: Ctrl+A, Ctrl+C, Ctrl+V, Supr, Ctrl+Z / Ctrl+Y.
7. **Mundo**: crear un tramo al lado de otro (brújula), arrastrarlo, Ver / Bloq. / Solo; mapa de la región.
8. **Encuentros**: zona de todo el tramo y una zona pintada (H), especies con pesos, pesos clásicos.
9. **Jugar**: F5 desde el inicio, Ctrl+F5 desde el ratón, cruzar de tramo andando, salen encuentros, Esc vuelve.
10. **Retoque**: abrir un tile con Ctrl + clic en el mapa, pintar, guardar y ver el cambio en el mapa.
11. **Entorno**: Ctrl / Alt + rueda y Ctrl+0 / Alt+0; tema claro; F1 y cambiar un atajo pulsándolo; Ver → Ventanas / Distribuciones; guardar una distribución.
12. **Guardar y reabrir**: cerrar la aplicación y volver a abrir el proyecto: todo sigue igual.

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

### Edición rápida (fase 6, B2)

- **Mayús + clic** con el lápiz o la goma: una **línea recta** desde la última casilla pintada hasta esa (un solo Ctrl+Z).
- **Voltear y girar el sello**: X (horizontal), Y (vertical), R (girar a la derecha), Mayús+R (a la izquierda). Se gira
  el bloque y cada tile; la vista previa del sello en el mapa ya sale girada.
- **Sellos guardados**: Ctrl+Alt+1…9 guarda el sello actual; Alt+1…9 lo recupera.
- Las casillas guardan el giro en tres bits (voltear H, voltear V, diagonal) que los mapas antiguos no tienen: siguen
  igual. El paso, el terreno y la prioridad son los del tile original.

**Probar en Unity**: pintar un tile, Mayús + clic cinco casillas más allá (sale la línea; Ctrl+Z la quita entera);
elegir un bloque de 2×1, pulsar R y X y pintar (sale girado y volteado, también en «Jugar»); guardar con Ctrl+Alt+1,
elegir otro y recuperarlo con Alt+1; guardar el proyecto y volver a abrirlo: los giros se conservan.

### Corte libre (fase 6, B1)

Para tiles grandes y objetos sueltos de ripeos que no siguen la rejilla. Se abre con **Corte libre…** en Recursos (en
un tileset ya cortado) o con el botón de selección de la cabecera de Tiles.

- **Arrastrar** en la imagen corta una pieza con precisión de píxel; arrastrar **dentro** la mueve y desde su **esquina**
  cambia el tamaño. Flechas: mover 1 píxel; Mayús + flechas: cambiar el tamaño; Supr: quitar. «Ajustar a la rejilla»
  hace que encaje en los tiles.
- **Lupa** con los píxeles alrededor del ratón, su posición y su color. Alt + rueda: zoom.
- **Detectar objetos** (el dado): busca los grupos de píxeles sueltos y crea una pieza para cada uno; luego se revisan.
- Cada pieza ocupa los **tiles enteros** que necesite y se apoya **abajo** de su bloque (los árboles quedan sobre el
  suelo). Los bloques se colocan en filas nuevas **al final de la paleta**: los números de los demás tiles no cambian, y
  quitar o añadir piezas no mueve las otras.
- Se guarda en el `.corte.json` (`piezas_libres`). Retocar un tile de una pieza abre la pieza en la imagen original.

**Probar en Unity**: cortar un tileset ripeado, «Corte libre…», «Detectar objetos», ajustar uno con la lupa, guardar:
las piezas salen al final de la paleta de Tiles y se pintan como un bloque; cambiar sus propiedades (paso, prioridad)
y volver a abrir el proyecto: siguen ahí.

