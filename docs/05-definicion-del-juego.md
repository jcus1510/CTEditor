# 05 · Definición del juego (GameDefinition.Domain)

`Runtime/GameDefinition/Domain` — el CONTENIDO puro: definiciones **inmutables y compartidas** (hay UNA especie
«Charmander» y de ella nacen muchos individuos). Sin Unity. Cada tipo se construye por constructor (con parámetros
opcionales al final) y se referencia por `Id<T>` a través de un `ICatalog<T>`.

## Estadísticas y tipos

| Tipo | Qué es |
|---|---|
| `StatId` (`Stats/StatId.cs`) | Id de estadística (`hp`, `attack`, `defense`, `sp_attack`, `sp_defense`, `speed`, además `accuracy`, `evasion` para etapas). **Abiertas**: el autor puede inventar estadísticas. |
| `StatBlock` | Valores por estadística, por clave, inmutable (`With(...)` para derivar). Builder para construir. |
| `StatDefinition` | Definición autorable de una estadística. |
| `StatSpread` | Reparto «252 Atq / 4 PS / 252 Vel» (EVs o IVs de un miembro). Acepta español y abreviaturas de Showdown (`HP/Atk/Def/SpA/SpD/Spe`). `TryParse`, `Format`, `FormatShowdown`. |
| `Nature` | Naturaleza: estadística que sube y la que baja (y el %). Contenido, no enum. |
| `ElementType` | Tipo elemental: **contenido** (id + nombre), no enum. |
| `TypeChart` | Tabla de tipos inmutable (Builder). Solo guarda lo distinto de ×1. `Effectiveness(atk, def)`. |

## Especies (`Species/`)

`Species`: `Id, DisplayName, Types, BaseStats, Learnset (LearnableMove: nivel/MT/tutor/huevo), Evolutions,
Ability, SecondAbility, HiddenAbility, GrowthCurveId, BaseExpYield, EvYield, CatchRate, BaseFriendship, Dex
(PokedexEntry: número, categoría, altura, peso, color, descripción, % hembras, legendaria), MachineMoves,
TutorMoves, EggMoves, EggGroups, Forms, FormChanges, FormOf, VariantItem`.

- **Evoluciones** (`Evolution`): método `Level | Item | Friendship | Trade | LevelUp` + condiciones extra combinables
  (`EvolutionCondition`): `MinLevel, MinFriendship, HoldsItem, KnowsMove, KnowsMoveOfType, TimeOfDay, AtLocation,
  MapWeather, StatRelation, PartyHasSpecies, PartyHasType, Nature, Chance, GameFlag, Gender`.
- **Formas de combate** (`SpeciesForm`): tipos, estadísticas y habilidad propios; `RevertsOnSwitch`. Las **reglas de
  cambio** (`FormChange`: desde → hasta, `FormTrigger` = `HeldItem, UseMove, DamagingMove, HpBelow, HpAtLeast, Weather,
  MegaEvolution`, más habilidad requerida, «después del movimiento», movimiento conocido). `FormChange.AnyForm = "*"`.
- **Variantes**: especies separadas enlazadas por `FormOf` (Rotom Lavado es otra especie cuya base es Rotom);
  `VariantItem` = objeto que cambia entre base y variante fuera del combate.
- `Gender`/`GenderText`: macho, hembra, sin género.

## Movimientos (`Moves/`)

`Move`: `Id, DisplayName, Type, Category (Physical/Special/Status), Power, Accuracy, MaxPp, Priority, Target,
SecondaryEffects, MinHits/MaxHits, CritStage, TwoTurn (carga, recarga, semi-invulnerable...), MakesContact,
FixedDamage/FixedDamageAmount (Bomba Sónica, Sísmico...), RespectsTypeImmunity, PowerModifiers (con condiciones),
PowerFormula (expresión matemática), AttackStat/DefenseStat (qué estadísticas usa), Tags (puño, sonido...),
Requirements, TypeByWeather`.

`MoveEffect` (efectos, con probabilidad, objetivo y condiciones) — `MoveEffectKind`: `InflictStatus, Drain, Recoil,
HealSelf, ChangeStatStage, Flinch, RecoilMaxHp, Heal, CureStatus, SetWeather, SetHazard, ClearHazards, ForceSwitch,
SetSideCondition, ResetStages, CritBoost, Substitute, DisableMove, Encore, Rampage, Rage, CallRandomMove,
CallLastMove, CopyLastMove, Transform, ChangeType, SwitchSelf, Teleport, ClearSideConditions, RemoveItem, StealItem,
SwapItems, ConsumeTargetBerry, RestoreItem, CopyAbility, SwapAbility, SetAbility, DelayedHeal, DelayedDamage,
Stockpile, UseStockpile, TransferStatus, RaiseRandomStat, SwapOwnStats, SwapStages, CopyStages, SelfFaint,
HealNextSwitchIn, PainSplit, CallTeamMove, CallMove, CallTargetMove, TeamCureStatus, CallOwnMove, GiveAbility,
CopyTypes, GiveItem, AddType, InvertStages`. (Enum: solo se añade al final.)

## Estados (`Status/`)

`StatusConditionDefinition` — un estado es DATOS: `ResidualDamagePercent, ResidualHeals, ProgressiveResidual
(Tóxico), ActionPreventionChance, PassiveModifiers (stats), DurationTurns/DurationMaxTurns, ClearedOnSwitch,
RecoveryChancePerTurn, SelfDamageOnPreventedPercent (confusión), TransformsToStatus, ImmuneTypes, IsVolatile,
PreventsSwitch, BlocksIncomingMoves (Protección), SurvivesLethalHit (Aguante), ResidualHealsOpponent (Drenadoras),
HarderWhenRepeated, CatchMultiplier` + `StatusExtras` (volátiles de 3.ª-4.ª: Mofa, Levitón, Anticura, Encanto...).

## Habilidades (`Abilities/`)

`AbilityDefinition(id, nombre, bloques)` — una habilidad es una lista de **efectos por bloques**, como un objeto
([09](09-efectos-por-bloques.md)). `AbilityEffects.Read` traduce los bloques con forma clásica a las propiedades que
consulta el motor (inmunidades, reacción por contacto, al entrar, poca vida, fin de turno, potencia con condiciones y
`AbilityExtras`: clima, Rastro, Gula...); el resto queda en `GenericEffects` y se ejecuta como los bloques de un objeto.

## Objetos (`Items/`) y efectos (`Effects/`)

- `ItemDefinition`: identidad (`Id, DisplayName, Description, Category, Price`), dónde se usa
  (`UsableInBattle, UsableOutsideBattle, Consumable`), `IsBerry` y **`Effects`** (lista de `EffectBlock`). Las
  propiedades antiguas (`HealHp`, `CatchMultiplier`, `HeldTriggerHpPercent`...) son **vistas** de los bloques.
- `ItemCategory`: `Medicine, Revive, StatusCure, PpRestore, Ball, Evolution, BattleBoost, Held, Vitamin, Key, Other,
  Machine, Berry` (organiza la mochila y el editor).
- `ItemEffects`: ayudas para condiciones frecuentes (muy eficaz, vida llena, es de tipo, tipo del movimiento) y para reconocer bayas.
- `EffectBlock`, `EffectTrigger`, `EffectAction`, `BlockTarget`, `EffectRules` → capítulo [09](09-efectos-por-bloques.md).
- `IEffect`, `HealPartyEffect`, `GiveItemEffect`: efectos DECLARATIVOS de guiones del mundo (Eventing), otra cosa.

## Condiciones (`Conditions/Condition.cs`)

Una `Condition` pregunta algo durante el combate con «propio» (dueño de la ficha) y «rival» (el otro):
`ConditionKind` = `HasAnyStatus, HasStatus, HpPercent, IsType, Weather, Friendship, Level, LevelDifference,
StatStage, AlreadyActed, MoveType, MoveCategory, MovePower, MoveMakesContact, MoveHasTag, RandomChance,
DamagedThisTurn, TurnsOnField, TargetChoseAttack, UsedAllOtherMoves, StockpileCount, MoveEffectiveness,
MoveHasSecondary, HasItem, LostItem, WeightKg, SameGender, OppositeGender, FieldCondition, CanEvolve`, con
`Subject` (Self/Other), `Comparison`, `Number`, `Text` y `Negate`. `PowerModifier` = multiplicador + condiciones.
Se usan en movimientos, habilidades, bloques de objetos y efectos. Las evalúa `TurnResolver.EvaluateRaw`.

## Clima, trampas y efectos de lado

- `WeatherDefinition`: `DefaultTurns, TypePowerMultipliers, ResidualDamagePercent, ImmuneTypes`.
- `HazardDefinition` (Púas, Trampa Rocas...): capas, daño por capa o según eficacia de un tipo, estado por capa,
  etapa, inmunes, absorbida por tipos.
- `SideConditionDefinition` (Reflejo, Pantalla de Luz, Viento Afín, Espacio Raro, campos...): multiplicadores de daño
  físico/especial, bloqueos, velocidad, orden de turno invertido, precisión, suelo, críticos, tipos, `SuppressesItems`
  (Zona Mágica), `Group` (los campos se sustituyen entre sí), curación por turno, potencia por tipo.

## Reglas (`Rules/`)

- `Ruleset` — las perillas globales: `MaxPartySize, MaxMovesPerMonster, LevelCap, DamageFormula (FormulaId), MaxIv,
  MaxEvPerStat, MaxEvTotal, UsePp, StruggleMoveId, CritDenominators, CritMultiplier, Physical/Special Attack/Defense
  Stat`, más `Adventure`, `Generation` y `Mechanics`. `Ruleset.Classic` = valores clásicos.
- `AdventureRules` — alrededor del combate: `FleeAlwaysWorks, CanFleeTrainerBattles, CanCatchTrainerMonsters,
  CatchRateMultiplier, SendToBoxWhenFull, ExpShareAll/ExpShareOthersPercent, LearnMovesOnLevelUp, EvolveAfterBattle,
  FriendshipPerLevelUp, FriendshipLostOnFaint, StartingMoney, MoneyLostOnBlackoutPercent, HealOnBlackout`.
- `GenerationRules` — `Generation, CategoryByType, SpecialTypes, SingleSpecialStat, Abilities, HeldItems, Natures,
  Genders, MachinesConsumable`. `ForGeneration(n)`: 1 = por tipo + Especial único + sin habilidades/objetos/naturalezas/
  géneros + MT gastables; 2 = por tipo, sin habilidades ni naturalezas; 3 = por tipo; 4 = MT gastables; 5+ = moderno.
- `MechanicDefinition` (`Mechanics/`): `MechanicKind` (hoy `MegaEvolution`) + `MegaEvolutionSettings` (máximo por
  combate, objeto clave requerido, revertir al retirarse). Varias fichas; se activan en el Ruleset.
- `FormulaId`: nombre de la fórmula de daño (`classic`).

## Crecimiento (`Growth/`) y fórmulas (`Formulas/`)

- `GrowthCurve`: tabla acumulada XP → nivel. `GrowthCurvePresets`: las 6 curvas clásicas.
- `MathExpression`: evaluador de fórmulas del autor (curvas personalizadas, potencia por fórmula) con errores claros.

## Entrenadores y equipos (`Trainers/`)

- `TrainerDefinition`: `Id, DisplayName, TrainerClass, Team (TeamMemberSpec), Ai (nivel 1-7), AiSettings,
  AiProfileId (IA propia), BaseMoney, IntroLine/DefeatLine/VictoryLine, SetFormats, VariableSets, CanMegaEvolve,
  MaxTeamLevel` (+ mochila, curar, cambiar).
- `TeamMemberSpec`: RECETA de un miembro (especie, nivel, movimientos, objeto, naturaleza, IVs/EVs, habilidad, género,
  mote). La fabrica `TeamBuilder` ([08](08-aventura.md)).
- `TeamPreset`: equipo prearmado del jugador (miembros, dinero, mochila).
- `AiProfile`: cómo piensa un nivel de IA — `Brain (MoveBrain), MistakePercent, ItemUsePercent, Heal/HealBelowPercent,
  CanSwitch, Moveset (MovesetStyle), Synergies, UseMachine/Tutor/EggMoves, HeldItems (HeldItemStyle), Knowledge
  (AiKnowledge), CompetitiveTraining, PredictPercent, DefaultBag, UseCompetitiveSets, MegaTiming`.
  Niveles: 1 Novato · 2 Aficionado · 3 Veterano · 4 Élite · 5 Campeón · 6 Maestro · 7 Injusto.
- `MovesetPlanner`: elige movimientos cuando el autor no los escribe (por nivel, MT, tutor, huevo, sinergias).
- `ChallengeBuilder`: «Preparar un reto» (genera un entrenador).
- `CompetitiveSet`: set de Smogon (alternativas por hueco, puntuación de uso). `Choose(...)` pondera por uso.
- `Showdown.cs`: `ShowdownSet`, `ShowdownFormat` (parse/format del texto de Showdown), `ShowdownNames` (normaliza
  nombres en inglés), `ShowdownConverter` (Showdown ↔ `TeamMemberSpec`).

## Encuentros y catálogos

- `EncounterZone`: especies de una zona con niveles y peso (el sorteo lo hace la partida con `IRng`).
- `ICatalog<T>` (`TryGet`, `All`), `MemoryCatalog<T>`.
