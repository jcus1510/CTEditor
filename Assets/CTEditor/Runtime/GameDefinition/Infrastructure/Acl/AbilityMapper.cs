using System;
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

            StatId? statusBoostStat = string.IsNullOrWhiteSpace(data.StatusStatBoostStatId)
                ? (StatId?)null
                : new StatId(data.StatusStatBoostStatId);

            Id<ElementType>? lowHpType = data.LowHpBoostType != null
                ? new Id<ElementType>(data.LowHpBoostType.Id)
                : (Id<ElementType>?)null;

            var incoming = new List<TypeDamageMultiplier>();
            if (data.IncomingTypeMultipliers != null)
            {
                foreach (var e in data.IncomingTypeMultipliers)
                    if (e != null && e.type != null)
                        incoming.Add(new TypeDamageMultiplier(new Id<ElementType>(e.type.Id), e.multiplier));
            }

            StatId? endOfTurnStat = string.IsNullOrWhiteSpace(data.EndOfTurnStatId)
                ? (StatId?)null
                : new StatId(data.EndOfTurnStatId);

            return new AbilityDefinition(
                id: new AbilityId(data.Id),
                displayName: data.DisplayName,
                passiveModifiers: modifiers,
                statusImmunities: statusImmunities,
                typeImmunities: typeImmunities,
                absorbImmuneHealPercent: new Percentage(data.AbsorbImmuneHealPercent),
                contactReactionStatus: contactStatus,
                contactReactionChance: new Percentage(data.ContactReactionChance),
                onEntryStat: onEntryStat,
                onEntryStages: data.OnEntryStages,
                onEntryTargetsSelf: data.OnEntryTargetsSelf,
                statusStatBoostStat: statusBoostStat,
                statusStatBoostMultiplier: data.StatusStatBoostMultiplier,
                lowHpBoostType: lowHpType,
                lowHpThreshold: new Percentage(data.LowHpThresholdPercent),
                lowHpBoostMultiplier: data.LowHpBoostMultiplier,
                incomingTypeMultipliers: incoming,
                stabMultiplierOverride: data.StabMultiplierOverride,
                statusMovePriorityBonus: data.StatusMovePriorityBonus,
                preventsStatReduction: data.PreventsStatReduction,
                curesStatusOnSwitchOut: data.CuresStatusOnSwitchOut,
                healPercentOnSwitchOut: new Percentage(data.HealPercentOnSwitchOut),
                endOfTurnStat: endOfTurnStat,
                endOfTurnStages: data.EndOfTurnStages,
                endOfTurnHealPercent: new Percentage(data.EndOfTurnHealPercent),
                endOfTurnHealRequiresStatus: data.EndOfTurnHealRequiresStatus,
                endOfTurnCureStatusChance: new Percentage(data.EndOfTurnCureStatusChance),
                negatesStatusDamage: data.NegatesStatusDamage,
                offensivePowerModifiers: ConditionMapper.ToDomain(data.OffensivePowerModifiers),
                defensivePowerModifiers: ConditionMapper.ToDomain(data.DefensivePowerModifiers),
                extras: Extras(data));
        }

        private static StatId? Stat(string id) => string.IsNullOrWhiteSpace(id) ? (StatId?)null : new StatId(id.Trim());

        private static List<string> Strings(string[] arr)
        {
            var list = new List<string>();
            if (arr != null) foreach (var x in arr) if (!string.IsNullOrWhiteSpace(x)) list.Add(x.Trim());
            return list;
        }

        private static List<Id<ElementType>> Types(ElementTypeData[] arr)
        {
            var list = new List<Id<ElementType>>();
            if (arr != null) foreach (var t in arr) if (t != null && !string.IsNullOrWhiteSpace(t.Id)) list.Add(new Id<ElementType>(t.Id));
            return list;
        }

        private static AbilityExtras Extras(AbilityData d)
        {
            var hp = new List<(string, float)>();
            if (d.WeatherHpChanges != null) foreach (var w in d.WeatherHpChanges) if (w != null && !string.IsNullOrWhiteSpace(w.weatherId)) hp.Add((w.weatherId.Trim(), w.percent));
            var tbw = new List<(string, Id<ElementType>)>();
            if (d.TypeByWeather != null) foreach (var w in d.TypeByWeather) if (w != null && w.type != null && !string.IsNullOrWhiteSpace(w.weatherId)) tbw.Add((w.weatherId.Trim(), new Id<ElementType>(w.type.Id)));
            var cond = new List<ConditionalStat>();
            if (d.ConditionalStats != null) foreach (var c in d.ConditionalStats) if (c != null && !string.IsNullOrWhiteSpace(c.statId)) cond.Add(new ConditionalStat(new StatId(c.statId.Trim()), c.multiplier, ConditionMapper.ToDomain(c.conditions)));
            var onHit = new List<OnHitStat>();
            if (d.OnHitStats != null) foreach (var c in d.OnHitStats) if (c != null && !string.IsNullOrWhiteSpace(c.statId) && c.stages != 0) onHit.Add(new OnHitStat(new StatId(c.statId.Trim()), c.stages, ConditionMapper.ToDomain(c.conditions)));
            var contactAlt = new List<StatusId>();
            foreach (var st in Strings(d.ContactReactionStatuses)) contactAlt.Add(new StatusId(st));
            var drops = new List<StatId>();
            foreach (var st in Strings(d.PreventedStatDrops)) drops.Add(new StatId(st));

            return new AbilityExtras
            {
                OnEntryWeather = (d.OnEntryWeather ?? "").Trim(), OnEntryWeatherTurns = d.OnEntryWeatherTurns, SuppressesWeather = d.SuppressesWeather,
                WeatherHpChanges = hp, WeatherImmunities = Strings(d.WeatherImmunities), CuresStatusInWeather = (d.CuresStatusInWeather ?? "").Trim(),
                StatusImmuneInWeather = (d.StatusImmuneInWeather ?? "").Trim(), TypeByWeather = tbw,
                ConditionalStats = cond, AccuracyModifiers = ConditionMapper.ToDomain(d.AccuracyModifiers), EvasionModifiers = ConditionMapper.ToDomain(d.EvasionModifiers),
                NoRecoil = d.NoRecoil, NoIndirectDamage = d.NoIndirectDamage, CritImmune = d.CritImmune, CritStageBonus = d.CritStageBonus,
                CritDamageMultiplier = d.CritDamageMultiplier <= 0f ? 1f : d.CritDamageMultiplier, PreventedStatDrops = drops, FlinchImmune = d.FlinchImmune,
                OnFlinchStat = Stat(d.OnFlinchStat), OnFlinchStages = d.OnFlinchStages, SecondaryChanceMultiplier = d.SecondaryChanceMultiplier <= 0f ? 1f : d.SecondaryChanceMultiplier,
                BlocksIncomingSecondaries = d.BlocksIncomingSecondaries, RemovesOwnSecondaries = d.RemovesOwnSecondaries, FlinchChanceOnAttack = d.FlinchChanceOnAttack,
                ForcedSwitchImmune = d.ForcedSwitchImmune, TrapsOpponent = d.TrapsOpponent, TrapOnlyTypes = Types(d.TrapOnlyTypes), TrapOnlyGrounded = d.TrapOnlyGrounded,
                AlwaysEscapes = d.AlwaysEscapes, SleepAgesTwice = d.SleepAgesTwice, SynchronizeStatus = d.SynchronizeStatus, ContactReactionStatuses = contactAlt,
                ContactDamagePercent = d.ContactDamagePercent,
                OffensiveContactStatus = string.IsNullOrWhiteSpace(d.OffensiveContactStatus) ? (StatusId?)null : new StatusId(d.OffensiveContactStatus.Trim()),
                OffensiveContactChance = d.OffensiveContactChance, AftermathPercent = d.AftermathPercent, DisableOnHitChance = d.DisableOnHitChance,
                OnHitStats = onHit, CritMaxesAttack = d.CritMaxesAttack, OhkoImmune = d.OhkoImmune, SurvivesFromFullHp = d.SurvivesFromFullHp,
                ImmuneToMoveTags = Strings(d.ImmuneToMoveTags), BlocksMoveTagsForAll = Strings(d.BlocksMoveTagsForAll), OnlySuperEffectiveHits = d.OnlySuperEffectiveHits,
                OnImmuneStat = Stat(d.OnImmuneStat), OnImmuneStages = d.OnImmuneStages, BoostsAbsorbedType = d.BoostsAbsorbedType,
                TraceOnEntry = d.TraceOnEntry, TransformOnEntry = d.TransformOnEntry, DownloadOnEntry = d.DownloadOnEntry, AnnounceOnEntry = (d.AnnounceOnEntry ?? "").Trim(),
                NeutralizingGas = d.NeutralizingGas, Unnerve = d.Unnerve, ColorChange = d.ColorChange, Protean = d.Protean, Pressure = d.Pressure, Truant = d.Truant,
                IgnoresStages = d.IgnoresStages, StageMultiplier = Math.Max(1, d.StageMultiplier), InvertsStages = d.InvertsStages, LiquidOoze = d.LiquidOoze,
                NormalizeMoves = d.NormalizeMoves, IgnoresImmunityFor = Types(d.IgnoresImmunityFor), MoldBreaker = d.MoldBreaker, SkillLink = d.SkillLink,
                BadDreamsPercent = d.BadDreamsPercent, NoGuard = d.NoGuard, MovesLast = d.MovesLast, Klutz = d.Klutz, BerryThresholdPercent = d.BerryThresholdPercent,
                StickyHold = d.StickyHold, Pickpocket = d.Pickpocket, OnKoStat = Stat(d.OnKoStat), OnKoStages = d.OnKoStages,
                OnStatDroppedStat = Stat(d.OnStatDroppedStat), OnStatDroppedStages = d.OnStatDroppedStages, HarvestChance = d.HarvestChance, Moody = d.Moody,
                Infiltrator = d.Infiltrator, MagicBounce = d.MagicBounce, WeightMultiplier = d.WeightMultiplier <= 0f ? 1f : d.WeightMultiplier,
            };
        }
    }
}
