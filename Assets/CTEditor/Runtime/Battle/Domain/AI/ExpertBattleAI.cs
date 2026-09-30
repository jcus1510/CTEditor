using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Turn;

namespace CTEditor.Battle.Domain.AI
{
    /// <summary>
    /// IA EXPERTA: piensa cada movimiento con el MISMO cálculo de daño que el combate (TurnResolver.PreviewDamage:
    /// estadísticas reales, etapas, tipos, habilidades, clima...) y luego decide como lo haría un buen jugador:
    ///
    ///   • Si puede DEBILITAR al rival, remata (prefiere el golpe más seguro y, a igualdad, el más rápido).
    ///   • Si no, el que más % de PS quita, multiplicado por su probabilidad de acertar.
    ///   • Nunca usa un golpe que no afecta (inmune).
    ///   • Movimientos de apoyo con cabeza: mejora sus estadísticas si está sano y aún no lo hizo, pone un
    ///     estado solo si el rival no tiene ninguno, se cura cuando está bajo, pone trampas una vez.
    ///
    /// Quién cambia de monstruo y cuándo usa objetos lo decide el "cerebro" del entrenador (Adventure);
    /// esta clase solo elige el MOVIMIENTO.
    /// </summary>
    public sealed class ExpertBattleAI : IBattleAI
    {
        private readonly IRng _rng;
        private readonly ICatalog<Move> _moves;
        private readonly TurnResolver _resolver;
        private readonly Battle _battle;
        // Trampas que ya puso (para no repetirlas sin fin).
        private readonly HashSet<string> _hazardsSet = new HashSet<string>();
        // Efectos de lado que ya puso (Reflejo...): no los repite.
        private readonly HashSet<string> _sidesSet = new HashSet<string>();

        /// <summary>Lo que cree saber del rival (null = lo sabe todo: usa el daño real).</summary>
        public IOpponentModel Model { get; set; }

        public ExpertBattleAI(IRng rng, ICatalog<Move> moves, TurnResolver resolver, Battle battle)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _battle = battle;
        }

        public BattleAction ChooseAction(Combatant self, Combatant opponent)
        {
            if (self.Moves.Count == 0) return new Flee();

            float best = float.MinValue;
            var bestIdx = new List<int>();
            for (int i = 0; i < self.Moves.Count; i++)
            {
                if (!_resolver.CanChooseMove(self, i)) continue;   // PP, Mofa, Tormento, Anticura...
                if (!_moves.TryGet(self.Moves[i], out var move)) continue;
                float score = Score(move, self, opponent);
                if (score > best + 0.001f) { best = score; bestIdx.Clear(); bestIdx.Add(i); }
                else if (Math.Abs(score - best) <= 0.001f) bestIdx.Add(i);
            }
            if (bestIdx.Count == 0) return new UseMove(self.Moves[0]); // sin PP: el motor hace Forcejeo

            int chosen = bestIdx[_rng.Next(0, bestIdx.Count)];
            if (_moves.TryGet(self.Moves[chosen], out var picked))
                foreach (var fx in picked.SecondaryEffects)
                {
                    if (fx.Kind == MoveEffectKind.SetHazard && !string.IsNullOrEmpty(fx.HazardId)) _hazardsSet.Add(fx.HazardId);
                    if (fx.Kind == MoveEffectKind.SetSideCondition && !string.IsNullOrEmpty(fx.SideConditionId)) _sidesSet.Add(fx.SideConditionId);
                }
            return new UseMove(self.Moves[chosen]);
        }

        /// <summary>Daño esperado (media de la tirada, sin crítico, por los golpes medios) de 'move' AHORA.</summary>
        public static float ExpectedDamage(TurnResolver resolver, Battle battle, Combatant attacker, Combatant target, Move move)
            => ExpectedDamage(resolver, battle, attacker, target, move, null);

        /// <summary>Igual, pero con lo que la IA CREE (estimaciones de las estadísticas del jugador).</summary>
        public static float ExpectedDamage(TurnResolver resolver, Battle battle, Combatant attacker, Combatant target, Move move, IOpponentModel model)
        {
            float belief = model?.Belief(attacker, target, move) ?? 1f;
            return belief * RawExpected(resolver, battle, attacker, target, move);
        }

        private static float RawExpected(TurnResolver resolver, Battle battle, Combatant attacker, Combatant target, Move move)
        {
            if (!move.DealsDirectDamage) return 0f;
            var p = resolver.PreviewDamage(attacker, target, move.Id, battle);
            if (!p.DealsDamage || p.Effectiveness <= 0f || p.ImmuneByAbility) return 0f;
            float hits = (p.MinHits + p.MaxHits) / 2f;
            float acc = p.AccuracyPercent.HasValue ? p.AccuracyPercent.Value / 100f : 1f;
            return (p.Min + p.Max) / 2f * Math.Max(1f, hits) * acc;
        }

        private float Score(Move move, Combatant self, Combatant target)
        {
            float hpPct = self.MaxHp > 0 ? self.CurrentHp * 100f / self.MaxHp : 0f;

            if (move.DealsDirectDamage)
            {
                var p = _resolver.PreviewDamage(self, target, move.Id, _battle);
                if (!p.DealsDamage || p.Effectiveness <= 0f || p.ImmuneByAbility) return -1f; // no le afecta
                float hits = Math.Max(1f, (p.MinHits + p.MaxHits) / 2f);
                float acc = p.AccuracyPercent.HasValue ? p.AccuracyPercent.Value / 100f : 1f;
                float belief = Model?.Belief(self, target, move) ?? 1f;   // su estimación de tu Defensa
                float minTotal = p.Min * hits * belief;
                float avg = (p.Min + p.Max) / 2f * hits * belief;
                // Remate seguro: con la tirada MÍNIMA ya lo debilita.
                if (minTotal >= target.CurrentHp) return 300f + acc * 100f + move.Priority * 5f;
                // Remate probable: con la media.
                if (avg >= target.CurrentHp) return 200f + acc * 100f + move.Priority * 5f;
                return Math.Min(100f, avg * 100f / Math.Max(1, target.CurrentHp)) * acc;
            }

            // --- Movimientos de apoyo ---
            float support = 0f;
            foreach (var fx in move.SecondaryEffects)
            {
                switch (fx.Kind)
                {
                    case MoveEffectKind.InflictStatus:
                        if (fx.Target != EffectTarget.Self && !target.Status.HasValue) support = Math.Max(support, 45f);
                        break;
                    case MoveEffectKind.ChangeStatStage:
                        if (fx.Target == EffectTarget.Self && fx.Stages > 0)
                        {
                            int stage = self.GetStage(fx.Stat);
                            if (hpPct >= 60f && stage < 2) support = Math.Max(support, 40f - stage * 12f);
                        }
                        else if (fx.Target != EffectTarget.Self && fx.Stages < 0 && target.GetStage(fx.Stat) > -2)
                            support = Math.Max(support, 18f);
                        break;
                    case MoveEffectKind.HealSelf:
                    case MoveEffectKind.Heal:
                        if (fx.Target == EffectTarget.Self || fx.Kind == MoveEffectKind.HealSelf)
                            support = Math.Max(support, hpPct < 50f ? 70f : 0f);
                        break;
                    case MoveEffectKind.SetHazard:
                        if (!_hazardsSet.Contains(fx.HazardId)) support = Math.Max(support, 30f);
                        break;
                    case MoveEffectKind.SetWeather:
                    case MoveEffectKind.ForceSwitch:
                        support = Math.Max(support, 10f);
                        break;
                    // Cierre del combate: apoyo con cabeza.
                    case MoveEffectKind.SetSideCondition:
                        if (!_sidesSet.Contains(fx.SideConditionId)) support = Math.Max(support, 35f);
                        break;
                    case MoveEffectKind.Substitute:
                        if (!self.HasSubstitute && hpPct > 50f) support = Math.Max(support, 35f);
                        break;
                    case MoveEffectKind.ResetStages:
                        if (target.HasAnyStage && (fx.Target != EffectTarget.Self)) support = Math.Max(support, 40f);
                        break;
                    case MoveEffectKind.CritBoost:
                        if (self.CritBonus == 0 && hpPct > 60f) support = Math.Max(support, 20f);
                        break;
                    case MoveEffectKind.DisableMove:
                    case MoveEffectKind.Encore:
                        if (target.LastMoveUsed.HasValue && !target.DisabledMove.HasValue && !target.EncoreMove.HasValue) support = Math.Max(support, 18f);
                        break;
                    case MoveEffectKind.CallRandomMove:
                    case MoveEffectKind.Transform:
                        support = Math.Max(support, 15f);
                        break;
                }
            }
            return support;
        }
    }
}
