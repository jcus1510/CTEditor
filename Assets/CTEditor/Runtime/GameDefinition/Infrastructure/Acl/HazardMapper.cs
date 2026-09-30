using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Hazards;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de trampas de campo: HazardData (Unity) → HazardDefinition (dominio).</summary>
    public static class HazardMapper
    {
        public static HazardDefinition ToDomain(HazardData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            return new HazardDefinition(d.Id, d.DisplayName, d.MaxLayers,
                d.DamagePercentByLayer ?? new float[0],
                TypeId(d.DamageScalesWithType),
                d.StatusByLayer ?? new string[0],
                string.IsNullOrWhiteSpace(d.StatId) ? (StatId?)null : new StatId(d.StatId.Trim()), d.Stages,
                Types(d.ImmuneTypes), TypeId(d.ImmuneIfAbilityBlocksType), Types(d.AbsorbedByTypes));
        }

        private static Id<ElementType>? TypeId(ElementTypeData t)
            => t != null && !string.IsNullOrWhiteSpace(t.Id) ? new Id<ElementType>(t.Id) : (Id<ElementType>?)null;

        private static List<Id<ElementType>> Types(ElementTypeData[] types)
        {
            var list = new List<Id<ElementType>>();
            if (types != null) foreach (var t in types) if (t != null && !string.IsNullOrWhiteSpace(t.Id)) list.Add(new Id<ElementType>(t.Id));
            return list;
        }
    }
}
