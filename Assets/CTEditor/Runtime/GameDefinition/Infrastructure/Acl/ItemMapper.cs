using System;
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
                d.HeldTriggerHealHp, d.HeldTriggerHealPercent, d.HeldConsumedOnTrigger);
        }
    }
}
