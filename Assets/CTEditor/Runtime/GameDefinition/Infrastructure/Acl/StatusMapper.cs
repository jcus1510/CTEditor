using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
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

            // Tipos inmunes: referencia a ficha -> id tipado (las entradas vacías se ignoran).
            var immune = new List<Id<ElementType>>();
            if (data.ImmuneTypes != null)
                foreach (var t in data.ImmuneTypes)
                    if (t != null && !string.IsNullOrWhiteSpace(t.Id)) immune.Add(new Id<ElementType>(t.Id));

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
                data.ResidualHeals,
                immune,
                data.IsVolatile,
                data.DurationMaxTurns,
                data.PreventsSwitch,
                data.BlocksIncomingMoves,
                data.SurvivesLethalHit,
                data.ResidualHealsOpponent,
                data.HarderWhenRepeated,
                data.CatchMultiplier,
                Extras(data));
        }

        private static List<Id<ElementType>> TypeIds(ElementTypeData[] arr)
        {
            var list = new List<Id<ElementType>>();
            if (arr != null) foreach (var t in arr) if (t != null && !string.IsNullOrWhiteSpace(t.Id)) list.Add(new Id<ElementType>(t.Id));
            return list;
        }

        private static Id<ElementType>? TypeId(ElementTypeData t)
            => t != null && !string.IsNullOrWhiteSpace(t.Id) ? new Id<ElementType>(t.Id) : (Id<ElementType>?)null;

        private static StatusExtras Extras(StatusConditionData d) => new StatusExtras
        {
            BlocksStatusMoves = d.BlocksStatusMoves,
            BlocksRepeatedMove = d.BlocksRepeatedMove,
            BlocksHealing = d.BlocksHealing,
            BlocksItems = d.BlocksItems,
            SuppressesAbility = d.SuppressesAbility,
            ResidualRequiresStatus = string.IsNullOrWhiteSpace(d.ResidualRequiresStatus) ? (StatusId?)null : new StatusId(d.ResidualRequiresStatus.Trim()),
            FaintsWhenEnds = d.FaintsWhenEnds,
            DestinyBond = d.DestinyBond,
            Grudge = d.Grudge,
            ReflectsStatusMoves = d.ReflectsStatusMoves,
            StealsBoostMoves = d.StealsBoostMoves,
            BoostedType = TypeId(d.BoostedType),
            BoostMultiplier = d.BoostMultiplier <= 0f ? 1f : d.BoostMultiplier,
            BoostConsumed = d.BoostConsumed,
            TypeImmunities = TypeIds(d.ExtraTypeImmunities),
            SuppressedType = TypeId(d.SuppressedType),
            Identified = d.Identified,
            HittableByTypes = TypeIds(d.HittableByTypes),
            SureHit = d.SureHit,
            Imprisons = d.Imprisons,
            Grounded = d.Grounded,
            RequiresOppositeGender = d.RequiresOppositeGender,
            ProtectOnlyDamaging = d.ProtectOnlyDamaging,
            ProtectContactStat = string.IsNullOrWhiteSpace(d.ProtectContactStat) ? (CTEditor.GameDefinition.Domain.Stats.StatId?)null
                                 : new CTEditor.GameDefinition.Domain.Stats.StatId(d.ProtectContactStat.Trim()),
            ProtectContactStages = d.ProtectContactStages,
            ProtectContactDamagePercent = d.ProtectContactDamagePercent,
            ProtectContactStatus = (d.ProtectContactStatus ?? "").Trim(),
        };
    }
}
