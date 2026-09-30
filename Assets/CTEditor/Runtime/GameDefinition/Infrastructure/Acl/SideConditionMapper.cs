using System;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de efectos de lado: SideConditionData (Unity) → SideConditionDefinition (dominio).</summary>
    public static class SideConditionMapper
    {
        public static SideConditionDefinition ToDomain(SideConditionData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var types = new System.Collections.Generic.Dictionary<string, float>();
            if (d.TypeDamageMultipliers != null)
                foreach (var e in d.TypeDamageMultipliers)
                    if (e != null && e.type != null && !string.IsNullOrWhiteSpace(e.type.Id)) types[e.type.Id] = e.multiplier;
            return new SideConditionDefinition(d.Id, d.DisplayName, d.Turns, d.PhysicalDamageMultiplier, d.SpecialDamageMultiplier,
                d.BlocksStatDrops, d.BlocksStatus, d.SpeedMultiplier, d.ReversesTurnOrder, d.AccuracyMultiplier, d.GroundsTargets,
                d.BlocksCrits, types, d.SwapsDefenses, d.SuppressesItems, d.Group, d.EndOfTurnHealPercent,
                d.GroundedStatusBlock, Map(d.TypePowerMultipliers), d.BlocksPriorityOnGrounded);
        }

        private static System.Collections.Generic.Dictionary<string, float> Map(SideConditionData.TypeMultiplierEntry[] list)
        {
            var map = new System.Collections.Generic.Dictionary<string, float>();
            if (list != null)
                foreach (var e in list)
                    if (e != null && e.type != null && !string.IsNullOrWhiteSpace(e.type.Id)) map[e.type.Id] = e.multiplier;
            return map;
        }
    }
}
