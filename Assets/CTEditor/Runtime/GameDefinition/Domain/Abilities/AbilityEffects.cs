using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Abilities
{
    /// <summary>
    /// ABILITIES as EFFECT BLOCKS. An ability is a list of «when / if / then» blocks, like an item. This class reads the
    /// blocks into the values the battle engine asks for (AbilityDefinition's properties: «views»), so every ability of
    /// every generation — or a new one — is built from the same pieces. Blocks that are not one of the ability shapes
    /// below go to <see cref="AbilityViews.Generic"/> and run like a held item's blocks (heal at end of turn, a chance to
    /// flinch, a multiplier with conditions...), so they can also be combined freely.
    /// </summary>
    public static class AbilityEffects
    {
        /// <summary>A one-of-a-kind behaviour (Trace, Truant, Mold Breaker...): «especial» actions, keyed by id.</summary>
        public sealed class SpecialInfo
        {
            public string Key, Label, Help;
            public EffectTrigger Trigger;
            /// <summary>The block's Amount means something (Gluttony: HP %; Harvest: chance).</summary>
            public bool UsesAmount;
            public Action<AbilityViews, EffectBlock> Apply;
        }

        private static SpecialInfo S(string key, string label, string help, EffectTrigger trigger, Action<AbilityExtras> set)
            => new SpecialInfo { Key = key, Label = label, Help = help, Trigger = trigger, Apply = (v, b) => set(v.Extras) };

        private static SpecialInfo SN(string key, string label, string help, EffectTrigger trigger, Action<AbilityExtras, float> set)
            => new SpecialInfo { Key = key, Label = label, Help = help, Trigger = trigger, UsesAmount = true, Apply = (v, b) => set(v.Extras, b.Amount) };

        /// <summary>Every special behaviour. The keys are the ids used in Excel («siempre: rastro»).</summary>
        public static readonly SpecialInfo[] Specials =
        {
            // Entering the field
            S("rastro", "Copia la habilidad del rival", "Rastro.", EffectTrigger.OnEntry, x => x.TraceOnEntry = true),
            S("impostor", "Se transforma en el rival", "Impostor.", EffectTrigger.OnEntry, x => x.TransformOnEntry = true),
            S("descarga", "Sube Ataque o At. Esp. según la defensa más baja del rival", "Descarga.", EffectTrigger.OnEntry, x => x.DownloadOnEntry = true),
            // While it is on the field
            S("gas_reactivo", "Las demás habilidades no funcionan", "Gas Reactivo.", EffectTrigger.Passive, x => x.NeutralizingGas = true),
            S("nerviosismo", "El rival no puede comer bayas", "Nerviosismo.", EffectTrigger.Passive, x => x.Unnerve = true),
            S("anula_clima", "El clima no hace nada", "Aclimatación, Bucle Aire.", EffectTrigger.Passive, x => x.SuppressesWeather = true),
            S("presion", "El rival gasta 1 PP más", "Presión.", EffectTrigger.Passive, x => x.Pressure = true),
            S("rompemoldes", "Ignora las habilidades del rival al atacar", "Rompemoldes, Turbollama, Terravoltaje.", EffectTrigger.Passive, x => x.MoldBreaker = true),
            S("allanamiento", "Atraviesa pantallas y sustitutos", "Allanamiento.", EffectTrigger.Passive, x => x.Infiltrator = true),
            S("indefenso", "Sus movimientos y los que le lanzan nunca fallan", "Indefenso.", EffectTrigger.Passive, x => x.NoGuard = true),
            S("ignorante", "Ignora las etapas del rival", "Ignorante.", EffectTrigger.Passive, x => x.IgnoresStages = true),
            S("invierte_etapas", "Sus cambios de etapa se invierten", "Respondón.", EffectTrigger.Passive, x => x.InvertsStages = true),
            S("normalidad", "Todos sus movimientos son de tipo Normal", "Normalidad.", EffectTrigger.Passive, x => x.NormalizeMoves = true),
            S("mutatipo", "Pasa a ser del tipo del movimiento que usa", "Mutatipo.", EffectTrigger.Passive, x => x.Protean = true),
            S("cambio_color", "Pasa a ser del tipo del ataque que le golpea", "Cambio Color.", EffectTrigger.Passive, x => x.ColorChange = true),
            S("ausente", "Actúa un turno sí y otro no", "Ausente.", EffectTrigger.Passive, x => x.Truant = true),
            S("rezagado", "Actúa el último dentro de su prioridad", "Rezagado.", EffectTrigger.Passive, x => x.MovesLast = true),
            S("encadenado", "Sus golpes múltiples siempre dan el máximo", "Encadenado.", EffectTrigger.Passive, x => x.SkillLink = true),
            S("veleta", "Cada turno sube mucho una estadística al azar y baja otra", "Veleta.", EffectTrigger.EndOfTurn, x => x.Moody = true),
            S("zoquete", "Su objeto equipado no hace nada", "Zoquete.", EffectTrigger.Passive, x => x.Klutz = true),
            // Damage and hits
            S("sin_retroceso", "No sufre daño de retroceso", "Cabeza Roca.", EffectTrigger.Passive, x => x.NoRecoil = true),
            S("solo_daño_directo", "Solo le dañan los ataques (ni estados, ni clima, ni trampas)", "Muro Mágico.", EffectTrigger.Passive, x => x.NoIndirectDamage = true),
            new SpecialInfo { Key = "anula_daño_estado", Label = "Los estados no le hacen daño", Help = "Cura Veneno.", Trigger = EffectTrigger.Passive,
                Apply = (v, b) => v.NegatesStatusDamage = true },
            S("sin_criticos", "No recibe golpes críticos", "Armadura Batalla, Caparazón.", EffectTrigger.Passive, x => x.CritImmune = true),
            S("irascible", "Un crítico le sube el Ataque al máximo", "Irascible.", EffectTrigger.AfterHit, x => x.CritMaxesAttack = true),
            S("sin_retroceder", "No retrocede", "Foco Interno.", EffectTrigger.Passive, x => x.FlinchImmune = true),
            S("bloquea_secundarios", "Los efectos secundarios que le lanzan no le afectan", "Polvo Escudo.", EffectTrigger.Passive, x => x.BlocksIncomingSecondaries = true),
            S("sin_secundarios", "Sus movimientos pierden los efectos secundarios", "Potencia Bruta (el ×1,3 va aparte).", EffectTrigger.Passive, x => x.RemovesOwnSecondaries = true),
            S("sin_ko_directo", "Inmune a los movimientos que debilitan de un golpe", "Robustez.", EffectTrigger.Passive, x => x.OhkoImmune = true),
            S("superguarda", "Solo le afectan los golpes muy eficaces", "Superguarda.", EffectTrigger.Passive, x => x.OnlySuperEffectiveHits = true),
            S("lodo_liquido", "Quien le drena PS los pierde", "Lodo Líquido.", EffectTrigger.Passive, x => x.LiquidOoze = true),
            S("espejo_magico", "Devuelve los movimientos de estado", "Espejo Mágico.", EffectTrigger.Passive, x => x.MagicBounce = true),
            S("potencia_absorbido", "Tras absorber un golpe, sus movimientos de ese tipo ×1,5", "Absorbe Fuego.", EffectTrigger.OnAbsorb, x => x.BoostsAbsorbedType = true),
            // Switching and fleeing
            S("no_cambio_forzado", "No se le puede obligar a cambiar", "Ventosas.", EffectTrigger.Passive, x => x.ForcedSwitchImmune = true),
            S("siempre_escapa", "Siempre puede huir de un salvaje", "Fuga.", EffectTrigger.Passive, x => x.AlwaysEscapes = true),
            // Status
            S("madrugar", "El sueño se le pasa el doble de rápido", "Madrugar.", EffectTrigger.Passive, x => x.SleepAgesTwice = true),
            S("sincronia", "Devuelve quemadura, veneno y parálisis a quien se la pone", "Sincronía.", EffectTrigger.OnStatus, x => x.SynchronizeStatus = true),
            // Items
            S("viscosidad", "No le pueden quitar el objeto", "Viscosidad.", EffectTrigger.Passive, x => x.StickyHold = true),
            S("hurto", "Roba el objeto de quien le golpea con contacto", "Hurto.", EffectTrigger.ContactTaken, x => x.Pickpocket = true),
            S("roba_objeto", "Al golpear roba el objeto del rival si no lleva nada", "Prestidigitador.", EffectTrigger.OnDealDamage, x => x.StealOnHit = true),
            S("contagia_habilidad", "Quien le golpea con contacto pasa a tener esta habilidad", "Momia.", EffectTrigger.ContactTaken, x => x.SpreadsAbilityOnContact = true),
            SN("umbral_bayas", "Come las bayas antes: con este % de PS", "Gula: 50.", EffectTrigger.Passive, (x, n) => x.BerryThresholdPercent = (int)Math.Round(n)),
            SN("baya_extra", "Al comer una baya recupera además este % de PS", "Carrillo: 33,33.", EffectTrigger.Passive, (x, n) => x.BerryBonusHealPercent = n),
            SN("cosecha", "% de recuperar la baya gastada al final del turno (con sol, siempre)", "Cosecha: 50.", EffectTrigger.EndOfTurn, (x, n) => x.HarvestChance = n),
        };

        public static SpecialInfo Special(string key)
            => Specials.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        // ================================================================= READING

        /// <summary>Reads the blocks into the values the engine uses. Blocks with no ability shape go to Generic.</summary>
        public static AbilityViews Read(IEnumerable<EffectBlock> blocks)
        {
            var v = new AbilityViews();
            if (blocks == null) return v;
            foreach (var b in blocks)
            {
                if (b == null) continue;
                v.All.Add(b);
                if (!Take(v, b)) v.Generic.Add(b);
            }
            v.Extras.ConditionalStats = v.ConditionalStats;
            v.Extras.AccuracyModifiers = v.AccuracyModifiers;
            v.Extras.EvasionModifiers = v.EvasionModifiers;
            v.Extras.WeatherHpChanges = v.WeatherHpChanges;
            v.Extras.WeatherImmunities = v.WeatherImmunities;
            v.Extras.TypeByWeather = v.TypeByWeather;
            v.Extras.PreventedStatDrops = v.PreventedStatDrops;
            v.Extras.ContactReactionStatuses = v.ContactReactionStatuses;
            v.Extras.OnHitStats = v.OnHitStats;
            v.Extras.ImmuneToMoveTags = v.ImmuneToMoveTags;
            v.Extras.BlocksMoveTagsForAll = v.BlocksMoveTagsForAll;
            v.Extras.TrapOnlyTypes = v.TrapOnlyTypes;
            v.Extras.IgnoresImmunityFor = v.IgnoresImmunityFor;
            return v;
        }

        /// <summary>Does the engine run this block when an ability has it (as an ability shape or like a held item)?</summary>
        public static bool IsSupported(EffectBlock b)
        {
            if (b == null) return false;
            var v = new AbilityViews();
            if (Take(v, b)) return true;
            return b.Trigger != EffectTrigger.OnUse && b.Trigger != EffectTrigger.OnWalk && !b.Consumes
                && b.Action != EffectAction.ConsumeItem && EffectRules.IsSupported(b);
        }

        private static bool Plain(EffectBlock b) => b.Chance >= 100f && b.MaxPerBattle == 0 && !b.Consumes;
        private static bool NoConds(EffectBlock b) => b.Conditions.Count == 0;
        private static IEnumerable<string> Refs(EffectBlock b) => b.RefList;

        private static bool Only(EffectBlock b, ConditionKind kind, out Condition c)
        {
            c = b.Conditions.Count == 1 && b.Conditions[0].Kind == kind && !b.Conditions[0].Negate ? b.Conditions[0] : null;
            return c != null;
        }

        private static bool IsSelfStatus(EffectBlock b)
            => Only(b, ConditionKind.HasAnyStatus, out var c) && c.Subject == ConditionSubject.Self;

        /// <summary>Takes a block as an ability shape. False = not one (it goes to Generic).</summary>
        private static bool Take(AbilityViews v, EffectBlock b)
        {
            var x = v.Extras;
            float n = b.Amount;
            switch (b.Action)
            {
                case EffectAction.Special:
                {
                    var s = Special(b.Ref);
                    if (s == null || !NoConds(b) || !Plain(b)) return false;
                    s.Apply(v, b);
                    return true;
                }
                case EffectAction.MultiplyStat:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || b.Ref.Length == 0) return false;
                    if (NoConds(b)) v.PassiveModifiers.Add(new StatPassiveModifier(new StatId(b.Ref), n));
                    else if (IsSelfStatus(b) && !v.StatusStatBoostStat.HasValue) { v.StatusStatBoostStat = new StatId(b.Ref); v.StatusStatBoostMultiplier = n; }
                    else v.ConditionalStats.Add(new ConditionalStat(new StatId(b.Ref), n, b.Conditions));
                    return true;
                case EffectAction.ImmuneToType:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    foreach (var t in Refs(b)) v.TypeImmunities.Add(new Id<ElementType>(t));
                    return true;
                case EffectAction.ImmuneToStatus:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b)) return false;
                    if (NoConds(b) && b.Ref.Length > 0) { foreach (var s in Refs(b)) v.StatusImmunities.Add(new StatusId(s)); return true; }
                    if (b.Ref.Length == 0 && Only(b, ConditionKind.Weather, out var w) && x.StatusImmuneInWeather.Length == 0) { x.StatusImmuneInWeather = w.Text; return true; }
                    return false;
                case EffectAction.PowerMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b)) return false;
                    v.OffensivePowerModifiers.Add(new PowerModifier(n, b.Conditions));
                    return true;
                case EffectAction.PowerTakenMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b)) return false;
                    v.DefensivePowerModifiers.Add(new PowerModifier(n, b.Conditions));
                    return true;
                case EffectAction.DamageDealtMultiplier:
                {
                    // Blaze / Torrent: «siempre [si mov.tipo=fire & propio.vida<=33]: daño x1,5».
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || b.Conditions.Count != 2 || v.LowHpBoostType.HasValue) return false;
                    var type = b.Conditions.FirstOrDefault(c => c.Kind == ConditionKind.MoveType && !c.Negate);
                    var hp = b.Conditions.FirstOrDefault(c => c.Kind == ConditionKind.HpPercent && c.Subject == ConditionSubject.Self
                        && c.Comparison == Comparison.LessOrEqual && !c.Negate);
                    if (type == null || hp == null) return false;
                    v.LowHpBoostType = new Id<ElementType>(type.Text);
                    v.LowHpThreshold = new Percentage(Clamp100(hp.Number));
                    v.LowHpBoostMultiplier = n;
                    return true;
                }
                case EffectAction.DamageTakenMultiplier:
                    // Thick Fat: «antes_de_golpe [si mov.tipo=fire]: daño_recibido x0,5».
                    if (b.Trigger != EffectTrigger.BeforeHit || !Plain(b) || !Only(b, ConditionKind.MoveType, out var mt)) return false;
                    v.IncomingTypeMultipliers.Add(new TypeDamageMultiplier(new Id<ElementType>(mt.Text), n));
                    return true;
                case EffectAction.StabMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    v.StabMultiplierOverride = n;
                    return true;
                case EffectAction.PriorityBonus:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b)) return false;
                    if (Only(b, ConditionKind.MoveCategory, out var cat) && string.Equals(cat.Text, "Status", StringComparison.OrdinalIgnoreCase))
                    { v.StatusMovePriorityBonus = (int)Math.Round(n); return true; }
                    if (Only(b, ConditionKind.MoveType, out var pt)) { x.PriorityType = pt.Text; x.PriorityTypeBonus = (int)Math.Round(n); return true; }
                    return false;
                case EffectAction.BlockStatDrops:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    if (b.Ref.Length == 0) v.PreventsStatReduction = true;
                    else foreach (var s in Refs(b)) v.PreventedStatDrops.Add(new StatId(s));
                    return true;
                case EffectAction.CureStatus:
                    if (!Plain(b) && !(b.Trigger == EffectTrigger.EndOfTurn && b.Chance < 100f && b.MaxPerBattle == 0 && !b.Consumes)) return false;
                    if (b.Ref.Length > 0 || b.Target != BlockTarget.Self) return false;
                    if (b.Trigger == EffectTrigger.OnSwitchOut && NoConds(b) && Plain(b)) { v.CuresStatusOnSwitchOut = true; return true; }
                    if (b.Trigger == EffectTrigger.EndOfTurn && NoConds(b) && b.Chance < 100f) { v.EndOfTurnCureStatusChance = new Percentage(b.Chance); return true; }
                    if (b.Trigger == EffectTrigger.EndOfTurn && Plain(b) && Only(b, ConditionKind.Weather, out var cw) && x.CuresStatusInWeather.Length == 0)
                    { x.CuresStatusInWeather = cw.Text; return true; }
                    return false;
                case EffectAction.HealPercent:
                    if (!Plain(b) || b.Target != BlockTarget.Self) return false;
                    switch (b.Trigger)
                    {
                        case EffectTrigger.OnSwitchOut when NoConds(b): v.HealPercentOnSwitchOut = new Percentage(Clamp100(n)); return true;
                        case EffectTrigger.OnAbsorb when NoConds(b): v.AbsorbImmuneHealPercent = new Percentage(Clamp100(n)); return true;
                        case EffectTrigger.EndOfTurn when NoConds(b) && v.EndOfTurnHealPercent.Value <= 0f:
                            v.EndOfTurnHealPercent = new Percentage(Clamp100(n)); return true;
                        case EffectTrigger.EndOfTurn when IsSelfStatus(b) && v.EndOfTurnHealPercent.Value <= 0f:
                            v.EndOfTurnHealPercent = new Percentage(Clamp100(n)); v.EndOfTurnHealRequiresStatus = true; return true;
                        case EffectTrigger.EndOfTurn when Only(b, ConditionKind.Weather, out var hw):
                            v.WeatherHpChanges.Add((hw.Text, n)); return true;
                    }
                    return false;
                case EffectAction.LoseHpPercent:
                    if (!Plain(b)) return false;
                    if (b.Trigger == EffectTrigger.EndOfTurn && b.Target == BlockTarget.Self && Only(b, ConditionKind.Weather, out var lw))
                    { v.WeatherHpChanges.Add((lw.Text, -n)); return true; }
                    if (b.Trigger == EffectTrigger.EndOfTurn && b.Target == BlockTarget.Other && Only(b, ConditionKind.HasStatus, out var sl)
                        && sl.Subject == ConditionSubject.Other && string.Equals(sl.Text, "sleep", StringComparison.OrdinalIgnoreCase))
                    { x.BadDreamsPercent = n; return true; }
                    if (b.Trigger == EffectTrigger.ContactTaken && b.Target == BlockTarget.Other)
                    {
                        if (NoConds(b)) { x.ContactDamagePercent = n; return true; }
                        if (Only(b, ConditionKind.HpPercent, out var dead) && dead.Subject == ConditionSubject.Self
                            && dead.Comparison == Comparison.LessOrEqual && dead.Number <= 0f) { x.AftermathPercent = n; return true; }
                    }
                    return false;
                case EffectAction.ChangeStage:
                {
                    if (!Plain(b) || b.Ref.Length == 0) return false;
                    var stat = new StatId(b.Ref);
                    int st = (int)Math.Round(n);
                    switch (b.Trigger)
                    {
                        case EffectTrigger.OnEntry when NoConds(b) && !v.OnEntryStat.HasValue:
                            v.OnEntryStat = stat; v.OnEntryStages = st; v.OnEntryTargetsSelf = b.Target == BlockTarget.Self; return true;
                        case EffectTrigger.EndOfTurn when NoConds(b) && b.Target == BlockTarget.Self && !v.EndOfTurnStat.HasValue:
                            v.EndOfTurnStat = stat; v.EndOfTurnStages = st; return true;
                        case EffectTrigger.OnAbsorb when NoConds(b) && b.Target == BlockTarget.Self && !x.OnImmuneStat.HasValue:
                            x.OnImmuneStat = stat; x.OnImmuneStages = st; return true;
                        case EffectTrigger.OnKo when NoConds(b) && b.Target == BlockTarget.Self && !x.OnKoStat.HasValue:
                            x.OnKoStat = stat; x.OnKoStages = st; return true;
                        case EffectTrigger.OnStatDropped when NoConds(b) && b.Target == BlockTarget.Self && !x.OnStatDroppedStat.HasValue:
                            x.OnStatDroppedStat = stat; x.OnStatDroppedStages = st; return true;
                        case EffectTrigger.OnFlinch when NoConds(b) && b.Target == BlockTarget.Self && !x.OnFlinchStat.HasValue:
                            x.OnFlinchStat = stat; x.OnFlinchStages = st; return true;
                        case EffectTrigger.ContactTaken when NoConds(b) && b.Target == BlockTarget.Other && !x.ContactStatDrop.HasValue:
                            x.ContactStatDrop = stat; x.ContactStatDropStages = st; return true;
                        case EffectTrigger.AfterHit when b.Target == BlockTarget.Self && st != 0:
                            v.OnHitStats.Add(new OnHitStat(stat, st, b.Conditions)); return true;
                    }
                    return false;
                }
                case EffectAction.InflictStatus:
                    if (b.MaxPerBattle > 0 || b.Consumes || b.Target != BlockTarget.Other || b.Ref.Length == 0) return false;
                    if (b.Trigger == EffectTrigger.ContactTaken && NoConds(b) && !v.ContactReactionStatus.HasValue && v.ContactReactionStatuses.Count == 0)
                    {
                        var list = Refs(b).ToList();
                        if (list.Count == 1) v.ContactReactionStatus = new StatusId(list[0]);
                        else foreach (var s in list) v.ContactReactionStatuses.Add(new StatusId(s));
                        v.ContactReactionChance = new Percentage(b.Chance);
                        return true;
                    }
                    if (b.Trigger == EffectTrigger.OnDealDamage && Only(b, ConditionKind.MoveMakesContact, out _) && !x.OffensiveContactStatus.HasValue
                        && Refs(b).Count() == 1)
                    { x.OffensiveContactStatus = new StatusId(b.Ref); x.OffensiveContactChance = b.Chance; return true; }
                    return false;
                case EffectAction.Flinch:
                    // Stench: «al_hacer_daño: retroceso; prob=10». Without a chance it is a held-item style block.
                    if (b.Trigger != EffectTrigger.OnDealDamage || !NoConds(b) || b.Chance >= 100f || b.MaxPerBattle > 0 || b.Consumes
                        || b.Target != BlockTarget.Other || x.FlinchChanceOnAttack > 0f) return false;
                    x.FlinchChanceOnAttack = b.Chance;
                    return true;
                case EffectAction.DisableMove:
                    if (b.Trigger != EffectTrigger.AfterHit || !NoConds(b) || b.MaxPerBattle > 0 || b.Consumes) return false;
                    x.DisableOnHitChance = b.Chance;
                    return true;
                case EffectAction.CritStage:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    x.CritStageBonus += (int)Math.Round(n);
                    return true;
                case EffectAction.CritDamageMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    x.CritDamageMultiplier = n <= 0f ? 1f : n;
                    return true;
                case EffectAction.AccuracyMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b)) return false;
                    v.AccuracyModifiers.Add(new PowerModifier(n, b.Conditions));
                    return true;
                case EffectAction.EvasionMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b)) return false;
                    v.EvasionModifiers.Add(new PowerModifier(n, b.Conditions));
                    return true;
                case EffectAction.SetWeather:
                    if (b.Trigger != EffectTrigger.OnEntry || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    x.OnEntryWeather = b.Ref; x.OnEntryWeatherTurns = Math.Max(0, (int)Math.Round(n));
                    return true;
                case EffectAction.ImmuneToWeather:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    v.WeatherImmunities.AddRange(Refs(b));
                    return true;
                case EffectAction.SetType:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || b.Ref.Length == 0 || !Only(b, ConditionKind.Weather, out var tw)) return false;
                    v.TypeByWeather.Add((tw.Text, new Id<ElementType>(b.Ref)));
                    return true;
                case EffectAction.SecondaryChanceMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    x.SecondaryChanceMultiplier = n <= 0f ? 1f : n;
                    return true;
                case EffectAction.StageMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    x.StageMultiplier = Math.Max(1, (int)Math.Round(n));
                    return true;
                case EffectAction.WeightMultiplier:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    x.WeightMultiplier = n <= 0f ? 1f : n;
                    return true;
                case EffectAction.ConvertNormalType:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    x.ConvertNormalTo = b.Ref; x.ConvertBoost = n <= 0f ? 1f : n;
                    return true;
                case EffectAction.Announce:
                    if (b.Trigger != EffectTrigger.OnEntry || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    x.AnnounceOnEntry = b.Ref;
                    return true;
                case EffectAction.Trap:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    x.TrapsOpponent = true;
                    foreach (var t in Refs(b)) v.TrapOnlyTypes.Add(new Id<ElementType>(t));
                    return true;
                case EffectAction.TrapGrounded:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b)) return false;
                    x.TrapOnlyGrounded = true;
                    return true;
                case EffectAction.IgnoreImmunity:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    foreach (var t in Refs(b)) v.IgnoresImmunityFor.Add(new Id<ElementType>(t));
                    return true;
                case EffectAction.ImmuneToMoveTag:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    v.ImmuneToMoveTags.AddRange(Refs(b));
                    return true;
                case EffectAction.BlockMoveTag:
                    if (b.Trigger != EffectTrigger.Passive || !Plain(b) || !NoConds(b) || b.Ref.Length == 0) return false;
                    v.BlocksMoveTagsForAll.AddRange(Refs(b));
                    return true;
                case EffectAction.SurviveAt1Hp:
                    // Sturdy: «antes_de_golpe [si propio.vida>=100]: aguantar» (no item to use up).
                    if (b.Trigger != EffectTrigger.BeforeHit || !Plain(b) || !Only(b, ConditionKind.HpPercent, out var full)
                        || full.Subject != ConditionSubject.Self || full.Comparison != Comparison.GreaterOrEqual || full.Number < 100f) return false;
                    x.SurvivesFromFullHp = true;
                    return true;
            }
            return false;
        }

        private static float Clamp100(float f) => Math.Max(0f, Math.Min(100f, f));
    }

    /// <summary>What an ability's blocks mean for the engine (filled by AbilityEffects.Read).</summary>
    public sealed class AbilityViews
    {
        public readonly List<EffectBlock> All = new List<EffectBlock>();
        /// <summary>Blocks with no ability shape: they run like held-item blocks.</summary>
        public readonly List<EffectBlock> Generic = new List<EffectBlock>();

        public readonly List<StatPassiveModifier> PassiveModifiers = new List<StatPassiveModifier>();
        public readonly List<StatusId> StatusImmunities = new List<StatusId>();
        public readonly List<Id<ElementType>> TypeImmunities = new List<Id<ElementType>>();
        public Percentage AbsorbImmuneHealPercent;
        public StatusId? ContactReactionStatus;
        public Percentage ContactReactionChance;
        public StatId? OnEntryStat;
        public int OnEntryStages;
        public bool OnEntryTargetsSelf;
        public StatId? StatusStatBoostStat;
        public float StatusStatBoostMultiplier = 1f;
        public Id<ElementType>? LowHpBoostType;
        public Percentage LowHpThreshold;
        public float LowHpBoostMultiplier = 1f;
        public readonly List<TypeDamageMultiplier> IncomingTypeMultipliers = new List<TypeDamageMultiplier>();
        public float StabMultiplierOverride;
        public int StatusMovePriorityBonus;
        public bool PreventsStatReduction;
        public bool CuresStatusOnSwitchOut;
        public Percentage HealPercentOnSwitchOut;
        public StatId? EndOfTurnStat;
        public int EndOfTurnStages;
        public Percentage EndOfTurnHealPercent;
        public bool EndOfTurnHealRequiresStatus;
        public Percentage EndOfTurnCureStatusChance;
        public bool NegatesStatusDamage;
        public readonly List<PowerModifier> OffensivePowerModifiers = new List<PowerModifier>();
        public readonly List<PowerModifier> DefensivePowerModifiers = new List<PowerModifier>();

        public readonly AbilityExtras Extras = new AbilityExtras();
        public readonly List<ConditionalStat> ConditionalStats = new List<ConditionalStat>();
        public readonly List<PowerModifier> AccuracyModifiers = new List<PowerModifier>();
        public readonly List<PowerModifier> EvasionModifiers = new List<PowerModifier>();
        public readonly List<(string weather, float percent)> WeatherHpChanges = new List<(string, float)>();
        public readonly List<string> WeatherImmunities = new List<string>();
        public readonly List<(string weather, Id<ElementType> type)> TypeByWeather = new List<(string, Id<ElementType>)>();
        public readonly List<StatId> PreventedStatDrops = new List<StatId>();
        public readonly List<StatusId> ContactReactionStatuses = new List<StatusId>();
        public readonly List<OnHitStat> OnHitStats = new List<OnHitStat>();
        public readonly List<string> ImmuneToMoveTags = new List<string>();
        public readonly List<string> BlocksMoveTagsForAll = new List<string>();
        public readonly List<Id<ElementType>> TrapOnlyTypes = new List<Id<ElementType>>();
        public readonly List<Id<ElementType>> IgnoresImmunityFor = new List<Id<ElementType>>();
    }
}
