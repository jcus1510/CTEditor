using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Turn;

namespace CTEditor.Battle.Domain.AI
{
    /// <summary>
    /// IA PREDICTORA (niveles Maestro e Injusto): piensa como la experta y, además, ANTICIPA tu jugada con lo que
    /// sabe de ti (IOpponentModel):
    ///
    ///   • Si te tiene contra las cuerdas (te quita mucho y tú a él poco), supone que CAMBIARÁS y elige el ataque que
    ///     mejor golpea a quien crees que va a entrar (castiga el cambio).
    ///   • Si tu golpe previsto lo debilita antes de que actúe, se PROTEGE para ganar información (si tiene
    ///     Protección y no la acaba de usar) o remata con prioridad si puede.
    ///   • El resto de turnos juega como la experta.
    ///
    /// Solo predice un «PredictPercent» % de los turnos (el Injusto, siempre). Los cambios de Pokémon por predicción
    /// los decide el cerebro del entrenador (Adventure), con el mismo modelo.
    /// </summary>
    public sealed class PredictorBattleAI : IBattleAI
    {
        private readonly IRng _rng;
        private readonly ICatalog<Move> _moves;
        private readonly TurnResolver _resolver;
        private readonly Battle _battle;
        private readonly ExpertBattleAI _expert;
        private readonly IOpponentModel _model;
        private readonly int _predictPercent;
        private Id<Move>? _lastProtect;

        /// <summary>Qué predijo el último turno (para el editor y los tests). Null = no predijo.</summary>
        public string LastPrediction { get; private set; }

        public PredictorBattleAI(IRng rng, ICatalog<Move> moves, TurnResolver resolver, Battle battle, IOpponentModel model, int predictPercent)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _battle = battle;
            _model = model;
            _predictPercent = Math.Max(0, Math.Min(100, predictPercent));
            _expert = new ExpertBattleAI(rng, moves, resolver, battle) { Model = model };
        }

        public BattleAction ChooseAction(Combatant self, Combatant opponent)
        {
            LastPrediction = null;
            var baseline = _expert.ChooseAction(self, opponent);
            if (_model == null || opponent == null || opponent.IsFainted || self.Moves.Count == 0) return Remember(baseline);
            if (_predictPercent < 100 && _rng.NextFloat() * 100f >= _predictPercent) return Remember(baseline);

            // Lo que CREE que vas a hacer: tu ataque que más daño le hace.
            var (predicted, theirHit) = BestAgainst(opponent, self, _model.KnownMoves(opponent));
            var usable = Enumerable.Range(0, self.Moves.Count).Where(i => _resolver.CanChooseMove(self, i))
                .Select(i => _moves.TryGet(self.Moves[i], out var m) ? m : null).Where(m => m != null).ToList();
            if (usable.Count == 0) return Remember(baseline);

            float myBestPct = usable.Max(m => Pct(Damage(self, opponent, m), opponent));
            float theirPct = theirHit * 100f / Math.Max(1, self.CurrentHp);

            // 1) Te tiene contra las cuerdas: lo lógico es que cambies. Castiga al que crees que entrará.
            if (myBestPct >= 60f && theirPct < 35f)
            {
                var myTop = usable.OrderByDescending(m => Damage(self, opponent, m)).First();
                var switchIn = LikelySwitchIn(myTop);
                if (switchIn != null)
                {
                    var pick = usable.Where(m => m.DealsDirectDamage)
                        .OrderByDescending(m => 0.4f * Math.Min(100f, Pct(Damage(self, opponent, m), opponent))
                                              + 0.6f * Math.Min(100f, Pct(Damage(self, switchIn, m), switchIn)))
                        .FirstOrDefault();
                    if (pick != null)
                    {
                        LastPrediction = "cambio:" + switchIn.Id.Value;
                        return Remember(new UseMove(pick.Id));
                    }
                }
            }

            // 2) Tu golpe previsto lo debilita antes de que actúe.
            bool slower = _resolver.EffectiveSpeed(opponent) > _resolver.EffectiveSpeed(self) || (predicted?.Priority ?? 0) > 0;
            if (predicted != null && theirHit >= self.CurrentHp && slower)
            {
                // ¿Remata con prioridad?
                var priorityKo = usable.Where(m => m.Priority > (predicted.Priority) && m.DealsDirectDamage && Damage(self, opponent, m) >= opponent.CurrentHp)
                    .FirstOrDefault();
                if (priorityKo != null) { LastPrediction = "remate:" + predicted.Id.Value; return Remember(new UseMove(priorityKo.Id)); }
                // ¿Se protege? (no dos turnos seguidos)
                var protect = usable.FirstOrDefault(IsProtect);
                if (protect != null && !(_lastProtect.HasValue && _lastProtect.Value == protect.Id && self.LastMoveUsed == protect.Id))
                {
                    LastPrediction = "proteccion:" + predicted.Id.Value;
                    return Remember(new UseMove(protect.Id));
                }
            }
            return Remember(baseline);
        }

        private BattleAction Remember(BattleAction a)
        {
            _lastProtect = a is UseMove um && _moves.TryGet(um.Move, out var m) && IsProtect(m) ? um.Move : (Id<Move>?)null;
            return a;
        }

        private static bool IsProtect(Move m)
            => m.Category == MoveCategory.Status && m.SecondaryEffects.Any(e => e.Kind == MoveEffectKind.InflictStatus
               && e.Target == EffectTarget.Self && (e.Status.Value == "protect" || e.Status.Value == "kings_shield" || e.Status.Value == "spiky_shield"));

        // ¿Quién de tu reserva (que conozca) entraría a aguantar su mejor golpe? El que menos % recibe.
        private Combatant LikelySwitchIn(Move myTop)
        {
            var team = _battle?.PlayerTeam;
            if (team == null) return null;
            var self = _battle.Enemy;
            Combatant best = null; float bestPct = float.MaxValue;
            foreach (var r in team.Reserves())
            {
                if (!_model.HasSeen(r)) continue;
                float pct = Pct(Damage(self, r, myTop), r);
                if (pct < bestPct) { bestPct = pct; best = r; }
            }
            return best;
        }

        private (Move move, float damage) BestAgainst(Combatant attacker, Combatant target, IReadOnlyList<Move> moves)
        {
            Move best = null; float bestDmg = 0f;
            foreach (var m in moves ?? Array.Empty<Move>())
            {
                float d = Damage(attacker, target, m);
                if (d > bestDmg) { bestDmg = d; best = m; }
            }
            return (best, bestDmg);
        }

        private float Damage(Combatant attacker, Combatant target, Move m)
            => m == null ? 0f : ExpertBattleAI.ExpectedDamage(_resolver, _battle, attacker, target, m, _model);

        private static float Pct(float damage, Combatant target) => damage * 100f / Math.Max(1, target.CurrentHp);
    }
}
