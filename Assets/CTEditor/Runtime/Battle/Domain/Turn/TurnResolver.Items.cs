using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Battle.Domain.Events;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>Where a block comes from: the held item, or an ability's blocks that are not an ability shape (they run like
    /// a held item's: «al entrar: curar 10 %»...). Abilities are never used up.</summary>
    internal sealed class EffectSource
    {
        public ItemDefinition Item;
        public AbilityDefinition Ability;
        public string Id => Item != null ? Item.Id : Ability.Id.Value;
        public bool IsBerry => Item != null && Item.IsBerry;
        public string Key(int index) => (Item != null ? "item:" + Item.Id : "ability:" + Ability.Id.Value) + "#" + index;
    }

    /// <summary>
    /// HELD ITEMS as EFFECT BLOCKS. Every held item is a list of «when / if / then» blocks; the turn calls these helpers at
    /// each trigger point (end of turn, before/after a hit, on contact, on status, low HP...) and for the passive queries
    /// (stat, power, accuracy, crit, immunities...). Each block honours its conditions, its chance, its uses per battle and
    /// whether it uses the item up. Nothing here is specific to an item or a generation.
    /// </summary>
    public sealed partial class TurnResolver
    {
        /// <summary>Real type of the hit while a DEFENDER's blocks are evaluated (MoveType conditions use the attacker's type).</summary>
        private Id<ElementType>? _hitTypeOverride;

        /// <summary>Nesting of «si le bajan una estadística» blocks (they can answer each other).</summary>
        private int _statDropDepth;

        private static string UseKey(EffectSource src, int index) => src.Key(index);

        /// <summary>The blocks for one trigger of the working held item (none if items are off, suppressed or missing) and of
        /// the ability's generic blocks (the ones that are not an ability shape).</summary>
        private List<(EffectSource item, int index, EffectBlock block)> HeldBlocks(Combatant c, EffectTrigger trigger, EffectAction? action = null)
        {
            var list = new List<(EffectSource, int, EffectBlock)>();
            if (c == null) return list;
            if (TryGetHeldItem(c, out var item))
            {
                var src = new EffectSource { Item = item };
                for (int i = 0; i < item.Effects.Count; i++)
                {
                    var b = item.Effects[i];
                    if (b.Trigger == trigger && (!action.HasValue || b.Action == action.Value)) list.Add((src, i, b));
                }
            }
            if (TryGetAbility(c, out var ab) && ab.GenericEffects.Count > 0)
            {
                var src = new EffectSource { Ability = ab };
                for (int i = 0; i < ab.GenericEffects.Count; i++)
                {
                    var b = ab.GenericEffects[i];
                    if (b.Trigger == trigger && !b.Consumes && (!action.HasValue || b.Action == action.Value)) list.Add((src, i, b));
                }
            }
            return list;
        }

        /// <summary>Does this block fire now? Uses left, berry allowed, conditions and — last, only if needed — its chance roll.</summary>
        private bool Fires(Combatant holder, EffectSource item, int index, EffectBlock b, Combatant other, Move move, Id<ElementType>? hitType = null)
        {
            if (b.MaxPerBattle > 0 && holder.EffectUses(UseKey(item, index)) >= b.MaxPerBattle) return false;
            // Nerviosismo: the foe does not let it eat berries.
            if (b.Consumes && item.IsBerry)
            {
                var foe = OpponentOf(holder);
                if (foe != null && !foe.IsFainted && X(foe).Unnerve) return false;
            }
            var saved = _hitTypeOverride;
            _hitTypeOverride = hitType;
            try { if (!AllConditions(b.Conditions, holder, other, move)) return false; }
            finally { _hitTypeOverride = saved; }
            if (b.Chance < 100f && !(_rng.NextFloat() * 100f < b.Chance)) return false;
            return true;
        }

        private void NoteFired(Combatant holder, EffectSource item, int index, EffectBlock b)
        {
            if (b.MaxPerBattle > 0) holder.NoteEffectUse(UseKey(item, index));
        }

        // ================================================================= PASSIVE QUERIES

        /// <summary>Product of the passive multipliers of one kind (1 = none).</summary>
        private float HeldProduct(Combatant c, EffectAction action, Combatant other, Move move, Func<EffectBlock, bool> filter = null)
        {
            float m = 1f;
            foreach (var (item, i, b) in HeldBlocks(c, EffectTrigger.Passive, action))
                if ((filter == null || filter(b)) && Fires(c, item, i, b, other, move)) { m *= b.Amount; NoteFired(c, item, i, b); }
            return m;
        }

        /// <summary>Sum of the passive amounts of one kind (0 = none).</summary>
        private float HeldSum(Combatant c, EffectAction action, Combatant other, Move move, Func<EffectBlock, bool> filter = null)
        {
            float s = 0f;
            foreach (var (item, i, b) in HeldBlocks(c, EffectTrigger.Passive, action))
                if ((filter == null || filter(b)) && Fires(c, item, i, b, other, move)) { s += b.Amount; NoteFired(c, item, i, b); }
            return s;
        }

        /// <summary>Is a passive rule active (Choice lock, no status moves...)?</summary>
        private bool HeldHas(Combatant c, EffectAction action, Combatant other, Move move, Func<EffectBlock, bool> filter = null)
        {
            foreach (var (item, i, b) in HeldBlocks(c, EffectTrigger.Passive, action))
                if ((filter == null || filter(b)) && Fires(c, item, i, b, other, move)) return true;
            return false;
        }

        /// <summary>Is the holder immune to this type because of its item (Air Balloon: Ground)?</summary>
        private bool HeldImmuneTo(Combatant c, Id<ElementType> type, Combatant attacker = null, Move move = null)
            => HeldHas(c, EffectAction.ImmuneToType, attacker, move, b => string.Equals(b.Ref, type.Value, StringComparison.OrdinalIgnoreCase));

        /// <summary>Stat multiplier from the item for this stat (Choice Band, Assault Vest, Eviolite...).</summary>
        private float HeldStatMultiplier(Combatant c, StatId stat)
            => HeldProduct(c, EffectAction.MultiplyStat, OpponentOf(c), null, b => string.Equals(b.Ref, stat.Value, StringComparison.OrdinalIgnoreCase));

        /// <summary>Extra turns for the weather the holder sets (weather rocks).</summary>
        private int HeldWeatherBonus(Combatant c, string weatherId)
            => (int)HeldSum(c, EffectAction.ExtendWeather, OpponentOf(c), null,
                b => b.Ref.Length == 0 || string.Equals(b.Ref, weatherId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Acts first within its priority this turn (Quick Claw): rolled once per turn.</summary>
        private bool HeldActsFirst(Combatant c)
            => c != null && !c.IsFainted && HeldHas(c, EffectAction.ActFirst, OpponentOf(c), null);

        // ================================================================= INSTANT TRIGGERS

        /// <summary>
        /// Fires the held item's blocks for a trigger. The blocks that pass are chosen FIRST (so the item being used up by one
        /// does not stop the others of the same moment), then the item is used up once if any of them consumes it, then they run
        /// in order. 'applies' is checked before the chance roll (e.g. «the target can still flinch»). Returns true if any fired.
        /// </summary>
        private bool RunHeld(Combatant holder, EffectTrigger trigger, Combatant other, Move move, List<IDomainEvent> events,
            int damageDealt = 0, StatusId? justGot = null, Func<EffectBlock, bool> applies = null, Id<ElementType>? hitType = null)
        {
            var fired = new List<(EffectSource item, int index, EffectBlock block)>();
            foreach (var (item, i, b) in HeldBlocks(holder, trigger))
            {
                if (!EffectRules.InstantActions.Contains(b.Action)) continue;
                if (applies != null && !applies(b)) continue;
                if (Fires(holder, item, i, b, other, move, hitType)) fired.Add((item, i, b));
            }
            if (fired.Count == 0) return false;

            // Announced once per source (the item, then the ability).
            var announced = new HashSet<EffectSource>();
            Action AnnounceFor(EffectSource src) => () =>
            {
                if (!announced.Add(src)) return;
                if (src.Item != null) events.Add(new HeldItemActivatedEvent(holder.Id, src.Item.Id, false));
                else events.Add(new AbilityTriggeredEvent(holder.Id, src.Ability.Id.Value));
            };
            var usedUp = fired.FirstOrDefault(f => f.block.Consumes && f.item.Item != null).item;
            if (usedUp != null) { UseUpItem(holder, events); announced.Add(usedUp); }

            foreach (var (item, i, b) in fired)
            {
                NoteFired(holder, item, i, b);
                var who = b.Target == BlockTarget.Self ? holder : other;
                Execute(holder, who, item, trigger, b, move, damageDealt, justGot, events, AnnounceFor(item));
            }
            return true;
        }

        private void Execute(Combatant holder, Combatant who, EffectSource item, EffectTrigger trigger, EffectBlock b, Move move,
            int damageDealt, StatusId? justGot, List<IDomainEvent> events, Action announce)
        {
            if (who == null) return;
            switch (b.Action)
            {
                case EffectAction.HealHp:
                case EffectAction.HealPercent:
                case EffectAction.HealFromDamagePercent:
                {
                    if (who.IsFainted || who.CurrentHp >= who.MaxHp || HasVolatileFlag(who, d => d.Extras.BlocksHealing)) return;
                    int amount = b.Action == EffectAction.HealHp ? (int)b.Amount
                        : b.Action == EffectAction.HealPercent ? Math.Max(1, (int)(who.MaxHp * b.Amount / 100f))
                        : Math.Max(1, (int)(damageDealt * b.Amount / 100f));
                    // CARRILLO: eating a berry heals an extra % of max HP.
                    if (item.IsBerry && b.Consumes && who == holder && b.Action != EffectAction.HealFromDamagePercent && X(holder).BerryBonusHealPercent > 0f)
                        amount += (int)(holder.MaxHp * X(holder).BerryBonusHealPercent / 100f);
                    if (amount <= 0) return;
                    if (trigger != EffectTrigger.OnDealDamage) announce();
                    int before = who.CurrentHp;
                    who.HealHp(amount);
                    if (who.CurrentHp > before) events.Add(new HpRestoredEvent(who.Id, who.CurrentHp - before));
                    return;
                }
                case EffectAction.LoseHpPercent:
                {
                    if (who.IsFainted || X(who).NoIndirectDamage) return;
                    int loss = Math.Max(1, (int)(who.MaxHp * b.Amount / 100f));
                    who.TakeDamage(loss);
                    if (trigger == EffectTrigger.OnDealDamage && who == holder) events.Add(new RecoilDamageEvent(who.Id, loss));
                    else { announce(); events.Add(new StatusDamageEvent(who.Id, InjuryStatus, loss)); }
                    if (who.IsFainted) events.Add(new MonsterFaintedEvent(who.Id));
                    return;
                }
                case EffectAction.CureStatus:
                    CureWithBlock(who, b, justGot, events, announce);
                    return;
                case EffectAction.InflictStatus:
                {
                    if (who.IsFainted || b.Ref.Length == 0 || !TryGetStatus(new StatusId(b.Ref), out var def)) return;
                    if (def.IsVolatile ? who.HasVolatile(def.Id) : who.Status.HasValue) return;
                    int at = events.Count;
                    TryInflictStatus(who, def.Id, events, who == holder ? null : holder);
                    bool applied = def.IsVolatile ? who.HasVolatile(def.Id) : who.Status.HasValue;
                    if (applied) events.Insert(at, item.Item != null ? (IDomainEvent)new HeldItemActivatedEvent(holder.Id, item.Id, false)
                        : new AbilityTriggeredEvent(holder.Id, item.Id));
                    return;
                }
                case EffectAction.ChangeStage:
                    if (who.IsFainted || b.Ref.Length == 0 || (int)b.Amount == 0) return;
                    ChangeStageFrom(holder, who, new StatId(b.Ref), (int)b.Amount, events);
                    return;
                case EffectAction.Flinch:
                    if (!who.IsFainted) who.SetFlinched();
                    return;
                case EffectAction.ConsumeItem:
                    return;   // the item was already used up (Consumes)
                case EffectAction.SetSideCondition:
                    if (b.Ref.Length == 0) return;
                    announce();
                    SetSideCondition(holder, who, b.Ref, events);   // Electrogénesis: al entrar pone el Campo Eléctrico
                    return;
            }
        }

        /// <summary>
        /// CureStatus. Right after getting a status (OnStatus): cures THAT one if the block lists it (empty list = any main status
        /// or confusion). Otherwise: cures its main status and the listed volatile ones (empty list = main status + confusion).
        /// </summary>
        private void CureWithBlock(Combatant who, EffectBlock b, StatusId? justGot, List<IDomainEvent> events, Action announce)
        {
            if (who.IsFainted) return;
            var confusion = new StatusId("confusion");
            var toCure = new List<StatusId>();
            if (justGot.HasValue)
            {
                var s = justGot.Value;
                bool isMain = who.Status.HasValue && who.Status.Value == s;
                if (!isMain && !who.HasVolatile(s)) return;
                if (b.Ref.Length == 0 ? isMain || s == confusion : b.Cures(s.Value)) toCure.Add(s);
            }
            else
            {
                if (who.Status.HasValue && b.Cures(who.Status.Value.Value)) toCure.Add(who.Status.Value);
                if (b.Ref.Length == 0) { if (who.HasVolatile(confusion)) toCure.Add(confusion); }
                else foreach (var id in b.RefList) { var v = new StatusId(id); if (who.HasVolatile(v) && !toCure.Contains(v)) toCure.Add(v); }
            }
            if (toCure.Count == 0) return;
            announce();
            foreach (var s in toCure)
            {
                if (who.Status.HasValue && who.Status.Value == s) who.ClearStatus(); else who.RemoveVolatile(s);
                events.Add(new StatusFadedEvent(who.Id, s));
            }
        }

        // ================================================================= TRIGGER POINTS (called by the turn)

        /// <summary>BEFORE a hit on 'target': damage taken multipliers (resist berries...). Returns the final damage.</summary>
        private int ApplyDamageTakenBlocks(Combatant target, Combatant attacker, Move move, Id<ElementType> hitType, int damage, List<IDomainEvent> events)
        {
            if (damage <= 0) return damage;
            var fired = HeldBlocks(target, EffectTrigger.BeforeHit, EffectAction.DamageTakenMultiplier)
                .Where(f => Fires(target, f.item, f.index, f.block, attacker, move, hitType)).ToList();
            if (fired.Count == 0) return damage;
            float mult = 1f;
            foreach (var (item, i, b) in fired) { mult *= b.Amount; NoteFired(target, item, i, b); }
            if (fired.Any(f => f.block.Consumes && f.item.Item != null)) UseUpItem(target, events);
            return Math.Max(1, (int)(damage * mult));
        }

        /// <summary>BEFORE a hit on 'target': a knockout blow leaves it at 1 HP (Focus Sash). Returns the final damage.</summary>
        private int ApplySurviveBlocks(Combatant target, Combatant attacker, Move move, int damage, List<IDomainEvent> events)
        {
            if (damage < target.CurrentHp || target.CurrentHp <= 1) return damage;
            foreach (var (item, i, b) in HeldBlocks(target, EffectTrigger.BeforeHit, EffectAction.SurviveAt1Hp))
                if (Fires(target, item, i, b, attacker, move, MoveTypeOf(attacker, move)))
                {
                    NoteFired(target, item, i, b);
                    if (b.Consumes && item.Item != null) UseUpItem(target, events);
                    events.Add(new EnduredEvent(target.Id));
                    return target.CurrentHp - 1;
                }
            return damage;
        }

        /// <summary>LOW HP: blocks whose threshold is reached (pinch berries). Gluttony eats berries earlier.</summary>
        private void CheckHeldTrigger(Combatant c, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted || c.MaxHp <= 0) return;
            float hp = c.CurrentHp * 100f / c.MaxHp;
            RunHeld(c, EffectTrigger.LowHp, OpponentOf(c), null, events, applies: b =>
            {
                float threshold = b.Threshold;
                if (TryGetHeldItem(c, out var it) && it.IsBerry && X(c).BerryThresholdPercent > 0) threshold = Math.Max(threshold, X(c).BerryThresholdPercent);
                return hp <= threshold;
            });
        }

        /// <summary>End of turn: Leftovers, Black Sludge, Flame Orb... (then pinch berries).</summary>
        private void ApplyEndOfTurnHeldItem(Combatant c, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted) return;
            RunHeld(c, EffectTrigger.EndOfTurn, OpponentOf(c), null, events);
            CheckHeldTrigger(c, events);
        }

        /// <summary>When it enters the field.</summary>
        private void ApplyEntryHeldItem(Combatant c, Combatant opponent, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted) return;
            RunHeld(c, EffectTrigger.OnEntry, opponent, null, events);
        }

        /// <summary>After the holder got a status: status berries (Lum).</summary>
        private void CheckLum(Combatant target, StatusId statusId, List<IDomainEvent> events)
        {
            if (target == null || target.IsFainted) return;
            RunHeld(target, EffectTrigger.OnStatus, OpponentOf(target), null, events, justGot: statusId);
        }

        /// <summary>After the holder damaged someone: Life Orb, Shell Bell, King's Rock...</summary>
        private void ApplyGen6AfterAttack(Combatant actor, Combatant target, Move move, int damageDealt, bool hitSubstitute, List<IDomainEvent> events)
        {
            if (actor == null || actor.IsFainted || damageDealt <= 0 || !move.DealsDirectDamage) return;
            RunHeld(actor, EffectTrigger.OnDealDamage, target, move, events, damageDealt, applies: b =>
                b.Action != EffectAction.Flinch
                || target != null && target != actor && !target.IsFainted && !hitSubstitute
                   && !HasEffect(move, MoveEffectKind.Flinch) && !X(target).BlocksIncomingSecondaries);
        }

        /// <summary>After the holder took a damaging hit: Weakness Policy, Air Balloon pops... (and Pickpocket of the attacker).</summary>
        private void ApplyGen6OnHit(Combatant actor, Combatant target, Move move, float effectiveness, List<IDomainEvent> events)
        {
            if (target == null || target == actor) return;
            if (!target.IsFainted) RunHeld(target, EffectTrigger.AfterHit, actor, move, events, hitType: MoveTypeOf(actor, move));
            // Prestidigitador: el atacante le roba el objeto si él no lleva ninguno.
            if (!actor.IsFainted && X(actor).StealOnHit && actor.HeldItem == null && target.HeldItem != null && !X(target).StickyHold)
            {
                var item = target.TakeHeldItem();
                actor.SetHeldItem(item);
                events.Add(new AbilityTriggeredEvent(actor.Id, AbilityIdOf(actor), "roba"));
                events.Add(new ItemTransferredEvent(target.Id, actor.Id, item));
            }
        }

        /// <summary>Bug Bite / Pluck: the actor eats the target's berry and gets its low-HP / status effects.</summary>
        private bool EatTargetBerry(Combatant actor, Combatant who, List<IDomainEvent> events)
        {
            if (who == null || who.IsFainted || actor.IsFainted || !TryGetHeldItem(who, out var berry) || !berry.IsBerry) return false;
            who.TakeHeldItem();
            events.Add(new ItemTransferredEvent(who.Id, actor.Id, berry.Id));
            foreach (var b in berry.Effects)
                if ((b.Trigger == EffectTrigger.LowHp || b.Trigger == EffectTrigger.OnStatus) && EffectRules.InstantActions.Contains(b.Action))
                {
                    var target = b.Target == BlockTarget.Self ? actor : who;
                    Execute(actor, target, new EffectSource { Item = berry }, b.Trigger, b, null, 0, null, events, () => { });
                }
            return true;
        }
    }
}
