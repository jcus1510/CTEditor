using System.Linq;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de HABILIDADES (menú CTEditor → Criaturas → Habilidades), con una biblioteca de 37 habilidades
    /// clásicas lista para usar o para tomar como punto de partida.
    ///
    /// Las plantillas se expresan con las MISMAS piezas del motor (inmunidades, reacción por contacto,
    /// cambio de etapa al entrar, refuerzo a poca vida...), así que al combinarlas o ajustarlas el
    /// autor puede inventar habilidades nuevas sin programar.
    /// </summary>
    public sealed class AbilityEditorWindow : ContentEditorWindow<AbilityData>
    {
        [MenuItem(EditorMenus.Creatures + "Habilidades", false, EditorMenus.CreaturesOrder + 3)]
        public static void Open() => OpenWindow<AbilityEditorWindow>("Habilidades");

        protected override string Category => ContentFolders.Abilities;
        protected override string Noun => "habilidad";
        protected override string Intro =>
            "Cada especie puede tener una habilidad pasiva. Usa una plantilla clásica o combina los campos para inventar la tuya.";

        protected override string Title => "Habilidades";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear las habilidades clásicas» o elige una plantilla para la habilidad seleccionada.",
            "Combina piezas: inmunidades, reacción por contacto, efecto al entrar, fin de turno…",
            "Para potenciar ataques usa «Potencia al atacar» con condiciones (ej.: ×1,5 si la potencia es 60 o menos).",
            "Asígnala a una especie en su ficha (campo «Habilidad»).",
        };

        private int _presetIndex;

        // ---------------- Biblioteca clásica ----------------

        private sealed class Preset
        {
            public string Group, Id, Name, Summary;
            public Action<SerializedObject, Ctx> Fill;
        }

        // Contexto de aplicación: buscar tipos por id y anotar los que falten.
        private sealed class Ctx
        {
            private readonly Dictionary<string, ElementTypeData> _types = new Dictionary<string, ElementTypeData>();
            public readonly List<string> Missing = new List<string>();
            public Ctx() { foreach (var t in ContentAssets.LoadAll<ElementTypeData>()) if (!string.IsNullOrEmpty(t.Id)) _types[t.Id] = t; }
            public ElementTypeData Type(string id)
            {
                if (_types.TryGetValue(id, out var t)) return t;
                if (!Missing.Contains(id)) Missing.Add(id);
                return null;
            }
        }

        private static readonly Preset[] Library =
        {
            // Al entrar al campo
            P("Al entrar", "intimidate", "Intimidación", "Baja 1 etapa el Ataque del rival al entrar.",
                (so, c) => { Str(so, "onEntryStatId", "attack"); Int(so, "onEntryStages", -1); }),

            // Reacción por contacto
            P("Por contacto", "static", "Electricidad Estática", "30% de paralizar a quien le golpee con contacto.",
                (so, c) => Contact(so, "paralysis", 30)),
            P("Por contacto", "flame_body", "Cuerpo Llama", "30% de quemar a quien le golpee con contacto.",
                (so, c) => Contact(so, "burn", 30)),
            P("Por contacto", "poison_point", "Punto Tóxico", "30% de envenenar a quien le golpee con contacto.",
                (so, c) => Contact(so, "poison", 30)),

            // Inmunidades de tipo
            P("Inmunidad de tipo", "water_absorb", "Absorbe Agua", "Inmune a Agua: en vez de daño, recupera 25% de PS.",
                (so, c) => { Types(so, "typeImmunities", c.Type("water")); Flt(so, "absorbImmuneHealPercent", 25); }),
            P("Inmunidad de tipo", "volt_absorb", "Absorbe Elec", "Inmune a Eléctrico: en vez de daño, recupera 25% de PS.",
                (so, c) => { Types(so, "typeImmunities", c.Type("electric")); Flt(so, "absorbImmuneHealPercent", 25); }),
            P("Inmunidad de tipo", "levitate", "Levitación", "Inmune a los movimientos de tipo Tierra.",
                (so, c) => Types(so, "typeImmunities", c.Type("ground"))),

            // Inmunidades de estado
            P("Inmunidad de estado", "immunity", "Inmunidad", "No puede ser envenenado.",
                (so, c) => Strs(so, "statusImmunities", "poison", "toxic")),
            P("Inmunidad de estado", "insomnia", "Insomnio", "No puede dormirse.",
                (so, c) => Strs(so, "statusImmunities", "sleep", "drowsy")),
            P("Inmunidad de estado", "limber", "Flexibilidad", "No puede ser paralizado.",
                (so, c) => Strs(so, "statusImmunities", "paralysis")),
            P("Inmunidad de estado", "water_veil", "Velo Agua", "No puede ser quemado.",
                (so, c) => Strs(so, "statusImmunities", "burn")),
            P("Inmunidad de estado", "magma_armor", "Escudo Magma", "No puede ser congelado.",
                (so, c) => Strs(so, "statusImmunities", "freeze")),
            P("Inmunidad de estado", "own_tempo", "Ritmo Propio", "No puede ser confundido.",
                (so, c) => Strs(so, "statusImmunities", "confusion")),

            // Stats pasivas y condicionales
            P("Estadísticas", "huge_power", "Potencia", "Duplica su Ataque.",
                (so, c) => Passive(so, "attack", 2f)),
            P("Estadísticas", "guts", "Agallas", "Ataque ×1.5 si tiene un estado alterado.",
                (so, c) => { Str(so, "statusStatBoostStatId", "attack"); Flt(so, "statusStatBoostMultiplier", 1.5f); }),
            P("Estadísticas", "quick_feet", "Pies Rápidos", "Velocidad ×1.5 si tiene un estado alterado.",
                (so, c) => { Str(so, "statusStatBoostStatId", "speed"); Flt(so, "statusStatBoostMultiplier", 1.5f); }),
            P("Estadísticas", "marvel_scale", "Escama Especial", "Defensa ×1.5 si tiene un estado alterado.",
                (so, c) => { Str(so, "statusStatBoostStatId", "defense"); Flt(so, "statusStatBoostMultiplier", 1.5f); }),

            // A poca vida
            P("A poca vida", "blaze", "Mar Llamas", "Movimientos de Fuego ×1.5 con 1/3 de PS o menos.",
                (so, c) => LowHp(so, c.Type("fire"))),
            P("A poca vida", "overgrow", "Espesura", "Movimientos de Planta ×1.5 con 1/3 de PS o menos.",
                (so, c) => LowHp(so, c.Type("grass"))),
            P("A poca vida", "torrent", "Torrente", "Movimientos de Agua ×1.5 con 1/3 de PS o menos.",
                (so, c) => LowHp(so, c.Type("water"))),
            P("A poca vida", "swarm", "Enjambre", "Movimientos de Bicho ×1.5 con 1/3 de PS o menos.",
                (so, c) => LowHp(so, c.Type("bug"))),

            // Daño recibido
            P("Daño recibido", "thick_fat", "Sebo", "Recibe la mitad de daño de Fuego y de Hielo.",
                (so, c) => Incoming(so, (c.Type("fire"), 0.5f), (c.Type("ice"), 0.5f))),

            // Ataque / prioridad / defensa de stats
            P("Combate", "adaptability", "Adaptable", "El bonus por mismo tipo (STAB) pasa de ×1,5 a ×2.",
                (so, c) => Flt(so, "stabMultiplierOverride", 2f)),
            P("Combate", "prankster", "Bromista", "+1 de prioridad a sus movimientos de estado.",
                (so, c) => Int(so, "statusMovePriorityBonus", 1)),
            P("Combate", "clear_body", "Cuerpo Puro", "El rival no puede bajarle las estadísticas.",
                (so, c) => Bool(so, "preventsStatReduction", true)),

            // Al cambiarse
            P("Al cambiarse", "natural_cure", "Cura Natural", "Se cura el estado alterado al retirarse.",
                (so, c) => Bool(so, "curesStatusOnSwitchOut", true)),
            P("Al cambiarse", "regenerator", "Regeneración", "Recupera 1/3 de sus PS al retirarse.",
                (so, c) => Flt(so, "healPercentOnSwitchOut", 33.33f)),

            // Fin de turno
            P("Fin de turno", "speed_boost", "Impulso", "Sube 1 etapa su Velocidad al final de cada turno.",
                (so, c) => { Str(so, "endOfTurnStatId", "speed"); Int(so, "endOfTurnStages", 1); }),
            P("Fin de turno", "poison_heal", "Antídoto", "Con estado alterado, no sufre daño residual y recupera 1/8 de PS por turno.",
                (so, c) => { Flt(so, "endOfTurnHealPercent", 12.5f); Bool(so, "endOfTurnHealRequiresStatus", true); Bool(so, "negatesStatusDamage", true); }),
            P("Fin de turno", "shed_skin", "Mudar", "1/3 de probabilidad de curarse el estado al final de cada turno.",
                (so, c) => Flt(so, "endOfTurnCureStatusChance", 33.33f)),

            // Lote A: potencia con condiciones (se combinan con las etiquetas y las condiciones de los movimientos)
            P("Potencia", "technician", "Experto", "Movimientos de potencia 60 o menos: ×1,5.",
                (so, c) => Offensive(so, 1.5f, new Condition(ConditionKind.MovePower, comparison: Comparison.LessOrEqual, number: 60))),
            P("Potencia", "iron_fist", "Puño Férreo", "Movimientos con la etiqueta 'puño': ×1,2.",
                (so, c) => Offensive(so, 1.2f, new Condition(ConditionKind.MoveHasTag, text: "puño"))),
            P("Potencia", "strong_jaw", "Mandíbula Fuerte", "Movimientos con la etiqueta 'mordisco': ×1,5.",
                (so, c) => Offensive(so, 1.5f, new Condition(ConditionKind.MoveHasTag, text: "mordisco"))),
            P("Potencia", "tough_claws", "Garra Dura", "Movimientos que hacen contacto: ×1,3.",
                (so, c) => Offensive(so, 1.3f, new Condition(ConditionKind.MoveMakesContact))),
            P("Potencia", "solar_power", "Poder Solar", "Movimientos especiales ×1,5 si hace sol.",
                (so, c) => Offensive(so, 1.5f, new Condition(ConditionKind.MoveCategory, text: "Special"), new Condition(ConditionKind.Weather, text: "sun"))),
            P("Potencia", "fluffy", "Peluche", "Recibe la mitad de daño de los movimientos de contacto.",
                (so, c) => Defensive(so, 0.5f, new Condition(ConditionKind.MoveMakesContact))),
            P("Potencia", "multiscale", "Compensación", "Con la vida al máximo recibe la mitad de daño.",
                (so, c) => Defensive(so, 0.5f, new Condition(ConditionKind.HpPercent, ConditionSubject.Self, Comparison.GreaterOrEqual, 100))),
        };

        /// <summary>Crea las habilidades clásicas que falten. Devuelve cuántas creó.</summary>
        public static int CreateClassicSet() => CreateClassicSet(out _);

        /// <summary>Igual, pero sin crear los ids de 'skipIds' (los trae un pack: sus datos mandan).</summary>
        public static int CreateClassicSet(out List<string> missingTypes, ICollection<string> skipIds = null)
        {
            int created = 0;
            var ctx = new Ctx();
            foreach (var p in Library)
            {
                if (skipIds != null && skipIds.Contains(p.Id)) continue;
                var preset = p;
                if (ContentAssets.CreateIfMissing<AbilityData>(ContentFolders.Abilities, p.Id, p.Name,
                        so => { Reset(so); preset.Fill(so, ctx); }))
                    created++;
            }
            missingTypes = ctx.Missing;
            return created;
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(p => (p.Id, p.Name, p.Group)).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            var p = Library.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            Reset(so);
            p.Fill(so, new Ctx());
            so.FindProperty("displayName").stringValue = p.Name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear las {Library.Length} habilidades clásicas"))
            {
                int n = CreateClassicSet(out var missing);
                FinishBulk(n, "habilidades");
                if (missing.Count > 0)
                    EditorUtility.DisplayDialog("Faltan tipos",
                        "Algunas habilidades usan tipos que aún no existen: " + string.Join(", ", missing) +
                        ".\nCrea los tipos (CTEditor → Combate → Tipos → 'Crear los 18 tipos clásicos') y vuelve a aplicar esas plantillas.", "Vale");
            }
        }

        protected override void DrawPresets(AbilityData d)
        {
            EditorGUILayout.LabelField("Plantilla clásica (reemplaza el comportamiento; el id se mantiene)", EditorStyles.boldLabel);
            var labels = new string[Library.Length];
            for (int i = 0; i < Library.Length; i++) labels[i] = $"{Library[i].Group}/{Library[i].Name}";

            EditorGUILayout.BeginHorizontal();
            _presetIndex = EditorGUILayout.Popup(_presetIndex, labels);
            if (GUILayout.Button("Aplicar", GUILayout.Width(70)))
            {
                var p = Library[_presetIndex];
                var ctx = new Ctx();
                EditSelected(so =>
                {
                    Reset(so);
                    p.Fill(so, ctx);
                    if (string.IsNullOrWhiteSpace(d.DisplayName) || d.DisplayName == d.Id)
                        so.FindProperty("displayName").stringValue = p.Name;
                });
                if (ctx.Missing.Count > 0)
                    ShowNotification(new GUIContent("Faltan tipos: " + string.Join(", ", ctx.Missing)));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(Library[_presetIndex].Summary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
        }

        protected override void DrawPreview(AbilityData d)
        {
            EditorGUILayout.LabelField("Resumen de lo que hace", EditorStyles.boldLabel);
            var lines = Describe(d);
            EditorGUILayout.HelpBox(lines.Count == 0 ? "No hace nada todavía (todos los campos en su valor neutro)." : "• " + string.Join("\n• ", lines), MessageType.Info);
        }

        // Traducción de los campos a frases, para que el autor verifique que hace lo que quiere.
        private static List<string> Describe(AbilityData d)
        {
            var l = new List<string>();
            if (d.PassiveModifiers != null)
                foreach (var m in d.PassiveModifiers)
                    if (m != null && !string.IsNullOrEmpty(m.statId) && !Mathf.Approximately(m.multiplier, 1f))
                        l.Add($"{StatLabels.NameOf(m.statId)} ×{m.multiplier} siempre.");
            if (d.StatusImmunities != null && d.StatusImmunities.Length > 0)
                l.Add("Inmune a los estados: " + string.Join(", ", d.StatusImmunities) + ".");
            if (d.TypeImmunities != null)
                foreach (var t in d.TypeImmunities)
                    if (t != null) l.Add($"Inmune a {t.DisplayName}" + (d.AbsorbImmuneHealPercent > 0 ? $" (recupera {d.AbsorbImmuneHealPercent}% PS)." : "."));
            if (!string.IsNullOrEmpty(d.ContactReactionStatus) && d.ContactReactionChance > 0)
                l.Add($"{d.ContactReactionChance}% de infligir '{d.ContactReactionStatus}' a quien le golpee con contacto.");
            if (!string.IsNullOrEmpty(d.OnEntryStatId) && d.OnEntryStages != 0)
                l.Add($"Al entrar: {(d.OnEntryStages > 0 ? "sube" : "baja")} {Math.Abs(d.OnEntryStages)} etapa(s) de {StatLabels.NameOf(d.OnEntryStatId)} {(d.OnEntryTargetsSelf ? "propia" : "del rival")}.");
            if (!string.IsNullOrEmpty(d.StatusStatBoostStatId) && !Mathf.Approximately(d.StatusStatBoostMultiplier, 1f))
                l.Add($"Con estado alterado: {StatLabels.NameOf(d.StatusStatBoostStatId)} ×{d.StatusStatBoostMultiplier}.");
            if (d.LowHpBoostType != null && !Mathf.Approximately(d.LowHpBoostMultiplier, 1f))
                l.Add($"Con {d.LowHpThresholdPercent}% de PS o menos: movimientos {d.LowHpBoostType.DisplayName} ×{d.LowHpBoostMultiplier}.");
            if (d.IncomingTypeMultipliers != null)
                foreach (var m in d.IncomingTypeMultipliers)
                    if (m != null && m.type != null && !Mathf.Approximately(m.multiplier, 1f))
                        l.Add($"Daño recibido de {m.type.DisplayName} ×{m.multiplier}.");
            if (d.StabMultiplierOverride > 0) l.Add($"STAB ×{d.StabMultiplierOverride}.");
            if (d.StatusMovePriorityBonus != 0) l.Add($"Movimientos de estado con {d.StatusMovePriorityBonus:+#;-#} de prioridad.");
            if (d.PreventsStatReduction) l.Add("El rival no puede bajarle las estadísticas.");
            if (d.CuresStatusOnSwitchOut) l.Add("Se cura el estado al retirarse.");
            if (d.HealPercentOnSwitchOut > 0) l.Add($"Recupera {d.HealPercentOnSwitchOut}% de PS al retirarse.");
            if (!string.IsNullOrEmpty(d.EndOfTurnStatId) && d.EndOfTurnStages != 0)
                l.Add($"Fin de turno: {(d.EndOfTurnStages > 0 ? "sube" : "baja")} {Math.Abs(d.EndOfTurnStages)} etapa(s) de {StatLabels.NameOf(d.EndOfTurnStatId)}.");
            if (d.EndOfTurnHealPercent > 0) l.Add($"Fin de turno: recupera {d.EndOfTurnHealPercent}% de PS" + (d.EndOfTurnHealRequiresStatus ? " (solo con estado alterado)." : "."));
            if (d.EndOfTurnCureStatusChance > 0) l.Add($"Fin de turno: {d.EndOfTurnCureStatusChance}% de curarse el estado.");
            if (d.OffensivePowerModifiers != null)
                foreach (var m in d.OffensivePowerModifiers)
                    if (m != null) l.Add($"Al atacar: potencia ×{m.multiplier:0.##} {ConditionText.Describe(System.Linq.Enumerable.Select(m.conditions ?? new ConditionData[0], ConditionText.FromData))}.");
            if (d.DefensivePowerModifiers != null)
                foreach (var m in d.DefensivePowerModifiers)
                    if (m != null) l.Add($"Al recibir: potencia del golpe ×{m.multiplier:0.##} {ConditionText.Describe(System.Linq.Enumerable.Select(m.conditions ?? new ConditionData[0], ConditionText.FromData))}.");
            if (d.NegatesStatusDamage) l.Add("No sufre daño residual por estados.");
            // 5.ª y 6.ª gen.
            if (!string.IsNullOrWhiteSpace(d.PriorityType) && d.PriorityTypeBonus != 0) l.Add($"Movimientos de tipo '{d.PriorityType}' con +{d.PriorityTypeBonus} de prioridad (Alas Vendaval).");
            if (!string.IsNullOrWhiteSpace(d.ContactStatDrop) && d.ContactStatDropStages != 0) l.Add($"Quien le golpea con contacto: {StatLabels.NameOf(d.ContactStatDrop)} {d.ContactStatDropStages} (Baba).");
            if (d.StealOnHit) l.Add("Al golpear roba el objeto del rival si no lleva ninguno (Prestidigitador).");
            if (d.SpreadsAbilityOnContact) l.Add("Quien le golpea con contacto se queda con esta habilidad (Momia).");
            if (!string.IsNullOrWhiteSpace(d.ConvertNormalTo)) l.Add($"Sus movimientos Normales pasan a ser de tipo '{d.ConvertNormalTo}'" + (d.ConvertBoost != 1f ? $" y pegan ×{d.ConvertBoost:0.##}." : "."));
            if (d.BerryBonusHealPercent > 0) l.Add($"Al comer una baya recupera además el {d.BerryBonusHealPercent:0.##}% de sus PS (Carrillo).");
            return l;
        }

        // ---------------- Helpers de plantillas ----------------

        private static Preset P(string group, string id, string name, string summary, Action<SerializedObject, Ctx> fill)
            => new Preset { Group = group, Id = id, Name = name, Summary = summary, Fill = fill };

        // Deja la habilidad "en blanco" (valores neutros) antes de aplicar una plantilla.
        private static void Reset(SerializedObject so)
        {
            so.FindProperty("passiveModifiers").arraySize = 0;
            so.FindProperty("statusImmunities").arraySize = 0;
            so.FindProperty("typeImmunities").arraySize = 0;
            Flt(so, "absorbImmuneHealPercent", 0);
            Str(so, "contactReactionStatus", ""); Flt(so, "contactReactionChance", 0);
            Str(so, "onEntryStatId", ""); Int(so, "onEntryStages", 0); Bool(so, "onEntryTargetsSelf", false);
            Str(so, "statusStatBoostStatId", ""); Flt(so, "statusStatBoostMultiplier", 1);
            so.FindProperty("lowHpBoostType").objectReferenceValue = null;
            Flt(so, "lowHpThresholdPercent", 33.33f); Flt(so, "lowHpBoostMultiplier", 1);
            so.FindProperty("incomingTypeMultipliers").arraySize = 0;
            Flt(so, "stabMultiplierOverride", 0); Int(so, "statusMovePriorityBonus", 0);
            Bool(so, "preventsStatReduction", false); Bool(so, "curesStatusOnSwitchOut", false);
            Flt(so, "healPercentOnSwitchOut", 0);
            Str(so, "endOfTurnStatId", ""); Int(so, "endOfTurnStages", 0);
            Flt(so, "endOfTurnHealPercent", 0); Bool(so, "endOfTurnHealRequiresStatus", false);
            Flt(so, "endOfTurnCureStatusChance", 0); Bool(so, "negatesStatusDamage", false);
            var off = so.FindProperty("offensivePowerModifiers"); if (off != null) off.arraySize = 0;
            var def = so.FindProperty("defensivePowerModifiers"); if (def != null) def.arraySize = 0;
        }

        // Un único modificador de potencia al ATACAR / al RECIBIR con sus condiciones.
        private static void Offensive(SerializedObject so, float mult, params Condition[] conditions)
            => ConditionText.WriteModifiers(so.FindProperty("offensivePowerModifiers"), new List<(float, Condition[])> { (mult, conditions) });
        private static void Defensive(SerializedObject so, float mult, params Condition[] conditions)
            => ConditionText.WriteModifiers(so.FindProperty("defensivePowerModifiers"), new List<(float, Condition[])> { (mult, conditions) });

        private static void Str(SerializedObject so, string f, string v) => so.FindProperty(f).stringValue = v;
        private static void Int(SerializedObject so, string f, int v) => so.FindProperty(f).intValue = v;
        private static void Flt(SerializedObject so, string f, float v) => so.FindProperty(f).floatValue = v;
        private static void Bool(SerializedObject so, string f, bool v) => so.FindProperty(f).boolValue = v;

        private static void Contact(SerializedObject so, string status, float chance)
        {
            Str(so, "contactReactionStatus", status);
            Flt(so, "contactReactionChance", chance);
        }

        private static void Strs(SerializedObject so, string field, params string[] values)
        {
            var arr = so.FindProperty(field);
            arr.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) arr.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        private static void Types(SerializedObject so, string field, params ElementTypeData[] types)
        {
            var arr = so.FindProperty(field);
            arr.arraySize = types.Length;
            for (int i = 0; i < types.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = types[i];
        }

        private static void Passive(SerializedObject so, string stat, float mul)
        {
            var arr = so.FindProperty("passiveModifiers");
            arr.arraySize = 1;
            var e = arr.GetArrayElementAtIndex(0);
            e.FindPropertyRelative("statId").stringValue = stat;
            e.FindPropertyRelative("multiplier").floatValue = mul;
        }

        private static void LowHp(SerializedObject so, ElementTypeData type)
        {
            so.FindProperty("lowHpBoostType").objectReferenceValue = type;
            Flt(so, "lowHpThresholdPercent", 33.33f);
            Flt(so, "lowHpBoostMultiplier", 1.5f);
        }

        private static void Incoming(SerializedObject so, params (ElementTypeData type, float mul)[] entries)
        {
            var arr = so.FindProperty("incomingTypeMultipliers");
            arr.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("type").objectReferenceValue = entries[i].type;
                e.FindPropertyRelative("multiplier").floatValue = entries[i].mul;
            }
        }
    }
}
