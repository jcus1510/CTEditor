using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;

namespace CTEditor.GameDefinition.Domain.Items
{
    /// <summary>
    /// Converts the OLD per-feature item fields (healHp, heldTriggerHpPercent, ItemExtras...) into effect blocks with the
    /// exact same behaviour. Used for old assets, old CSV columns and code that still builds items the old way.
    /// </summary>
    public static class ItemLegacy
    {
        public static List<EffectBlock> ToBlocks(int healHp = 0, float healPercent = 0f, bool curesAllStatus = false,
            string curesStatusId = null, bool revives = false, float reviveHpPercent = 0f, int restorePp = 0,
            bool restorePpAllMoves = false, int friendshipChange = 0, float catchMultiplier = 0f,
            string battleStatId = null, int battleStages = 0, IReadOnlyList<PowerModifier> heldPowerModifiers = null,
            float heldEndOfTurnHealPercent = 0f, float heldTriggerHpPercent = 0f, int heldTriggerHealHp = 0,
            float heldTriggerHealPercent = 0f, bool heldConsumedOnTrigger = true, ItemExtras extras = null)
        {
            var l = new List<EffectBlock>();
            void Add(EffectTrigger t, EffectAction a, float amount = 0f, string reference = null, IReadOnlyList<Condition> conditions = null,
                BlockTarget target = BlockTarget.Self, float threshold = 0f, bool consumes = false, float chance = 100f)
                => l.Add(new EffectBlock(t, a, amount, reference, conditions, target, threshold, consumes, chance));

            // --- Used from the bag ---
            if (healHp > 0) Add(EffectTrigger.OnUse, EffectAction.HealHp, healHp);
            if (healPercent > 0f) Add(EffectTrigger.OnUse, EffectAction.HealPercent, healPercent);
            if (curesAllStatus) Add(EffectTrigger.OnUse, EffectAction.CureStatus);
            else if (!string.IsNullOrWhiteSpace(curesStatusId)) Add(EffectTrigger.OnUse, EffectAction.CureStatus, 0f, curesStatusId);
            if (revives) Add(EffectTrigger.OnUse, EffectAction.Revive, reviveHpPercent);
            if (restorePp > 0) Add(EffectTrigger.OnUse, restorePpAllMoves ? EffectAction.RestorePpAll : EffectAction.RestorePp, restorePp);
            if (friendshipChange != 0) Add(EffectTrigger.OnUse, EffectAction.Friendship, friendshipChange);
            if (catchMultiplier > 0f) Add(EffectTrigger.OnUse, EffectAction.Catch, catchMultiplier);
            if (!string.IsNullOrWhiteSpace(battleStatId) && battleStages != 0) Add(EffectTrigger.OnUse, EffectAction.ChangeStage, battleStages, battleStatId);

            // --- Held ---
            foreach (var m in heldPowerModifiers ?? new PowerModifier[0])
                if (m != null) Add(EffectTrigger.Passive, EffectAction.PowerMultiplier, m.Multiplier, null, m.Conditions);
            if (heldEndOfTurnHealPercent > 0f) Add(EffectTrigger.EndOfTurn, EffectAction.HealPercent, heldEndOfTurnHealPercent);
            if (heldTriggerHpPercent > 0f)
            {
                if (heldTriggerHealHp > 0 || heldTriggerHealPercent <= 0f)
                    Add(EffectTrigger.LowHp, EffectAction.HealHp, heldTriggerHealHp, threshold: heldTriggerHpPercent, consumes: heldConsumedOnTrigger);
                if (heldTriggerHealPercent > 0f)
                    Add(EffectTrigger.LowHp, EffectAction.HealPercent, heldTriggerHealPercent, threshold: heldTriggerHpPercent, consumes: heldConsumedOnTrigger);
            }

            var x = extras;
            if (x == null) return l;
            foreach (var s in x.StatMultipliers) Add(EffectTrigger.Passive, EffectAction.MultiplyStat, s.Multiplier, s.Stat.Value, s.Conditions);
            if (x.ChoiceLock) Add(EffectTrigger.Passive, EffectAction.ChoiceLock);
            if (x.BlocksStatusMoves) Add(EffectTrigger.Passive, EffectAction.BlockStatusMoves);
            if (x.CritStageBonus != 0) Add(EffectTrigger.Passive, EffectAction.CritStage, x.CritStageBonus);
            if (x.AccuracyMultiplier != 1f) Add(EffectTrigger.Passive, EffectAction.AccuracyMultiplier, x.AccuracyMultiplier);
            if (x.EvasionMultiplier != 1f) Add(EffectTrigger.Passive, EffectAction.EvasionMultiplier, x.EvasionMultiplier);
            if (x.SuperEffectiveBoost != 1f)
                Add(EffectTrigger.Passive, EffectAction.DamageDealtMultiplier, x.SuperEffectiveBoost, null, new[] { SuperEffectiveOn(ConditionSubject.Other) });
            if (x.QuickClawChance > 0f) Add(EffectTrigger.Passive, EffectAction.ActFirst, chance: x.QuickClawChance);
            if (x.WeatherTurnsBonus != 0) Add(EffectTrigger.Passive, EffectAction.ExtendWeather, x.WeatherTurnsBonus);
            if (x.ScreenTurnsBonus != 0) Add(EffectTrigger.Passive, EffectAction.ExtendScreens, x.ScreenTurnsBonus);
            if (x.AirBalloon)
            {
                Add(EffectTrigger.Passive, EffectAction.ImmuneToType, 0f, "ground");
                Add(EffectTrigger.AfterHit, EffectAction.ConsumeItem, consumes: true);
            }
            if (x.ResistBerryType.Length > 0)
            {
                var cond = new List<Condition> { new Condition(ConditionKind.MoveType, text: x.ResistBerryType) };
                if (!string.Equals(x.ResistBerryType, "normal", System.StringComparison.OrdinalIgnoreCase)) cond.Add(SuperEffectiveOn(ConditionSubject.Self));
                Add(EffectTrigger.BeforeHit, EffectAction.DamageTakenMultiplier, 0.5f, null, cond, consumes: true);
            }
            if (x.SurviveFromFullHp)
                Add(EffectTrigger.BeforeHit, EffectAction.SurviveAt1Hp, 0f, null, new[] { FullHp() }, consumes: true);
            foreach (var h in x.OnHitStats)
                Add(EffectTrigger.AfterHit, EffectAction.ChangeStage, h.Stages, h.Stat.Value, h.Conditions, consumes: x.OnHitConsumed);
            if (x.ContactDamagePercent > 0f) Add(EffectTrigger.ContactTaken, EffectAction.LoseHpPercent, x.ContactDamagePercent, target: BlockTarget.Other);
            if (x.HealOnDamagePercent > 0f) Add(EffectTrigger.OnDealDamage, EffectAction.HealFromDamagePercent, x.HealOnDamagePercent);
            if (x.AttackRecoilPercent > 0f) Add(EffectTrigger.OnDealDamage, EffectAction.LoseHpPercent, x.AttackRecoilPercent);
            if (x.FlinchChance > 0f) Add(EffectTrigger.OnDealDamage, EffectAction.Flinch, target: BlockTarget.Other, chance: x.FlinchChance);
            if (x.CuresAnyStatus) Add(EffectTrigger.OnStatus, EffectAction.CureStatus, consumes: true);
            if (x.BlackSludge)
            {
                Add(EffectTrigger.EndOfTurn, EffectAction.HealPercent, 6.25f, null, new[] { IsType("poison") });
                Add(EffectTrigger.EndOfTurn, EffectAction.LoseHpPercent, 12.5f, null, new[] { IsType("poison", negate: true) });
            }
            if (x.SelfStatusEndOfTurn.Length > 0) Add(EffectTrigger.EndOfTurn, EffectAction.InflictStatus, 0f, x.SelfStatusEndOfTurn);
            return l;
        }

        /// <summary>«The move is super effective against [subject]».</summary>
        public static Condition SuperEffectiveOn(ConditionSubject subject)
            => new Condition(ConditionKind.MoveEffectiveness, subject, Comparison.Greater, 1f);
        public static Condition FullHp() => new Condition(ConditionKind.HpPercent, ConditionSubject.Self, Comparison.GreaterOrEqual, 100f);
        public static Condition IsType(string type, bool negate = false) => new Condition(ConditionKind.IsType, ConditionSubject.Self, text: type, negate: negate);

        /// <summary>Berries are eaten: pinch berries, status berries and resist berries.</summary>
        public static bool LooksLikeBerry(IEnumerable<EffectBlock> blocks) => blocks.Any(b => b.Consumes
            && (b.Trigger == EffectTrigger.LowHp || b.Trigger == EffectTrigger.OnStatus
                || b.Trigger == EffectTrigger.BeforeHit && b.Action == EffectAction.DamageTakenMultiplier));
    }
}
