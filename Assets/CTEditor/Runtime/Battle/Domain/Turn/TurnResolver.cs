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
                if (IsPreventedByStatus(play.actor, events)) continue; // p.ej. paralizado/dormido

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

            // --- FASE: FIN DE TURNO (daño residual de estados: veneno, quemadura) ---
            if (!battle.IsOver)
            {
                ApplyResidual(battle.Player, events);
                ApplyResidual(battle.Enemy, events);
            }

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
        private void ResolveMove(Combatant actor, Combatant target, UseMove useMove, List<IDomainEvent> events)
        {
            var move = _moves.Get(useMove.Move); // resuelve el id -> Move vía catálogo
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
            if (move.DealsDirectDamage)
            {
                // EFECTIVIDAD (tabla de tipos: tipo del movimiento contra los tipos del objetivo).
                float effectiveness = _typeChart.Effectiveness(move.Type, target.Types).Multiplier;

                // STAB: ¿el tipo del movimiento está entre los tipos del atacante?
                bool stab = ContainsType(actor.Types, move.Type);

                // SELECCIÓN DE STATS por categoría (aquí "vive" Físico vs Especial).
                int attackStat = move.Category == MoveCategory.Physical ? actor.Stats.Attack : actor.Stats.SpAttack;
                int defenseStat = move.Category == MoveCategory.Physical ? target.Stats.Defense : target.Stats.SpDefense;

                var context = new DamageContext(
                    actor.Level, attackStat, defenseStat, move.Power, effectiveness, stab, _rng);
                int damage = _damageFormula.Compute(context);

                target.TakeDamage(damage);
                events.Add(new DamageDealtEvent(target.Id, damage, effectiveness));

                if (target.IsFainted)
                {
                    events.Add(new MonsterFaintedEvent(target.Id));
                    return; // un objetivo debilitado ya no recibe efectos secundarios
                }
            }

            // EFECTOS DEL MOVIMIENTO: se aplican si el movimiento CONECTÓ. Da igual si fue un efecto
            // SECUNDARIO de un movimiento de daño (10% de quemar) o el efecto PRINCIPAL de uno de
            // estado (Fuego Fatuo, 100% de quemar y sin daño): es el MISMO mecanismo.
            ApplyMoveEffects(actor, target, move, events);
        }

        // Aplica los efectos del movimiento, cada uno sujeto a su probabilidad.
        private void ApplyMoveEffects(Combatant actor, Combatant target, Move move, List<IDomainEvent> events)
        {
            if (_statuses == null || move.SecondaryEffects.Count == 0) return;

            foreach (var effect in move.SecondaryEffects)
            {
                if (_rng.NextFloat() < effect.Chance.AsFraction)
                    TryInflictStatus(target, effect.InflictsStatus, events);
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
                events.Add(new ActionPreventedEvent(actor.Id, actor.Status.Value));
                return true;
            }
            return false;
        }

        // Daño residual de un estado al final del turno.
        private void ApplyResidual(Combatant combatant, List<IDomainEvent> events)
        {
            if (_statuses == null || combatant.IsFainted || !combatant.Status.HasValue) return;
            if (!TryGetStatus(combatant.Status.Value, out var def)) return;

            float frac = def.ResidualDamagePercent.AsFraction;
            if (frac <= 0f) return;

            int damage = Math.Max(1, (int)(combatant.MaxHp * frac));
            combatant.TakeDamage(damage);
            events.Add(new StatusDamageEvent(combatant.Id, combatant.Status.Value, damage));

            if (combatant.IsFainted)
                events.Add(new MonsterFaintedEvent(combatant.Id));
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

            int sa = a.actor.Stats.Speed, sb = b.actor.Stats.Speed;
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
