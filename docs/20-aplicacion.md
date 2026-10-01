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
| Puertas | O herramienta Puerta · Esc cancela la puerta a medias |
| Piezas y pinceles | Ctrl+Mayús+C guardar la selección como pieza · Ctrl+Mayús+B piezas · Ctrl+Mayús+R pinceles |
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

### Puertas que se enlazan solas (fase 6, B3)

Herramienta **Puerta** (O) en la barra del mapa:

- **Clic en una casilla** → menú: **Entrada a un interior nuevo…** (nombre y tamaño: casa pequeña, casa, tienda,
  gimnasio, cueva) crea juntos el interior, la puerta de fuera y la alfombra de salida (abajo en el centro), enlazadas;
  o **Enlazar con otra casilla**: luego clic donde sale (en este mapa o en otro que abras en Mapas). Esc cancela.
- **Arrastrar** una puerta la mueve (el enlace sigue: es por id, no por posición). **Doble clic** lleva a la otra puerta.
- **Clic derecho** en una puerta: ir a la otra, hacia dónde sale el jugador al llegar (abajo fuera, arriba dentro, por
  defecto), cambiar el nombre, quitar esta (la otra queda sin salida y la ventana Problemas lo dice) o quitar las dos.
- En el mapa cada puerta muestra adónde lleva («→ Casa»), una marca en el lado por el que se sale, y en rojo si no lleva a
  ningún sitio. Todo se deshace con Ctrl+Z (las dos puertas de una vez).
- **Jugando**, al pisar una puerta se aparece un paso fuera de la otra, mirando hacia ese lado. La casilla de la puerta
  tiene que ser pisable (paso del tile).
- **Problemas** avisa: puerta sin salida, la otra se borró o lleva a un mapa que ya no existe, la otra no vuelve aquí, o al
  llegar se saldría del mapa.

**Probar en Unity**: en un pueblo, Puerta → clic en la puerta de una casa → «Entrada a un interior nuevo…» → Casa; se abre
el interior con su alfombra. Jugar: entrar y salir. Mover la puerta de fuera y volver a jugar. Quitar la de dentro: la
ventana Problemas lo dice; Ctrl+Z la devuelve.

### Autotiles (fase 6, B4)

Agua, caminos, acantilados... cuyos bordes se dibujan solos. Se añaden en **Corte libre**:

- **Desde otra imagen** (botón del cubo): se elige el PNG/BMP y se reconoce por sus medidas: **RPG Maker XP** (3 × 4
  tiles; los animados, de varios fotogramas a lo ancho, usan el primero), **VX / MV** (2 × 3) o **47 piezas** (8 × 6, en el
  orden de CTEditor). Se **escala** al tile del proyecto (un autotile de XP de 32 px sirve en un proyecto de 16).
- **Desde esta imagen**: se corta la pieza y en sus opciones se elige «Autotile XP / VX / 47 piezas» (la ventana dice si
  por las medidas lo parece).
- Sus **47 piezas** salen al final de la paleta en un bloque de 8 × 6. Se pinta con **cualquiera**: al pintar, rellenar,
  pegar o **borrar**, la casilla y sus 8 vecinas toman la pieza que les toca. Una esquina solo cuenta si los dos lados
  que la tocan también son del mismo autotile (por eso son 47); el borde del mapa cuenta como «igual» (el agua que llega
  al borde no tiene orilla allí), como en RPG Maker. Todo se deshace de una vez.
- Se guarda en el `.corte.json` (`autotile`: `xp`, `vx` o `47`; `imagen` si viene de otro archivo). El tileset necesita
  al menos 8 columnas.

**Probar en Unity**: Corte libre de un tileset → importar un autotile de agua de XP → guardar; pintar un lago con el
lápiz y el rectángulo (los bordes salen solos), borrar en medio (aparece la orilla de dentro), Ctrl+Z.

### Pinceles aleatorios (fase 6, B5)

Botón del **dado** en la cabecera de Tiles → ventana **Pinceles aleatorios**:

- **Nuevo pincel con los tiles elegidos**: los tiles del bloque elegido en Tiles (los repetidos pesan más). Se puede dar
  nombre, **añadir** más tiles (+) o quitarlos, y cambiar el **peso** de cada uno (rueda o flechas): sale ese número de
  veces más que uno de peso 1; debajo se ve su porcentaje.
- **Usar**: el lápiz, el rectángulo y el relleno pintan cada casilla con un tile al azar del pincel (hierba con flores
  aquí y allá, rocas variadas). En la cabecera de Tiles sale «Pincel: …  ×» (clic para volver al sello). Elegir un tile
  en Tiles también vuelve al sello normal. Se deshace de una vez y funciona con capas automáticas y autotiles.
- Se guardan en `datos/pinceles.json`, con los tiles por **id de tileset**: sirven en cualquier mapa que use ese tileset
  (si no, la ventana lo avisa).
- **Terreno**: los bordes que se dibujan solos son los autotiles (B4); un pincel puede mezclar tiles normales y de autotile.

**Probar en Unity**: elegir en Tiles hierba y dos de flores, dado → «Nuevo pincel…», subir el peso de la hierba a 6,
«Usar» y pintar con el rectángulo: hierba con flores sueltas; Ctrl+Z lo quita entero.

### Piezas reutilizables (fase 6, B6)

Botón de la **casa** en la cabecera de Tiles (o Ctrl+Mayús+B) → ventana **Piezas reutilizables**:

- **Guardar la selección como pieza** (o Ctrl+Mayús+C con una zona seleccionada): se guarda el trozo con **todas sus
  capas** (suelo, detalles y lo que va encima, como el tejado) y los tilesets que usa.
- **Colocar**: la herramienta Pegar con la pieza; cada clic pone una copia (varias seguidas; Esc u otra herramienta
  termina). Si el mapa no tiene alguno de sus tilesets, se le añade. Cada capa va a la capa de su tipo; los tiles
  girados o volteados se conservan (antes, pegar perdía el giro: arreglado).
- Colocar hace una **copia**: cambiar el nombre de la pieza o quitarla no toca los mapas donde ya está.
- Se guardan en `datos/piezas.json` (cada capa en filas de números, como los mapas).

**Probar en Unity**: seleccionar una casa con su tejado, Ctrl+Mayús+C; abrir otro pueblo, Ctrl+Mayús+B → «Colocar», clic
en dos sitios; quitar la pieza: las casas siguen ahí; Ctrl+Z quita la última.

### Resumen de la fase 6 (B1-B6)

| Qué | Dónde | Atajo |
|---|---|---|
| Corte libre y autotiles | Recursos → «Corte libre…» o Tiles → selección | — |
| Edición rápida | Mapa | Mayús+clic, X, Y, R, Alt+1-9, Ctrl+Alt+1-9 |
| Puertas enlazadas | Barra del mapa → Puerta | O |
| Pinceles aleatorios | Tiles → dado | Ctrl+Mayús+R |
| Piezas reutilizables | Tiles → casa | Ctrl+Mayús+B, Ctrl+Mayús+C |

### Bloque C: el contenido del juego en la aplicación (base, C1-C3)

Nuevo ensamblado puro `CTEditor.Content` (sin Unity), con pruebas:

- **Esquemas** (`ContentSchemas`): cada categoría (especies, movimientos, habilidades, objetos, tipos y su tabla,
  naturalezas, grupos huevo, curvas, estados, climas, efectos de lado, trampas, entrenadores, equipos, sets, reglas)
  con su archivo de `datos/`, sus columnas (texto, número, si/no, opción, referencia, listas, aprendizaje por nivel,
  evoluciones, equipos, efectos escritos), cuáles hacen falta y a qué apuntan. Las columnas que no conoce se conservan.
- **Base de datos** (`ContentDatabase`): lee los CSV de `datos/` (comillas, «;», BOM) y guarda solo lo que cambió, a
  través de un archivo temporal; un archivo que no se puede leer se avisa y no se toca.
- **Comprobaciones** (`ContentChecks`): ids vacíos, repetidos o con espacios; celdas que hacen falta; números; si/no;
  opciones; referencias a cosas que no existen. Cada aviso dice **archivo, fila y columna**. Con el pack Gen7 real
  solo salen 7 avisos de ids con «__».
- **Quién usa qué** (`ReferenceIndex`): todas las referencias entre hojas (una especie en un equipo o una evolución,
  un movimiento en un aprendizaje, un tipo en la tabla...) más las de fuera (encuentros de los mapas).
- **Operaciones seguras** (`ContentSession`): crear (id a partir del nombre), duplicar, cambiar, **borrar** con sus
  usos (sustituir por otro, quitarlos o dejarlos para que Problemas los señale), **renombrar** un id en todas partes
  (incluida la columna de la tabla de tipos), **papelera** (`datos/papelera.csv`) para recuperar, y todo con Ctrl+Z.

### Ventanas de datos (bloque C, C3-C5)

Menú **Datos** (agrupado como en Unity: Criaturas, Combate, Objetos, Personajes) y **Base de datos** (tarjetas con
cuántas fichas hay y cuántos problemas). Cada categoría tiene su ventana con la distribución de los editores de Unity:

- **Barra de la ventana**: nuevo (+), duplicar, cambiar el id, ¿quién lo usa?, borrar, deshacer y rehacer. «Cómo usar»
  está en la «i».
- **Izquierda**: buscador (nombre, id o número), filtro por tipo con su color (especies y movimientos), orden (n.º,
  nombre, total), lista virtual con la marca de color de cada fila (su tipo o su color) y un aviso rojo si la ficha
  tiene errores.
- **Derecha**: nombre, id e insignias (tipos con su color, categoría, legendario); los problemas de la ficha arriba;
  vista previa (**Especies**: barras de estadísticas con colores y total, editables con la rueda; **Tipos**: muy eficaz,
  débil, resiste e inmune sacados de la tabla); el formulario por secciones (referencias como fichas con nombre, clic =
  abrir, rojo si no existe; listas con + y ×; aprendizaje por nivel como tabla con el tipo de cada movimiento; colores con
  el selector; si/no; opciones); las columnas propias en «Otros»; y **Lo usan** (clic = ir, también a los mapas).
- **Borrar** con usos abre un diálogo con la lista: **Sustituir por otra…**, **Quitar los usos** o **Dejarlos**
  (Problemas los lista). Sin usos, se borra al momento (a la papelera).
- **Cambiar el id** lo cambia en todos sus usos (y en los encuentros de los mapas).
- **Papelera**: lo borrado, con «Recuperar».
- Se guarda solo un momento después del último cambio (y con Ctrl+S). Ctrl+Z en una ventana de datos deshace en los
  datos.

**Probar en Unity**: Datos → Criaturas → Especies; buscar «pika», subir su velocidad con la rueda, Ctrl+Z; borrar
Pikachu (si una ruta o un entrenador lo usa, el diálogo lo dice) → «Sustituir por otra…» → Raichu: el entrenador y la
ruta tienen ahora Raichu; Ctrl+Z lo devuelve todo. Cambiar el id de un movimiento: los aprendizajes cambian. Problemas:
clic en un error de datos abre su ficha.

Lo propio de cada editor de Unity, ya en la aplicación:

- **Entrenadores y equipos**: el equipo en tarjetas (especie con sus tipos, nivel, sexo, hasta 4 movimientos, objeto,
  naturaleza y habilidad; subir, bajar, quitar; «+ Añadir al equipo», máximo 6) y, debajo, el mismo equipo como texto
  (como en Excel). Se conservan EVs, IVs y mote. Naturalezas y habilidades de los equipos también cuentan como usos.
- **Especies**: la **familia** (de quién evoluciona, a qué y cómo, variantes), clicable.
- **Tabla de tipos**: la **matriz** entera (filas atacan, columnas defienden); clic en una casilla: ×1 → ×2 → ×½ → ×0.
- **Naturalezas**: el **5 × 5** (sube × baja) con la elegida resaltada.
- **Curvas de experiencia**: barras de XP cada 5 niveles y los totales de las 6 clásicas para comparar.

### Cambiar de generación (C6)

**Proyecto → Cambiar de generación / traer un pack…** (o Datos → Cambiar de generación…):

1. Elegir el pack (Gen1…Gen7 o uno propio).
2. **Vista previa** por categoría: cuántas fichas son nuevas, cuáles cambian (y en qué columnas), cuántas iguales y
   cuáles no trae el pack (y si se usan). Se marcan las categorías que se quieren cambiar y qué hacer con lo que el pack
   no trae: **dejarlo** o mandarlo a la **papelera**.
3. **Cambiar**: primero una copia de seguridad (Historial de versiones), luego se aplica en un solo paso. Las columnas
   que solo tiene el proyecto se conservan. **Ctrl+Z** en una ventana de datos lo deja exactamente como estaba.

### Rediseño de las ventanas de datos (tras la primera prueba)

- **Secciones plegables** en todas las fichas (se recuerda cuáles abres); los textos largos **crecen hacia abajo** en
  vez de salirse de la ventana.
- **Nada de escribir códigos**: desplegables para las opciones (y para cualquier columna con pocos valores, como el
  objetivo o la estadística de ataque, con «Otro…»), naturalezas con «↑ sube ↓ baja» al lado, EVs que da como
  «estadística + número», EVs e IVs con **deslizadores** (total sobre 510), evoluciones con desplegables (a quién,
  cómo: nivel, objeto, amistad, intercambio, subir de nivel; y de día / de noche).
- **Buscadores con filtros**: tipos (siempre en su color, el elegido con borde blanco), categoría de movimiento,
  bolsillo del objeto, **categoría de habilidad** (deducida de sus efectos y cambiable a mano), formato del set...
  Cada fila dice algo útil (tipo, categoría y potencia de un movimiento; ↑/↓ de una naturaleza).
- **Especies**: estadísticas, General, Habilidades, Crecimiento, Movimientos (por nivel, MT/MO, tutor y huevo, todos
  como tablas con tipo, categoría, potencia y precisión), Crianza (grupos huevo con «i» para ver quién más está, y
  «¿Quién lo pasa?» en cada movimiento huevo), Evolución y familia (el **árbol entero** desde la primera fase, con
  variantes, y botón de árbol en la barra), Pokédex, Formas.
- **Sets de competición**: la lista dice «Heatran · OU · Z-Move»; el id se forma solo (especie_formato_nombre);
  formato con desplegable; objeto, habilidad (solo las de su especie, con casilla para ver todas) y naturaleza con
  alternativas; los 4 movimientos en tabla con sus alternativas (aviso si no lo aprende); EVs/IVs plegables.
- **Equipos de entrenadores**: cada miembro en una tarjeta plegable con especie, nivel, sexo, objeto, naturaleza,
  habilidad (de su especie; **aviso de habilidad ilegal**, sin prohibirla), movimientos en tabla y EVs/IVs.
- **Avisos** (no errores) en Problemas: habilidad ilegal y movimientos que la especie no aprende, en sets y equipos.
- **Nuevo** con **plantillas** de los packs (con filtros) o vacío; en Curvas, «Crear las 6 clásicas».
- **Rapidez**: las comprobaciones se calculan una vez por cambio y volver a una pestaña no la rehace si nada cambió.

**Efectos por bloques** (Objetos y Habilidades), como en Unity: los efectos agrupados por «cuándo» en secciones
plegables; cada tarjeta es «cuándo → qué» con desplegables, los parámetros que pide esa acción (tipo, estado,
estadística, clima, movimiento, número con su unidad, a quién), sus **condiciones** como una frase editable («Si el
rival tiene como mucho 50 % de vida», con NO), probabilidad, veces por combate, «se gasta» (solo objetos), subir,
bajar y quitar; debajo, la frase de lo que hará el motor (en naranja si el motor aún no lo aplica). «+ Añadir
efecto…» pregunta primero CUÁNDO y luego QUÉ (con los comportamientos especiales de habilidades). Todos los efectos de
los packs Gen1…Gen7 se leen (prueba `PackEffectsTests`). El traductor vive ahora en `CTEditor.GameDefinition.Text`,
compartido por Unity y la aplicación.

**Movimientos por bloques**: los **efectos** (y el efecto Z) en tarjetas elegidas de un menú agrupado (Estados,
Estadísticas, Daño y curación, Clima y campo, Cambios, Control, Tipos y forma, Objetos y habilidades, Usar otros
movimientos), cada una con solo los datos que pide (estado, estadística y etapas, %, clima y turnos, trampa, efecto de
lado, tipo, habilidad, movimiento…), a quién, probabilidad, «mismo dado que el anterior» y condiciones; los
**requisitos** («solo funciona si…»), los **cambios de potencia** («×2 si…») y el **tipo según el clima** también con
desplegables. Todos los movimientos de los packs se leen (prueba). El traductor de movimientos también está ya en
`CTEditor.GameDefinition.Text`.

**Validadores de Unity** sobre los datos (`GameRuleChecks`, en la ventana Problemas y arriba de cada ficha):
efectos que no se pueden leer o usan cosas que no existen, movimientos de daño con potencia 0, daño especial en un
movimiento de estado, golpes mín/máx al revés, efectos sin estado o con 0 etapas, especies con más de 2 grupos huevo o
repetidos, evolución con objeto sin decir cuál o nivel < 1, «al subir de nivel» sin condición, de día y de noche a la
vez, especies sin aprendizaje por nivel, números de Pokédex repetidos, equipos con más miembros que las reglas, nivel
por encima del máximo, más de 4 movimientos, un miembro sin movimientos que no aprende ninguno a su nivel, EVs por
encima de los topes, objetos de la mochila que no se usan en combate, entrenadores que no dan dinero, climas, trampas y
efectos de lado que no hacen nada, naturalezas de 100 % o más, reglas con topes de EVs incoherentes o Forcejeo que no
existe. Las comprobaciones de curvas con tabla y tramos quedan para cuando las curvas propias se editen en la
aplicación.

**Contenido clásico integrado**: los estados, climas, trampas y efectos de lado que el motor ya trae (Quemadura, Lluvia,
Púas, Reflejo, campos…) cuentan como existentes aunque el proyecto no tenga su hoja, salen en las listas y se ven con
su nombre en español.

Arreglo de datos: en el pack **Gen5**, 3 entrenadores llevaban Chaleco Asalto (no existe hasta la 6.ª gen.): ahora
llevan Restos. Ningún pack da errores con estas reglas (prueba).
