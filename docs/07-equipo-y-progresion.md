# 07 · Equipo y progresión (Party.Domain)

`Runtime/Party/Domain` — los INDIVIDUOS del jugador y lo que les pasa fuera del combate. Depende solo de
SharedKernel y GameDefinition.Domain.

| Pieza | Qué hace |
|---|---|
| `MonsterInstance` | Un individuo (ENTIDAD con identidad): especie, nivel, XP, PS actuales, estado, movimientos y PP, IVs, EVs (`EffortValues`), naturaleza, género, amistad, objeto equipado, mote, habilidad (hueco), `StatsFor(forma)`. `LearnMove`, `Heal`, `Revive`, `ChangeFriendship`, `Evolve(nuevaEspecie, stats, fórmula)` (conserva el daño recibido), ganar XP y subir de nivel recalculando. |
| `MonsterFactory` | Crea un individuo: estadísticas desde la base con la fórmula, nivel recortado al tope del Ruleset, IVs (fijos, al azar o por estadística con `StatSpread`), naturaleza y género (o ninguno si las reglas de generación los apagan), movimientos de arranque, PS llenos. `GenderRoll`. |
| `IStatGrowthFormula` + `ClassicStatGrowthFormula` | `común = (2·base + IV + EV/4)·nivel/100`; PS = común + nivel + 10; el resto (común + 5) × naturaleza. |
| `EffortValues` | EVs con dos topes (por stat y total) del Ruleset; 0 = juego sin EVs. |
| `Level`, `Experience` | Nivel y XP como valores. |
| `Party` | AGREGADO: el equipo, con su tope (`MaxPartySize`), orden, añadir/quitar/intercambiar. |
| `Bag` | Mochila: cuántas unidades de cada objeto (por id), con tope. No sabe qué hace cada objeto. |
| `EvolutionRules` | ¿Evoluciona? Según el disparador (`EvolutionTrigger`: subir de nivel, usar objeto, intercambio...) y el `EvolutionContext` (hora, lugar, clima del mapa, equipo, marcas de la partida). `Find`, `Check`, `ItemCanEvolve`, `Personality` (valor estable del individuo). |
| `ItemUse` | Usar un objeto FUERA del combate sobre un individuo: revivir, curar PS, curar estados, PP, amistad y detectar evolución por objeto. **Solo gasta el objeto si hizo algo.** Lee las vistas de `ItemDefinition` (los bloques «Al usarlo»). |

## Cómo llega un individuo al combate y vuelve

1. La Aventura hace una **foto** (`BattleParticipant`) de cada miembro (estadísticas y formas ya calculadas).
2. El combate trabaja sobre sus `Combatant` y devuelve un `BattleResult`.
3. `BattleResultToPartyFlow` / `BattleSession` aplican al individuo: PS, estado, PP, objeto (consumido o robado),
   XP (y subidas de nivel en mitad del combate), EVs, amistad; los capturados se crean con `MonsterFactory`.

## Reglas que afectan a la progresión

- `Ruleset`: `LevelCap`, `MaxIv`, `MaxEvPerStat`, `MaxEvTotal`, `UsePp`, `MaxMovesPerMonster`.
- `AdventureRules`: aprender movimientos al subir de nivel, evolucionar tras el combate, amistad por nivel y al
  debilitarse, repartir experiencia.
- `GenerationRules`: sin naturalezas / sin géneros → la fábrica no los asigna.
