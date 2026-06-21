using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>Traduce la ficha AbilityData (Unity) a AbilityDefinition (dominio puro).</summary>
    public static class AbilityMapper
    {
        public static AbilityDefinition ToDomain(AbilityData data)
        {
            var modifiers = new List<StatPassiveModifier>();
            if (data.PassiveModifiers != null)
            {
                foreach (var m in data.PassiveModifiers)
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.statId)) continue;
                    modifiers.Add(new StatPassiveModifier(new StatId(m.statId), m.multiplier));
                }
            }

            var statusImmunities = new List<StatusId>();
            if (data.StatusImmunities != null)
            {
                foreach (var s in data.StatusImmunities)
                    if (!string.IsNullOrWhiteSpace(s)) statusImmunities.Add(new StatusId(s));
            }

            var typeImmunities = new List<Id<ElementType>>();
            if (data.TypeImmunities != null)
            {
                foreach (var t in data.TypeImmunities)
                    if (t != null) typeImmunities.Add(new Id<ElementType>(t.Id));
            }

            StatusId? contactStatus = string.IsNullOrWhiteSpace(data.ContactReactionStatus)
                ? (StatusId?)null
                : new StatusId(data.ContactReactionStatus);

            StatId? onEntryStat = string.IsNullOrWhiteSpace(data.OnEntryStatId)
                ? (StatId?)null
                : new StatId(data.OnEntryStatId);

            StatId? statusBoostStat = string.IsNullOrWhiteSpace(data.StatusStatBoostStatId)
                ? (StatId?)null
                : new StatId(data.StatusStatBoostStatId);

            Id<ElementType>? lowHpType = data.LowHpBoostType != null
                ? new Id<ElementType>(data.LowHpBoostType.Id)
                : (Id<ElementType>?)null;

            var incoming = new List<TypeDamageMultiplier>();
            if (data.IncomingTypeMultipliers != null)
            {
                foreach (var e in data.IncomingTypeMultipliers)
                    if (e != null && e.type != null)
                        incoming.Add(new TypeDamageMultiplier(new Id<ElementType>(e.type.Id), e.multiplier));
            }

            StatId? endOfTurnStat = string.IsNullOrWhiteSpace(data.EndOfTurnStatId)
                ? (StatId?)null
                : new StatId(data.EndOfTurnStatId);

            return new AbilityDefinition(
                id: new AbilityId(data.Id),
                displayName: data.DisplayName,
                passiveModifiers: modifiers,
                statusImmunities: statusImmunities,
                typeImmunities: typeImmunities,
                absorbImmuneHealPercent: new Percentage(data.AbsorbImmuneHealPercent),
                contactReactionStatus: contactStatus,
                contactReactionChance: new Percentage(data.ContactReactionChance),
                onEntryStat: onEntryStat,
                onEntryStages: data.OnEntryStages,
                onEntryTargetsSelf: data.OnEntryTargetsSelf,
                statusStatBoostStat: statusBoostStat,
                statusStatBoostMultiplier: data.StatusStatBoostMultiplier,
                lowHpBoostType: lowHpType,
                lowHpThreshold: new Percentage(data.LowHpThresholdPercent),
                lowHpBoostMultiplier: data.LowHpBoostMultiplier,
                incomingTypeMultipliers: incoming,
                stabMultiplierOverride: data.StabMultiplierOverride,
                statusMovePriorityBonus: data.StatusMovePriorityBonus,
                preventsStatReduction: data.PreventsStatReduction,
                curesStatusOnSwitchOut: data.CuresStatusOnSwitchOut,
                healPercentOnSwitchOut: new Percentage(data.HealPercentOnSwitchOut),
                endOfTurnStat: endOfTurnStat,
                endOfTurnStages: data.EndOfTurnStages,
                endOfTurnHealPercent: new Percentage(data.EndOfTurnHealPercent),
                endOfTurnHealRequiresStatus: data.EndOfTurnHealRequiresStatus,
                endOfTurnCureStatusChance: new Percentage(data.EndOfTurnCureStatusChance),
                negatesStatusDamage: data.NegatesStatusDamage);
        }
    }
}
