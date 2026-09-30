using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// PAPELERA: lo que «borraste» desde los editores. Aquí puedes:
    ///   • ↩ Recuperar: vuelve a su carpeta y TODAS sus referencias funcionan otra vez (mismo GUID).
    ///   • ⇄ Pasar sus referencias a la nueva: si ya creaste otra ficha con el mismo id, las especies,
    ///     entrenadores... que apuntaban a la vieja pasan a la nueva.
    ///   • ✖ Borrar para siempre (una) o 🧹 Vaciar la papelera (todas).
    /// Menú: CTEditor → Herramientas → Papelera.
    /// </summary>
    public sealed class TrashWindow : EditorWindow
    {
        private List<ScriptableObject> _items = new List<ScriptableObject>();
        private readonly Dictionary<ScriptableObject, int> _uses = new Dictionary<ScriptableObject, int>();
        private Vector2 _scroll;

        [MenuItem(EditorMenus.Tools + "Papelera", false, EditorMenus.ToolsOrder + 3)]
        public static void Open()
        {
            var w = GetWindow<TrashWindow>();
            w.titleContent = new GUIContent("🗑 Papelera");
            w.Reload();
            w.Show();
        }

        private void OnEnable() => Reload();
        private void OnFocus() => Reload();

        private void Reload()
        {
            var all = ContentTrash.Items();
            // Los GRUPOS (un cambio de generación entero) se ven como una sola tarjeta: no se busca quién usa cada ficha.
            _groups = all.Where(a => ContentTrash.GroupOfPath(AssetDatabase.GetAssetPath(a)).Length > 0)
                .GroupBy(a => ContentTrash.GroupOfPath(AssetDatabase.GetAssetPath(a))).OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());
            _items = all.Where(a => ContentTrash.GroupOfPath(AssetDatabase.GetAssetPath(a)).Length == 0)
                .OrderBy(a => ContentTrash.CategoryOfPath(AssetDatabase.GetAssetPath(a))).ThenBy(a => a.name).ToList();
            _uses.Clear();
            foreach (var a in _items) _uses[a] = ReferenceFinder.FindReferencesTo(a).Count;
            Repaint();
        }

        // Zoom de texto (Ctrl + rueda, o los botones A− / A+ de los editores).
        private void OnGUI()
        {
            EditorZoom.Begin(this);
            try { DrawWindow(); }
            finally { EditorZoom.End(); }
        }

        private void DrawWindow()
        {
            EditorTheme.TitleBand("Papelera",
                "Lo que borras desde los editores viene aquí. Recuperar lo devuelve a su sitio con TODAS sus referencias. " +
                "Mientras está aquí el juego no lo carga.", EditorTheme.Tools, "🗑");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("↻ Refrescar", GUILayout.Height(22))) Reload();
            GUI.enabled = _items.Count > 0;
            if (GUILayout.Button($"🧹 Vaciar la papelera ({_items.Count})", GUILayout.Height(22))) EmptyAll();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_items.Count == 0 && _groups.Count == 0) { EditorTheme.Chip("✔ La papelera está vacía.", EditorTheme.Ok); return; }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var g in _groups.ToList())
            {
                EditorTheme.BeginCard(EditorTheme.Tools, "📦 " + g.Key);
                var byCategory = g.Value.GroupBy(a => ContentTrash.CategoryOfPath(AssetDatabase.GetAssetPath(a)))
                    .Select(c => $"{c.Key}: {c.Count()}");
                EditorTheme.Paragraph($"{g.Value.Count} fichas ({string.Join(" · ", byCategory)}). Se mandaron juntas (p. ej. al cambiar de generación).");
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("↩ Recuperar todo el grupo")) RestoreGroup(g.Key, g.Value);
                if (GUILayout.Button("✖ Borrar el grupo para siempre", GUILayout.Width(210))
                    && EditorUtility.DisplayDialog("Borrar el grupo", $"¿Borrar para siempre las {g.Value.Count} fichas de «{g.Key}»? No se puede deshacer. " +
                                                   "Lo que aún las use quedará con referencias rotas (el validador lo avisa).", "Borrar", "Cancelar"))
                {
                    foreach (var a in g.Value) if (a != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(a));
                    AssetDatabase.DeleteAsset(ContentTrash.Root + "/" + ContentTrash.GroupPrefix + g.Key);
                    AssetDatabase.SaveAssets();
                    ContentAssets.ClearCache();
                    Reload();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                EditorTheme.EndCard();
            }
            string lastCategory = null;
            foreach (var a in _items.ToList())
            {
                if (a == null) continue;
                string category = ContentTrash.CategoryOfPath(AssetDatabase.GetAssetPath(a));
                if (category != lastCategory) { EditorTheme.Section(string.IsNullOrEmpty(category) ? "Sin categoría" : category, EditorTheme.ForCategory(category)); lastCategory = category; }

                int uses = _uses.TryGetValue(a, out var u) ? u : 0;
                bool twin = ContentTrash.HasLiveTwin(a);
                EditorTheme.BeginCard(uses > 0 ? EditorTheme.Warn : EditorTheme.Rules);
                EditorGUILayout.LabelField(ContentAssets.Label(a), EditorStyles.boldLabel);
                EditorTheme.Paragraph(
                    (uses > 0 ? $"⚠ Todavía la usan {uses} ficha(s) (siguen funcionando). " : "Nadie la usa. ") +
                    (twin ? "Ya existe OTRA ficha viva con su mismo id." : ""));

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !twin;
                if (GUILayout.Button(new GUIContent("↩ Recuperar", twin ? "Hay otra ficha con el mismo id: usa «Pasar referencias a la nueva» o renombra una." : "Vuelve a su carpeta con todas sus referencias."))) Restore(a);
                GUI.enabled = twin && uses > 0;
                if (GUILayout.Button(new GUIContent("⇄ Pasar referencias a la nueva", "Las fichas que usaban esta pasan a usar la viva con el mismo id."))) Redirect(a);
                GUI.enabled = true;
                if (GUILayout.Button("Ubicar", GUILayout.Width(60))) EditorGUIUtility.PingObject(a);
                if (GUILayout.Button("✖ Borrar para siempre", GUILayout.Width(150))) DeleteOne(a, uses);
                EditorGUILayout.EndHorizontal();
                EditorTheme.EndCard();
            }
            EditorGUILayout.EndScrollView();
        }

        private Dictionary<string, List<ScriptableObject>> _groups = new Dictionary<string, List<ScriptableObject>>();

        private void RestoreGroup(string name, List<ScriptableObject> items)
        {
            var failed = new List<string>();
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var a = items[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Recuperar el grupo", ContentAssets.Label(a), (float)i / items.Count)) break;
                    string error = a == null ? "" : ContentTrash.Restore(a);
                    if (!string.IsNullOrEmpty(error)) failed.Add($"{ContentAssets.Label(a)}: {error}");
                }
            }
            finally { EditorUtility.ClearProgressBar(); AssetDatabase.SaveAssets(); ContentAssets.ClearCache(); }
            EditorUtility.DisplayDialog("Recuperar el grupo", failed.Count == 0
                ? $"Recuperadas las {items.Count} fichas de «{name}»."
                : $"Recuperadas {items.Count - failed.Count}. No se pudieron recuperar {failed.Count} (ya hay otra con su id):\n" + string.Join("\n", failed.Take(10)), "Vale");
            EditorCatalog.ClearCounts();
            Reload();
            GUIUtility.ExitGUI();
        }

        private void Restore(ScriptableObject a)
        {
            string error = ContentTrash.Restore(a);
            if (!string.IsNullOrEmpty(error)) EditorUtility.DisplayDialog("Recuperar", error, "Vale");
            else ShowNotification(new GUIContent($"↩ {ContentAssets.Label(a)} recuperada."));
            EditorCatalog.ClearCounts();
            Reload();
        }

        private void Redirect(ScriptableObject a)
        {
            int n = ContentTrash.RedirectToLiveTwin(a);
            EditorUtility.DisplayDialog("Pasar referencias",
                $"{n} referencia(s) pasan ahora a la ficha viva '{ContentAssets.KeyOf(a)}'. La de la papelera ya se puede borrar para siempre.", "Vale");
            Reload();
        }

        private void DeleteOne(ScriptableObject a, int uses)
        {
            string warn = uses > 0 ? $"\n\n⚠ Todavía la usan {uses} ficha(s):\n{ContentTrash.DescribeUsers(ReferenceFinder.FindReferencesTo(a))}\nEsas referencias quedarán rotas." : "";
            if (!EditorUtility.DisplayDialog("Borrar para siempre", $"¿Borrar '{ContentAssets.Label(a)}' para siempre? No se puede deshacer.{warn}", "Borrar", "Cancelar")) return;
            ContentTrash.DeleteForever(a);
            Reload();
        }

        private void EmptyAll()
        {
            int used = _items.Count(a => _uses.TryGetValue(a, out var u) && u > 0);
            string warn = used > 0 ? $"\n\n⚠ {used} de ellas todavía se usan: esas referencias quedarán rotas. (Consejo: recupéralas o pasa sus referencias antes.)" : "";
            if (!EditorUtility.DisplayDialog("Vaciar la papelera", $"¿Borrar para siempre las {_items.Count} fichas de la papelera?{warn}", "Vaciar", "Cancelar")) return;
            foreach (var a in _items.ToList()) ContentTrash.DeleteForever(a);
            Reload();
        }
    }
}
