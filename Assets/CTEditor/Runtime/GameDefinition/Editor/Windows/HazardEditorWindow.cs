using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de TRAMPAS DE CAMPO (menú CTEditor → Combate → Trampas de campo): Púas, Trampa Rocas, Púas Tóxicas y
    /// Red Viscosa como plantillas, y la libertad de inventar las tuyas (capas, daño por capa, daño según
    /// la eficacia de un tipo, estados, etapas, inmunes y quién las retira).
    /// </summary>
    public sealed class HazardEditorWindow : ContentEditorWindow<HazardData>
    {
        [MenuItem(EditorMenus.Battle + "Trampas de campo", false, EditorMenus.BattleOrder + 6)]
        public static void Open() => OpenWindow<HazardEditorWindow>("Trampas de campo");

        protected override string Category => ContentFolders.Hazards;
        protected override string Noun => "trampa";
        protected override string Title => "Trampas de campo";
        protected override string Intro =>
            "Se colocan con un movimiento en el lado del rival y afectan a cada monstruo que ENTRE en ese lado: daño, estados o bajadas de estadísticas.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear las 4 trampas clásicas» (Púas, Trampa Rocas, Púas Tóxicas, Red Viscosa).",
            "Cada trampa tiene capas: cuantas más, más daño o peor estado. Elige qué hace al entrar y quién se libra.",
            "Un movimiento la coloca con el efecto «Poner una trampa»; Giro Rápido y Despejar la quitan con «Quitar trampas».",
        };

        protected override Color? RowMark(HazardData d) => d.Color;

        // (id, nombre, capas, daño por capa, tipo que escala, estados por capa, stat, etapas, inmunes, inmune por habilidad a, la retiran, color)
        public static readonly (string id, string name, int layers, float[] dmg, string scale, string[] status, string stat, int stages,
            string[] immune, string abilityType, string[] absorbed, Color color)[] Library =
        {
            ("spikes", "Púas", 3, new[] { 12.5f, 16.67f, 25f }, "", new string[0], "", 0,
                new[] { "flying" }, "ground", new string[0], new Color(0.65f, 0.55f, 0.40f)),
            ("stealth_rock", "Trampa Rocas", 1, new[] { 12.5f }, "rock", new string[0], "", 0,
                new string[0], "", new string[0], new Color(0.70f, 0.62f, 0.45f)),
            ("toxic_spikes", "Púas Tóxicas", 2, new float[0], "", new[] { "poison", "toxic" }, "", 0,
                new[] { "flying", "steel" }, "ground", new[] { "poison" }, new Color(0.60f, 0.35f, 0.70f)),
            ("sticky_web", "Red Viscosa", 1, new float[0], "", new string[0], "speed", -1,
                new[] { "flying" }, "ground", new string[0], new Color(0.85f, 0.80f, 0.35f)),
        };

        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var h in Library)
            {
                string id = h.id;
                if (ContentAssets.CreateIfMissing<HazardData>(ContentFolders.Hazards, h.id, h.name, so => Fill(so, id))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        public static void Fill(SerializedObject so, string presetId)
        {
            foreach (var h in Library)
            {
                if (h.id != presetId) continue;
                so.FindProperty("displayName").stringValue = h.name;
                so.FindProperty("color").colorValue = h.color;
                so.FindProperty("maxLayers").intValue = h.layers;
                var dmg = so.FindProperty("damagePercentByLayer");
                dmg.arraySize = h.dmg.Length;
                for (int i = 0; i < h.dmg.Length; i++) dmg.GetArrayElementAtIndex(i).floatValue = h.dmg[i];
                so.FindProperty("damageScalesWithType").objectReferenceValue = string.IsNullOrEmpty(h.scale) ? null : ContentAssets.FindById<ElementTypeData>(h.scale);
                var st = so.FindProperty("statusByLayer");
                st.arraySize = h.status.Length;
                for (int i = 0; i < h.status.Length; i++) st.GetArrayElementAtIndex(i).stringValue = h.status[i];
                so.FindProperty("statId").stringValue = h.stat;
                so.FindProperty("stages").intValue = h.stages;
                SetTypes(so.FindProperty("immuneTypes"), h.immune);
                so.FindProperty("immuneIfAbilityBlocksType").objectReferenceValue = string.IsNullOrEmpty(h.abilityType) ? null : ContentAssets.FindById<ElementTypeData>(h.abilityType);
                SetTypes(so.FindProperty("absorbedByTypes"), h.absorbed);
            }
        }

        private static void SetTypes(SerializedProperty arr, string[] ids)
        {
            var found = ids.Select(ContentAssets.FindById<ElementTypeData>).Where(t => t != null).ToList();
            arr.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(p => (p.id, p.name, "Trampas clásicas")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            Fill(so, id);
            var t = Templates.FirstOrDefault(x => x.id == id); if (t.id != null) so.FindProperty("displayName").stringValue = t.name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear las {Library.Length} trampas clásicas")) FinishBulk(CreateClassicSet(), "trampas");
        }

        protected override void DrawPresets(HazardData d)
        {
            EditorGUILayout.LabelField("Plantillas clásicas (rellenan los campos; el id se mantiene)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var h in Library)
            {
                string id = h.id;
                if (GUILayout.Button(h.name, EditorStyles.miniButton)) EditSelected(so => Fill(so, id));
            }
            EditorGUILayout.EndHorizontal();
        }

        protected override void DrawPreview(HazardData d)
        {
            EditorTheme.Section("Qué pasa al entrar en su lado", Accent);
            foreach (var line in Describe(d)) EditorTheme.Tip(line, d.Color, "▸");
            var users = ContentAssets.LoadAll<MoveData>()
                .Where(m => m.SecondaryEffects != null && m.SecondaryEffects.Any(e => e != null && e.hazardId == d.Id)).Select(ContentAssets.Label).ToList();
            EditorTheme.Tip(users.Count == 0 ? "Ningún movimiento la usa todavía: añade a uno el efecto «Poner una trampa» con esta trampa."
                                             : "La usan: " + string.Join(", ", users), users.Count == 0 ? EditorTheme.Warn : EditorTheme.Ok);
        }

        /// <summary>La trampa explicada en frases (para la vista previa y los resúmenes).</summary>
        public static List<string> Describe(HazardData d)
        {
            var l = new List<string>();
            l.Add(d.MaxLayers > 1 ? $"Se puede poner hasta {d.MaxLayers} veces (capas)." : "Solo se puede poner una vez.");
            var dmg = d.DamagePercentByLayer ?? new float[0];
            if (dmg.Length > 0)
            {
                string per = dmg.Length == 1 ? $"{dmg[0]:0.##}% de sus PS máximos"
                    : string.Join(" · ", dmg.Select((v, i) => $"{i + 1} capa{(i > 0 ? "s" : "")}: {v:0.##}%"));
                l.Add($"Quita {per}" + (d.DamageScalesWithType != null ? $", multiplicado por la eficacia de {d.DamageScalesWithType.DisplayName} contra el que entra." : "."));
            }
            var st = (d.StatusByLayer ?? new string[0]).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (st.Count > 0) l.Add("Estado: " + string.Join(" · ", st.Select((s, i) => $"{i + 1} capa{(i > 0 ? "s" : "")}: {s}")) + ".");
            if (!string.IsNullOrWhiteSpace(d.StatId) && d.Stages != 0)
                l.Add($"{(d.Stages > 0 ? "Sube" : "Baja")} {System.Math.Abs(d.Stages)} etapa(s) de {StatLabels.NameOf(d.StatId)}.");
            var immune = (d.ImmuneTypes ?? new ElementTypeData[0]).Where(t => t != null).Select(t => t.DisplayName).ToList();
            if (d.ImmuneIfAbilityBlocksType != null) immune.Add($"quien sea inmune a {d.ImmuneIfAbilityBlocksType.DisplayName} por su habilidad");
            if (immune.Count > 0) l.Add("No afecta a: " + string.Join(", ", immune) + ".");
            var absorbed = (d.AbsorbedByTypes ?? new ElementTypeData[0]).Where(t => t != null).Select(t => t.DisplayName).ToList();
            if (absorbed.Count > 0) l.Add($"Un monstruo de tipo {string.Join(" o ", absorbed)} la RETIRA al entrar.");
            return l;
        }
    }
}
