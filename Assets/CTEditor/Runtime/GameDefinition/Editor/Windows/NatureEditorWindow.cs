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
    /// Editor de NATURALEZAS (menú CTEditor → Criaturas → Naturalezas). Incluye las 25 clásicas con sus nombres
    /// oficiales en español, una tabla 5×5 para verlas de un vistazo, y el efecto de la seleccionada.
    /// </summary>
    public sealed class NatureEditorWindow : ContentEditorWindow<NatureData>
    {
        [MenuItem(EditorMenus.Creatures + "Naturalezas", false, EditorMenus.CreaturesOrder + 4)]
        public static void Open() => OpenWindow<NatureEditorWindow>("Naturalezas");

        protected override string Category => ContentFolders.Natures;
        protected override string Noun => "naturaleza";
        protected override string Intro =>
            "La naturaleza es la 'personalidad' de cada individuo: sube una estadística y baja otra (clásico ±10%). " +
            "Se asigna al azar al crear cada monstruo, salvo que su ficha fije una. Carpeta vacía = todos neutros.";

        protected override string Title => "Naturalezas";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear las 25 naturalezas clásicas».",
            "Cada naturaleza sube una estadística y baja otra (clásico ±10%). Si sube y baja la misma, es neutra.",
            "Si la carpeta está vacía, todos los monstruos son neutros.",
        };

        // Orden clásico (tabla 5×5): fila = stat que SUBE, columna = stat que BAJA.
        // Las de la diagonal suben y bajan la misma stat: son neutras.
        private static readonly string[] Stats5 = { "attack", "defense", "speed", "sp_attack", "sp_defense" };
        private static readonly (string id, string name)[,] Grid =
        {
            { ("hardy", "Fuerte"),   ("lonely", "Huraña"),  ("brave", "Audaz"),    ("adamant", "Firme"),  ("naughty", "Pícara") },
            { ("bold", "Osada"),     ("docile", "Dócil"),   ("relaxed", "Plácida"), ("impish", "Agitada"), ("lax", "Floja") },
            { ("timid", "Miedosa"),  ("hasty", "Activa"),   ("serious", "Seria"),  ("jolly", "Alegre"),   ("naive", "Ingenua") },
            { ("modest", "Modesta"), ("mild", "Afable"),    ("quiet", "Mansa"),    ("bashful", "Tímida"), ("rash", "Alocada") },
            { ("calm", "Serena"),    ("gentle", "Amable"),  ("sassy", "Grosera"),  ("careful", "Cauta"),  ("quirky", "Rara") },
        };

        /// <summary>Crea las 25 naturalezas clásicas que falten. Público: lo usa el Centro de Contenido.</summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            for (int up = 0; up < 5; up++)
                for (int down = 0; down < 5; down++)
                {
                    var (id, name) = Grid[up, down];
                    string upStat = Stats5[up], downStat = Stats5[down];
                    if (ContentAssets.CreateIfMissing<NatureData>(ContentFolders.Natures, id, name, so =>
                        {
                            so.FindProperty("boostedStatId").stringValue = upStat;
                            so.FindProperty("hinderedStatId").stringValue = downStat;
                            so.FindProperty("boostPercent").intValue = 10;
                        }))
                        created++;
                }
            return created;
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Enumerable.Range(0, 25).Select(i => (Grid[i / 5, i % 5].Item1, Grid[i / 5, i % 5].Item2, "Naturalezas clásicas")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            for (int up = 0; up < 5; up++)
                for (int down = 0; down < 5; down++)
                {
                    if (Grid[up, down].Item1 != id) continue;
                    so.FindProperty("boostedStatId").stringValue = Stats5[up];
                    so.FindProperty("hinderedStatId").stringValue = Stats5[down];
                    so.FindProperty("boostPercent").intValue = 10;
                    so.FindProperty("displayName").stringValue = Grid[up, down].Item2;
                }
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear las 25 naturalezas clásicas")) FinishBulk(CreateClassicSet(), "naturalezas");
        }

        protected override void DrawPreview(NatureData d)
        {
            // Efecto en palabras + ejemplo numérico.
            EditorGUILayout.LabelField("Efecto", EditorStyles.boldLabel);
            bool neutral = string.IsNullOrEmpty(d.BoostedStatId) && string.IsNullOrEmpty(d.HinderedStatId)
                           || d.BoostedStatId == d.HinderedStatId;
            if (neutral)
            {
                EditorGUILayout.HelpBox("Neutra: no cambia ninguna estadística.", MessageType.Info);
            }
            else
            {
                int p = d.BoostPercent;
                string up = string.IsNullOrEmpty(d.BoostedStatId) ? "" : $"+{p}% {StatLabels.NameOf(d.BoostedStatId)} (100 → {100 + p})";
                string down = string.IsNullOrEmpty(d.HinderedStatId) ? "" : $"−{p}% {StatLabels.NameOf(d.HinderedStatId)} (100 → {100 - p})";
                EditorGUILayout.HelpBox($"{up}   {down}".Trim(), MessageType.Info);
            }

            // Tabla 5×5 clásica: clic en una celda para seleccionar esa naturaleza (si existe).
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tabla clásica (fila = sube, columna = baja)", EditorStyles.boldLabel);
            // Una sola carga por dibujado (no 25 búsquedas en el proyecto).
            var byId = new System.Collections.Generic.Dictionary<string, NatureData>();
            foreach (var n in ContentAssets.LoadAll<NatureData>())
                if (!string.IsNullOrEmpty(n.Id)) byId[n.Id] = n;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(80));
            foreach (var s in Stats5) GUILayout.Label("−" + StatLabels.NameOf(s), EditorStyles.miniLabel, GUILayout.Width(84));
            EditorGUILayout.EndHorizontal();

            for (int up = 0; up < 5; up++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("+" + StatLabels.NameOf(Stats5[up]), EditorStyles.miniLabel, GUILayout.Width(80));
                for (int down = 0; down < 5; down++)
                {
                    var (id, name) = Grid[up, down];
                    byId.TryGetValue(id, out var existing);
                    var style = existing == Selected ? EditorStyles.boldLabel : EditorStyles.label;
                    GUI.enabled = existing != null;
                    if (GUILayout.Button(existing != null ? name : "(" + name + ")", style, GUILayout.Width(84)) && existing != null)
                        Select(existing);
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.LabelField("Entre paréntesis = aún no creada. Las de la diagonal son neutras.", EditorStyles.miniLabel);
        }
    }
}
