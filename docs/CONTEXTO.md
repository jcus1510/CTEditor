# CTEditor — Documento de contexto (para migrar o retomar el proyecto)

> Qué es, cómo está hecho, qué se ha construido, cómo se verifica y qué falta. Pensado para que otra persona (u otra
> sesión de IA) pueda continuar sin el historial de conversaciones. Última actualización: tras el **paso 7** (asistente
> «Cambiar de generación»).

---

## 1. Qué es

**CTEditor** es un **motor + editor** para hacer juegos de combate por turnos al estilo Pokémon dentro de **Unity
6 (6000.3.7f1)**. Todo el texto de la interfaz, el código comentado y las hojas de datos están **en español**.

**Premisa central: TODO es editable/personalizable.** Nada debe quedar atado a una generación concreta: las
generaciones son *datos* (packs) y *reglas* (perillas del Ruleset), nunca código especial. Los datos de fábrica
(Gen1…Gen6) se generan desde PokeAPI y Smogon, pero el autor puede cambiarlo todo o crear contenido nuevo.

- Repositorio: `jcus1510/CTEditor`. Rama de trabajo: **`develop`** (`git push -u origin develop`).
- No se crean PR salvo que se pida.
- Commits: mensaje en español, descriptivo; con trailers `Co-Authored-By:` / `Claude-Session:` cuando los hace Claude.

## 2. Mapa del repositorio

```
Assets/
  CTEditor/
    Runtime/                    ← el código del juego (DDD, un asmdef por contexto)
      SharedKernel/             Id<T>, Chance, Percentage, Result, IRng, IClock, IDomainEvent/IEventBus
      GameDefinition/
        Domain/                 Definiciones PURAS: especies, movimientos, tipos, estados, objetos, habilidades,
                                reglas, fórmulas, condiciones, climas, trampas, entrenadores, sets…
        Infrastructure/         Unity: ScriptableObjects (*Data), mappers ACL (*Mapper), catálogos
        Editor/                 Ventanas del editor (Windows/), comunes (Common/), Excel (Csv/), pruebas (Tools/)
      Battle/Domain/            Motor de combate: Battle, Combatant, TurnResolver (partial), IA, eventos, fórmulas
      Party/Domain/             Monstruos del jugador: MonsterInstance, MonsterFactory, Party, Bag, experiencia, EVs
      Adventure/Domain/         Orquestación: GameData, BattleSession, TeamBuilder, TrainerBrain, FieldActions,
                                RulesReview, torneo de IAs, guardado
      Eventing/Domain/          EventScript, EffectDispatcher (eventos del mundo)
      GameContracts/            Comandos entre contextos (IPartyCommands, IInventoryCommands)
    Bootstrap/                  Arranque en escena: ContentLibrary (carga desde Resources), BattleScreen, BattleLab,
                                menús, input, flujos (captura → equipo…); Editor/ = constructores de escena, torneo
    Tests/EditMode/             Tests NUnit (EditMode). La mayoría corren también fuera de Unity (§8)
  GameContent/
    Resources/<Categoría>/      Las fichas (.asset) del proyecto: Species, Moves, Items, Types, Rulesets, …
    Resources/…/Papelera        (Assets/GameContent/Papelera) fichas borradas, recuperables
    Packs/Gen1 … Gen6/          Un pack por generación: hojas CSV + INFORME.txt
    README.md                   Manual del AUTOR (cómo usar cada editor y cada sistema) ← leer también
Excel/                          Carpeta por defecto de exportación CSV (fuera de Assets); Excel/copias = copias de seguridad
Tools/
  README.md                     Manual de las herramientas Python
  verificar_pack/               generar_packs.py, generar_base.py, formas.py, generar_sets.py, verificar_pack.py,
                                pokeapi.py, csvlib.py
  datos_fuente/                 Datos de apoyo para generar los packs
  probar_dominio/               ProbarDominio.csproj → compila el dominio y los tests con .NET (sin Unity)
docs/
  CONTEXTO.md                   este documento
  PROPUESTA_EDITOR_OBJETOS.md   propuesta de rediseño del editor de objetos (pendiente de decidir)
```

## 3. Arquitectura (DDD estricto)

### 3.1 Contextos y asmdefs

| asmdef | Depende de | Contenido |
|---|---|---|
| `CTEditor.SharedKernel` | — | Tipos base sin dominio. |
| `CTEditor.GameDefinition.Domain` | SharedKernel | Definiciones puras (sin Unity). |
| `CTEditor.Party.Domain` | SharedKernel, GameDefinition.Domain | Instancias de monstruos y mochila. |
| `CTEditor.Battle.Domain` | SharedKernel, GameDefinition.Domain | Motor de combate. |
| `CTEditor.Adventure.Domain` | los anteriores + GameContracts | Casos de uso que cruzan contextos. |
| `CTEditor.Eventing.Domain`, `CTEditor.GameContracts` | SharedKernel | Eventos de mundo / contratos. |
| `CTEditor.GameDefinition` (Infrastructure) | Domain + UnityEngine | ScriptableObjects y mappers. |
| `CTEditor.GameDefinition.Editor` | todo + UnityEditor | Editores (solo en el Editor de Unity). |
| `CTEditor.Bootstrap` (+ `.Editor`) | todo | Escenas, UI de juego, carga de contenido. |
| `CTEditor.Tests.EditMode` | todo | Tests. |

**Regla de oro:** el dominio **no conoce Unity**. Todo lo que es lógica se escribe en `*.Domain` y se prueba con .NET.

### 3.2 Flujo de datos

```
Autor ─► Editor (ventanas) ─► ScriptableObject *Data (.asset en Resources) ─► Mapper (ACL) ─► objeto de dominio
                    ▲                                                                           │
          Excel CSV ┘ (CsvImporter)                                     GameData (Adventure) ◄──┘
                                                                              │
                          Juego: ContentLibrary (Resources) ─► GameData ─► BattleSession ─► Battle/TurnResolver
                          Editor: EditorGameData.Get() ─► GameData (para «Sugerir», revisar reglas, simular…)
```

- **`*Data`** (`Infrastructure/ScriptableObjects`): campos serializados que rellena el autor. Implementan
  `IContentAsset` (Id, DisplayName). Las referencias entre fichas suelen ser **objeto + id de respaldo** (p. ej.
  `TeamMemberData.species` + `speciesId`) para poder reenlazar si una ficha se borra y se recrea
  (`ReferenceRelinker`).
- **Mappers** (`Infrastructure/Acl/*Mapper.ToDomain`): traducen y validan. Son el único punto donde Unity toca el dominio.
- **`GameData`** (Adventure): catálogos de todo + Ruleset + sets; `Exists(kind, id)`, `TryGetSpecies`, `Snapshot`…
- **`ContentLibrary`** (Bootstrap) lo arma en tiempo de juego desde `Resources`; **`EditorGameData.Get()`** en el editor
  (cacheado por `ContentAssets.Version`).

### 3.3 Convenciones de código

- Todo en español (nombres de UI, comentarios, mensajes); identificadores de código en inglés.
- **Enums: solo se añaden valores AL FINAL** (se serializan como entero).
- Parámetros nuevos de constructores de dominio: **opcionales al final** para no romper llamadas.
- Etiquetas de campos → **`Editor/Common/Etiquetas.cs`** (nombre de campo → etiqueta en español y cabecera CSV).
  ¡Ojo! Dos campos con el mismo nombre en fichas distintas comparten etiqueta: por eso hay nombres como
  `abilitiesEnabled`, `mechanicKind`, `itemOptions` (evitar choques con `items` = «Mochila», etc.).
- `.meta` de cada archivo nuevo con GUID propio (se generan a mano fuera de Unity).
- `*.csproj` está en `.gitignore`; excepción: `!/Tools/probar_dominio/*.csproj`.

## 4. El editor (Unity)

- **Centro de Contenido** (`ContentHubWindow`): catálogo de todos los editores (`EditorCatalog`, categorías
  Criaturas, Combate, Objetos, Personajes, Mundo, Interfaz, Herramientas, Pruebas), elegir e **importar pack**,
  crear contenido clásico, recuento de errores del validador.
- **Base de ventanas**: `ContentEditorWindow<T>` (lista con filtros, crear/duplicar/borrar a papelera, plantillas,
  vista previa, zoom). Helpers en `ContentAssets` (`LoadAll`, `FindById`, `Create`, `CreateIfMissing`, `Edit`,
  `Label`, `ClearCache`).
- **Validador** (`ContentValidator` + ventana): errores/avisos de todo el contenido.
- **Papelera** (`ContentTrash`, `TrashWindow`): borrar = mover a `Assets/GameContent/Papelera/<Categoría>` (mismo GUID
  → al recuperar, las referencias vuelven). **Grupos** `~<nombre>` (p. ej. un cambio de generación) que se recuperan o
  borran enteros.
- **Excel/CSV** (`Editor/Csv`): `CsvSchema<T>` (columnas a mano, en español) y `CsvReflectiveSchema<T>` (automático).
  Solo se aplican las columnas presentes. Importación en dos fases: **Analizar** (qué cambiaría) → **Aplicar** (con
  copia de seguridad y reenlazado). Orden de importación por `Order`:

  | Orden | Hoja | Orden | Hoja |
  |---|---|---|---|
  | 10 | tipos.csv | 50 | curvas.csv |
  | 15 | tabla_tipos.csv, grupos_huevo.csv | 58 | mecanicas.csv |
  | 20 | estados.csv | 60 | reglas.csv |
  | 30 | habilidades.csv | 70 | movimientos.csv |
  | 25-27 | climas, trampas, efectos_lado | 80 | especies.csv |
  | 35 | objetos.csv | 85 | sets.csv |
  | 40 | naturalezas.csv | 88-95 | niveles_ia, entrenadores, zonas, equipos |

  Codecs especiales: `CsvTeamCodecs` (equipos, ver §7),
  `CsvFormCodecs` (`formas`, `cambios_forma`), `CsvCodecs` (condiciones, modificadores…).
- **Herramientas de prueba**: Calculadora de daño, Simulador de combate (`BattleSandbox`), Torneo de IAs, BattleLab
  (escena de pruebas generada por `BattleLabSceneBuilder`).

## 5. Packs y herramientas Python (`Tools/`)

- `generar_packs.py [--gen N]` crea `Packs/GenN` **fiel a la generación** desde PokeAPI (cache en `Tools/.cache`):
  especies, movimientos (categoría por tipo hasta la 3.ª), habilidades (3.ª+), naturalezas (3.ª+), grupos huevo (2.ª+),
  tabla de tipos, objetos, entrenadores, nombres en inglés (`nombre_en`), INFORME.txt.
- `formas.py`: formas de combate, variantes y megas (48 megas en Gen6 con su megapiedra).
- `generar_sets.py [--gen N]`: `sets.csv` desde Smogon (`pkmn.github.io/smogon/data/{sets,stats}/genNfmt.json`,
  formatos ou/ubers/uu/ru/nu/pu/lc), puntuación por uso real, ids únicos por (especie, formato).
- `verificar_pack.py <carpeta> [--pokeapi --gen N]`: formato, referencias y fidelidad. Termina con código 1 si hay
  errores. **Estado actual: 0 errores en Gen1-Gen6** (los avisos son movimientos especiales de líderes, intencionados).
- Importar un pack: Centro de Contenido → Pack → 📦 Importar («solo lo que falta» / «actualizar también»). Las
  plantillas del código crean la base que el pack no trae (estados, climas, Forcejeo, objetos con efecto, menús…);
  **si el pack trae la hoja, manda el pack**.

## 6. Qué se ha construido (historia funcional)

### Base (commits iniciales, PR #1-#16)
SharedKernel; definiciones (stats, tipos, movimientos, especies, reglas, catálogos); ScriptableObjects + mappers;
Party (instancias, niveles, crecimiento); Battle (fórmula de daño, acciones, eventos, turnos); Eventing; bootstrap con
escena de prueba; IA básica; ventanas de editor para movimientos, estados y habilidades; tests.

### Lotes A-F y siguientes (antes de esta serie de pasos)
- Condiciones genéricas (`Condition`/`ConditionKind`), potencia condicional, climas, trampas de campo, efectos de lado,
  estados principales y volátiles, cambios forzados, movimientos con requisitos, evoluciones con varias condiciones.
- Interfaz del juego: menús con gráficos propios, controles/teclado/mando, caja de texto, mapa de menús.
- IA por niveles (`AiLevelData`/`AiProfile`), memoria del rival, IA experta/predictora, **torneo de IAs** que comprueba
  que cada nivel gana al anterior; IA propia por entrenador.
- Género, objetos y habilidades de 5.ª-6.ª, grupos huevo, zoom y filtros en editores.
- Excel robusto (un fallo no bloquea todo), ids de respaldo y reenlazado, papelera.
- Packs Gen1…Gen6 fieles; «✨ Sugerir según la IA» para equipos; plantillas de entrenadores desde el pack.

### Plan de 8 pasos (esta serie)

| Paso | Qué | Piezas clave |
|---|---|---|
| **1** ✅ Reglas de generación y mecánicas | `GenerationRules` (categoría por tipo, «Especial» único, habilidades/objetos/naturalezas/géneros on/off; `ForGeneration(n)`), fichas `MechanicData` (Megaevolución con máx. por combate, objeto clave, revertir al cambiar). El Ruleset las lleva; `BattleRules.CategoryOf`, `TurnResolver.ApplyGenerationRules` (etapas enlazadas con Especial único), `MonsterFactory`/`TeamBuilder` respetan las perillas. Editor: botones por generación en Reglas. |
| **2** ✅ Formas | `SpeciesForm` (tipos/stats/habilidad propios), `FormChange` + `FormTrigger` (objeto, movimiento, PS, clima, mega…), **variantes** como especies enlazadas (`formOf`, `variantItem`). Motor: `Combatant.ChangeForm`, `TurnResolver.Forms`. Fuera de combate: `FieldActions.ChangeVariant`. Editor: sección «Formas y variantes», **Árbol de familia** (antes Cadena evolutiva). |
| **3** ✅ Megaevolución | `TurnResolver.Mega` (antes del orden, una por bando), botón en `BattleScreen`, `BattleSession.CanPlayerMegaEvolve`, IA con `MegaTiming` (Never/ASAP/Smart por nivel), `canMegaEvolve` por entrenador, 48 megas en Gen6. |
| **4** ✅ EVs/IVs/habilidad por miembro | `StatSpread` («252 Atq / 4 PS / 252 Vel», acepta abreviaturas Showdown), `TeamMemberData.evs/ivs/abilityId`, `TeamBuilder` los aplica; `TeamPreview` muestra stats estimadas. |
| **5** ✅ Showdown | `ShowdownFormat` (parse/format), `ShowdownNames`, `ShowdownConverter`; campo `englishName` en especies/movimientos/habilidades/objetos/naturalezas; ventana importar/exportar en Entrenadores y Equipos. |
| **6** ✅ Sets de Smogon | `CompetitiveSet` (alternativas por hueco, elección ponderada por puntuación), `CompetitiveSetData`, `sets.csv`, IA por nivel usa sets (`UseCompetitiveSets`), fijo (semilla por entrenador) o cambiante por combate, selector «🏆 Set de Smogon…», validador. |
| **7** ✅ Cambiar de generación | `GenerationWizardWindow` (Herramientas): pack → checklist → aplicar (copia en `Excel/copias`, sobrantes a papelera en grupo / borrar / dejar, importar pack, limpiar tabla de tipos, `RulesetEditorWindow.GenerationPreset`) → **Adaptar a las reglas** (`RulesReview` en dominio: especie/movimiento/objeto que no existe, objetos/naturalezas/géneros/habilidades/EVs apagados, megapiedra sin mega; cada fila «arreglar» o «dejar y avisar»). Entrenadores/equipos/zonas del autor nunca se borran. |
| **8** ⏳ Gen 7 | Variantes de Alola (como variantes enlazadas), **movimientos Z** (nueva `MechanicKind` + objetos Cristal Z), pack Gen7 (`generar_packs.py --gen 7`, `formas.py`, `generar_sets.py`), reglas de gen 7 (mega sigue activa). |

## 7. Sistemas clave en detalle

- **Reglas (`Ruleset`)**: fórmula de daño, críticos, EVs/IVs máx., niveles, aventura (huir, capturar, dinero, derrota,
  experiencia), `Generation` (GenerationRules) y `Mechanics` (lista de MechanicDefinition). Se crea «classic».
- **Combate**: `Battle` + `Combatant` (partial: Gen4, Forms) + `TurnResolver` (partial: Gen4, Gen6, Forms, Mega).
  Eventos de dominio (`MoveUsedEvent`, `FormChangedEvent`, `MegaEvolvedEvent`…) que narra `BattleScreen`.
  Objetos equipados: `ItemDefinition` + `ItemExtras` (ver propuesta de rediseño).
- **IA**: `TrainerBrain.Decide` (con perfil `AiProfile`), IAs `Simple/Aggressive/Expert/Predictor`, memoria del rival,
  mega inteligente, sets competitivos.
- **Equipos**: `TeamMemberSpec` → `TeamBuilder` (movimientos automáticos por IA, sugerencias, sets, EVs) →
  `MonsterFactory`.
- **Formato de equipo en CSV** (miembros separados por `|`):
  `especie@nivel%género[mov1/mov2]{objeto}~naturaleza!habilidad(EVs)#iv(IVs)"mote"` — todo tras el nivel es opcional
  (ver `CsvTeamCodecs.FormatMember` y el README del autor).

## 8. Cómo verificar sin Unity

Unity no está en el contenedor de desarrollo. Se verifica así:

1. **Dominio + tests** (.NET 8; en Debian/Ubuntu: `apt-get update && apt-get install -y dotnet-sdk-8.0`):
   ```bash
   dotnet test Tools/probar_dominio      # hoy: 213 tests, todos en verde
   ```
   El `.csproj` incluye SharedKernel, GameContracts, Eventing, GameDefinition/Domain, Battle, Party, Adventure y los
   tests que no usan Unity (la lista de exclusiones está en el propio `.csproj`).
2. **Infrastructure** (opcional): compilarla contra *stubs* de UnityEngine (`UnityStubs.cs` con `ScriptableObject`,
   `SerializeField`, `Range`, `Tooltip`…) en un proyecto de consola aparte. Se usó en la sesión; no está en el repo.
3. **Editor**: `dotnet build Tools/compilar_unity` compila TODO Runtime (incluido el editor) y los tests contra las DLL
   de referencia de Unity (NuGet `Unity3D.SDK`, Unity 2021: lo exclusivo de Unity 6 daría falso error). **Hacerlo antes de
   cada commit que toque el editor**: si no compila, Unity se queda con el código viejo sin que se note.
4. **Packs**: `python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/GenN [--pokeapi --gen N]`.

## 9. Pendiente / siguientes pasos

1. **Paso 8 (Gen 7)** — ver tabla §6.
2. **Rediseño del editor de objetos** — `docs/PROPUESTA_EDITOR_OBJETOS.md`: efectos por bloques «cuándo + si + qué»,
   desplegables de tipos/estados, sin secciones por generación, plantillas paramétricas, migración por fases.
   Tiene 4 preguntas abiertas para el autor.
3. Después, aplicar el mismo motor de bloques a **habilidades** (`AbilityExtras` tiene el mismo problema).
4. Probar en Unity el asistente del paso 7 (código de editor no compilado fuera de Unity).

## 10. Problemas conocidos y trucos

- PokeAPI: las descripciones en español de los grupos de versión 15-16 están desalineadas → se usan las de vg ≥ 17.
- Megapiedras: la generación de un objeto se saca de `item_game_indices`, no del grupo de versión.
- Smogon: «Camerupt» y «Camerupt-Mega» son la misma especie aquí → contador por (especie, formato) en los ids.
- Nidoran♀/♂ normalizan igual → nombres en inglés «Nidoran-F» / «Nidoran-M».
- Etiquetas: evitar nombres de campo que choquen con otros (§3.3).
- En el contenedor de Claude, el comando Bash a veces falla con «classifier gave no verdict»: reintentar o usar las
  herramientas de archivo.
