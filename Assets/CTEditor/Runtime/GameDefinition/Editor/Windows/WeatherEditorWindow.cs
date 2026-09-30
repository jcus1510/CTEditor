using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de CLIMAS (menú CTEditor → Combate → Climas). Trae los 4 clásicos (lluvia, sol, tormenta de arena y
    /// nieve) y deja inventar los tuyos: qué tipos potencia o debilita, si daña cada turno y a quién no.
    /// Un movimiento lo activa con el efecto «Cambiar el clima» (plantillas Danza Lluvia y Día Soleado), y
    /// cualquier condición puede preguntar "si hace este clima".
    /// </summary>
    public sealed class WeatherEditorWindow : ContentEditorWindow<WeatherData>
    {
        [MenuItem(EditorMenus.Battle + "Climas", false, EditorMenus.BattleOrder + 5)]
        public static void Open() => OpenWindow<WeatherEditorWindow>("Climas");

        protected override string Category => ContentFolders.Weathers;
        protected override string Noun => "clima";
        protected override string Intro =>
            "Un clima potencia o debilita tipos y puede dañar cada turno. Lo activa un movimiento (efecto «Cambiar el clima») " +
            "y las condiciones de movimientos y habilidades pueden preguntar por él.";

        protected override string Title => "Climas";
        protected override Color? RowMark(WeatherData d) => d.Color;

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los climas clásicos» (lluvia, sol, tormenta de arena, nieve y granizo).",
            "Cada clima puede potenciar o debilitar tipos y dañar cada turno a todos salvo a los inmunes.",
            "Un movimiento lo activa con el efecto «Cambiar el clima» (plantillas Danza Lluvia, Día Soleado…).",
        };

        // (id, nombre, turnos, [tipo, multiplicador]..., daño %, inmunes, color)
        public static readonly (string id, string name, (string type, float mult)[] mods, float residual, string[] immune, Color color)[] Classic =
        {
            ("rain", "Lluvia", new[] { ("water", 1.5f), ("fire", 0.5f) }, 0f, new string[0], new Color(0.35f, 0.55f, 0.95f)),
            ("sun", "Sol intenso", new[] { ("fire", 1.5f), ("water", 0.5f) }, 0f, new string[0], new Color(0.98f, 0.7f, 0.25f)),
            ("sandstorm", "Tormenta de arena", new (string, float)[0], 6.25f, new[] { "rock", "ground", "steel" }, new Color(0.8f, 0.7f, 0.45f)),
            ("snow", "Nieve", new (string, float)[0], 0f, new string[0], new Color(0.8f, 0.92f, 1f)),
            // Granizo (3.ª-8.ª gen.): daña 1/16 a todos salvo a los de tipo Hielo.
            ("hail", "Granizo", new (string, float)[0], 6.25f, new[] { "ice" }, new Color(0.75f, 0.88f, 0.98f)),
        };

        /// <summary>Crea los climas clásicos que falten. Público: lo usa el Centro de Contenido.</summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var w in Classic)
            {
                var preset = w;
                if (ContentAssets.CreateIfMissing<WeatherData>(ContentFolders.Weathers, w.id, w.name, so => Fill(so, preset.id)))
                    created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        /// <summary>Rellena un clima con su plantilla clásica (no toca el id).</summary>
        public static void Fill(SerializedObject so, string presetId)
        {
            foreach (var w in Classic)
            {
                if (w.id != presetId) continue;
                so.FindProperty("displayName").stringValue = w.name;
                so.FindProperty("defaultTurns").intValue = 5;
                so.FindProperty("residualDamagePercent").floatValue = w.residual;
                so.FindProperty("color").colorValue = w.color;

                var mods = so.FindProperty("typePowerMultipliers");
                var found = new List<(ElementTypeData, float)>();
                foreach (var (type, mult) in w.mods)
                {
                    var t = ContentAssets.FindById<ElementTypeData>(type);
                    if (t != null) found.Add((t, mult));
                }
                mods.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++)
                {
                    mods.GetArrayElementAtIndex(i).FindPropertyRelative("type").objectReferenceValue = found[i].Item1;
                    mods.GetArrayElementAtIndex(i).FindPropertyRelative("multiplier").floatValue = found[i].Item2;
                }

                var immune = so.FindProperty("immuneTypes");
                var imm = new List<ElementTypeData>();
                foreach (var id in w.immune) { var t = ContentAssets.FindById<ElementTypeData>(id); if (t != null) imm.Add(t); }
                immune.arraySize = imm.Count;
                for (int i = 0; i < imm.Count; i++) immune.GetArrayElementAtIndex(i).objectReferenceValue = imm[i];
            }
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Classic.Select(p => (p.id, p.name, "Climas clásicos")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            Fill(so, id);
            var t = Templates.FirstOrDefault(x => x.id == id); if (t.id != null) so.FindProperty("displayName").stringValue = t.name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear los {Classic.Length} climas clásicos")) FinishBulk(CreateClassicSet(), "climas");
        }

        protected override void DrawPresets(WeatherData d)
        {
            EditorGUILayout.LabelField("Plantillas clásicas (rellenan los campos; el id se mantiene)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var w in Classic)
            {
                string id = w.id;
                if (GUILayout.Button(w.name)) EditSelected(so => Fill(so, id));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        protected override void DrawPreview(WeatherData d)
        {
            var l = new List<string>();
            if (d.TypePowerMultipliers != null)
                foreach (var m in d.TypePowerMultipliers)
                    if (m != null && m.type != null && !Mathf.Approximately(m.multiplier, 1f))
                        l.Add($"• Movimientos de tipo {m.type.DisplayName}: potencia ×{m.multiplier:0.##} " +
                              (m.multiplier > 1f ? "(más fuertes)" : "(más débiles)"));
            if (d.ResidualDamagePercent > 0f)
            {
                var immune = new List<string>();
                if (d.ImmuneTypes != null) foreach (var t in d.ImmuneTypes) if (t != null) immune.Add(t.DisplayName);
                l.Add($"• Al final de cada turno quita {d.ResidualDamagePercent:0.##}% de PS a todos" +
                      (immune.Count > 0 ? $" salvo a los de tipo {string.Join(", ", immune)}." : "."));
            }
            l.Add(d.DefaultTurns > 0 ? $"• Dura {d.DefaultTurns} turnos (si el movimiento no dice otra cosa)." : "• Dura hasta que otro clima lo reemplace.");

            EditorGUILayout.LabelField("Resumen", EditorStyles.boldLabel);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = d.Color;
            EditorGUILayout.HelpBox(l.Count == 1 ? "Este clima todavía no cambia nada en combate.\n" + l[0] : string.Join("\n", l), MessageType.Info);
            GUI.backgroundColor = old;
            EditorGUILayout.HelpBox($"Para usarlo: en un movimiento añade el efecto «Cambiar el clima» con el clima '{d.Id}', " +
                                    $"o una condición 'el clima es…' = '{d.Id}'.", MessageType.None);
        }
    }
}
