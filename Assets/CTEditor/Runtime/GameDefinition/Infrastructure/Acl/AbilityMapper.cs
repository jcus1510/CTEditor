using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL for abilities: AbilityData (Unity) → AbilityDefinition (domain), made of its effect blocks.</summary>
    public static class AbilityMapper
    {
        public static AbilityDefinition ToDomain(AbilityData data)
            => new AbilityDefinition(new AbilityId(data.Id), data.DisplayName, Effects(data));

        /// <summary>All the ability's blocks.</summary>
        public static List<EffectBlock> Effects(AbilityData d)
        {
            var list = new List<EffectBlock>();
            foreach (var e in d.Effects ?? new EffectBlockData[0])
                if (e != null) list.Add(ItemMapper.ToDomain(e));
            return list;
        }
    }
}
