using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de climas: WeatherData (Unity) → WeatherDefinition (dominio).</summary>
    public static class WeatherMapper
    {
        public static WeatherDefinition ToDomain(WeatherData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var mult = new Dictionary<Id<ElementType>, float>();
            if (data.TypePowerMultipliers != null)
                foreach (var m in data.TypePowerMultipliers)
                    if (m != null && m.type != null && !string.IsNullOrWhiteSpace(m.type.Id))
                        mult[new Id<ElementType>(m.type.Id)] = m.multiplier;
            var immune = new List<Id<ElementType>>();
            if (data.ImmuneTypes != null)
                foreach (var t in data.ImmuneTypes)
                    if (t != null && !string.IsNullOrWhiteSpace(t.Id)) immune.Add(new Id<ElementType>(t.Id));
            return new WeatherDefinition(data.Id, data.DisplayName, data.DefaultTurns, mult,
                new Percentage(data.ResidualDamagePercent), immune);
        }
    }
}
