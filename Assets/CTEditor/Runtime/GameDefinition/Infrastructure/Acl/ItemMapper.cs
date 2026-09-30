using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL for items: ItemData (Unity) → ItemDefinition (domain), effect blocks included.</summary>
    public static class ItemMapper
    {
        public static ItemDefinition ToDomain(ItemData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var effects = Effects(d);
            return new ItemDefinition(d.Id, d.DisplayName, d.Category, d.Description, d.Price,
                d.UsableInBattle, d.UsableOutsideBattle, d.Consumable, effects: effects,
                isBerry: d.IsBerry || d.Category == ItemCategory.Berry || ItemLegacy.LooksLikeBerry(effects));
        }

        /// <summary>All the item's blocks: its effects plus whatever is still in the OLD fields (converted, same behaviour).</summary>
        public static List<EffectBlock> Effects(ItemData d)
        {
            var list = new List<EffectBlock>();
            foreach (var e in d.Effects ?? new EffectBlockData[0])
                if (e != null) list.Add(ToDomain(e));
            if (d.HasLegacyEffects) list.AddRange(LegacyBlocks(d));
            return list;
        }

        public static EffectBlock ToDomain(EffectBlockData e)
            => new EffectBlock(e.trigger, e.action, e.amount, (e.reference ?? "").Trim(), ConditionMapper.ToDomain(e.conditions),
                e.target, e.threshold, e.consumes, e.chance, e.maxPerBattle);

        /// <summary>The OLD fields as blocks (what the editor writes when it converts an old item).</summary>
        public static List<EffectBlock> LegacyBlocks(ItemData d)
            => ItemLegacy.ToBlocks(d.HealHp, d.HealPercent, d.CuresAllStatus,
                (d.CuresStatusId ?? "").Trim(), d.Revives, d.ReviveHpPercent, d.RestorePp, d.RestorePpAllMoves,
                d.FriendshipChange, d.CatchMultiplier, (d.BattleStatId ?? "").Trim(), d.BattleStages,
                ConditionMapper.ToDomain(d.HeldPowerModifiers), d.HeldEndOfTurnHealPercent, d.HeldTriggerHpPercent,
                d.HeldTriggerHealHp, d.HeldTriggerHealPercent, d.HeldConsumedOnTrigger, Extras(d));

        private static ItemExtras Extras(ItemData d)
        {
            var cond = new List<ConditionalStat>();
            if (d.HeldStatMultipliers != null) foreach (var c in d.HeldStatMultipliers)
                if (c != null && !string.IsNullOrWhiteSpace(c.statId)) cond.Add(new ConditionalStat(new StatId(c.statId.Trim()), c.multiplier, ConditionMapper.ToDomain(c.conditions)));
            var onHit = new List<OnHitStat>();
            if (d.HeldOnHitStats != null) foreach (var c in d.HeldOnHitStats)
                if (c != null && !string.IsNullOrWhiteSpace(c.statId) && c.stages != 0) onHit.Add(new OnHitStat(new StatId(c.statId.Trim()), c.stages, ConditionMapper.ToDomain(c.conditions)));
            return new ItemExtras
            {
                StatMultipliers = cond, ChoiceLock = d.HeldChoiceLock, AttackRecoilPercent = d.HeldAttackRecoilPercent,
                SurviveFromFullHp = d.HeldSurviveFromFullHp, ContactDamagePercent = d.HeldContactDamagePercent,
                OnHitStats = onHit, OnHitConsumed = d.HeldOnHitConsumed, AirBalloon = d.HeldAirBalloon,
                BlocksStatusMoves = d.HeldBlocksStatusMoves, CritStageBonus = d.HeldCritStageBonus,
                AccuracyMultiplier = d.HeldAccuracyMultiplier <= 0f ? 1f : d.HeldAccuracyMultiplier,
                EvasionMultiplier = d.HeldEvasionMultiplier <= 0f ? 1f : d.HeldEvasionMultiplier,
                ResistBerryType = (d.HeldResistBerryType ?? "").Trim(), CuresAnyStatus = d.HeldCuresAnyStatus,
                SelfStatusEndOfTurn = (d.HeldSelfStatusEndOfTurn ?? "").Trim(), FlinchChance = d.HeldFlinchChance,
                HealOnDamagePercent = d.HeldHealOnDamagePercent, WeatherTurnsBonus = d.HeldWeatherTurnsBonus,
                ScreenTurnsBonus = d.HeldScreenTurnsBonus, BlackSludge = d.HeldBlackSludge, QuickClawChance = d.HeldQuickClawChance,
                SuperEffectiveBoost = d.HeldSuperEffectiveBoost < 1f ? 1f : d.HeldSuperEffectiveBoost,
            };
        }
    }
}
