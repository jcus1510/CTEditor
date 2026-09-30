using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Domain.Conditions;

namespace CTEditor.GameDefinition.Domain.Effects
{
    /// <summary>WHEN a block fires. Values are serialized as integers: only append at the END.</summary>
    public enum EffectTrigger
    {
        OnUse,          // used from the bag (in or out of battle)
        Passive,        // always on while held (multipliers, immunities, locks)
        OnEntry,        // when the holder enters the field
        EndOfTurn,      // at the end of every turn
        BeforeHit,      // while a hit on the holder is computed (reduce damage, survive)
        AfterHit,       // after the holder takes a damaging hit (holder still standing)
        ContactTaken,   // after the holder is hit by a contact move (works even if it faints)
        OnDealDamage,   // after the holder damages someone
        OnStatus,       // when the holder gets a status
        LowHp,          // when the holder's HP drops to Threshold % or below
        OnWalk,         // each step in the overworld (repels)
    }

    /// <summary>WHAT a block does. Values are serialized as integers: only append at the END.</summary>
    public enum EffectAction
    {
        HealHp,                 // Amount = HP
        HealPercent,            // Amount = % of max HP
        LoseHpPercent,          // Amount = % of max HP (Target = who loses it)
        HealFromDamagePercent,  // Amount = % of the damage just dealt
        CureStatus,             // Ref = "a|b" (empty = any status)
        InflictStatus,          // Ref = status id
        Revive,                 // Amount = % of max HP
        RestorePp,              // Amount = PP for one move
        RestorePpAll,           // Amount = PP for every move
        ChangeStage,            // Ref = stat id, Amount = stages
        MultiplyStat,           // Ref = stat id, Amount = ×
        CritStage,              // Amount = + critical stages
        AccuracyMultiplier,     // Amount = × to the holder's accuracy
        EvasionMultiplier,      // Amount = × to the accuracy of whoever targets the holder
        PowerMultiplier,        // Amount = × to the power of the holder's moves
        DamageDealtMultiplier,  // Amount = × to the damage the holder deals
        DamageTakenMultiplier,  // Amount = × to the damage the holder takes
        SurviveAt1Hp,           // a knockout hit leaves it at 1 HP
        ImmuneToType,           // Ref = type id
        ActFirst,               // acts first within its priority (use Chance)
        ChoiceLock,             // can only repeat the first move it uses
        BlockStatusMoves,       // cannot use status moves
        ExtendWeather,          // Ref = weather id (empty = any), Amount = extra turns
        ExtendScreens,          // Amount = extra turns for its screens
        Flinch,                 // makes the target flinch (use Chance)
        ConsumeItem,            // the item is used up (Air Balloon pops)
        Catch,                  // Amount = ball multiplier (255 = always)
        Friendship,             // Amount = ± friendship
        Evs,                    // Ref = stat id, Amount = ± EVs
        LevelUp,                // Amount = levels
        TeachMove,              // Ref = move id
        EscapeBattle,           // flee from a wild battle
        Repel,                  // Amount = steps
        ChangeForm,             // Ref = form id
        EnableMechanic,         // Ref = mechanic id (mega stones, Z crystals...)
    }

    /// <summary>Who an action affects: the holder or the other combatant.</summary>
    public enum BlockTarget { Self, Other }

    /// <summary>
    /// An EFFECT BLOCK: «WHEN [trigger], IF [conditions], THEN [action] on [target]». Items (and, later, abilities) are just
    /// a list of these, so any item of any generation — or a brand new one — is built from the same pieces. Every block can
    /// have a chance, a limit of uses per battle and consume the item.
    /// </summary>
    public sealed class EffectBlock
    {
        public EffectTrigger Trigger { get; }
        public IReadOnlyList<Condition> Conditions { get; }
        public EffectAction Action { get; }
        public BlockTarget Target { get; }
        /// <summary>Id the action needs (type, status, stat, weather, move, form...). See EffectAction.</summary>
        public string Ref { get; }
        public float Amount { get; }
        /// <summary>For LowHp: fires at this % of HP or below.</summary>
        public float Threshold { get; }
        /// <summary>The item is used up when this block fires.</summary>
        public bool Consumes { get; }
        /// <summary>0-100 %: 100 = always.</summary>
        public float Chance { get; }
        /// <summary>Times it can fire per battle: 0 = no limit.</summary>
        public int MaxPerBattle { get; }

        public EffectBlock(EffectTrigger trigger, EffectAction action, float amount = 0f, string reference = null,
            IReadOnlyList<Condition> conditions = null, BlockTarget target = BlockTarget.Self, float threshold = 0f,
            bool consumes = false, float chance = 100f, int maxPerBattle = 0)
        {
            Trigger = trigger;
            Action = action;
            Amount = amount;
            Ref = (reference ?? "").Trim();
            Conditions = conditions == null ? Array.Empty<Condition>() : new List<Condition>(conditions);
            Target = target;
            Threshold = Math.Max(0f, Math.Min(100f, threshold));
            Consumes = consumes;
            Chance = Math.Max(0f, Math.Min(100f, chance));
            MaxPerBattle = Math.Max(0, maxPerBattle);
        }

        public bool Unconditional => Conditions.Count == 0 && Chance >= 100f && MaxPerBattle == 0;

        /// <summary>Status ids of CureStatus («a|b»); empty = any.</summary>
        public IEnumerable<string> RefList => Ref.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0);

        /// <summary>Does CureStatus cure this status?</summary>
        public bool Cures(string statusId) => Action == EffectAction.CureStatus
            && (Ref.Length == 0 || RefList.Any(s => string.Equals(s, statusId, StringComparison.OrdinalIgnoreCase)));

        public EffectBlock With(float? amount = null, string reference = null, IReadOnlyList<Condition> conditions = null,
            bool? consumes = null, float? chance = null, int? maxPerBattle = null, float? threshold = null, BlockTarget? target = null)
            => new EffectBlock(Trigger, Action, amount ?? Amount, reference ?? Ref, conditions ?? Conditions, target ?? Target,
                threshold ?? Threshold, consumes ?? Consumes, chance ?? Chance, maxPerBattle ?? MaxPerBattle);
    }

    /// <summary>What the engine already runs, and which triggers make sense for each action (the editor uses it).</summary>
    public static class EffectRules
    {
        /// <summary>Actions the battle/field engine executes today with this trigger. The rest are saved but do nothing yet.</summary>
        public static bool IsSupported(EffectBlock b)
        {
            switch (b.Trigger)
            {
                case EffectTrigger.OnUse:
                    switch (b.Action)
                    {
                        case EffectAction.HealHp: case EffectAction.HealPercent: case EffectAction.CureStatus: case EffectAction.Revive:
                        case EffectAction.RestorePp: case EffectAction.RestorePpAll: case EffectAction.ChangeStage: case EffectAction.Catch:
                        case EffectAction.Friendship: case EffectAction.ChangeForm:
                            return true;
                        default: return false;
                    }
                case EffectTrigger.Passive:
                    switch (b.Action)
                    {
                        case EffectAction.MultiplyStat: case EffectAction.CritStage: case EffectAction.AccuracyMultiplier:
                        case EffectAction.EvasionMultiplier: case EffectAction.PowerMultiplier: case EffectAction.DamageDealtMultiplier:
                        case EffectAction.ImmuneToType: case EffectAction.ActFirst: case EffectAction.ChoiceLock:
                        case EffectAction.BlockStatusMoves: case EffectAction.ExtendWeather: case EffectAction.ExtendScreens:
                        case EffectAction.EnableMechanic:
                            return true;
                        default: return false;
                    }
                case EffectTrigger.BeforeHit:
                    return b.Action == EffectAction.DamageTakenMultiplier || b.Action == EffectAction.SurviveAt1Hp;
                case EffectTrigger.OnWalk:
                    return false;
                default:   // OnEntry, EndOfTurn, AfterHit, ContactTaken, OnDealDamage, OnStatus, LowHp
                    return InstantActions.Contains(b.Action);
            }
        }

        /// <summary>Actions that happen once when a trigger fires (not multipliers).</summary>
        public static readonly HashSet<EffectAction> InstantActions = new HashSet<EffectAction>
        {
            EffectAction.HealHp, EffectAction.HealPercent, EffectAction.LoseHpPercent, EffectAction.HealFromDamagePercent,
            EffectAction.CureStatus, EffectAction.InflictStatus, EffectAction.ChangeStage, EffectAction.Flinch, EffectAction.ConsumeItem,
        };
    }
}
