using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// ACL para estados: StatusConditionData (Unity) -> StatusConditionDefinition (dominio puro).
    /// Convierte los porcentajes amigables del Inspector en Percentage, y las sub-fichas de
    /// modificadores pasivos en StatPassiveModifier del dominio.
    /// </summary>
    public static class StatusMapper
    {
        public static StatusConditionDefinition ToDomain(StatusConditionData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var modifiers = new List<StatPassiveModifier>();
            if (data.PassiveModifiers != null)
            {
                foreach (var m in data.PassiveModifiers)
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.statId)) continue;
                    modifiers.Add(new StatPassiveModifier(new StatId(m.statId), m.multiplier));
                }
            }

            StatusId? transformsTo = string.IsNullOrWhiteSpace(data.TransformsToStatus)
                ? (StatusId?)null
                : new StatusId(data.TransformsToStatus);

            return new StatusConditionDefinition(
                new StatusId(data.Id),
                data.DisplayName,
                new Percentage(data.ResidualDamagePercent),
                new Percentage(data.ActionPreventionChance),
                data.ClearedOnSwitch,
                modifiers,
                data.ProgressiveResidual,
                data.DurationTurns,
                new Percentage(data.RecoveryChancePerTurn),
                new Percentage(data.SelfDamageOnPreventedPercent),
                transformsTo,
                data.ResidualHeals);
        }
    }
}
