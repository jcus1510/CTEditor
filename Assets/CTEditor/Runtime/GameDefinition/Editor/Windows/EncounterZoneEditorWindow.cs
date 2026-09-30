using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de ZONAS SALVAJES (menú CTEditor → Mundo → Zonas salvajes): qué especies aparecen en cada zona, a qué
    /// niveles y con qué frecuencia. La vista previa muestra el % real de cada especie.
    /// </summary>
    public sealed class EncounterZoneEditorWindow : ContentEditorWindow<EncounterZoneData>
    {
        [MenuItem(EditorMenus.World + "Zonas salvajes", false, EditorMenus.WorldOrder + 1)]
        public static void Open() => OpenWindow<EncounterZoneEditorWindow>("Zonas salvajes");

        protected override string Category => ContentFolders.Encounters;
        protected override string Noun => "zona";
        protected override string Title => "Zonas salvajes";
        protected override string Intro =>
            "Dónde salen los monstruos salvajes: cada zona es una lista de especies con su rango de niveles y su frecuencia.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear las zonas de ejemplo» (Ruta 1, Bosque Verde y una cueva).",
            "Añade especies con su nivel mínimo, máximo y frecuencia: 30 frente a 10 = sale el triple.",
            "Mira la vista previa: el % real de cada especie y sus niveles.",
            "En la escena de pruebas, «Combate salvaje» usa la zona elegida.",
        };

        public static readonly (string id, string name, (string sp, int min, int max, int w)[] entries)[] Library =
        {
            ("ruta_1", "Ruta 1", new[] { ("pidgey", 2, 5, 50), ("rattata", 2, 4, 50) }),
            ("bosque_verde", "Bosque Verde", new[] { ("caterpie", 3, 5, 35), ("weedle", 3, 5, 35), ("metapod", 4, 6, 10), ("kakuna", 4, 6, 10), ("pikachu", 3, 5, 5), ("pidgey", 4, 6, 5) }),
            ("cueva", "Cueva", new[] { ("zubat", 6, 10, 55), ("geodude", 7, 10, 30), ("paras", 8, 10, 10), ("onix", 12, 14, 5) }),
        };

        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var z in Library)
            {
                string id = z.id;
                if (ContentAssets.CreateOrRepair<EncounterZoneData>(ContentFolders.Encounters, z.id, z.name, so => Fill(so, id))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        public static void Fill(SerializedObject so, string presetId)
        {
            foreach (var z in Library)
            {
                if (z.id != presetId) continue;
                so.FindProperty("displayName").stringValue = z.name;
                var found = z.entries.Select(e => (s: ContentAssets.FindById<SpeciesData>(e.sp), e)).Where(x => x.s != null).ToList();
                if (found.Count == 0) // sin la 1ª generación: las primeras especies del proyecto
                    found = ContentAssets.LoadAll<SpeciesData>().Take(3).Select((s, i) => (s, (s.Id, 2 + i, 5 + i, 10))).ToList();
                var arr = so.FindProperty("entries");
                arr.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++)
                {
                    var el = arr.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative("species").objectReferenceValue = found[i].s;
                    el.FindPropertyRelative("minLevel").intValue = found[i].e.Item2;
                    el.FindPropertyRelative("maxLevel").intValue = found[i].e.Item3;
                    el.FindPropertyRelative("weight").intValue = found[i].e.Item4;
                }
            }
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(p => (p.id, p.name, "Zonas de ejemplo")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            Fill(so, id);
            var t = Templates.FirstOrDefault(x => x.id == id); if (t.id != null) so.FindProperty("displayName").stringValue = t.name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear las {Library.Length} zonas de ejemplo")) FinishBulk(CreateClassicSet(), "zonas");
        }

        protected override void DrawPresets(EncounterZoneData d)
        {
            EditorGUILayout.LabelField("Plantillas (rellenan todo; el id se mantiene)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var z in Library)
            {
                string id = z.id;
                if (GUILayout.Button(z.name, EditorStyles.miniButton)) EditSelected(so => Fill(so, id));
            }
            EditorGUILayout.EndHorizontal();
        }

        protected override void DrawPreview(EncounterZoneData d)
        {
            EditorTheme.Section("Probabilidad de aparición", Accent);
            var entries = (d.Entries ?? new EncounterEntryData[0]).Where(e => e != null && e.species != null).ToList();
            int total = entries.Sum(e => System.Math.Max(1, e.weight));
            if (total == 0) { EditorTheme.Tip("La zona está vacía: añade especies.", EditorTheme.Warn, "⚠"); return; }
            foreach (var e in entries.OrderByDescending(e => e.weight))
            {
                float pct = 100f * System.Math.Max(1, e.weight) / total;
                var color = e.species.Types != null && e.species.Types.Length > 0 && e.species.Types[0] != null ? e.species.Types[0].Color : Accent;
                int lo = System.Math.Min(e.minLevel, e.maxLevel), hi = System.Math.Max(e.minLevel, e.maxLevel);
                EditorTheme.Bar($"{e.species.DisplayName} (Nv. {lo}{(hi != lo ? "-" + hi : "")})", pct, 100f, color, $"{pct:0.#}%");
            }
        }
    }
}
