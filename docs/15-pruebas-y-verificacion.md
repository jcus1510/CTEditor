# 15 · Pruebas y verificación

## Tests (`Assets/CTEditor/Tests/EditMode`, NUnit)

38 archivos, ~300 tests. Casi todos son **dominio puro**: construyen contenido en código (`MemoryCatalog` /
`InMemoryCatalog`), usan `SeededRng` (determinista) y comprueban eventos y estado. Temas: combate completo
(`CombatClosureTests`, `LoteABTests`, `Gen4MechanicsTests`, `LoteFTests`, `SpecialDamageTests`, `HazardTests`),
objetos (`ItemTests`, `EffectBlockTests`, `EffectTextTests`), formas y megas (`FormTests`, `MegaTests`), reglas de
generación (`GenerationRulesTests`), progresión (`GeneticsTests`, `GrowthCurveAndXpTests`, `XpAwardTests`,
`EvAwardTests`, `EvolutionConditionTests`), partida (`AdventureTests`, `FieldActionsTests`), IA (`TrainerAiTests`,
`CompetitiveSetTests`), Showdown, CSV, interfaz, `RulesReviewTests`, `DamagePreviewTests`...

## Tres formas de ejecutarlos

| Cómo | Qué cubre | Cuándo |
|---|---|---|
| **Unity → Test Runner (EditMode)** | Todo. | Referencia final, con Unity abierto. |
| `dotnet test Tools/probar_dominio` | Dominio puro + tests puros (excluye los que usan editor o fichas; lista `Exclude` en el `.csproj`). Hoy: **311 / 311**. | Tras tocar el motor, en segundos. |
| `dotnet test Tools/compilar_unity` | **Compila TODO Runtime (incluido el editor)** contra las DLL de Unity (NuGet `Unity3D.SDK`) y ejecuta TODOS los tests. Hoy: **390 pasan; 9 fallan siempre fuera de Unity** (`GrowthCurveMapperTests` ×4, `ReferenceFinderTests` ×4, `ConsoleBattleTests` ×1: necesitan el motor de Unity). Cualquier otro fallo es real. | **Antes de cada commit que toque el editor.** |
| `dotnet build Tools/compilar_app` | Compila la **aplicación** (`Assets/CTEditor/App`) y sus dominios contra las DLL de Unity 2021.3 (NuGet `UnityEngine.Modules`, con UI Toolkit en tiempo de ejecución). Ver [20](20-aplicacion.md). | Antes de cada commit que toque la aplicación. |

Requisito: .NET 8 SDK (`apt-get install -y dotnet-sdk-8.0` en Debian/Ubuntu). La primera vez se descargan los paquetes.

### ¿Por qué `compilar_unity` es imprescindible?

Si el código del editor NO compila, Unity **se queda con el último ensamblado que sí compiló** y parece que los cambios
«no han llegado». Ocurrió con `Handles.DrawDashedLine` (no existe): durante varios pasos Unity usó código viejo
(`sets.csv` se leía como «Especies»). Las DLL de referencia son de Unity 2021: una API exclusiva de Unity 6 daría un
falso error (hoy no pasa en Runtime; en Bootstrap sí: `FindObjectsByType`, etc., por eso Bootstrap no se incluye).

### Comprobar Bootstrap (opcional)

Bootstrap usa uGUI y TextMeshPro, que no están en esas DLL. Se puede compilar con *stubs* mínimos de `UnityEngine.UI`
y `TMPro` en un proyecto aparte; los únicos errores esperables son las APIs de Unity 6 (`FindObjectsByType`,
`FindAnyObjectByType`, `GetComponentInParent(bool)`, `TMP_FontAsset.HasCharacter`).

## Datos

- `python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/GenN [--pokeapi --gen N]` → 0 errores.
- En Unity: **Herramientas → Validar contenido** (errores y avisos del proyecto).
- Equilibrio: **Pruebas → Simulador de combate** y **🏆 Torneo de IAs** (cada nivel debe ganar al anterior).

## Lista antes de un commit

1. `dotnet test Tools/probar_dominio` → todo verde.
2. `dotnet test Tools/compilar_unity` → solo los 9 fallos conocidos.
3. Si se tocaron packs: `verificar_pack.py` → 0 errores.
4. Archivos nuevos en `Assets/` con su `.meta`.
5. Releer el diff: enums solo crecen al final; textos de interfaz en español; identificadores en inglés.
