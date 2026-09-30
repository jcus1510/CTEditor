# 11 · El editor

`Runtime/GameDefinition/Editor` (asmdef `CTEditor.GameDefinition.Editor`, solo Editor) y `Bootstrap/Editor`.
Todo en español; los textos de campos y opciones salen de `Common/Etiquetas.cs` (ver [16](16-convenciones.md)).

## Menú `CTEditor/`

| Categoría | Ventanas |
|---|---|
| **Centro de Contenido** | Puerta de entrada: catálogo de todos los editores con recuento, elegir **pack** e **importar** («solo lo que falta» / «actualizar también»), «✨ Crear TODO» (contenido clásico), errores del validador. |
| **Criaturas** | Especies (stats con barras, calculadora de stats reales, formas y variantes, Pokédex) · **Árbol de familia** (evoluciones, formas ⚔, variantes punteadas, «+ Forma», «+ Variante») · Habilidades (efectos por bloques en tarjetas; plantillas = las habilidades del pack) · Naturalezas (tabla 5×5) · Grupos huevo · Curvas de experiencia (gráfico, fórmulas propias). |
| **Combate** | Movimientos (plantillas por mecánica) · Tipos + **Tabla de tipos** (matriz, por épocas) · Estados · Climas · Trampas de campo · Efectos de lado · **Mecánicas especiales** (Megaevolución, Movimientos Z; «Crear las clásicas» crea las dos) · **Reglas del juego** (plantillas por generación, mecánicas activas, aventura). |
| **Objetos** | Todos los objetos (efectos por bloques, [09](09-efectos-por-bloques.md)); plantillas = los objetos del pack o Excel elegido como fuente. |
| **Personajes** | Entrenadores (equipo, IA, mochila, frases, «✨ Sugerir según la IA», «🏆 Set de Smogon…», Showdown) · Plantillas de entrenadores (del pack) · Niveles de IA (7 clásicos) · Sets de competición · Equipos prearmados. |
| **Mundo** | Zonas salvajes (con % real de aparición). |
| **Interfaz** | Menús (vista previa jugable) · Controles y caja de texto · Mapa de menús (Bootstrap.Editor). |
| **Herramientas** | Excel y compartir (CSV) · Validar contenido · Papelera · **Cambiar de generación** · Reenlazar referencias por id. |
| **Pruebas** | Calculadora de daño · Simulador de combate · Torneo de IAs · escena de pruebas de combate (BattleLab). |

`Common/EditorCatalog.cs` registra cada ventana (categoría, orden, título, icono, descripción, color, cómo abrirla,
contador, palabras clave). Las rutas de menú y órdenes están en `EditorMenus` (`Root = "CTEditor/"`).

## Piezas comunes (`Common/`)

| Pieza | Qué hace |
|---|---|
| `ContentEditorWindow<T>` | Ventana base (Template Method): buscador, filtros, orden, lista con marca de color, crear por id en su carpeta, duplicar, borrar (a la papelera), «¿quién usa esto?», plantillas (`Templates`/`ApplyTemplate`), creación en bloque (`DrawBulkPresets`), `DrawPresets`, inspector embebido, `DrawPreview`, guía rápida, zoom. `EditSelected(so => ...)` edita con Deshacer. |
| `SpanishInspector` (+ `SpanishInspectors`) | Inspector en español para todas las fichas: etiquetas de `Etiquetas`, opciones de enum traducidas, oculta lo que no aplica (`IsVisible`), secciones de color, ayuda bajo cada campo. Ganchos: `DrawsItself(nombre)`, `DrawCustom()` (los usa `ItemDataInspector`). |
| `ContentAssets` | `LoadAll<T>`, `FindById<T>`, `Label`, `Create/CreateIfMissing/CreateOrRepair<T>`, `Edit(asset, so => ...)`, `EnsureFolder`, `Version`/`ClearCache`. `StatLabels` (nombres de estadísticas). |
| `ContentTrash` + `TrashWindow` | **Papelera**: borrar = mover a `Assets/GameContent/Papelera/<Categoría>` (mismo GUID → al recuperar vuelven todas las referencias). **Grupos** `~<nombre>` (cambio de generación): recuperar o borrar el grupo entero. «Pasar sus referencias a la nueva» si ya existe otra con el mismo id. |
| `ContentValidator` (+ `.Advanced`, `.Adventure`, `.Interface`, `.Progression`) + `ContentValidationWindow` | Errores y avisos de TODO el contenido (referencias rotas, equipos imposibles, objetos sin efecto, sets, reglas...), con botón «Ver». |
| `ReferenceFinder` / `ReferenceRelinker` | «¿Quién usa esto?» (por objeto y por id, sin confundir familias) y reenlazar referencias rotas por id. |
| `EditorGameData` | `GameData` del editor (cap. [10](10-infraestructura-unity.md)). |
| `TeamPreview` | Tarjetas de un equipo (tipos, stats estimadas con EVs, movimientos con los que saldrá, avisos). |
| `ConditionText` / `ConditionDataDrawer` | Condiciones como frase editable y como código corto para Excel. |
| `EffectText` / `EffectBlocksGui` / `ItemInspector` / `ItemEffectsEditing` | Efectos por bloques ([09](09-efectos-por-bloques.md)). |
| `FormEditing`, `EvolutionText` | Formas/variantes y evoluciones en frases + plantillas. |
| `ClassicMovePresets`, `ClassicStatusPresets`, `TypeChartTools` | Plantillas clásicas. |
| `PackTools` | Importar / actualizar desde el pack elegido o una carpeta de Excel. |
| `EditorTheme`, `EditorZoom`, `LineChart` | Estilo (colores por categoría, chips, secciones, consejos), zoom 80-160 %, gráficos. |

## Asistentes y herramientas destacadas

- **Cambiar de generación** (`Windows/GenerationWizardWindow`): pack → lista de lo que cambia → aplicar (copia en
  `Excel/copias`, sobrantes a la papelera en grupo / borrar / dejar, importar el pack, limpiar la tabla de tipos,
  plantilla de reglas `RulesetEditorWindow.GenerationPreset(gen)`) → **Adaptar a las reglas** (`RulesReview`: cada
  conflicto «arreglar» o «dejar y avisar»). Nunca borra entrenadores, equipos ni zonas del autor.
- **Showdown** (`ShowdownWindow`): importar/exportar equipos en el formato estándar (nombres en inglés `englishName`).
- **Sets** (`SetsEditorWindow`, `SetPickerWindow`): sets de Smogon del pack y selector por miembro.
- **Plantillas de entrenadores** (`TrainerTemplatesWindow`): entrenadores del pack como plantillas con vista previa.
- **Calculadora de daño**, **Simulador de combate** (N combates con el motor real), **Torneo de IAs**.

## Bootstrap.Editor

`BattleLabSceneBuilder` (crea la escena de pruebas ya cableada), `SceneMenuBuilder` (crea menús editables en la escena:
clic derecho → CTEditor UI), `MenuFlowWindow` (mapa de menús), `AiTournamentWindow`, inspectores de escena.

## Rendimiento del editor (reglas para que no se vuelva lento)

Con cientos de especies, movimientos y objetos, lo que se hace en cada repintado (`OnGUI`) se multiplica. Reglas:

1. **Nada de buscar en disco al repintar**: siempre `ContentAssets.LoadAll<T>()` / `FindById<T>()` (caché por tipo e
   índice por id; se vacían solas al importar, mover o borrar assets). Nunca `AssetDatabase.FindAssets` en un bucle.
2. **La validación completa está en caché** (`ContentValidator.IssuesFor` / `ValidateCached`): se rehace solo si cambió
   la lista de fichas (`ContentAssets.Version`) o sus datos (`ContentAssets.EditStamp`, que sube con
   `ContentAssets.Edit` y al editar en el inspector de las ventanas). Elegir otra ficha ya no valida todo el proyecto.
3. **«¿Quién usa esto?» en una sola pasada**: `ReferenceFinder.FindReferencesToAny(fichas)` recorre el proyecto UNA vez
   para muchas fichas (la papelera, el validador) con un «plan» por tipo calculado una sola vez.
4. **Listas virtuales**: `ContentEditorWindow` solo dibuja las filas visibles y prepara nombres/marcas al buscar o
   filtrar, no en cada repintado.
5. **No repintar al mover el ratón** salvo las ventanas con gráficos que lo necesitan (`RepaintOnMouseMove`).
6. Todo lo que se calcule para dibujar (opciones de desplegables, planes de movimientos, recuentos) se guarda y se
   invalida con `ContentAssets.Version` / `EditStamp`, nunca por tiempo.
7. **Relaciones entre fichas en caché**: el Árbol de familia calcula padres, bases, variantes y etiquetas de cadena una vez
   por cambio de contenido (diccionarios), no buscando en toda la lista en cada evento (eso era O(n²)-O(n³)).
