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

            return new AbilityDefinition(
                new AbilityId(data.Id),
                data.DisplayName,
                modifiers,
                statusImmunities,
                typeImmunities,
                new Percentage(data.AbsorbImmuneHealPercent),
                contactStatus,
                new Percentage(data.ContactReactionChance),
                onEntryStat,
                data.OnEntryStages,
                data.OnEntryTargetsSelf);
        }
    }
}
