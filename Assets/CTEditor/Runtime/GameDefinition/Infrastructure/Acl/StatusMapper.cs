using System;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// ACL para estados: StatusConditionData (Unity) -> StatusConditionDefinition (dominio puro).
    /// Convierte los porcentajes amigables del Inspector en los Percentage del dominio.
    /// </summary>
    public static class StatusMapper
    {
        public static StatusConditionDefinition ToDomain(StatusConditionData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return new StatusConditionDefinition(
                new StatusId(data.Id),
                data.DisplayName,
                new Percentage(data.ResidualDamagePercent),
                new Percentage(data.ActionPreventionChance),
                data.ClearedOnSwitch);
        }
    }
}
