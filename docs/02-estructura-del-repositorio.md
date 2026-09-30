# 02 · Estructura del repositorio

```
CTEditor/
├─ Assets/
│  ├─ CTEditor/                       ← TODO el código
│  │  ├─ Runtime/                     ← el motor y el editor (un asmdef por contexto, ver cap. 03)
│  │  │  ├─ SharedKernel/             Id<T>, Chance, Percentage, Result, IRng, IClock, eventos de dominio
│  │  │  ├─ GameDefinition/
│  │  │  │  ├─ Domain/                definiciones PURAS (sin Unity): Species, Move, Types, Status, Abilities,
│  │  │  │  │                         Items, Effects, Rules, Conditions, Weather, Hazards, Battlefield, Trainers,
│  │  │  │  │                         Growth, Stats, Encounters, Catalog, Formulas (MathExpression)
│  │  │  │  ├─ Infrastructure/        Unity: ScriptableObjects (*Data), Acl (*Mapper), Catalog (carpetas, catálogos)
│  │  │  │  └─ Editor/                solo en el Editor de Unity: Windows/, Common/, Csv/, Tools/, validador
│  │  │  ├─ Battle/Domain/            motor de combate: Battle, Combatant, Turn/TurnResolver*, AI/, Events/, Formulas/
│  │  │  ├─ Party/Domain/             individuos, equipo, mochila, XP, EVs, evoluciones, usar objetos
│  │  │  ├─ Adventure/Domain/         la partida: GameData, BattleSession, TeamBuilder, TrainerBrain, FieldActions,
│  │  │  │                            PlayerSave, AiTournament, RulesReview, Interface/ (menús, input, texto)
│  │  │  ├─ Eventing/Domain/          EventScript + EffectDispatcher (efectos del mundo)
│  │  │  ├─ Art/Domain/               imagen de píxeles, corte de tilesets y hojas de personaje (aplicación)
│  │  │  ├─ Project/                  carpeta de proyecto: proyecto.json, JSON, PNG, catálogo, corte (aplicación)
│  │  │  ├─ Workspace/                entorno de trabajo: tema, paneles, atajos (aplicación)
│  │  ├─ App/                        la APLICACIÓN CTEditor (UI Toolkit): ventana, paneles, recursos, corte ([20](20-aplicacion.md))
│  │  │  └─ GameContracts/            contratos entre contextos (IPartyCommands, IInventoryCommands)
│  │  ├─ Bootstrap/                   escena y juego: GameBootstrap, ContentLibrary, BattleScreen, BattleLab,
│  │  │                               PartyHolder, Interface/ (UI), GameFlow/, Platform/ (EventBus, SystemRng)
│  │  │  └─ Editor/                   constructores de escena, torneo de IAs, mapa de menús, inspectores de escena
│  │  └─ Tests/EditMode/              tests NUnit (38 archivos)
│  └─ GameContent/
│     ├─ Resources/<Categoría>/       las FICHAS del proyecto (.asset): Species, Moves, Items, Types, Rulesets...
│     ├─ Papelera/                    fichas «borradas» (recuperables); grupos «~nombre» (cambios de generación)
│     ├─ Packs/Gen1 … Gen7/           un pack por generación: hojas CSV + INFORME.txt
│     └─ README.md                    MANUAL DEL AUTOR
├─ Excel/                             carpeta por defecto de exportación CSV (fuera de Assets, Unity no la importa)
│  └─ copias/                         copias de seguridad automáticas (importaciones, cambios de generación)
├─ Tools/
│  ├─ verificar_pack/                 Python: generar_packs.py, formas.py, generar_sets.py, generar_base.py,
│  │                                  verificar_pack.py, pokeapi.py, csvlib.py
│  ├─ datos_fuente/                   datos de apoyo para los generadores
│  ├─ probar_dominio/                 ProbarDominio.csproj → dominio + tests puros con .NET
│  ├─ compilar_unity/                 CompilarUnity.csproj → TODO Runtime (incluido el editor) + tests con las DLL de Unity
│  └─ README.md                       manual de las herramientas
├─ docs/                              ESTA documentación
├─ Packages/, ProjectSettings/        de Unity (versión en ProjectSettings/ProjectVersion.txt)
└─ My project.slnx                    solución generada por Unity
```

## Qué NO se versiona (`.gitignore`)

- `Library/`, `Temp/`, `Logs/`, `obj/`, `Build*/`, `UserSettings/` (de Unity).
- `*.csproj` y `*.sln` generados por Unity. **Excepciones**: `Tools/probar_dominio/*.csproj` y
  `Tools/compilar_unity/*.csproj` (negaciones `!/Tools/.../*.csproj`), y sus `bin/` y `obj/` sí se ignoran.
- `Tools/.cache/` (descargas de PokeAPI y Smogon: se regeneran solas).

## Archivos `.meta`

Unity identifica cada archivo por el GUID de su `.meta`. **Todo archivo nuevo dentro de `Assets/` necesita su `.meta`**
(si se crea fuera de Unity, se genera a mano con un GUID nuevo; ver [16](16-convenciones.md)). Mover un archivo con su
`.meta` conserva las referencias; perder el `.meta` las rompe.
