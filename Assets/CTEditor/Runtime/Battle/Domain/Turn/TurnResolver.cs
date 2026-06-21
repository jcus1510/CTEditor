using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// El DOMAIN SERVICE que resuelve un turno: el pipeline de fases de M.1 (orden -> resolución ->
    /// chequeo de desenlace), versión 1v1. No guarda estado de partida; recibe el Battle y dos
    /// acciones, muta los combatientes y DEVUELVE una línea de tiempo de eventos (M.2). El dominio
    /// resuelve al instante; la presentación reproducirá esos eventos a su ritmo.
    ///
    /// Sus dependencias son TODAS abstracciones o contenido (catálogo de movimientos, tabla de tipos,
    /// fórmula de daño, azar). Se inyectan: por eso el resolvedor es puro y, con un IRng de semilla
    /// fija, totalmente determinista y testeable.
    /// </summary>
    public sealed class TurnResolver
    {
        private readonly ICatalog<Move> _moves;
        private readonly TypeChart _typeChart;
        private readonly IDamageFormula _damageFormula;
        private readonly IRng _rng;
        private readonly ICatalog<StatusConditionDefinition> _statuses; // opcional: null = sin estados

        public TurnResolver(
            ICatalog<Move> moves,
            TypeChart typeChart,
            IDamageFormula damageFormula,
            IRng rng,
            ICatalog<StatusConditionDefinition> statuses = null)
        {
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));
            _typeChart = typeChart ?? throw new ArgumentNullException(nameof(typeChart));
            _damageFormula = damageFormula ?? throw new ArgumentNullException(nameof(damageFormula));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _statuses = statuses; // si es null, las mecánicas de estado quedan inertes (demos/tests viejos)
        }

        /// <summary>Resuelve un turno completo y devuelve los eventos en orden.</summary>
        public IReadOnlyList<IDomainEvent> ResolveTurn(Battle battle, BattleAction playerAction, BattleAction enemyAction)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            var events = new List<IDomainEvent>();
            if (battle.IsOver) return events;

            // --- FASE: ORDEN ---
            // Armamos las dos "jugadas" (actor, objetivo, acción) y decidimos quién va primero.
            var plays = new List<(Combatant actor, Combatant target, BattleAction action)>
            {
                (battle.Player, battle.Enemy, playerAction),
                (battle.Enemy, battle.Player, enemyAction)
            };
            OrderPlays(plays);

            // --- FASE: RESOLUCIÓN (cada jugada, en orden) ---
            foreach (var play in plays)
            {
                if (battle.IsOver) break;            // alguien huyó o el combate ya acabó
                if (play.actor.IsFainted) continue;  // un debilitado no actúa

                // RECARGA: tras un movimiento de recarga, este turno se pierde por completo.
                if (play.actor.MustRecharge)
                {
                    events.Add(new RechargingEvent(play.actor.Id));
                    play.actor.ClearRecharge();
                    continue;
                }

                // CARGA COMPROMETIDA: si venía cargando, lanza ESE movimiento e ignora la acción elegida.
                // Simplificación deliberada: una carga ya iniciada se libera (no la interrumpe estado/flinch).
                if (play.actor.ChargingMove.HasValue)
                {
                    var release = new UseMove(play.actor.ChargingMove.Value);
                    play.actor.ClearChargingMove();
                    ResolveMove(play.actor, play.target, release, events, isChargedRelease: true);
                    continue;
                }

                if (IsPreventedByStatus(play.actor, events)) continue; // p.ej. paralizado/dormido
                if (play.actor.Flinched)             // retrocedió: pierde el turno (lo provocó alguien que actuó antes)
                {
                    events.Add(new FlinchedEvent(play.actor.Id));
                    continue;
                }

                switch (play.action)
                {
                    case Flee _:
                        battle.SetOutcome(BattleOutcome.Fled);
                        events.Add(new BattleEndedEvent(BattleOutcome.Fled));
                        break;
                    case UseMove useMove:
                        ResolveMove(play.actor, play.target, useMove, events);
                        break;
                }
            }

            // --- FASE: FIN DE TURNO (estados: daño residual, progresión, expiración por duración) ---
            if (!battle.IsOver)
            {
                ApplyEndOfTurnStatus(battle.Player, events);
                ApplyEndOfTurnStatus(battle.Enemy, events);
            }

            // El retroceso (flinch) solo dura este turno: se limpia siempre.
            battle.Player.ClearFlinch();
            battle.Enemy.ClearFlinch();

            // --- FASE: DESENLACE ---
            if (!battle.IsOver)
            {
                if (battle.Enemy.IsFainted)
                {
                    battle.SetOutcome(BattleOutcome.PlayerWon);
                    events.Add(new BattleEndedEvent(BattleOutcome.PlayerWon));
                }
                else if (battle.Player.IsFainted)
                {
                    battle.SetOutcome(BattleOutcome.PlayerLost);
                    events.Add(new BattleEndedEvent(BattleOutcome.PlayerLost));
                }
            }

            return events;
        }

        // Resuelve un UseMove: emite el evento de uso, tira precisión, y si conecta calcula el daño
        // (si lo hay) y aplica los efectos del movimiento (infligir estado, etc.), incluso para los
        // movimientos de Estado sin daño como Fuego Fatuo.
        private void ResolveMove(Combatant actor, Combatant target, UseMove useMove, List<IDomainEvent> events, bool isChargedRelease = false)
        {
            var move = _moves.Get(useMove.Move); // resuelve el id -> Move vía catálogo

            // DOS TURNOS (CARGA): el primer uso solo carga; el golpe llega al turno siguiente.
            if (move.TwoTurn == TwoTurnKind.Charge && !isChargedRelease)
            {
                actor.SetChargingMove(move.Id);
                events.Add(new ChargingStartedEvent(actor.Id, move.Id));
                return;
            }

            events.Add(new MoveUsedEvent(actor.Id, move.Id));

            // PRECISIÓN: si no es "nunca falla", tiramos contra su precisión.
            if (!move.NeverMisses)
            {
                float acc = move.Accuracy.Value.AsFraction; // aquí Accuracy no es null (ver Move)
                if (_rng.NextFloat() >= acc)
                {
                    events.Add(new MoveMissedEvent(actor.Id, move.Id));
                    return;
                }
            }

            // Movimientos sin daño directo (categoría Estado, como Fuego Fatuo): no calculan daño,
            // pero SÍ aplican sus efectos abajo. Por eso ya no salimos aquí.
            int damageDealt = 0;
            if (move.DealsDirectDamage)
            {
                // EFECTIVIDAD (tabla de tipos: tipo del movimiento contra los tipos del objetivo).
                float effectiveness = _typeChart.Effectiveness(move.Type, target.Types).Multiplier;

                // STAB: ¿el tipo del movimiento está entre los tipos del atacante?
                bool stab = ContainsType(actor.Types, move.Type);

                // SELECCIÓN DE STATS por categoría (aquí "vive" Físico vs Especial). Se calcula una
                // sola vez: no cambia entre golpes de un mismo movimiento.
                int attackStat = EffectiveStat(actor, move.Category == MoveCategory.Physical ? StatId.Attack : StatId.SpAttack);
                int defenseStat = EffectiveStat(target, move.Category == MoveCategory.Physical ? StatId.Defense : StatId.SpDefense);

                // GOLPE MÚLTIPLE: cuántas veces impacta (1 si es normal; al azar en el rango si no).
                int hits = move.MaxHits <= move.MinHits ? move.MinHits : _rng.Next(move.MinHits, move.MaxHits + 1);

                for (int h = 0; h < hits; h++)
                {
                    // Cada impacto recalcula su daño: la aleatoriedad y el crítico se tiran por golpe.
                    var context = new DamageContext(
                        actor.Level, attackStat, defenseStat, move.Power, effectiveness, stab, _rng, move.CritStage);
                    var result = _damageFormula.Compute(context);
                    int damage = result.Damage;
                    damageDealt += damage;

                    target.TakeDamage(damage);
                    events.Add(new DamageDealtEvent(target.Id, damage, effectiveness));
                    if (result.WasCritical) events.Add(new CriticalHitEvent(target.Id));

                    if (target.IsFainted)
                    {
                        events.Add(new MonsterFaintedEvent(target.Id));
                        return; // objetivo debilitado: ni más golpes ni efectos secundarios
                    }
                }
            }

            // EFECTOS DEL MOVIMIENTO: se aplican si el movimiento CONECTÓ. Da igual si fue un efecto
            // SECUNDARIO de un movimiento de daño (10% de quemar) o el efecto PRINCIPAL de uno de
            // estado (Fuego Fatuo, 100% de quemar y sin daño): es el MISMO mecanismo.
            ApplyMoveEffects(actor, target, move, damageDealt, events);

            // DOS TURNOS (RECARGA): si el movimiento conectó, el próximo turno deberá recargar.
            if (move.TwoTurn == TwoTurnKind.Recharge)
                actor.SetMustRecharge();
        }

        // Aplica los efectos del movimiento, cada uno sujeto a su probabilidad.
        // Aplica los efectos del movimiento, cada uno sujeto a su probabilidad. Drenaje y retroceso
        // usan el daño ya causado; la autocuración usa los PS máximos del atacante.
        private void ApplyMoveEffects(Combatant actor, Combatant target, Move move, int damageDealt, List<IDomainEvent> events)
        {
            if (move.SecondaryEffects.Count == 0) return;

            foreach (var effect in move.SecondaryEffects)
            {
                if (_rng.NextFloat() >= effect.Chance.AsFraction) continue;

                switch (effect.Kind)
                {
                    case MoveEffectKind.InflictStatus:
                        if (_statuses != null)
                        {
                            // El objetivo puede ser el rival (lo normal) o uno mismo (p.ej. Resto se duerme).
                            var statusTarget = effect.Target == EffectTarget.Self ? actor : target;
                            TryInflictStatus(statusTarget, effect.Status, events);
                        }
                        break;

                    case MoveEffectKind.Drain:
                    {
                        int heal = (int)(damageDealt * effect.Amount.AsFraction);
                        if (heal > 0 && !actor.IsFainted)
                        {
                            actor.HealHp(heal);
                            events.Add(new HpRestoredEvent(actor.Id, heal));
                        }
                        break;
                    }

                    case MoveEffectKind.Recoil:
                    {
                        int recoil = (int)(damageDealt * effect.Amount.AsFraction);
                        if (recoil > 0)
                        {
                            actor.TakeDamage(recoil);
                            events.Add(new RecoilDamageEvent(actor.Id, recoil));
                            if (actor.IsFainted)
                                events.Add(new MonsterFaintedEvent(actor.Id));
                        }
                        break;
                    }

                    case MoveEffectKind.HealSelf:
                    {
                        int heal = (int)(actor.MaxHp * effect.Amount.AsFraction);
                        if (heal > 0 && !actor.IsFainted)
                        {
                            actor.HealHp(heal);
                            events.Add(new HpRestoredEvent(actor.Id, heal));
                        }
                        break;
                    }

                    case MoveEffectKind.ChangeStatStage:
                    {
                        // Sube/baja una etapa. El objetivo puede ser uno mismo (Danza Espada) o el rival (Gruñido).
                        var affected = effect.Target == EffectTarget.Self ? actor : target;
                        if (affected.IsFainted) break;
                        int applied = affected.ChangeStage(effect.Stat, effect.Stages);
                        if (applied != 0) // si ya estaba en el tope (+6/-6), no pasa nada y no narramos
                            events.Add(new StatStageChangedEvent(affected.Id, effect.Stat, applied));
                        break;
                    }

                    case MoveEffectKind.Flinch:
                    {
                        // Marca el retroceso en el objetivo. Solo le hará perder el turno si todavía
                        // no actuó (el bucle de resolución comprueba la marca antes de cada acción).
                        var affected = effect.Target == EffectTarget.Self ? actor : target;
                        if (!affected.IsFainted) affected.SetFlinched();
                        break;
                    }
                }
            }
        }

        // Intenta infligir un estado. Reglas clásicas: no se apila otro estado principal y un
        // debilitado no se ve afectado. El estado debe existir en el catálogo (lo definió el autor).
        private void TryInflictStatus(Combatant target, StatusId statusId, List<IDomainEvent> events)
        {
            if (target.IsFainted || target.Status.HasValue) return;
            if (!TryGetStatus(statusId, out _)) return;

            target.SetStatus(statusId);
            events.Add(new StatusInflictedEvent(target.Id, statusId));
        }

        // ¿El estado del combatiente le impide actuar este turno? (parálisis/sueño/congelado).
        private bool IsPreventedByStatus(Combatant actor, List<IDomainEvent> events)
        {
            if (_statuses == null || !actor.Status.HasValue) return false;
            if (!TryGetStatus(actor.Status.Value, out var def)) return false;

            float chance = def.ActionPreventionChance.AsFraction;
            if (chance <= 0f) return false;

            if (_rng.NextFloat() < chance)
            {
                // Confusión y similares: al impedir la acción, el portador puede hacerse daño a sí mismo.
                float selfFrac = def.SelfDamageOnPreventedPercent.AsFraction;
                if (selfFrac > 0f)
                {
                    int selfDamage = Math.Max(1, (int)(actor.MaxHp * selfFrac));
                    actor.TakeDamage(selfDamage);
                    events.Add(new StatusDamageEvent(actor.Id, actor.Status.Value, selfDamage));
                    if (actor.IsFainted)
                        events.Add(new MonsterFaintedEvent(actor.Id));
                }

                events.Add(new ActionPreventedEvent(actor.Id, actor.Status.Value));
                return true;
            }
            return false;
        }

        // Fin de turno para un combatiente: avanza el contador del estado, aplica el daño residual
        // (fijo o PROGRESIVO como el tóxico), y expira el estado si cumplió su duración.
        private void ApplyEndOfTurnStatus(Combatant combatant, List<IDomainEvent> events)
        {
            if (_statuses == null || combatant.IsFainted || !combatant.Status.HasValue) return;
            if (!TryGetStatus(combatant.Status.Value, out var def)) return;

            combatant.AdvanceStatusTurn(); // ahora lleva 1, 2, 3... turnos con este estado

            // Daño residual: si es progresivo, escala con los turnos (tóxico = n × base).
            float frac = def.ResidualDamagePercent.AsFraction;
            if (frac > 0f)
            {
                float effective = def.ProgressiveResidual ? frac * combatant.StatusTurns : frac;
                int amount = Math.Max(1, (int)(combatant.MaxHp * effective));

                if (def.ResidualHeals)
                {
                    combatant.HealHp(amount);
                    events.Add(new HpRestoredEvent(combatant.Id, amount));
                }
                else
                {
                    combatant.TakeDamage(amount);
                    events.Add(new StatusDamageEvent(combatant.Id, combatant.Status.Value, amount));

                    if (combatant.IsFainted)
                    {
                        events.Add(new MonsterFaintedEvent(combatant.Id));
                        return; // debilitado: no tiene sentido seguir con la duración
                    }
                }
            }

            // Recuperación: por azar cada turno (despertar / salir de confusión) o al cumplir la duración.
            float recovery = def.RecoveryChancePerTurn.AsFraction;
            bool recovered = recovery > 0f && _rng.NextFloat() < recovery;
            bool capped = def.DurationTurns > 0 && combatant.StatusTurns >= def.DurationTurns;

            if (recovered || capped)
                EndOrTransformStatus(combatant, def, events);
        }

        // Termina el estado: si está marcado para transformarse (somnoliento -> dormido) lo cambia; si
        // no, lo cura. Reusa eventos: StatusInflicted para el nuevo estado, StatusFaded al curarse.
        private static void EndOrTransformStatus(Combatant combatant, StatusConditionDefinition def, List<IDomainEvent> events)
        {
            if (def.TransformsToStatus.HasValue)
            {
                var next = def.TransformsToStatus.Value;
                combatant.SetStatus(next); // reinicia el contador para el nuevo estado
                events.Add(new StatusInflictedEvent(combatant.Id, next));
            }
            else
            {
                var faded = combatant.Status.Value;
                combatant.ClearStatus();
                events.Add(new StatusFadedEvent(combatant.Id, faded));
            }
        }

        // Calcula la stat EFECTIVA: base × los multiplicadores pasivos del estado actual (quemado
        // baja Ataque, parálisis baja Velocidad). Nunca toca la base; la efectiva se computa al vuelo.
        private int EffectiveStat(Combatant c, StatId stat)
        {
            float value = c.Stats.Of(stat);

            // 1) Modificadores pasivos del estado (quemar -> Ataque x0.5, parálisis -> Velocidad x0.5).
            if (_statuses != null && c.Status.HasValue && TryGetStatus(c.Status.Value, out var def))
            {
                foreach (var mod in def.PassiveModifiers)
                    if (mod.Stat == stat)
                        value *= mod.Multiplier;
            }

            // 2) Etapas de combate (-6..+6). Multiplicador clásico: etapa>=0 -> (2+etapa)/2;
            //    etapa<0 -> 2/(2-etapa). Así +1 = x1.5, +2 = x2, -1 = x0.66, etc.
            value *= StageMultiplier(c.GetStage(stat));

            return (int)value;
        }

        /// <summary>Multiplicador clásico de una etapa de stat (-6..+6).</summary>
        private static float StageMultiplier(int stage)
        {
            if (stage >= 0) return (2f + stage) / 2f;
            return 2f / (2f - stage);
        }

        // El catálogo se indexa por el texto del id; envolvemos el StatusId en un Id<...> tipado.
        private bool TryGetStatus(StatusId id, out StatusConditionDefinition def)
            => _statuses.TryGet(new Id<StatusConditionDefinition>(id.Value), out def);

        // Ordena las dos jugadas: prioridad del movimiento, luego velocidad, luego moneda al aire.
        private void OrderPlays(List<(Combatant actor, Combatant target, BattleAction action)> plays)
        {
            if (plays.Count < 2) return;
            if (FirstGoesAfter(plays[0], plays[1]))
            {
                var tmp = plays[0];
                plays[0] = plays[1];
                plays[1] = tmp;
            }
        }

        // ¿La jugada 'a' debería ir DESPUÉS de 'b'? (es decir, 'b' es más rápida/prioritaria)
        private bool FirstGoesAfter(
            (Combatant actor, Combatant target, BattleAction action) a,
            (Combatant actor, Combatant target, BattleAction action) b)
        {
            int pa = PriorityOf(a.action), pb = PriorityOf(b.action);
            if (pa != pb) return pa < pb; // menor prioridad -> va después

            int sa = EffectiveStat(a.actor, StatId.Speed), sb = EffectiveStat(b.actor, StatId.Speed);
            if (sa != sb) return sa < sb; // menor velocidad -> va después

            return _rng.Next(0, 2) == 0;  // empate exacto: azar
        }

        private int PriorityOf(BattleAction action)
        {
            if (action is Flee) return 100;                 // huir va antes que atacar
            if (action is UseMove useMove) return _moves.Get(useMove.Move).Priority;
            return 0;
        }

        private static bool ContainsType(IReadOnlyList<Id<ElementType>> types, Id<ElementType> type)
        {
            for (int i = 0; i < types.Count; i++)
                if (types[i] == type)
                    return true;
            return false;
        }
    }
}
