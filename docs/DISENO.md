# Diseño visual de la aplicación

Documento **vivo**: se actualiza cada vez que pulimos algo de la interfaz. Recoge las reglas visuales (para que todo
se vea igual), cómo está hecha la interfaz por dentro y la lista de lo que falta por pulir.

---

## 1. Cómo está hecha la interfaz (y por qué)

### 1.1 Qué tecnología usa

La aplicación usa **UI Toolkit**, el sistema de interfaz oficial de Unity. Es el mismo con el que Unity dibuja su
propio editor: el Inspector, la Hierarchy y el UI Builder. Las demás opciones eran peores para este caso:

| Opción | Por qué no |
|---|---|
| uGUI (Canvas, GameObjects con `Image`/`Button`) | Pensado para menús de juego. Con cientos de controles (paneles, listas, rejillas) es lento y no tiene distribución automática tipo «flex». Un editor con paneles acoplables sería muy pesado. |
| IMGUI (`OnGUI`) | Es el sistema antiguo. Se redibuja entero en cada fotograma y no tiene estilos ni distribución flexible. |
| **UI Toolkit** ✔ | Retenido (solo se redibuja lo que cambia), con distribución flexible (como CSS), estilos y texto nítido. Vale para el juego y para el editor. |

**¿Por qué no se ven los elementos en la Hierarchy?** Porque UI Toolkit **no usa GameObjects**. Toda la interfaz cuelga
de un único `UIDocument` (el objeto «Interfaz»). Para verla por dentro en Unity:

- **Window → UI Toolkit → Debugger** → elige el panel «CTEditor (interfaz)». Muestra el árbol completo de elementos.
  Al pasar el ratón se resalta cada uno, y se pueden ver y probar sus estilos en vivo. Es el «Inspector» de la interfaz.
- **UI Builder** (Window → UI Toolkit → UI Builder): sirve para editar visualmente las plantillas `.uxml` y hojas
  `.uss`. Hoy casi toda la interfaz se construye por código; ver el plan del apartado 4.

### 1.2 ¿Por qué no una aplicación externa a Unity (WPF, Electron, Avalonia…)?

Se evaluó y **no conviene**:

1. **Jugar al instante.** Lo que se edita se juega en el mismo motor y en la misma ventana, sin exportar nada. Con un
   programa externo habría dos motores (el del editor y Unity) comunicándose entre sí. Eso añade más lentitud, más
   errores y el doble de código para dibujar mapas.
2. **El mapa se dibuja con el Tilemap de Unity.** Lo que se ve al editar es exactamente lo que se ve en el juego. Fuera
   de Unity habría que reescribir el dibujo y mantener dos versiones iguales.
3. **El dominio ya es independiente.** Las reglas (mapas, encuentros, corte, píxeles) están en C# puro, sin Unity. Si
   algún día hiciera falta otra interfaz, se reutilizan tal cual. Solo cambiaría la capa `App`.
4. **Un solo lenguaje y un solo proyecto.** Todo es C# y se compila y prueba igual.

Lo que sí se mejora es **cómo se escribe** la interfaz: se pasa de estilos dentro del código a hojas de estilo y
plantillas editables con UI Builder (apartado 4).

### 1.3 Piezas

| Pieza | Archivo | Qué hace |
|---|---|---|
| Arranque | `App/AppRoot.cs` | Crea la ventana, el `UIDocument` y la escala. Vuelve a enganchar la interfaz si Unity reconstruye el documento (pasaba al seleccionar «Interfaz» en la Hierarchy). |
| Controles | `App/Ui.cs` | Botones, campos, casillas, deslizadores, filas y columnas con el tema aplicado. **Toda la interfaz los usa**: un cambio aquí cambia toda la aplicación. |
| Iconos | `App/IconArt.cs` + `App/Icons.cs` | Iconos dibujados con formas en una cuadrícula de 24 × 24. Se rasterizan a los píxeles reales de la pantalla, así que siempre se ven nítidos. |
| Ayudas emergentes | `AppShell` (Tooltips) | En una aplicación, Unity no muestra el `tooltip`: la aplicación dibuja el suyo tras 0,45 s. |
| Tema | `Runtime/Workspace/Theme.cs` | Los colores por nombre (tokens). El usuario puede cambiarlos en *Entorno*. |

---

## 2. Reglas visuales

### 2.1 Colores (tokens del tema)

Nunca se escribe un color a mano: se usa `Ui.C("token")`.

| Token | Para qué |
|---|---|
| `fondo` | Fondo de la ventana y de los campos de texto |
| `panel` / `panel_alt` | Fondo de paneles / cabeceras, filas alternas, ayudas emergentes |
| `borde` | Bordes y separadores |
| `texto` / `texto_suave` | Texto principal / secundario (etiquetas, pistas) |
| `acento` | Botón principal, herramienta elegida, borde del campo con foco |
| `seleccion` | Fila o elemento seleccionado |
| `aviso` / `error` / `exito` | Estados |
| `rejilla` | Rejilla del mapa |

### 2.2 Medidas

| Medida | Valor | Dónde se define |
|---|---|---|
| Altura de control | `FontSize + 14` (27 px con letra 13) | `Ui.ControlHeight`: botones, campos, cajas de número y botones de icono **miden lo mismo**, para que queden alineados en una fila. |
| Chip (opción) | `ControlHeight − 4`, esquinas de 12 | `Ui.Chip` |
| Icono | `FontSize × 1,25` (16 px) | `Ui.IconSize` |
| Botón de icono | cuadrado de `ControlHeight` | `Ui.IconButton` |
| Separación en filas | 4-6 px | `Ui.Row(gap)` |
| Esquinas | 4 px (controles), 6 px (tarjetas) | `Ui.Radius`, `Ui.Card` |

### 2.3 Distribución (para que nada se solape)

- **Textos en una línea** (`Ui.Text` sin `wrap`): si no caben, terminan en «…». Nunca se pintan encima del vecino.
- **Botones, chips, casillas y cajas de número no se encogen** (`flexShrink = 0`). Si una fila no cabe, la fila se
  parte en varias líneas (`.Wrap()`) o el texto que crece (`.Grow()`) se recorta con «…».
- **Etiqueta + campo en la misma línea, centrados.** El campo crece y la etiqueta mantiene su ancho.
- **Caja de número = [−][valor][+] unidos**, como un solo control.
- **Pantalla de inicio de tamaño fijo** (1040 px de ancho), centrada; si la ventana es menor, se desplaza. No se
  escala con Ctrl + rueda (la escala de la interfaz se cambia dentro del proyecto o en *Entorno*).

### 2.4 Herramientas e iconos

- Las herramientas usan **iconos**, no texto: lápiz, rectángulo, relleno, goma, cuentagotas, selección, pegar, zona,
  inicio; y en Retoque también línea, rectángulo relleno y reemplazar color.
- Cada icono tiene su **ayuda emergente** con el nombre, qué hace y su atajo.
- La herramienta elegida va rellena con el color de acento.
- Las opciones que se encienden y se apagan (rejilla, vecinos) son **botones de icono que se quedan hundidos**.
- Cerrar, quitar, más y menos también son iconos.
- **Añadir un icono:** una entrada en `IconArt.Library` con sus formas en la cuadrícula de 24 × 24 (`Line`, `Quad`,
  `Poly`, `Box`, `Frame`, `Circle`, `Ring`; `Cut` recorta). Un módulo puede añadir los suyos con `IconArt.Register`.

### 2.5 Controles propios

- **Deslizador** (`Ui.Range`): pista fina, relleno de acento, bola, y el valor a la derecha («75 %»). Se arrastra desde
  cualquier punto. Mientras se arrastra, la ventana no se reconstruye.
- **Barras de desplazamiento**: 10 px de zona, píldora de 6 px redondeada, sin flechas ni pista.
- **Submenús**: `MenuItem.Submenu(nombre, hijos)`; se abren a la derecha al pasar el ratón (o a la izquierda si no caben).
- **Arrastrar para reordenar**: un asa (seis puntos) a la izquierda de la fila; una línea de acento marca dónde caerá.
- **Palabras**: «ventana» para lo que se acopla dentro de la aplicación (Mapa, Tiles, Capas...); «distribución» para
  cómo están colocadas.

### 2.6 Texto

- Tamaño base: `FontSize` (13 por defecto, ajustable en *Entorno*).
- Títulos ×1,6; encabezados ×1,15; pistas ×0,92 (color `texto_suave`).
- **Nitidez en Unity:** en la vista *Game* con «Scale» menor que 1× (por ejemplo, Full HD dentro de una ventana pequeña
  sale «0.48x»), Unity reduce la imagen y el texto se ve borroso. Para verla como será en el programa, ajusta *Game* a
  **Free Aspect** (o pon Scale en 1×) y maximízala (Shift + Espacio). En el programa compilado se ve a resolución
  nativa.

---

## 3. Registro de cambios de diseño

| Fecha | Cambio |
|---|---|
| 2026-10-02 | Iconos en las pestañas (mapa, árbol, rejilla, capas, propiedades, carpeta, eventos, aviso, base de datos, mundo, zona); botón Guardar (disquete) en la barra de menús; paleta de órdenes (Ctrl+P) con el atajo de cada acción en una «tecla». |
| 2026-10-01 (6) | Encuentros: lista de zonas, métodos en pestañas, tarjetas de especie con barra de %, horas «todas encendidas = siempre», «Dejar de pintar», resaltar todas las zonas. Encabezados de sección en mayúsculas pequeñas. |
| 2026-10-01 (5) | Rejilla como malla ajustada al píxel con sombra; ayudas con `panel.Pick`; barra del mapa con botones más grandes y botón para colocarla; «Nuevo mapa» en dos columnas con brújula y ficha resumen. |
| 2026-10-01 (4) | Barra del mapa colocable con clic derecho (por defecto a la izquierda, en vertical solo iconos); aviso de capas automáticas en Capas; chips de terreno pequeños; ayuda de los modos también en la línea inferior; etiquetas de cajas de número pegadas a su caja. |
| 2026-10-01 (3) | Mapa sin descuadres (renderizadores en el origen); tilesets de Propiedades compactos; textos largos en botones con «…»; escala por ventana (Alt + rueda / Alt + 0); atajos por categorías, grabables y con buscador. |
| 2026-10-01 (2) | Tiles compacta con iconos; Capas con iconos, asa para reordenar y deslizador propio con %; barras finas tipo píldora; importar con vista previa y tipo sugerido; menú Ver con submenús Ventanas / Distribuciones; Ctrl + 0; proyección del mapa fijada (los tiles se estrechaban al hacer zoom). |
| 2026-10-01 | Primera prueba en Unity. Arreglados: la rejilla del mapa no coincidía con los tiles (proporción de la cámara y ajuste al píxel); la interfaz desaparecía al seleccionar «Interfaz» en la Hierarchy; textos encima de botones (ahora «…»); campos más altos que los botones; caja de número unida; inicio de tamaño fijo. Nuevo: iconos en las herramientas y ayudas emergentes. |

---

## 4. Pendiente de pulir (en orden)

1. **Hojas de estilo `.uss` + UI Builder.** Pasar medidas, estados (hover, pulsado, foco, desactivado) y clases
   (`ct-boton`, `ct-chip`, `ct-campo`…) a `Resources/CTEditorApp/CTEditor.uss`. Los colores del tema irán como variables
   USS. Así el diseño se toca desde UI Builder sin compilar, y el hover deja de hacerse por código.
2. **Plantillas `.uxml`** para las pantallas fijas (inicio, diálogos de corte, nuevo mapa, ajustes): se editan
   arrastrando en UI Builder.
3. **Fuente propia** con más pesos (Inter o Noto Sans): títulos más finos y números tabulares en las cajas.
4. **Barra de herramientas vertical** a la izquierda del mapa (como Aseprite y Tiled), con grupos.
5. **Casillas y deslizadores propios**: la casilla de Unity con el tema oscuro aún se ve grande y blanca.
6. **Estados vacíos con ilustración** (mapa sin tileset, proyecto sin mapas) y animaciones suaves (150 ms) al abrir
   paneles y menús.
7. **Iconos en los paneles y en el árbol de mapas** (pueblo, ruta, cueva, interior), y en las pestañas.
8. **Revisión de contraste** del tema claro y del de alto contraste con los iconos nuevos.
