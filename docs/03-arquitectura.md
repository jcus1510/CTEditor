# 03 · Arquitectura (DDD)

El proyecto sigue **Domain-Driven Design estricto**: la lógica vive en dominios puros (C# sin Unity) separados en
contextos; Unity solo aporta el almacenamiento (ScriptableObjects), la interfaz y el editor.

## Contextos y ensamblados (asmdef)

| asmdef | Carpeta | Depende de | Qué contiene |
|---|---|---|---|
| `CTEditor.SharedKernel` | `Runtime/SharedKernel` | — | Tipos base sin significado de juego ([04](04-nucleo-compartido.md)). |
| `CTEditor.GameDefinition.Domain` | `Runtime/GameDefinition/Domain` | SharedKernel | El CONTENIDO puro: definiciones inmutables ([05](05-definicion-del-juego.md)). |
| `CTEditor.GameContracts` | `Runtime/GameContracts` | SharedKernel | Contratos delgados entre contextos. |
| `CTEditor.Party.Domain` | `Runtime/Party/Domain` | SharedKernel, GameDefinition.Domain | Individuos y equipo ([07](07-equipo-y-progresion.md)). |
| `CTEditor.Battle.Domain` | `Runtime/Battle/Domain` | SharedKernel, GameDefinition.Domain | Motor de combate ([06](06-combate.md)). **No conoce Party.** |
| `CTEditor.Eventing.Domain` | `Runtime/Eventing/Domain` | SharedKernel, GameDefinition.Domain, GameContracts | Guiones de efectos del mundo. |
| `CTEditor.Art.Domain` | `Runtime/Art/Domain` | SharedKernel | Imagen de píxeles, corte (`SliceDefinition`), hojas de personaje, herramientas de píxeles y sus comandos, `IImageRepository`. |
| `CTEditor.World.Domain` | `Runtime/World/Domain` | SharedKernel, Art.Domain | El MUNDO: tilesets (id fijo, piezas), terrenos, mapas/tramos (varios tilesets, capas con papel, objetos), mundo continuo (`WorldLayout`), encuentros (`EncounterArea`, métodos, `EncounterResolver`), mapa de la región, herramientas y comandos, reglas de paso, el jugador andando entre tramos, repositorios. |
| `CTEditor.Editing` | `Runtime/Editing` | SharedKernel, Art.Domain, World.Domain | Capa de APLICACIÓN del editor: casos de uso `MapEditorSession` y `PixelEditorSession` (estado compartido por los paneles, deshacer, guardar). |
| `CTEditor.Project` | `Runtime/Project` | Art.Domain, World.Domain | INFRAESTRUCTURA de la carpeta de proyecto: `proyecto.json`, JSON propio, PNG, catálogo de «graficos/», archivo de corte y los repositorios (`JsonMapRepository`, `FolderTilesetRepository`, `PngImageRepository`). |
| `CTEditor.Workspace` | `Runtime/Workspace` | Project | Entorno de trabajo del usuario: tema, paneles, atajos (con registro para módulos nuevos). |
| `CTEditor.App` | `App` | todo lo anterior + Input System | La aplicación (UI Toolkit): solo dibuja y reenvía el ratón y el teclado a las sesiones ([20](20-aplicacion.md)). |
| `CTEditor.Adventure.Domain` | `Runtime/Adventure/Domain` | SharedKernel, GameDefinition.Domain, Party.Domain, Battle.Domain | La partida: orquesta combate y equipo ([08](08-aventura.md)). |
| `CTEditor.GameDefinition` | `Runtime/GameDefinition/Infrastructure` | GameDefinition.Domain, SharedKernel, Adventure.Domain | ScriptableObjects y mappers ([10](10-infraestructura-unity.md)). |
| `CTEditor.GameDefinition.Editor` | `Runtime/GameDefinition/Editor` | todo lo anterior (menos Eventing/Contracts) | El editor (solo plataforma Editor) ([11](11-editor.md)). |
| `CTEditor.Bootstrap` | `Bootstrap` | todos los dominios + Infrastructure, uGUI, TextMeshPro, InputSystem | Escena, UI y composition root ([14](14-escena-e-interfaz.md)). |
| `CTEditor.Bootstrap.Editor` | `Bootstrap/Editor` | Bootstrap, Infrastructure, Editor, Adventure, uGUI, TMP | Herramientas de escena. |
| `CTEditor.Tests.EditMode` | `Tests/EditMode` | todo menos Bootstrap + TestRunner | Tests ([15](15-pruebas-y-verificacion.md)). |

```
                    SharedKernel
                         ▲
        ┌────────────────┼──────────────────┐
 GameDefinition.Domain   GameContracts       │
   ▲     ▲     ▲             ▲               │
   │   Party  Battle      Eventing           │
   │     ▲     ▲                             │
   │     └─ Adventure ◄── Infrastructure ◄── Editor
   │                           ▲               ▲
   └────────────────────── Bootstrap ──────────┘ (y Bootstrap.Editor)
```

**Reglas de oro**
- Los `*.Domain` **no referencian Unity** (`noEngineReferences` en la práctica: no usan `UnityEngine`). Así se compilan y
  prueban con .NET fuera de Unity.
- **Battle no conoce Party**: recibe *fotos* (`BattleParticipant`) y devuelve un `BattleResult`; la Aventura aplica los
  resultados al equipo.
- Un contexto solo habla con otro por sus contratos (GameContracts) o a través del orquestador (Adventure/Bootstrap).

## Flujo de datos

```
AUTOR ──► ventanas del Editor / Excel ──► ScriptableObject *Data (.asset en GameContent/Resources)
                                                   │
                                           Mapper (ACL, Infrastructure/Acl)
                                                   ▼
                                      objeto de DOMINIO inmutable (Species, Move, ItemDefinition...)
                                                   │
                            GameData (Adventure) = todos los catálogos + Ruleset + sets
                     ┌─────────────────────────────┼──────────────────────────────┐
             JUEGO: ContentLibrary        EDITOR: EditorGameData.Get()       TESTS: MemoryCatalog
             (Resources, en Play)         (sin Play; «Sugerir», revisar)     (contenido en código)
                                                   ▼
                     BattleSession ──► TurnResolver (Battle) ──► eventos ──► BattleScreen (UI)
```

## Patrones que se repiten

| Patrón | Dónde | Por qué |
|---|---|---|
| **ACL (Anti-Corruption Layer)** | `Infrastructure/Acl/*Mapper` | La ficha de Unity es cómoda para el autor; el dominio es uniforme y validado. El mapper traduce y es el único punto de contacto. |
| **Catálogo por id** (`ICatalog<T>`) | Domain/Catalog, `MemoryCatalog`, `ScriptableObjectCatalog`, `ResourcesContentCatalog` | Las definiciones se referencian por `Id<T>` (no por objeto); el catálogo resuelve el id. Da igual si el contenido viene de assets, CSV o tests. |
| **Estrategias inyectables** | `IDamageFormula`, `ICatchFormula`, `IXpFormula`, `IStatGrowthFormula`, `IBattleAI`, `IRng`, `IClock` | Reglas intercambiables sin tocar el motor; tests deterministas con semilla. |
| **Eventos de dominio** | `Battle/Domain/Events`, `Adventure/SessionEvents` | El dominio resuelve al instante y devuelve una LÍNEA DE TIEMPO de eventos; la UI la reproduce a su ritmo. |
| **Agregados con portero** | `Party`, `Battle`, `EffortValues` | Las invariantes (tope de equipo, topes de EVs) se validan en una sola puerta. |
| **Partial classes por tema** | `TurnResolver.*.cs`, `Combatant.*.cs`, `ContentValidator.*.cs` | El resolvedor es grande: cada archivo agrupa un tema (Gen4, Gen6, Formas, Mega, Items). |
| **Template Method** | `ContentEditorWindow<T>` | Todas las ventanas de contenido comparten lista, buscador, crear/duplicar/borrar; cada una rellena lo suyo. |
| **Datos antes que código** | Estados, habilidades, objetos (bloques), climas, trampas, efectos de lado | Añadir contenido no requiere programar; el motor interpreta piezas genéricas. |

## Dónde poner algo nuevo (guía rápida)

| Quiero... | Va en... |
|---|---|
| una regla nueva del juego (perilla) | `Ruleset` / `GenerationRules` / `AdventureRules` (dominio) + `RulesetData` + `RulesetMapper` + editor de Reglas + `Etiquetas`. |
| un efecto nuevo de objeto | `EffectAction` (al FINAL del enum) + ejecución en `TurnResolver.Items` (o Party/Adventure si es fuera de combate) + `EffectText` (etiqueta, parámetros, frase, clave Excel) + `EffectRules.IsSupported`. Ver [09](09-efectos-por-bloques.md). |
| una categoría de contenido nueva | dominio + `*Data` + mapper + `ContentFolders` + ventana (`ContentEditorWindow<T>`) + `EditorCatalog` + esquema CSV + validador + catálogo en `GameData`/`ContentLibrary`/`EditorGameData`. |
| una mecánica especial (Z, Dinamax...) | `MechanicKind` + ajustes en `MechanicDefinition` + `MechanicData`/mapper + un partial `TurnResolver.<Mecánica>.cs` + UI de combate. |


## Aplicación: capas y revisión DDD

```
  App (UI Toolkit, Unity)              ← presentación: paneles, dibujo, ratón y teclado
    │ llama a
  Editing (casos de uso)               ← MapEditorSession, PixelEditorSession: estado, deshacer, guardar
    │ usa                    ▲ implementa las interfaces del dominio
  World.Domain · Art.Domain            ← reglas: mapas, tiles, paso, movimiento, píxeles (sin Unity, sin archivos)
    ▲
  Project (infraestructura)            ← JSON, PNG, carpetas: JsonMapRepository, FolderTilesetRepository...
  SharedKernel                         ← CommandHistory / IEditCommand (deshacer común), Id, eventos
```

**Revisión (fase 3) — qué se corrigió para que escale:**
- Las propiedades de tile, los terrenos y las reglas de paso estaban en `Project` (infraestructura). Ahora están en
  `World.Domain`; `Project` solo traduce JSON ↔ dominio.
- La interfaz leía y escribía archivos directamente. Ahora pasa por los casos de uso (`Editing`) y los repositorios:
  el dominio define `IMapRepository`, `ITilesetRepository` e `IImageRepository`; `Project` los implementa. Cambiar
  JSON por otra cosa (o usar memoria en los tests) no toca nada más.
- Deshacer era por editor; ahora hay un historial común (`SharedKernel.Editing.CommandHistory`) y cada módulo aporta
  sus comandos (`TilePaintCommand`, `MapStructureCommand`, `TileAttributeCommand`, `PixelPaintCommand`).
- Listas cerradas convertidas en registros: `PanelCatalog.Register`, `ShortcutMap.RegisterAction`,
  `PanelRegistry.Register` (App) y `TerrainCatalog.Register`. Un módulo nuevo se añade sin tocar el código existente.

**Cómo añadir un módulo nuevo (p. ej. eventos, fase 7):**
1. Dominio puro en `Runtime/<Contexto>/Domain` (modelo, reglas, comandos deshacibles, interfaz de repositorio).
2. Caso de uso en `Runtime/Editing` (sesión con eventos que los paneles escuchan).
3. Repositorio en `Runtime/Project` (archivos del proyecto).
4. Panel en `App/Panels` registrado con `PanelCatalog.Register` + `PanelRegistry.Register`; acciones con
   `ShortcutMap.RegisterAction`.
5. Tests del dominio y del caso de uso (sin Unity).
