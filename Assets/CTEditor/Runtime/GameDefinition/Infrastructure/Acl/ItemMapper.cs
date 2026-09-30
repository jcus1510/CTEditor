using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de objetos: ItemData (Unity) → ItemDefinition (dominio).</summary>
    public static class ItemMapper
    {
        public static ItemDefinition ToDomain(ItemData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            return new ItemDefinition(d.Id, d.DisplayName, d.Category, d.Description, d.Price,
                d.UsableInBattle, d.UsableOutsideBattle, d.Consumable, d.HealHp, d.HealPercent, d.CuresAllStatus,
                (d.CuresStatusId ?? "").Trim(), d.Revives, d.ReviveHpPercent, d.RestorePp, d.RestorePpAllMoves,
                d.FriendshipChange, d.CatchMultiplier, (d.BattleStatId ?? "").Trim(), d.BattleStages,
                ConditionMapper.ToDomain(d.HeldPowerModifiers), d.HeldEndOfTurnHealPercent, d.HeldTriggerHpPercent,
                d.HeldTriggerHealHp, d.HeldTriggerHealPercent, d.HeldConsumedOnTrigger, Extras(d));
        }

        /// <summary>Los efectos de competición del objeto (público: el editor lo usa para avisar si un equipable no hace nada).</summary>
        public static ItemExtras Extras(ItemData d)
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
