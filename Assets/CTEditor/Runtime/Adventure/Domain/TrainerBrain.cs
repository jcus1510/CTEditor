using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.AI;
using CTEditor.Battle.Domain.Turn;
using BattleAggregate = CTEditor.Battle.Domain.Battle;

namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// EL CEREBRO de un entrenador rival: cada turno decide si usa un OBJETO de su mochila, si CAMBIA de
    /// monstruo o qué MOVIMIENTO usa, según su NIVEL DE IA (1-5, ver AiProfile) y sus ajustes propios:
    ///
    ///   Cómo elige el movimiento: al azar, «el más eficaz» o experto (daño real, remates, estados).
    ///   Fallos: un % de turnos usa un movimiento al azar (el Campeón nunca).
    ///   Objetos: un % de veces se acuerda; la cura puede ser SIMPLE (en cuanto baja del umbral) o
    ///            INTELIGENTE: no se cura si el rival lo tumba igual, si le quita más de lo que cura o si
    ///            puede rematar él primero.
    ///   Cambios: si pierde claramente el duelo, saca a un compañero que lo gane.
    ///
    /// Su mochila es una copia para este combate: lo que gasta no se pierde de la ficha del entrenador.
    /// </summary>
    public sealed class TrainerBrain
    {
        private readonly GameData _data;
        private readonly BattleAggregate _battle;
        private readonly TurnResolver _resolver;
        private readonly TrainerDefinition _trainer;
        private readonly IRng _rng;
        private readonly IBattleAI _moveAi;
        private readonly IBattleAI _randomAi;
        private readonly AiProfile _profile;
        private readonly Dictionary<string, int> _bag = new Dictionary<string, int>();
        private readonly HashSet<string> _boosted = new HashSet<string>();
        private int _turn, _lastSwitchTurn = -10;

        public TrainerBrain(GameData data, BattleAggregate battle, TurnResolver resolver, TrainerDefinition trainer, IRng rng)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _trainer = trainer ?? throw new ArgumentNullException(nameof(trainer));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _profile = data.AiProfileFor(trainer);
            _randomAi = new SimpleBattleAI(rng) { CanUse = resolver.CanChooseMove };
            switch (_profile.Brain)
            {
                case MoveBrain.Random: _moveAi = _randomAi; break;
                case MoveBrain.Expert: _moveAi = new ExpertBattleAI(rng, data.Moves, resolver, battle); break;
                default: _moveAi = new AggressiveBattleAI(rng, data.Moves, data.TypeChart) { CanUse = resolver.CanChooseMove }; break;
            }
            // Su mochila; si no trae, la de su nivel de IA (si usa objetos).
            var bag = trainer.AiSettings.Items.Count > 0 ? trainer.AiSettings.Items : _profile.DefaultBag;
            foreach (var (id, qty) in bag)
                _bag[id] = (_bag.TryGetValue(id, out var n) ? n : 0) + qty;
        }

        /// <summary>El nivel de IA con el que piensa.</summary>
        public AiProfile Profile => _profile;

        /// <summary>Lo que le queda en la mochila (id → cantidad).</summary>
        public IReadOnlyDictionary<string, int> Bag => _bag;

        /// <summary>La decisión del turno: la acción y, si es un objeto, cuál (para narrarlo).</summary>
        public (BattleAction action, string itemId) Decide()
        {
            _turn++;
            var self = _battle.Enemy;
            var foe = _battle.Player;

            // Cargando o recargando: no hay decisión posible.
            if (self.ChargingMove.HasValue || self.MustRecharge)
                return (new UseMove(self.ChargingMove ?? (self.Moves.Count > 0 ? self.Moves[0] : default)), null);

            var item = ChooseItem(self, foe);
            if (item != null)
            {
                _bag[item.Id]--;
                if (_bag[item.Id] <= 0) _bag.Remove(item.Id);
                if (item.BattleStages != 0) _boosted.Add(self.Id.Value);
                return (new UseItemAction(self.Id, BattleItemEffect.From(item)), item.Id);
            }

            var sw = ChooseSwitch(self, foe);
            if (sw != null) { _lastSwitchTurn = _turn; return (new SwitchMonster(sw.Id), null); }

            // Un fallo de vez en cuando (según su nivel): elige al azar.
            if (_profile.MistakePercent > 0 && _rng.NextFloat() * 100f < _profile.MistakePercent)
                return (_randomAi.ChooseAction(self, foe), null);
            return (_moveAi.ChooseAction(self, foe), null);
        }

        // ---------------- Objetos ----------------

        private ItemDefinition ChooseItem(Combatant self, Combatant foe)
        {
            var settings = _trainer.AiSettings;
            if (!settings.UseItems || _bag.Count == 0 || self.IsFainted) return null;
            // Los despistados no siempre se acuerdan de sus objetos.
            if (_profile.ItemUsePercent < 100 && _rng.NextFloat() * 100f >= _profile.ItemUsePercent) return null;

            var items = new List<ItemDefinition>();
            foreach (var id in _bag.Keys)
                if (_data.TryGetItem(id, out var it) && it.UsableInBattle && !it.IsBall) items.Add(it);
            if (items.Count == 0) return null;

            // 1) CURAR.
            float hpPct = self.CurrentHp * 100f / Math.Max(1, self.MaxHp);
            // Umbral: el propio del entrenador si lo cambió; si no, el de su nivel.
            int threshold = settings.HealBelowPercent != TrainerAiSettings.Default.HealBelowPercent ? settings.HealBelowPercent : _profile.HealBelowPercent;
            var heal = _profile.Heal == HealStyle.Never ? null : BestHealItem(self, items);
            if (heal != null)
            {
                int healed = Math.Min(self.MaxHp, self.CurrentHp + HealAmount(self, heal));
                if (_profile.Heal == HealStyle.Simple ? hpPct <= threshold : WorthHealing(self, foe, healed, hpPct, threshold))
                    return heal;
            }

            // 2) QUITAR UN ESTADO (dormido, paralizado, quemado...).
            if (self.Status.HasValue)
                foreach (var it in items)
                    if (it.CuresStatus && it.CuresThis(self.Status.Value.Value)) return it;

            // 3) MEJORAS (Ataque X...): una vez por monstruo, sano y sin la mejora ya puesta.
            if (hpPct >= 70f && !_boosted.Contains(self.Id.Value))
                foreach (var it in items)
                    if (it.BattleStages > 0 && !string.IsNullOrEmpty(it.BattleStatId) && self.GetStage(new StatId(it.BattleStatId)) < 1)
                        return it;

            return null;
        }

        // ---------------- Cambiar de monstruo (solo Experto) ----------------

        private Combatant ChooseSwitch(Combatant self, Combatant foe)
        {
            if (!_profile.CanSwitch || !_trainer.AiSettings.CanSwitch) return null;
            if (_turn - _lastSwitchTurn < 3 || !_resolver.CanSwitchOut(self)) return null;
            var reserves = _battle.EnemyTeam.Reserves();
            if (reserves.Count == 0) return null;

            float myHit = BestDamage(self, foe) * 100f / Math.Max(1, foe.CurrentHp);   // % de SUS PS que le quito
            float theirHit = BestDamage(foe, self) * 100f / Math.Max(1, self.CurrentHp); // % de MIS PS que me quita
            // Solo si pierde claramente el duelo: le hacen mucho y él apenas hace nada.
            if (theirHit < 60f || myHit >= 30f) return null;

            Combatant best = null;
            float bestValue = 0f;
            foreach (var r in reserves)
            {
                float rMy = BestDamage(r, foe) * 100f / Math.Max(1, foe.CurrentHp);
                float rTheir = BestDamage(foe, r) * 100f / Math.Max(1, r.CurrentHp);
                if (rTheir >= theirHit * 0.6f || rMy <= myHit) continue; // no mejora lo bastante
                float value = rMy - rTheir;
                if (best == null || value > bestValue) { best = r; bestValue = value; }
            }
            return best;
        }

        // ---------------- Curación inteligente ----------------

        private ItemDefinition BestHealItem(Combatant self, List<ItemDefinition> items)
        {
            int missing = self.MaxHp - self.CurrentHp;
            if (missing <= 0) return null;
            ItemDefinition bestFit = null, biggest = null;
            int bestFitHeal = int.MaxValue, biggestHeal = 0;
            foreach (var it in items)
            {
                if (!it.HealsHp) continue;
                int h = HealAmount(self, it);
                if (h >= missing && h < bestFitHeal) { bestFit = it; bestFitHeal = h; }   // la más pequeña que llena
                if (h > biggestHeal) { biggest = it; biggestHeal = h; }
            }
            return bestFit ?? biggest;
        }

        private static int HealAmount(Combatant self, ItemDefinition it) => it.HealHp + (int)(self.MaxHp * it.HealPercent / 100f);

        /// <summary>
        /// ¿Le SIRVE curarse? Piensa como un buen jugador:
        ///   • Si puede debilitar al rival este turno (y no cae antes), ataca.
        ///   • Si el rival le quita tanto o más de lo que cura, curarse es perder el turno: ataca.
        ///   • Si con la cura aguanta al menos UN golpe más que sin ella, se cura.
        /// Solo lo piensa con poca vida (umbral) o cuando el próximo golpe lo debilitaría.
        /// </summary>
        public bool WorthHealing(Combatant self, Combatant foe, int healedHp, float hpPct, int threshold)
        {
            float theirHit = BestDamage(foe, self);
            bool inDanger = hpPct <= threshold || (theirHit > 0 && theirHit >= self.CurrentHp);
            if (!inDanger) return false;
            if (theirHit <= 0) return hpPct <= threshold;

            // ¿Remata antes? (es más rápido o aguanta su golpe)
            float myHit = BestDamage(self, foe);
            bool faster = _resolver.EffectiveSpeed(self) >= _resolver.EffectiveSpeed(foe);
            if (myHit >= foe.CurrentHp && (faster || theirHit < self.CurrentHp)) return false;

            // Lo que gana: tras curarse recibe un golpe igualmente.
            if (healedHp - theirHit <= self.CurrentHp * 0.5f + 1) return false;   // pierde casi todo lo curado: inútil
            int hitsNow = (int)Math.Ceiling(self.CurrentHp / theirHit);
            int hitsAfter = (int)Math.Ceiling(healedHp / theirHit) - 1;           // el turno de la cura también recibe
            return hitsAfter >= hitsNow + 1;
        }

        // El mayor daño esperado de 'attacker' contra 'target' con sus movimientos (con PP).
        private float BestDamage(Combatant attacker, Combatant target)
        {
            float best = 0f;
            for (int i = 0; i < attacker.Moves.Count; i++)
            {
                if (attacker.PpAt(i) == 0 || !_data.Moves.TryGet(attacker.Moves[i], out Move move)) continue;
                best = Math.Max(best, ExpertBattleAI.ExpectedDamage(_resolver, _battle, attacker, target, move));
            }
            return best;
        }
    }
}
