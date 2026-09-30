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

## Qué hay (fase 2)

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
| Resto de paneles | `Panels/PanelRegistry.cs` | Mapa, Mapas, Tiles, Capas, Propiedades, Juego (fase 3), Retoque (4), Eventos (7), Base de datos (10): de momento explican cuándo llegan. Avisos: el registro. |

## Reglas

- **Una sola ventana**: una aplicación de Unity en Windows no abre ventanas nativas sueltas; los paneles se acoplan,
  se redimensionan y los diálogos flotan dentro de la ventana.
- **Sin emoji ni símbolos raros** en los textos: la fuente de UI Toolkit puede no tenerlos (se verían cuadrados).
  Usar letras, `×`, `·`, `«»`, `—`, `•`.
- La lógica (corte, JSON, proyecto, paneles, atajos) vive en los dominios puros (`Art`, `Project`, `Workspace`) con
  tests; la aplicación solo dibuja y reenvía clics.
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
