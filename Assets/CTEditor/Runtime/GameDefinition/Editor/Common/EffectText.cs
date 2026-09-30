using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>What kind of id an action needs (the editor shows the matching dropdown).</summary>
    public enum EffectRefKind { None, Type, StatusList, Status, Stat, Weather, Move, Form, Mechanic,
        TypeList, StatList, WeatherList, TagList, Announce, Special, SideCondition }

    /// <summary>What the action's number means (the editor shows the matching field).</summary>
    public enum EffectAmountKind { None, Hp, Percent, Multiplier, Stages, Turns, Steps, Levels, Points, Ball, Duration }

    /// <summary>
    /// EFFECT BLOCKS for people: Spanish labels, which parameters each action needs, a sentence for each block and the
    /// Excel text format («antes_de_golpe [si mov.tipo=fire & propio.eficacia>1]: daño_recibido x0,5; se_gasta»).
    /// Shared by the item editor and the ability editor. UI text lives here so it can be translated in one place.
    /// </summary>
    public static class EffectText
    {
        // ---------------- Triggers ----------------

        private static readonly (EffectTrigger t, string key, string label, string help)[] Triggers =
        {
            (EffectTrigger.OnUse, "al_usar", "Al usarlo", "Desde la mochila (en combate o fuera, según «Dónde se usa»)."),
            (EffectTrigger.Passive, "siempre", "Mientras lo lleva", "Siempre activo mientras lo lleva equipado."),
            (EffectTrigger.OnEntry, "al_entrar", "Al entrar al combate", "Cuando sale al campo."),
            (EffectTrigger.EndOfTurn, "fin_de_turno", "Al final de cada turno", ""),
            (EffectTrigger.BeforeHit, "antes_de_golpe", "Antes de recibir un golpe", "Mientras se calcula el daño que va a recibir."),
            (EffectTrigger.AfterHit, "tras_golpe", "Tras recibir un golpe", "Tras un golpe que le hace daño (si sigue en pie)."),
            (EffectTrigger.ContactTaken, "contacto", "Al recibir un golpe con contacto", "Funciona aunque se debilite."),
            (EffectTrigger.OnDealDamage, "al_hacer_daño", "Tras hacer daño", "Después de golpear con un movimiento que hace daño."),
            (EffectTrigger.OnStatus, "al_sufrir_estado", "Al sufrir un estado", "En cuanto le ponen un estado."),
            (EffectTrigger.LowHp, "poca_vida", "Con poca vida", "Cuando sus PS bajan del umbral."),
            (EffectTrigger.OnWalk, "al_caminar", "Al caminar", "Cada paso fuera del combate."),
            (EffectTrigger.OnSwitchOut, "al_retirarse", "Al retirarse", "Cuando sale del campo (cambio)."),
            (EffectTrigger.OnAbsorb, "al_absorber", "Al absorber un golpe", "Cuando una inmunidad anula un golpe que le iba a dar."),
            (EffectTrigger.OnKo, "al_debilitar", "Al debilitar a un rival", ""),
            (EffectTrigger.OnStatDropped, "al_bajarle_stat", "Si el rival le baja una estadística", ""),
            (EffectTrigger.OnFlinch, "al_retroceder", "Al retroceder", ""),
        };

        /// <summary>Triggers that make sense for abilities (no «use from the bag» nor «walking»).</summary>
        public static EffectTrigger[] AbilityTriggers => AllTriggers.Where(t => t != EffectTrigger.OnUse && t != EffectTrigger.OnWalk).ToArray();

        /// <summary>The trigger's label; for abilities «Passive» reads «Siempre».</summary>
        public static string Label(EffectTrigger t, bool ability) => ability && t == EffectTrigger.Passive ? "Siempre" : Label(t);

        public static string Label(EffectTrigger t) => Triggers.First(x => x.t == t).label;
        public static string Help(EffectTrigger t) => Triggers.First(x => x.t == t).help;
        public static string Key(EffectTrigger t) => Triggers.First(x => x.t == t).key;
        public static EffectTrigger[] AllTriggers => Triggers.Select(x => x.t).ToArray();

        // ---------------- Actions ----------------

        private sealed class ActionInfo
        {
            public EffectAction Action; public string Key, Label; public EffectRefKind Ref; public EffectAmountKind Amount;
            public bool UsesTarget; public BlockTarget DefaultTarget;
        }

        private static ActionInfo A(EffectAction a, string key, string label, EffectRefKind r = EffectRefKind.None,
            EffectAmountKind n = EffectAmountKind.None, bool target = false, BlockTarget def = BlockTarget.Self)
            => new ActionInfo { Action = a, Key = key, Label = label, Ref = r, Amount = n, UsesTarget = target, DefaultTarget = def };

        private static readonly ActionInfo[] Actions =
        {
            A(EffectAction.HealHp, "curar", "Curar PS", n: EffectAmountKind.Hp, target: true),
            A(EffectAction.HealPercent, "curar_pct", "Curar % de PS", n: EffectAmountKind.Percent, target: true),
            A(EffectAction.LoseHpPercent, "perder", "Perder % de PS", n: EffectAmountKind.Percent, target: true),
            A(EffectAction.HealFromDamagePercent, "curar_del_daño", "Recuperar % del daño hecho", n: EffectAmountKind.Percent),
            A(EffectAction.CureStatus, "curar_estado", "Curar estados", EffectRefKind.StatusList, target: true),
            A(EffectAction.InflictStatus, "poner_estado", "Poner un estado", EffectRefKind.Status, target: true),
            A(EffectAction.Revive, "revivir", "Revivir", n: EffectAmountKind.Percent),
            A(EffectAction.RestorePp, "pp", "Recuperar PP (un movimiento)", n: EffectAmountKind.Points),
            A(EffectAction.RestorePpAll, "pp_todos", "Recuperar PP (todos)", n: EffectAmountKind.Points),
            A(EffectAction.ChangeStage, "etapa", "Cambiar una etapa", EffectRefKind.Stat, EffectAmountKind.Stages, target: true),
            A(EffectAction.MultiplyStat, "stat", "Multiplicar una estadística", EffectRefKind.Stat, EffectAmountKind.Multiplier),
            A(EffectAction.CritStage, "critico", "Subir el índice de crítico", n: EffectAmountKind.Stages),
            A(EffectAction.AccuracyMultiplier, "precision", "Precisión de sus movimientos", n: EffectAmountKind.Multiplier),
            A(EffectAction.EvasionMultiplier, "evasion", "Precisión de quien le ataca", n: EffectAmountKind.Multiplier),
            A(EffectAction.PowerMultiplier, "potencia", "Potencia de sus movimientos", n: EffectAmountKind.Multiplier),
            A(EffectAction.DamageDealtMultiplier, "daño", "Daño que hace", n: EffectAmountKind.Multiplier),
            A(EffectAction.DamageTakenMultiplier, "daño_recibido", "Daño que recibe", n: EffectAmountKind.Multiplier),
            A(EffectAction.SurviveAt1Hp, "aguantar", "Aguantar con 1 PS"),
            A(EffectAction.ImmuneToType, "inmune", "Inmune a un tipo", EffectRefKind.Type),
            A(EffectAction.ActFirst, "primero", "Actuar el primero"),
            A(EffectAction.ChoiceLock, "eleccion", "Solo repite su primer movimiento"),
            A(EffectAction.BlockStatusMoves, "sin_movs_estado", "No puede usar movimientos de estado"),
            A(EffectAction.ExtendWeather, "clima", "Alarga el clima que pone", EffectRefKind.Weather, EffectAmountKind.Turns),
            A(EffectAction.ExtendScreens, "pantallas", "Alarga sus pantallas", n: EffectAmountKind.Turns),
            A(EffectAction.Flinch, "retroceso", "Hacer retroceder", target: true, def: BlockTarget.Other),
            A(EffectAction.ConsumeItem, "gastar", "Se gasta el objeto"),
            A(EffectAction.Catch, "captura", "Capturar (bola)", n: EffectAmountKind.Ball),
            A(EffectAction.Friendship, "amistad", "Cambiar la amistad", n: EffectAmountKind.Points),
            A(EffectAction.Evs, "evs", "Cambiar EVs", EffectRefKind.Stat, EffectAmountKind.Points),
            A(EffectAction.LevelUp, "nivel", "Subir de nivel", n: EffectAmountKind.Levels),
            A(EffectAction.TeachMove, "enseñar", "Enseñar un movimiento (MT)", EffectRefKind.Move),
            A(EffectAction.EscapeBattle, "huir", "Huir del combate"),
            A(EffectAction.Repel, "repelente", "Repelente", n: EffectAmountKind.Steps),
            A(EffectAction.ChangeForm, "forma", "Cambiar de forma", EffectRefKind.Form),
            A(EffectAction.EnableMechanic, "mecanica", "Permite una mecánica", EffectRefKind.Mechanic),
            // --- abilities ---
            A(EffectAction.ImmuneToStatus, "inmune_estado", "Inmune a estados", EffectRefKind.StatusList),
            A(EffectAction.PowerTakenMultiplier, "potencia_recibida", "Potencia de los golpes que recibe", n: EffectAmountKind.Multiplier),
            A(EffectAction.StabMultiplier, "stab", "Bonus por mismo tipo (STAB)", n: EffectAmountKind.Multiplier),
            A(EffectAction.PriorityBonus, "prioridad", "Prioridad de sus movimientos", n: EffectAmountKind.Stages),
            A(EffectAction.BlockStatDrops, "sin_bajadas", "El rival no le baja estadísticas", EffectRefKind.StatList),
            A(EffectAction.SetWeather, "poner_clima", "Poner un clima", EffectRefKind.Weather, EffectAmountKind.Duration),
            A(EffectAction.ImmuneToWeather, "inmune_clima", "El clima no le daña", EffectRefKind.WeatherList),
            A(EffectAction.SetType, "cambiar_tipo", "Cambiar su tipo", EffectRefKind.Type),
            A(EffectAction.CritDamageMultiplier, "daño_critico", "Daño de sus críticos", n: EffectAmountKind.Multiplier),
            A(EffectAction.SecondaryChanceMultiplier, "prob_secundarios", "Probabilidad de sus efectos secundarios", n: EffectAmountKind.Multiplier),
            A(EffectAction.StageMultiplier, "etapas_x", "Multiplica sus cambios de etapa", n: EffectAmountKind.Multiplier),
            A(EffectAction.WeightMultiplier, "peso", "Multiplica su peso", n: EffectAmountKind.Multiplier),
            A(EffectAction.ConvertNormalType, "normal_a_tipo", "Sus movimientos Normales cambian de tipo", EffectRefKind.Type, EffectAmountKind.Multiplier),
            A(EffectAction.Announce, "avisar", "Avisar al entrar", EffectRefKind.Announce),
            A(EffectAction.Trap, "atrapar", "El rival no puede huir ni cambiarse", EffectRefKind.TypeList),
            A(EffectAction.TrapGrounded, "atrapar_en_suelo", "Solo atrapa a los que pisan el suelo"),
            A(EffectAction.IgnoreImmunity, "ignora_inmunidad", "Sus movimientos de estos tipos alcanzan a los inmunes", EffectRefKind.TypeList),
            A(EffectAction.ImmuneToMoveTag, "inmune_etiqueta", "Inmune a movimientos con etiqueta", EffectRefKind.TagList),
            A(EffectAction.BlockMoveTag, "bloquea_etiqueta", "Nadie puede usar movimientos con etiqueta", EffectRefKind.TagList),
            A(EffectAction.DisableMove, "anular", "Anular el movimiento que le golpeó"),
            A(EffectAction.Special, "especial", "Comportamiento especial", EffectRefKind.Special),
            A(EffectAction.SetSideCondition, "poner_lado", "Poner un efecto de lado o un campo", EffectRefKind.SideCondition),
            A(EffectAction.ZMove, "movimiento_z", "Cristal Z: convierte el movimiento en un movimiento Z", EffectRefKind.Move),
        };

        /// <summary>Is the reference a list («a,b»)?</summary>
        public static bool IsList(EffectRefKind k) => k == EffectRefKind.StatusList || k == EffectRefKind.TypeList
            || k == EffectRefKind.StatList || k == EffectRefKind.WeatherList || k == EffectRefKind.TagList;

        private static ActionInfo Info(EffectAction a) => Actions.First(x => x.Action == a);
        public static string Label(EffectAction a) => Info(a).Label;
        public static string Key(EffectAction a) => Info(a).Key;
        public static EffectRefKind RefOf(EffectAction a) => Info(a).Ref;
        public static EffectAmountKind AmountOf(EffectAction a) => Info(a).Amount;
        public static bool UsesTarget(EffectAction a) => Info(a).UsesTarget;
        public static BlockTarget DefaultTarget(EffectAction a) => Info(a).DefaultTarget;
        public static EffectAction[] AllActions => Actions.Select(x => x.Action).ToArray();

        /// <summary>Actions that make sense with this trigger for an ABILITY (for the dropdown).</summary>
        public static EffectAction[] AbilityActionsFor(EffectTrigger t)
        {
            var instant = new[] { EffectAction.HealPercent, EffectAction.LoseHpPercent, EffectAction.CureStatus, EffectAction.InflictStatus,
                EffectAction.ChangeStage, EffectAction.Flinch, EffectAction.HealFromDamagePercent };
            switch (t)
            {
                case EffectTrigger.Passive:
                    return new[] { EffectAction.MultiplyStat, EffectAction.PowerMultiplier, EffectAction.PowerTakenMultiplier,
                        EffectAction.DamageDealtMultiplier, EffectAction.StabMultiplier, EffectAction.CritStage, EffectAction.CritDamageMultiplier,
                        EffectAction.AccuracyMultiplier, EffectAction.EvasionMultiplier, EffectAction.PriorityBonus, EffectAction.ImmuneToType,
                        EffectAction.ImmuneToStatus, EffectAction.ImmuneToWeather, EffectAction.ImmuneToMoveTag, EffectAction.BlockStatDrops,
                        EffectAction.SetType, EffectAction.ConvertNormalType, EffectAction.SecondaryChanceMultiplier, EffectAction.StageMultiplier,
                        EffectAction.WeightMultiplier, EffectAction.Trap, EffectAction.TrapGrounded, EffectAction.IgnoreImmunity,
                        EffectAction.BlockMoveTag, EffectAction.Special };
                case EffectTrigger.BeforeHit:
                    return new[] { EffectAction.DamageTakenMultiplier, EffectAction.SurviveAt1Hp };
                case EffectTrigger.OnEntry:
                    return instant.Concat(new[] { EffectAction.SetWeather, EffectAction.SetSideCondition, EffectAction.Announce, EffectAction.Special }).ToArray();
                case EffectTrigger.AfterHit:
                    return instant.Concat(new[] { EffectAction.DisableMove, EffectAction.Special }).ToArray();
                default:
                    return instant.Concat(new[] { EffectAction.Special }).ToArray();
            }
        }

        /// <summary>Actions that make sense with this trigger (for the dropdown).</summary>
        public static EffectAction[] ActionsFor(EffectTrigger t)
        {
            switch (t)
            {
                case EffectTrigger.OnUse:
                    return new[] { EffectAction.HealHp, EffectAction.HealPercent, EffectAction.CureStatus, EffectAction.Revive, EffectAction.RestorePp,
                        EffectAction.RestorePpAll, EffectAction.ChangeStage, EffectAction.Catch, EffectAction.Friendship, EffectAction.Evs,
                        EffectAction.LevelUp, EffectAction.TeachMove, EffectAction.EscapeBattle, EffectAction.Repel, EffectAction.ChangeForm };
                case EffectTrigger.Passive:
                    return new[] { EffectAction.MultiplyStat, EffectAction.PowerMultiplier, EffectAction.DamageDealtMultiplier, EffectAction.CritStage,
                        EffectAction.AccuracyMultiplier, EffectAction.EvasionMultiplier, EffectAction.ImmuneToType, EffectAction.ActFirst,
                        EffectAction.ChoiceLock, EffectAction.BlockStatusMoves, EffectAction.ExtendWeather, EffectAction.ExtendScreens,
                        EffectAction.EnableMechanic, EffectAction.ZMove };
                case EffectTrigger.BeforeHit:
                    return new[] { EffectAction.DamageTakenMultiplier, EffectAction.SurviveAt1Hp };
                case EffectTrigger.OnWalk:
                    return new[] { EffectAction.Repel };
                default:
                    return EffectRules.InstantActions.ToArray();
            }
        }

        // ---------------- Numbers ----------------

        public static string N(float f) => f.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
        private static string Signed(float f) => (f > 0 ? "+" : "") + N(f);

        public static string AmountLabel(EffectAmountKind k)
        {
            switch (k)
            {
                case EffectAmountKind.Hp: return "PS";
                case EffectAmountKind.Percent: return "%";
                case EffectAmountKind.Multiplier: return "×";
                case EffectAmountKind.Stages: return "Etapas";
                case EffectAmountKind.Turns: return "Turnos extra";
                case EffectAmountKind.Steps: return "Pasos";
                case EffectAmountKind.Levels: return "Niveles";
                case EffectAmountKind.Points: return "Cantidad";
                case EffectAmountKind.Ball: return "× captura (255 = siempre)";
                case EffectAmountKind.Duration: return "Turnos (0 = hasta que otro lo cambie)";
                default: return "";
            }
        }

        // ---------------- Sentences ----------------

        /// <summary>A block in one Spanish sentence, WITHOUT the trigger (the editor groups by trigger as a heading).</summary>
        public static string Describe(EffectBlock b, Func<EffectRefKind, string, string> name = null, bool ability = false)
        {
            name = name ?? ((k, id) => id);
            string who = b.Target == BlockTarget.Other ? "el rival" : "él";
            string Ref(EffectRefKind k) => string.IsNullOrWhiteSpace(b.Ref) ? "?" : string.Join(", ", b.RefList.Select(r => name(k, r)));
            string what;
            switch (b.Action)
            {
                case EffectAction.HealHp: what = (b.Target == BlockTarget.Other ? "el rival recupera " : "recupera ") + $"{N(b.Amount)} PS"; break;
                case EffectAction.HealPercent: what = (b.Target == BlockTarget.Other ? "el rival recupera " : "recupera ") + $"el {N(b.Amount)} % de sus PS"; break;
                case EffectAction.LoseHpPercent: what = (b.Target == BlockTarget.Other ? "el rival pierde " : "pierde ") + $"el {N(b.Amount)} % de sus PS"; break;
                case EffectAction.HealFromDamagePercent: what = $"recupera el {N(b.Amount)} % del daño que hace"; break;
                case EffectAction.CureStatus: what = b.Ref.Length == 0 ? "se cura de cualquier estado" : $"se cura de: {Ref(EffectRefKind.StatusList)}"; break;
                case EffectAction.InflictStatus: what = $"{(b.Target == BlockTarget.Other ? "al rival se le pone" : "se pone")} el estado {Ref(EffectRefKind.Status)}"; break;
                case EffectAction.Revive: what = $"revive con el {N(b.Amount)} % de sus PS"; break;
                case EffectAction.RestorePp: what = b.Amount >= 99 ? "recupera todos los PP de un movimiento" : $"recupera {N(b.Amount)} PP de un movimiento"; break;
                case EffectAction.RestorePpAll: what = b.Amount >= 99 ? "recupera todos los PP de todos sus movimientos" : $"recupera {N(b.Amount)} PP de cada movimiento"; break;
                case EffectAction.ChangeStage: what = $"{(b.Target == BlockTarget.Other ? "al rival: " : "")}{Ref(EffectRefKind.Stat)} {Signed(b.Amount)}"; break;
                case EffectAction.MultiplyStat: what = $"{Ref(EffectRefKind.Stat)} ×{N(b.Amount)}"; break;
                case EffectAction.CritStage: what = $"índice de crítico {Signed(b.Amount)}"; break;
                case EffectAction.AccuracyMultiplier: what = $"precisión de sus movimientos ×{N(b.Amount)}"; break;
                case EffectAction.EvasionMultiplier: what = $"los que le atacan tienen precisión ×{N(b.Amount)}"; break;
                case EffectAction.PowerMultiplier: what = $"potencia de sus movimientos ×{N(b.Amount)}"; break;
                case EffectAction.DamageDealtMultiplier: what = $"hace ×{N(b.Amount)} de daño"; break;
                case EffectAction.DamageTakenMultiplier: what = $"recibe ×{N(b.Amount)} de daño"; break;
                case EffectAction.SurviveAt1Hp: what = "aguanta con 1 PS un golpe que le debilitaría"; break;
                case EffectAction.ImmuneToType: what = $"es inmune al tipo {Ref(EffectRefKind.Type)}"; break;
                case EffectAction.ActFirst: what = "actúa el primero dentro de su prioridad"; break;
                case EffectAction.ChoiceLock: what = "solo puede repetir el primer movimiento que usa (hasta que se retire)"; break;
                case EffectAction.BlockStatusMoves: what = "no puede usar movimientos de estado"; break;
                case EffectAction.ExtendWeather: what = $"{(b.Ref.Length == 0 ? "el clima" : name(EffectRefKind.Weather, b.Ref))} que pone dura {N(b.Amount)} turnos más"; break;
                case EffectAction.ExtendScreens: what = $"sus pantallas duran {N(b.Amount)} turnos más"; break;
                case EffectAction.Flinch: what = $"{who} retrocede"; break;
                case EffectAction.ConsumeItem: what = "el objeto se gasta"; break;
                case EffectAction.Catch: what = b.Amount >= 255 ? "bola: captura siempre" : $"bola: captura ×{N(b.Amount)}"; break;
                case EffectAction.Friendship: what = $"amistad {Signed(b.Amount)}"; break;
                case EffectAction.Evs: what = $"EVs de {Ref(EffectRefKind.Stat)} {Signed(b.Amount)}"; break;
                case EffectAction.LevelUp: what = $"sube {N(b.Amount)} nivel(es)"; break;
                case EffectAction.TeachMove: what = $"enseña {Ref(EffectRefKind.Move)}"; break;
                case EffectAction.EscapeBattle: what = "huye del combate"; break;
                case EffectAction.Repel: what = $"los salvajes más débiles no aparecen durante {N(b.Amount)} pasos"; break;
                case EffectAction.ChangeForm: what = $"cambia a la forma {Ref(EffectRefKind.Form)}"; break;
                case EffectAction.EnableMechanic: what = $"permite {Ref(EffectRefKind.Mechanic)}"; break;
                case EffectAction.ImmuneToStatus: what = b.Ref.Length == 0 ? "no le pueden poner estados" : $"no le pueden poner: {Ref(EffectRefKind.StatusList)}"; break;
                case EffectAction.PowerTakenMultiplier: what = $"los golpes que recibe tienen potencia ×{N(b.Amount)}"; break;
                case EffectAction.StabMultiplier: what = $"el bonus por mismo tipo es ×{N(b.Amount)}"; break;
                case EffectAction.PriorityBonus: what = $"prioridad {Signed(b.Amount)}"; break;
                case EffectAction.BlockStatDrops: what = b.Ref.Length == 0 ? "el rival no le puede bajar estadísticas" : $"el rival no le puede bajar: {Ref(EffectRefKind.StatList)}"; break;
                case EffectAction.SetWeather: what = $"pone el clima {Ref(EffectRefKind.Weather)}" + (b.Amount > 0 ? $" durante {N(b.Amount)} turnos" : ""); break;
                case EffectAction.ImmuneToWeather: what = b.Ref == "*" ? "ningún clima le daña" : $"no le daña el clima: {Ref(EffectRefKind.WeatherList)}"; break;
                case EffectAction.SetType: what = $"pasa a ser de tipo {Ref(EffectRefKind.Type)}"; break;
                case EffectAction.CritDamageMultiplier: what = $"sus críticos hacen ×{N(b.Amount)}"; break;
                case EffectAction.SecondaryChanceMultiplier: what = $"la probabilidad de sus efectos secundarios es ×{N(b.Amount)}"; break;
                case EffectAction.StageMultiplier: what = $"sus cambios de etapa valen ×{N(b.Amount)}"; break;
                case EffectAction.WeightMultiplier: what = $"pesa ×{N(b.Amount)}"; break;
                case EffectAction.ConvertNormalType: what = $"sus movimientos Normales pasan a ser de tipo {Ref(EffectRefKind.Type)} (×{N(b.Amount)})"; break;
                case EffectAction.Announce: what = $"avisa: {Ref(EffectRefKind.Announce)}"; break;
                case EffectAction.Trap: what = b.Ref.Length == 0 ? "el rival no puede huir ni cambiarse" : $"los rivales de tipo {Ref(EffectRefKind.TypeList)} no pueden huir ni cambiarse"; break;
                case EffectAction.TrapGrounded: what = "solo atrapa a los que pisan el suelo"; break;
                case EffectAction.IgnoreImmunity: what = $"sus movimientos de tipo {Ref(EffectRefKind.TypeList)} alcanzan a los inmunes"; break;
                case EffectAction.ImmuneToMoveTag: what = $"no le afectan los movimientos: {Ref(EffectRefKind.TagList)}"; break;
                case EffectAction.BlockMoveTag: what = $"nadie puede usar movimientos: {Ref(EffectRefKind.TagList)}"; break;
                case EffectAction.DisableMove: what = "anula el movimiento que le golpeó"; break;
                case EffectAction.SetSideCondition: what = $"pone {Ref(EffectRefKind.SideCondition)}"; break;
                case EffectAction.ZMove: what = $"una vez por combate, el movimiento se convierte en {Ref(EffectRefKind.Move)} (movimiento Z)"; break;
                case EffectAction.Special:
                {
                    var sp = AbilityEffects.Special(b.Ref);
                    what = sp == null ? $"especial «{b.Ref}» (desconocido)" : char.ToLowerInvariant(sp.Label[0]) + sp.Label.Substring(1)
                        + (sp.UsesAmount ? $": {N(b.Amount)}" : "");
                    break;
                }
                default: what = b.Action.ToString(); break;
            }
            var parts = new List<string>();
            if (b.Trigger == EffectTrigger.LowHp) parts.Add($"con el {N(b.Threshold)} % de PS o menos");
            if (b.Conditions.Count > 0) parts.Add(ConditionText.Describe(b.Conditions));
            string s = (parts.Count > 0 ? string.Join(", ", parts) + ": " : "") + what;
            var extra = new List<string>();
            if (b.Chance < 100f) extra.Add($"{N(b.Chance)} % de probabilidad");
            if (b.MaxPerBattle > 0) extra.Add(b.MaxPerBattle == 1 ? "una vez por combate" : $"{b.MaxPerBattle} veces por combate");
            if (b.Consumes) extra.Add("se gasta");
            if (extra.Count > 0) s += " (" + string.Join(", ", extra) + ")";
            if (!(ability ? AbilityEffects.IsSupported(b) : EffectRules.IsSupported(b))) s += " — aún sin efecto en el motor";
            return char.ToUpperInvariant(s[0]) + s.Substring(1) + ".";
        }

        // ---------------- Excel text ----------------

        public static string Format(IEnumerable<EffectBlock> blocks) => string.Join(" | ", blocks.Select(Format));

        public static string Format(EffectBlock b)
        {
            var info = Info(b.Action);
            string head = Key(b.Trigger) + (b.Trigger == EffectTrigger.LowHp ? "@" + N(b.Threshold) : "");
            if (b.Conditions.Count > 0) head += " [si " + ConditionText.FormatAll(b.Conditions) + "]";
            string args = "";
            switch (b.Action)
            {
                case EffectAction.HealPercent: args = " " + N(b.Amount) + "%"; info = Info(EffectAction.HealHp); break;
                case EffectAction.CureStatus: args = b.Ref.Length > 0 ? " " + string.Join(",", b.RefList) : ""; break;
                case EffectAction.Special:
                {
                    // «siempre: rastro» / «siempre: umbral_bayas 50%»: the special's key IS the action word.
                    var sp = AbilityEffects.Special(b.Ref);
                    string word = b.Ref.Length > 0 ? b.Ref : "especial";
                    return head + ": " + word + (sp != null && sp.UsesAmount ? " " + N(b.Amount) + "%" : "") + Options(b, info);
                }
                default:
                    // Lists are written with commas: «|» separates blocks.
                    if (info.Ref != EffectRefKind.None && b.Ref.Length > 0) args += " " + string.Join(",", b.RefList);
                    switch (info.Amount)
                    {
                        case EffectAmountKind.None: break;
                        case EffectAmountKind.Multiplier: case EffectAmountKind.Ball: args += " x" + N(b.Amount); break;
                        case EffectAmountKind.Percent: args += " " + N(b.Amount) + "%"; break;
                        case EffectAmountKind.Stages: case EffectAmountKind.Turns: args += " " + Signed(b.Amount); break;
                        case EffectAmountKind.Duration: args += " " + N(b.Amount); break;
                        default: args += " " + N(b.Amount); break;
                    }
                    break;
            }
            return head + ": " + info.Key + args + Options(b, info);
        }

        private static string Options(EffectBlock b, ActionInfo info)
        {
            var opts = new List<string>();
            if (b.Consumes) opts.Add("se_gasta");
            if (b.Target != info.DefaultTarget && info.UsesTarget) opts.Add(b.Target == BlockTarget.Other ? "al_rival" : "a_si_mismo");
            if (b.Chance < 100f) opts.Add("prob=" + N(b.Chance));
            if (b.MaxPerBattle > 0) opts.Add("veces=" + b.MaxPerBattle);
            return opts.Count > 0 ? "; " + string.Join("; ", opts) : "";
        }

        /// <summary>Reads «bloque | bloque». Throws FormatException with a clear Spanish message.</summary>
        public static List<EffectBlock> Parse(string text)
        {
            var list = new List<EffectBlock>();
            foreach (var raw in (text ?? "").Split('|'))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;
                int colon = item.IndexOf(':');
                int bracket = item.IndexOf('[');
                int bracketEnd = item.IndexOf(']');
                if (bracket >= 0 && bracket < colon) colon = item.IndexOf(':', bracketEnd);
                if (colon < 0) throw new FormatException($"'{raw}': falta «:» entre el cuándo y el qué (ej. fin_de_turno: curar 6,25%).");
                string head = item.Substring(0, colon).Trim(), body = item.Substring(colon + 1).Trim();

                // Head: trigger[@threshold] [si conditions]
                var conditions = new List<Condition>();
                int br = head.IndexOf('[');
                if (br >= 0)
                {
                    int close = head.LastIndexOf(']');
                    if (close < br) throw new FormatException($"'{raw}': falta cerrar el corchete ].");
                    conditions = ConditionText.ParseAll(head.Substring(br + 1, close - br - 1));
                    head = head.Substring(0, br).Trim();
                }
                float threshold = 0f;
                int at = head.IndexOf('@');
                if (at >= 0)
                {
                    if (!Csv.CsvTable.TryNumber(head.Substring(at + 1), out threshold)) throw new FormatException($"'{raw}': umbral no válido tras @ (ej. poca_vida@50).");
                    head = head.Substring(0, at).Trim();
                }
                var trig = Triggers.FirstOrDefault(x => x.key == head.ToLowerInvariant());
                if (trig.key == null) throw new FormatException($"'{raw}': «{head}» no es un momento válido. Usa: {string.Join(", ", Triggers.Select(x => x.key))}.");

                // Body: action args; options
                var pieces = body.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                if (pieces.Count == 0) throw new FormatException($"'{raw}': falta la acción tras «:».");
                var words = pieces[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                string key = words[0].ToLowerInvariant();
                var act = Actions.FirstOrDefault(x => x.Key == key);
                var args = words.Skip(1).ToList();
                string reference = "";
                if (act == null && AbilityEffects.Special(key) != null)
                {
                    // «siempre: rastro»: a special behaviour written by its own key.
                    act = Info(EffectAction.Special);
                    args.Insert(0, key);
                }
                if (act == null) throw new FormatException($"'{raw}': «{key}» no es una acción válida. Usa: {string.Join(", ", Actions.Where(x => x.Action != EffectAction.HealPercent).Select(x => x.Key))} o un comportamiento especial ({string.Join(", ", AbilityEffects.Specials.Select(x => x.Key))}).");
                var action = act.Action;
                float amount = 0f;

                bool TakeNumber(string w, out float v)
                {
                    string t = w.Trim().TrimStart('x', 'X', '×', '+').TrimEnd('%');
                    bool neg = w.Trim().StartsWith("-");
                    if (neg) t = t.TrimStart('-');
                    bool ok = Csv.CsvTable.TryNumber(t, out v);
                    if (neg) v = -v;
                    return ok;
                }
                foreach (var w in args)
                {
                    if (TakeNumber(w, out var v))
                    {
                        amount = v;
                        if (action == EffectAction.HealHp && w.EndsWith("%")) action = EffectAction.HealPercent;
                    }
                    else reference = reference.Length == 0 ? w : reference + "|" + w;
                }
                reference = string.Join("|", reference.Split(',', '|').Select(s => s.Trim()).Where(s => s.Length > 0));

                var target = DefaultTarget(action);
                bool consumes = false; float chance = 100f; int times = 0;
                foreach (var o in pieces.Skip(1))
                {
                    string op = o.ToLowerInvariant().Replace(" ", "");
                    if (op == "se_gasta" || op == "gasta") consumes = true;
                    else if (op == "al_rival") target = BlockTarget.Other;
                    else if (op == "a_si_mismo" || op == "propio") target = BlockTarget.Self;
                    else if (op.StartsWith("prob=") && Csv.CsvTable.TryNumber(op.Substring(5).TrimEnd('%'), out var p)) chance = p;
                    else if (op.StartsWith("veces=") && int.TryParse(op.Substring(6), out var n)) times = n;
                    else throw new FormatException($"'{raw}': opción «{o}» desconocida. Usa: se_gasta, al_rival, a_si_mismo, prob=N, veces=N.");
                }
                list.Add(new EffectBlock(trig.t, action, amount, reference, conditions, target, threshold, consumes, chance, times));
            }
            return list;
        }

        // ---------------- SerializedProperty helpers ----------------

        /// <summary>Writes blocks into an EffectBlockData[] property.</summary>
        public static void WriteAll(SerializedProperty array, IList<EffectBlock> blocks)
        {
            array.arraySize = blocks.Count;
            for (int i = 0; i < blocks.Count; i++) Write(array.GetArrayElementAtIndex(i), blocks[i]);
        }

        public static void Write(SerializedProperty el, EffectBlock b)
        {
            el.FindPropertyRelative(nameof(EffectBlockData.trigger)).intValue = (int)b.Trigger;
            el.FindPropertyRelative(nameof(EffectBlockData.action)).intValue = (int)b.Action;
            el.FindPropertyRelative(nameof(EffectBlockData.target)).intValue = (int)b.Target;
            el.FindPropertyRelative(nameof(EffectBlockData.reference)).stringValue = b.Ref;
            el.FindPropertyRelative(nameof(EffectBlockData.amount)).floatValue = b.Amount;
            el.FindPropertyRelative(nameof(EffectBlockData.threshold)).floatValue = b.Threshold;
            el.FindPropertyRelative(nameof(EffectBlockData.consumes)).boolValue = b.Consumes;
            el.FindPropertyRelative(nameof(EffectBlockData.chance)).floatValue = b.Chance;
            el.FindPropertyRelative(nameof(EffectBlockData.maxPerBattle)).intValue = b.MaxPerBattle;
            ConditionText.WriteAll(el.FindPropertyRelative(nameof(EffectBlockData.conditions)), b.Conditions.ToList());
        }

        /// <summary>A new block with sensible defaults for the action.</summary>
        public static EffectBlock Default(EffectTrigger t, EffectAction a)
        {
            float amount;
            switch (AmountOf(a))
            {
                case EffectAmountKind.Multiplier: amount = a == EffectAction.DamageTakenMultiplier ? 0.5f : 1.2f; break;
                case EffectAmountKind.Percent: amount = a == EffectAction.Revive ? 50f : 25f; break;
                case EffectAmountKind.Hp: amount = 20f; break;
                case EffectAmountKind.Stages: amount = 1f; break;
                case EffectAmountKind.Turns: amount = 3f; break;
                case EffectAmountKind.Steps: amount = 100f; break;
                case EffectAmountKind.Levels: amount = 1f; break;
                case EffectAmountKind.Points: amount = 10f; break;
                case EffectAmountKind.Ball: amount = 1f; break;
                case EffectAmountKind.Duration: amount = 0f; break;
                default: amount = 0f; break;
            }
            return new EffectBlock(t, a, amount, target: DefaultTarget(a), threshold: t == EffectTrigger.LowHp ? 50f : 0f,
                chance: a == EffectAction.ActFirst ? 20f : a == EffectAction.Flinch ? 10f : 100f);
        }
    }
}
