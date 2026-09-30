# 06 · El combate (Battle.Domain)

`Runtime/Battle/Domain` — el motor de combate 1 contra 1. **Puro, determinista con semilla y sin conocer Party.**

## Piezas

| Pieza | Qué hace |
|---|---|
| `BattleParticipant` | FOTO inmutable de entrada de un monstruo: id, especie, nivel, estadísticas ya calculadas, PS, tipos, movimientos y PP, estado, objeto, habilidad, género, amistad, peso, formas (`BattleForm`, ya calculadas), reglas de forma... Lo arma la Aventura. |
| `Battle` | Agregado raíz: los dos `BattleTeam` (activo + banca), clima, efectos de lado, trampas, efectos retardados, megas usadas por lado y el desenlace. |
| `Combatant` (+ `.Gen4`, `.Forms`) | Copia de TRABAJO de un participante: PS, estado, volátiles, etapas, PP, objeto (y el consumido), habilidad actual, forma, bloqueo Elección, sustituto, turnos en campo, contadores de usos de efectos por combate... |
| `TurnResolver` (partial) | El servicio que resuelve cada turno. No guarda estado entre llamadas (salvo un turno en pausa). |
| `BattleRules` | Reglas ya traducidas del Ruleset: críticos, estadísticas del daño, reglas de generación, mecánicas. `CategoryOf(move)` decide físico/especial (por tipo si la generación lo dice). |
| `BattleAction` | Acción elegida: `UseMove` (con `megaEvolve`), `Flee`, `SwitchMonster`, `UseItemAction` (`BattleItemEffect`), `Capturar`. |
| `BattleResult` | Desenlace (`BattleOutcome`: ganó, perdió, huyó, capturado...) + estado final de cada participante (PS, estado, PP, objeto), XP y EVs repartidos. |
| `IBattleAI` + `AI/` | IA de combate: `SimpleBattleAI` (al azar), `AggressiveBattleAI` (más daño esperado), `ExpertBattleAI` (usa `PreviewDamage`: remata, evita lo inútil, se protege), `PredictorBattleAI` (anticipa cambios con `IOpponentModel`). |
| `Formulas/` | `IDamageFormula` + `ClassicDamageFormula`; `ICatchFormula` + `ClassicCatchFormula` (sacudidas); `IXpFormula` + `ClassicXpFormula`. |
| `Events/` | ~90 eventos (`MoveUsedEvent`, `DamageDealtEvent`, `StatusInflictedEvent`, `HeldItemActivatedEvent`, `FormChangedEvent`, `MegaEvolvedEvent`...). |

## Construir el resolvedor

```csharp
new TurnResolver(moves, typeChart, damageFormula, rng,
    statuses, catchFormula, abilities, xpFormula, awardEffortValues, usePp, struggleMoveId,
    weathers, rules: BattleRules.From(ruleset), items, hazards, sideConditions);
```
Todo lo opcional puede ser `null` (sin estados, sin objetos...). La Aventura lo arma desde `GameData`.

## Un turno: `ResolveTurn(battle, accionJugador, accionRival)` → lista de eventos

1. **Preparación**: limpia marcas del turno (quién actuó, daño recibido).
2. **Megaevolución** (`TurnResolver.Mega`): antes de ordenar (la nueva Velocidad ya cuenta); primero el más rápido.
3. **Orden** (`OrderPlays`): huir, cambiar, usar un objeto y capturar van antes que cualquier movimiento; entre movimientos, **prioridad** → **Garra Rápida**
   (bloque «Actuar el primero», una tirada por turno) → **Rezagado** → **Velocidad** (con Espacio Raro invertida) →
   empate al azar.
4. **Resolución** de cada jugada. Para un movimiento (`ResolveMove`), en orden: bloqueo Elección → carga/recarga/
   Venganza/Contraataque → requisitos → inmunidad por etiqueta → Protección → Capa Mágica / Robo → **precisión** →
   Mutatipo → inmunidad de tipo en movimientos de estado → efectos retardados → preparación del golpe (eficacia,
   STAB, estadísticas) → **impactos** (multigolpe; por impacto: daño recibido de objetos [bayas de resistencia],
   sustituto, Aguante/Robustez/Banda Focus, Furia) → **efectos del movimiento** → Hedor → **objetos del atacante**
   (Vidasfera, Campana Concha, Roca del Rey) → reacciones al golpe (habilidades, **objetos del defensor**: Seguro
   Debilidad, Globo Helio) → recarga → reacciones por **contacto** (Estática, Casco Dentado). Tras cada jugada se miran
   las **bayas de poca vida**. Los **cambios de forma** se comprueban antes y después del movimiento.
5. **Fin de turno** (`FinishTurn`): estados (daño residual, duración), habilidades de fin de turno, clima, **objetos**
   (bloques «Al final de cada turno»: Restos, Lodo Negro, esferas... y luego bayas), efectos de lado/campo, mecánicas
   de 3.ª-4.ª y 5.ª-6.ª, formas de fin de turno; se limpian retroceso y protección.
6. **Experiencia** (antes del desenlace) y **desenlace** (`BattleEndedEvent`).

Otras entradas: `ResolveBattleStart` (habilidades, formas y **objetos «Al entrar al combate»** de los que empiezan),
`ResumeTurn` (el jugador elige quién entra tras Ida y Vuelta / Relevo: el turno queda **en pausa**,
`IsTurnSuspended`), `PreviewDamage` / `DamagePreview` (el mismo cálculo sin tocar el combate: lo usan la IA, la
calculadora de daño y el editor), `RestrictionFor` (¿puede usar este movimiento? Mofa, Elección, Chaleco...),
`CatchContextFor` (probabilidad de captura para la interfaz).

## Partials por tema

| Archivo | Tema |
|---|---|
| `TurnResolver.cs` | Núcleo: turno, movimientos, daño, estados, clima, efectos, condiciones (`EvaluateRaw`), estadísticas efectivas. |
| `TurnResolver.Gen4.cs` | Mecánicas de 3.ª-4.ª: habilidades complejas, restricciones, precisión avanzada, objetos robados/cambiados, efectos retardados... |
| `TurnResolver.Gen6.cs` | 5.ª-6.ª: campos, Zona Mágica/Extraña, protecciones con castigo, habilidades nuevas. |
| `TurnResolver.Items.cs` | **Objetos equipados por bloques de efecto** (ver [09](09-efectos-por-bloques.md)). |
| `TurnResolver.Forms.cs` | Cambios de forma (entrada, movimiento, fin de turno). |
| `TurnResolver.Mega.cs` | Megaevolución (piedra o movimiento; máximo por lado; objeto clave del jugador en la sesión). |

## Determinismo

Todo azar pasa por `IRng` en un orden fijo. Si se añade una tirada nueva, cambian las secuencias de los tests con
semilla: por eso los bloques solo tiran si su probabilidad es < 100 % y las condiciones se evalúan ANTES de la tirada.

## IA de combate vs IA de entrenador

- `IBattleAI` (Battle) elige un **movimiento** con la información del combate.
- `TrainerBrain` (Adventure, [08](08-aventura.md)) decide objeto / cambio / movimiento / megaevolución según el
  `AiProfile` del entrenador y usa estas IAs como «cerebro» de movimientos.
