using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de TIPOS (menú CTEditor → Combate → Tipos). Lista de tipos con su color y, para el seleccionado,
    /// su resumen ofensivo y defensivo ("muy eficaz contra", "débil a"...), sacado de la tabla de tipos.
    /// La tabla completa se edita en la matriz (CTEditor → Combate → Tabla de tipos).
    /// </summary>
    public sealed class TypeEditorWindow : ContentEditorWindow<ElementTypeData>
    {
        [MenuItem(EditorMenus.Battle + "Tipos", false, EditorMenus.BattleOrder + 2)]
        public static void Open() => OpenWindow<TypeEditorWindow>("Tipos");

        protected override string Category => ContentFolders.Types;
        protected override string Noun => "tipo";
        protected override string Intro =>
            "Los tipos elementales (Fuego, Agua...). Cómo se relacionan entre sí (×2, ×½, ×0) se define en la Tabla de Tipos.";

        protected override string Title => "Tipos";
        protected override Color? RowMark(ElementTypeData d) => d.Color;

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los 18 tipos clásicos» (también rellena la tabla de tipos).",
            "Puedes inventar tipos nuevos: créalos aquí y define sus enfrentamientos en «Tabla de tipos».",
            "El color de cada tipo se usa en toda la interfaz.",
        };

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear los 18 tipos clásicos + tabla"))
                FinishBulk(TypeChartTools.CreateClassicSet(), "tipos");
            if (GUILayout.Button("Abrir la Tabla de Tipos")) TypeChartWindow.Open();
        }

        protected override void DrawPreview(ElementTypeData d)
        {
            var chart = TypeChartTools.FindChart();
            if (chart == null)
            {
                EditorGUILayout.HelpBox("Aún no hay Tabla de Tipos. Créala con 'Crear los 18 tipos clásicos + tabla' o desde la Tabla de Tipos.", MessageType.Info);
                return;
            }

            var map = TypeChartTools.ReadAll(chart);
            var types = ContentAssets.LoadAll<ElementTypeData>();

            EditorGUILayout.LabelField($"Al ATACAR con {d.DisplayName}", EditorStyles.boldLabel);
            Line("Muy eficaz (×2) contra", types.Where(t => Get(map, d, t) > 1f));
            Line("Poco eficaz (×½) contra", types.Where(t => Get(map, d, t) > 0f && Get(map, d, t) < 1f));
            Line("Sin efecto (×0) contra", types.Where(t => Get(map, d, t) <= 0f));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Al DEFENDER siendo {d.DisplayName}", EditorStyles.boldLabel);
            Line("Débil (×2) a", types.Where(t => Get(map, t, d) > 1f));
            Line("Resiste (×½)", types.Where(t => Get(map, t, d) > 0f && Get(map, t, d) < 1f));
            Line("Inmune (×0) a", types.Where(t => Get(map, t, d) <= 0f));

            if (GUILayout.Button("Editar en la Tabla de Tipos")) TypeChartWindow.Open();
        }

        private static float Get(Dictionary<(ElementTypeData, ElementTypeData), float> map, ElementTypeData a, ElementTypeData b)
            => map.TryGetValue((a, b), out var m) ? m : 1f;

        private static void Line(string label, IEnumerable<ElementTypeData> types)
        {
            var names = types.Select(t => t.DisplayName).ToList();
            EditorGUILayout.LabelField(label, names.Count == 0 ? "—" : string.Join(", ", names));
        }
    }

    /// <summary>
    /// La TABLA DE TIPOS como matriz (menú CTEditor → Combate → Tabla de tipos): filas = tipo que ATACA,
    /// columnas = tipo que DEFIENDE. Clic en una celda para rotar su valor: 1 → 2 → ½ → 0 → 1.
    /// Verde = muy eficaz, rojo = poco eficaz, gris = sin efecto, blanco = normal (×1).
    /// </summary>
    public sealed class TypeChartWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem(EditorMenus.Battle + "Tabla de tipos", false, EditorMenus.BattleOrder + 3)]
        public static void Open()
        {
            var w = GetWindow<TypeChartWindow>();
            w.titleContent = new GUIContent("Tabla de Tipos");
            w.minSize = new Vector2(620, 480);
            w.Show();
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
            EditorTheme.TitleBand("Tabla de tipos",
                "Fila = tipo que ATACA · Columna = tipo que DEFIENDE. Haz clic en una celda para cambiarla: 1 → 2 → ½ → 0.",
                EditorTheme.Types, "⚔");
            // Leyenda de colores de las celdas.
            EditorTheme.Pills(("×2 muy eficaz", TypeChartTools.CellColor(2f)), ("×1 normal", TypeChartTools.CellColor(1f)),
                ("×½ poco eficaz", TypeChartTools.CellColor(0.5f)), ("×0 no afecta", TypeChartTools.CellColor(0f)));

            var chart = TypeChartTools.FindChart();
            var types = ContentAssets.LoadAll<ElementTypeData>();
            types.Sort((a, b) => IndexOfClassic(a).CompareTo(IndexOfClassic(b)));

            if (chart == null || types.Count == 0)
            {
                EditorGUILayout.HelpBox("No hay tipos o tabla todavía.", MessageType.Info);
                if (GUILayout.Button("Crear los 18 tipos clásicos + tabla"))
                {
                    TypeChartTools.CreateClassicSet();
                    ShowNotification(new GUIContent("Tipos y tabla clásicos creados"));
                }
                return;
            }

            var map = TypeChartTools.ReadAll(chart);
            const float cell = 32f, header = 96f, top = 30f;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // La rejilla se dibuja con posiciones EXACTAS (no con botones que ponen su propio margen): así cada
            // columna de la cabecera cae justo encima de sus casillas.
            int n = types.Count;
            var area = GUILayoutUtility.GetRect(header + n * cell + 4, top + n * cell + 4, GUILayout.ExpandWidth(false));
            var small = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(area.x, area.y, header, top), "ATACA ↓ · DEFIENDE →", EditorStyles.miniLabel);

            for (int c = 0; c < n; c++)
            {
                var def = types[c];
                var r = new Rect(area.x + header + c * cell, area.y, cell - 1, top - 2);
                EditorGUI.DrawRect(r, def.Color);
                GUI.Label(r, new GUIContent(Abbrev(def), def.DisplayName), small);
            }
            for (int row = 0; row < n; row++)
            {
                var atk = types[row];
                float y = area.y + top + row * cell;
                var hr = new Rect(area.x, y, header - 2, cell - 1);
                EditorGUI.DrawRect(hr, atk.Color);
                GUI.Label(new Rect(hr.x + 4, hr.y, hr.width - 4, hr.height), atk.DisplayName, EditorStyles.miniBoldLabel);
                for (int c = 0; c < n; c++)
                {
                    var def = types[c];
                    float m = map.TryGetValue((atk, def), out var v) ? v : 1f;
                    var r = new Rect(area.x + header + c * cell, y, cell - 1, cell - 1);
                    EditorGUI.DrawRect(r, TypeChartTools.CellColor(m));
                    var tip = $"{atk.DisplayName} contra {def.DisplayName}: ×{m:0.##} (clic para cambiar)";
                    if (GUI.Button(r, new GUIContent(TypeChartTools.Short(m), tip), small))
                        TypeChartTools.Set(chart, atk, def, TypeChartTools.Cycle(m));
                }
            }

            EditorGUILayout.EndScrollView();

            // ---------- Revisar y restaurar por épocas ----------
            EditorTheme.Section("Revisar la tabla", EditorTheme.Types);
            _era = (TypeChartTools.ChartEra)EditorGUILayout.Popup("Comparar con", (int)_era,
                new[] { TypeChartTools.EraName(TypeChartTools.ChartEra.Modern), TypeChartTools.EraName(TypeChartTools.ChartEra.Gen2To5), TypeChartTools.EraName(TypeChartTools.ChartEra.Gen1) });
            // Se recalcula SOLO en el evento Layout: si cambiara entre Layout y Repaint, Unity daría error de controles.
            if (_diffs == null || Event.current.type == EventType.Layout && (_diffsEra != _era || EditorApplication.timeSinceStartup - _diffsTime > 2))
            {
                _diffs = TypeChartTools.Differences(chart, _era);
                _diffsEra = _era;
                _diffsTime = EditorApplication.timeSinceStartup;
            }
            if (_diffs.Count == 0) EditorTheme.Tip("Tu tabla coincide con esa época entre sus tipos.", EditorTheme.Ok, "✔");
            else
            {
                EditorTheme.Tip($"{_diffs.Count} casilla(s) distintas de esa época (puede ser a propósito):", EditorTheme.Warn, "⚠");
                foreach (var line in _diffs.Take(12)) EditorTheme.Paragraph("• " + line);
                if (_diffs.Count > 12) EditorTheme.Paragraph($"… y {_diffs.Count - 12} más.");
            }
            if (GUILayout.Button("Poner la tabla de esa época (borra lo distinto entre sus tipos; tus tipos inventados no se tocan)"))
            {
                if (EditorUtility.DisplayDialog("Tabla de tipos", "Se sustituirán las casillas entre los tipos clásicos por las de:\n" + TypeChartTools.EraName(_era), "Aplicar", "Cancelar"))
                {
                    TypeChartTools.ApplyEra(_era);
                    _diffsTime = 0;   // se recalcula en el próximo Layout
                    ShowNotification(new GUIContent("Tabla aplicada"));
                    GUIUtility.ExitGUI();
                }
            }
        }

        private TypeChartTools.ChartEra _era = TypeChartTools.ChartEra.Modern;
        private TypeChartTools.ChartEra _diffsEra;
        private List<string> _diffs;
        private double _diffsTime;

        private static string Abbrev(ElementTypeData t)
        {
            string n = string.IsNullOrEmpty(t.DisplayName) ? t.Id : t.DisplayName;
            return n.Length <= 3 ? n : n.Substring(0, 3);
        }

        // Orden clásico primero; los tipos inventados al final.
        private static int IndexOfClassic(ElementTypeData t)
        {
            for (int i = 0; i < TypeChartTools.ClassicTypes.Length; i++)
                if (TypeChartTools.ClassicTypes[i].id == t.Id) return i;
            return 1000;
        }
    }
}
