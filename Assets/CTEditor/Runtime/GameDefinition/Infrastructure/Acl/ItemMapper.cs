using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Effects;
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
                d.UsableInBattle, d.UsableOutsideBattle, d.Consumable, effects,
                isBerry: d.IsBerry || d.Category == ItemCategory.Berry || ItemEffects.LooksLikeBerry(effects));
        }

        /// <summary>All the item's blocks.</summary>
        public static List<EffectBlock> Effects(ItemData d)
        {
            var list = new List<EffectBlock>();
            foreach (var e in d.Effects ?? new EffectBlockData[0])
                if (e != null) list.Add(ToDomain(e));
            return list;
        }

        public static EffectBlock ToDomain(EffectBlockData e)
            => new EffectBlock(e.trigger, e.action, e.amount, (e.reference ?? "").Trim(), ConditionMapper.ToDomain(e.conditions),
                e.target, e.threshold, e.consumes, e.chance, e.maxPerBattle);
    }
}
