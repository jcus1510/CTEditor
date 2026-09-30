# 10 · Infraestructura Unity (fichas, mappers, catálogos)

`Runtime/GameDefinition/Infrastructure` (asmdef `CTEditor.GameDefinition`) — lo único del contenido que conoce Unity.

## Fichas (`ScriptableObjects/*Data`)

Cada categoría de contenido es un `ScriptableObject` que implementa `IContentAsset` (`Id`, `DisplayName`). Es CÓMODA
para el autor (campos con nombre, listas, desplegables) y la traduce su mapper al dominio.

| Ficha | Carpeta (`Resources/…`) | Mapper → dominio |
|---|---|---|
| `SpeciesData` (+ `FormEntry`, `FormChangeEntry`, `LearnableMoveEntry`, `EvolutionEntry`, `EvolutionConditionData`...) | `Species` | `SpeciesMapper` → `Species` |
| `MoveData` (+ `MoveEffectData`) | `Moves` | `MoveMapper` → `Move` |
| `ElementTypeData` / `TypeChartData` (`MatchupEntry`) | `Types` | `ElementTypeMapper`, `TypeChartMapper` |
| `StatusConditionData` | `Status` | `StatusMapper` |
| `AbilityData` | `Abilities` | `AbilityMapper` |
| `ItemData` (+ `EffectBlockData`) | `Items` | `ItemMapper` (bloques de efecto) |
| `NatureData`, `GrowthCurveData`, `EggGroupData` | `Natures`, `Curves`, `EggGroups` | `NatureMapper`, `GrowthCurveMapper` |
| `WeatherData`, `HazardData`, `SideConditionData` | `Weathers`, `Hazards`, `SideConditions` | `WeatherMapper`, `HazardMapper`, `SideConditionMapper` |
| `RulesetData` | `Rulesets` | `RulesetMapper` (con `Func<string, MechanicDefinition>`) |
| `MechanicData` | `Mechanics` | `MechanicMapper` |
| `TrainerData`, `TeamPresetData`, `EncounterZoneData` (+ `TeamMemberData`) | `Trainers`, `Teams`, `Encounters` | `TrainerMapper` |
| `AiLevelData` | `AiLevels` | `AiLevelMapper` → `AiProfile` |
| `CompetitiveSetData` | `Sets` | `CompetitiveSetMapper` |
| `MenuData`, `InterfaceSettingsData` | `Menus`, `Interface` | `InterfaceMapper` |
| `ConditionData`, `PowerModifierData` | (dentro de otras) | `ConditionMapper` |

Las carpetas están en `Catalog/ContentFolders.cs` (**única fuente de verdad**, raíz `Assets/GameContent/Resources`).

### Referencias entre fichas
- Por **objeto** (`SpeciesData species`) **con id de respaldo** (`speciesId`): si la ficha se borra y se recrea con el
  mismo id, `ReferenceRelinker` (editor) vuelve a enlazar. `TeamMemberData.SpeciesKey`, `MoveKeys()`, `NatureKey`
  devuelven la referencia viva o el id guardado.
- Por **id de texto** con desplegable: `[ContentIdReference(typeof(X))]`, `[StatusIdReference]`, `[StatIdReference]`.

### Atributos

## Mappers (`Acl/`)

Traducción de UNA dirección, ficha → dominio. Resuelven referencias a ids, colapsan campos cómodos en estructuras
uniformes, validan y ponen valores por defecto. Son el único punto de contacto Unity ↔ dominio. Si un mapper lanza, la
ficha se salta (el validador lo reporta).

## Catálogos (`Catalog/`)

- `ScriptableObjectCatalog<TData, TDomain>`: mapea una colección de fichas UNA vez y resuelve por id.
- `ResourcesContentCatalog`: catálogo perezoso respaldado por una carpeta de `Resources`.
- `ContentFolders`: rutas.

## Carga en el juego

`Bootstrap/ContentLibrary` (estático, autocargable): `ContentLibrary.Moves`, `.Species`, `.Items`, `.Ruleset`... cargan
de `Resources/<carpeta>` la primera vez. `ContentLibrary.MechanicById` resuelve las mecánicas del Ruleset. De ahí sale el
`GameData` del juego.

## En el editor

`EditorGameData.Get()` arma el mismo `GameData` desde `ContentAssets.LoadAll<T>()` (sin Play), cacheado por
`ContentAssets.Version` (se invalida al cambiar fichas: `ContentAssetsWatcher`). Las fichas en la **Papelera** no cuentan.
