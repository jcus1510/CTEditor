using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.AI;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using BattleAggregate = CTEditor.Battle.Domain.Battle;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// LO QUE UN ENTRENADOR SABE DE TI, según su bloque de «Conocimiento» (AiKnowledge):
    ///
    ///   Nada        ve tu Pokémon (especie, tipos, barra de PS). Supone que tienes el mejor ataque de tu tipo que
    ///               aprendes por nivel y unas estadísticas «normales» (IVs 20, EVs 85, naturaleza neutra).
    ///   Combate     además APRENDE mientras lucha: anota cada movimiento que usas y, con cada golpe, ESTIMA tus
    ///               estadísticas (si le quitas más de lo esperado, entrenaste Ataque; si aguantas más, Defensa).
    ///   Memoria     lo mismo, pero lo GUARDA en tu partida (PlayerSave.MemoryOf): en la revancha ya te conoce.
    ///   Todo        lo sabe todo desde el principio: tus movimientos y estadísticas reales (injusto).
    ///
    /// Los cálculos usan el motor real (TurnResolver.PreviewDamage) corregidos con sus estimaciones: la IA no
    /// hace trampa salvo que su nivel sea «Todo».
    /// </summary>
    public sealed class OpponentModel : IOpponentModel
    {
        // Lo que se supone de un Pokémon del jugador que aún no se conoce.
        public const int AssumedIv = 20, AssumedEv = 85;

        private readonly GameData _data;
        private readonly BattleAggregate _battle;
        private readonly TurnResolver _resolver;
        private readonly AiKnowledge _knowledge;
        private readonly TrainerMemory _memory;
        private readonly HashSet<string> _seenThisBattle = new HashSet<string>();

        public AiKnowledge Knowledge => _knowledge;
        public TrainerMemory Memory => _memory;

        /// <param name="memory">La memoria guardada (conocimiento «Memoria»); con «Combate» o menos, una nueva que no se guarda.</param>
        public OpponentModel(GameData data, BattleAggregate battle, TurnResolver resolver, AiKnowledge knowledge, TrainerMemory memory)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _knowledge = knowledge;
            _memory = memory ?? new TrainerMemory();
            if (battle.Player != null) _seenThisBattle.Add(battle.Player.Id.Value);
        }

        public bool IsOpponent(Combatant c) => c != null && _battle.PlayerTeam.Find(c.Id) != null;

        public bool HasSeen(Combatant c)
        {
            if (c == null) return false;
            if (_knowledge == AiKnowledge.Omniscient) return true;
            if (_seenThisBattle.Contains(c.Id.Value)) return true;
            return _knowledge == AiKnowledge.Memory && _memory.Knows(c.Id.Value);
        }

        // ---------------- Movimientos ----------------

        public IReadOnlyList<Move> KnownMoves(Combatant opp)
        {
            if (opp == null) return Array.Empty<Move>();
            if (!IsOpponent(opp) || _knowledge == AiKnowledge.Omniscient) return Resolve(opp.Moves);
            var list = new List<Move>();
            if (_knowledge != AiKnowledge.None && _memory.Knows(opp.Id.Value))
                foreach (var id in _memory.About(opp.Id.Value).Moves)
                    if (_data.Moves.TryGet(new Id<Move>(id), out var m)) list.Add(m);
            // Si aún no le ha visto ningún ataque, supone el mejor de su tipo que aprende por nivel.
            if (!list.Any(m => m.DealsDirectDamage))
                list.AddRange(GuessedAttacks(opp));
            return list;
        }

        private List<Move> Resolve(IReadOnlyList<Id<Move>> ids)
        {
            var list = new List<Move>();
            foreach (var id in ids) if (_data.Moves.TryGet(id, out var m)) list.Add(m);
            return list;
        }

        /// <summary>Lo que un buen jugador supondría: el ataque más fuerte de cada uno de sus tipos que aprende a su nivel.</summary>
        public List<Move> GuessedAttacks(Combatant opp)
        {
            var result = new List<Move>();
            if (!_data.Species.TryGet(opp.SpeciesId, out var sp)) return result;
            bool physical = sp.BaseStats.Attack >= sp.BaseStats.SpAttack;
            foreach (var type in opp.Types)
            {
                Move best = null; double bestScore = 0;
                foreach (var lm in sp.Learnset)
                {
                    if (lm.Level > opp.Level || !_data.Moves.TryGet(lm.Move, out var m) || !m.DealsDirectDamage || m.Type != type) continue;
                    double score = m.Power * ((m.Category == MoveCategory.Physical) == physical ? 1.0 : 0.7);
                    if (score > bestScore) { bestScore = score; best = m; }
                }
                if (best != null) result.Add(best);
            }
            return result;
        }

        // ---------------- Estadísticas estimadas ----------------

        public float Belief(Combatant attacker, Combatant target, Move move)
        {
            if (_knowledge == AiKnowledge.Omniscient || move == null) return 1f;
            float f = 1f;
            if (IsOpponent(attacker) && move.FixedDamage == FixedDamageKind.None)
            {
                var stat = move.AttackStat ?? (move.Category == MoveCategory.Physical ? StatId.Attack : StatId.SpAttack);
                f *= (float)(Estimated(attacker, stat) / Math.Max(1, attacker.Stats.Of(stat)));
            }
            if (IsOpponent(target) && move.FixedDamage == FixedDamageKind.None)
            {
                var stat = move.DefenseStat ?? (move.Category == MoveCategory.Physical ? StatId.Defense : StatId.SpDefense);
                f *= (float)(Math.Max(1, target.Stats.Of(stat)) / Math.Max(1.0, Estimated(target, stat)));
            }
            return f;
        }

        /// <summary>La estadística que CREE que tiene tu Pokémon (sin etapas): la supuesta × su estimación.</summary>
        public double Estimated(Combatant c, StatId stat)
        {
            double assumed = Assumed(c, stat);
            if (_knowledge == AiKnowledge.None) return assumed;
            return _memory.Knows(c.Id.Value) ? assumed * _memory.About(c.Id.Value).FactorOf(stat.Value) : assumed;
        }

        /// <summary>La estadística «normal» de esa especie a ese nivel (IVs 20, EVs 85, naturaleza neutra).</summary>
        public double Assumed(Combatant c, StatId stat)
        {
            if (!_data.Species.TryGet(c.SpeciesId, out SpeciesDef sp)) return Math.Max(1, c.Stats.Of(stat));
            int b = sp.BaseStats.Of(stat);
            if (b <= 0) return Math.Max(1, c.Stats.Of(stat));
            return Math.Max(1, _data.Growth.Compute(stat, b, c.Level, AssumedIv, AssumedEv, 100));
        }

        // ---------------- Aprender de lo que pasa en el combate ----------------

        /// <summary>
        /// Mira los sucesos de un turno: anota tus movimientos y, con cada golpe normal (sin crítico, de un solo impacto,
        /// sin daño fijo ni fórmula), estima tu Ataque (si golpeas) o tu Defensa (si recibes).
        /// </summary>
        public void Observe(IReadOnlyList<IDomainEvent> events)
        {
            if (_knowledge == AiKnowledge.None || _knowledge == AiKnowledge.Omniscient || events == null) return;
            Combatant attacker = null; Move move = null;
            for (int i = 0; i < events.Count; i++)
            {
                switch (events[i])
                {
                    case SentOutEvent so: _seenThisBattle.Add(so.Combatant.Value); Touch(so.Combatant); break;
                    case MonsterSentEvent ms: _seenThisBattle.Add(ms.Combatant.Value); Touch(ms.Combatant); break;
                    case MoveUsedEvent mu:
                        attacker = FindAny(mu.Attacker);
                        move = _data.Moves.TryGet(mu.Move, out var m) ? m : null;
                        if (attacker != null && move != null && IsOpponent(attacker))
                        {
                            _seenThisBattle.Add(attacker.Id.Value);
                            _memory.About(attacker.Id.Value, attacker.SpeciesId.Value).Moves.Add(move.Id.Value);
                        }
                        break;
                    case DamageDealtEvent dd when attacker != null && move != null && dd.Amount > 0:
                    {
                        bool crit = i + 1 < events.Count && events[i + 1] is CriticalHitEvent;
                        bool clean = !crit && move.FixedDamage == FixedDamageKind.None && move.PowerFormula == null
                                     && move.MinHits == 1 && move.MaxHits == 1 && move.PowerModifiers.Count == 0;
                        var target = FindAny(dd.Target);
                        if (!clean || target == null || target.IsFainted && dd.Amount >= target.MaxHp) break;
                        float real = ExpertBattleAI.ExpectedDamage(_resolver, _battle, attacker, target, move);
                        if (real <= 0f) break;
                        double ratio = dd.Amount / (double)real;   // observado / esperado con tus estadísticas reales (≈ la tirada)
                        if (IsOpponent(attacker))
                        {
                            var stat = move.AttackStat ?? (move.Category == MoveCategory.Physical ? StatId.Attack : StatId.SpAttack);
                            double implied = attacker.Stats.Of(stat) * ratio;
                            _memory.About(attacker.Id.Value, attacker.SpeciesId.Value).Observe(stat.Value, implied / Assumed(attacker, stat));
                        }
                        else if (IsOpponent(target))
                        {
                            var stat = move.DefenseStat ?? (move.Category == MoveCategory.Physical ? StatId.Defense : StatId.SpDefense);
                            double implied = target.Stats.Of(stat) / Math.Max(0.01, ratio);
                            _memory.About(target.Id.Value, target.SpeciesId.Value).Observe(stat.Value, implied / Assumed(target, stat));
                        }
                        break;
                    }
                }
            }
        }

        private void Touch(Id<BattleParticipant> id)
        {
            var c = _battle.PlayerTeam.Find(id);
            if (c != null) _memory.About(c.Id.Value, c.SpeciesId.Value);
        }

        private Combatant FindAny(Id<BattleParticipant> id) => _battle.PlayerTeam.Find(id) ?? _battle.EnemyTeam.Find(id);
    }
}
