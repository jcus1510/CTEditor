# 08 · La partida (Adventure.Domain)

`Runtime/Adventure/Domain` — orquesta los contextos: arma equipos, lleva un combate de principio a fin con reglas
reales y aplica los resultados. Sigue siendo **puro** (se prueba sin Unity).

## GameData

`GameData` = TODO el contenido ya traducido a dominio en un paquete: catálogos de especies, movimientos, estados,
habilidades, objetos, climas, naturalezas, curvas, tipos, trampas, efectos de lado, perfiles de IA, sets de
competición, la tabla de tipos y el `Ruleset`. Métodos: `TryGetSpecies/Move/Item...`, `Exists(tipo, id)`,
`MaxPpOf(movimiento)`, `Snapshot(...)` (foto de combate de un individuo, con sus formas: `FormsFor`), `Sets`.

Quién lo construye:
- **Juego**: `ContentLibrary` (Bootstrap) desde `Resources`.
- **Editor**: `EditorGameData.Get()` desde las fichas del proyecto (se reconstruye cuando cambia `ContentAssets.Version`).
- **Tests**: con `MemoryCatalog` en código.

## BattleSession — un combate completo

1. Se arma desde la partida del jugador (`PlayerSave`) y el rival (`Wild(...)` salvaje o `Against(entrenador)`).
2. Cada turno el jugador elige (`PlayerChoice`: `FightChoice(índice, mega)`, `SwitchChoice`, `ItemChoice`,
   `BallChoice`, `RunChoice`). La sesión **valida** con las reglas (`WhyNot`: «no puedes huir de un entrenador»,
   «esa poción no haría nada», «no quedan PP», «necesitas el objeto clave para megaevolucionar») sin gastar el turno; si
   vale, el motor resuelve (`Submit`).
3. XP al momento (sube de nivel en combate), aprender movimientos (`AnswerLearnMove`), el rival saca al siguiente, el
   jugador elige sustituto (`ChooseReplacement`).
4. Al terminar todo vuelve a la partida: PS, estados, PP, objetos, EVs, capturado al equipo o al PC, premio
   (`PrizeMoney`) o derrota (pierde dinero y vuelve al Centro), evoluciones pendientes (`AnswerEvolution`).
5. Emite eventos de sesión (`SessionEvents.cs`: intro, sale, sube de nivel, aprende, dinero, derrota, evolución...)
   además de los del combate. `SessionPhase` dice qué espera (elegir acción, sustituto, aprender, evolucionar...).

## TeamBuilder — la fábrica de equipos

Convierte recetas (`TeamMemberSpec`) en individuos con la MISMA lógica para el jugador, los entrenadores y los
salvajes: `Build`, `NewGame`, `NewGameWithStarter`, `TrainerTeam`, `Wild`.
- Movimientos no escritos → `MovesetPlanner` según la IA (nivel, MT, tutor, huevo, sinergias).
- Objetos equipados según `HeldItemStyle` de la IA; naturaleza competitiva, EVs escritos o entrenamiento competitivo.
- **Sets de Smogon** (`WithCompetitiveSet`): si la IA usa sets, elige uno de la especie ponderado por uso; **fijo**
  (semilla por entrenador e índice) o **cambiante** (azar del combate).
- `Suggest(...)` = lo que saldría (lo usa «✨ Sugerir según la IA» del editor: mismo resultado que el juego).
- `AbilitySlotOf`, `ApplyEvs` (recortados a las reglas).

## TrainerBrain — el cerebro del entrenador

Cada turno decide **objeto** (curar según `HealStyle` y umbral, quitar estados, mejoras), **cambio** (si puede y le
conviene), **megaevolución** (`MegaTiming`: nunca / en cuanto pueda / inteligente) y **movimiento** (con la IA de su
`MoveBrain`: al azar, agresiva, experta, predictora), con probabilidad de error. Usa `OpponentModel` (lo que sabe de ti
según `AiKnowledge`: nada, lo visto en el combate, memoria entre combates, todo) y `TrainerMemory` (movimientos vistos
y estimaciones de tus IVs/EVs por el daño).

## FieldActions — fuera del combate

`UseItem` (con `ItemUse` + evoluciones por objeto + movimientos al evolucionar), `GiveItem` / `TakeItem` (`CanBeHeld`),
`Swap` (ordenar), `LearnMove` (olvidar uno), `Summary` (resumen del individuo: stats, movimientos), variantes
(`VariantTargetFor`, `VariantsOf`, `ChangeVariant`: el objeto de variante cambia entre base y variante sin gastarse).

## Otros

| Pieza | Qué hace |
|---|---|
| `PlayerSave` | La partida: equipo, PC (`StoredIn`), mochila, dinero, nombre, `WorldState` (marcas). |
| `AiTournament` | Enfrenta niveles de IA con el MISMO equipo muchas veces (motor real) → % de victorias (`DuelResult`). |
| `RulesReview` | «Adaptar a las reglas» (asistente de cambio de generación): conflictos de un miembro con el contenido y las reglas (`ConflictKind`: especie/movimiento/objeto/naturaleza que no existe, objetos/naturalezas/géneros/habilidades/EVs apagados, megapiedra sin Megaevolución, habilidad ajena). |
| `Interface/` | Dominio de la interfaz del juego: `Menus.cs` (menús, opciones, cursor, navegación espacial), `GameInput.cs` (botones del juego, enlaces de teclas/mando), `Text.cs` (variables `{jugador}`, paginado, tecleo, ajustes), `ClassicInterface.cs` (menús clásicos y acciones de pantalla). |
