using System.Linq;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Formulas;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de CURVAS DE EXPERIENCIA (menú CTEditor → Criaturas → Curvas de experiencia).
    ///
    /// Además del inspector, muestra lo que un diseñador necesita para decidir:
    ///   - un GRÁFICO de XP total por nivel, comparado con las 6 curvas clásicas (con lectura al pasar
    ///     el ratón),
    ///   - una TABLA de hitos (cuánta XP hace falta en cada tramo),
    ///   - el RITMO DE JUEGO estimado: cuántos combates cuesta llegar a cada nivel.
    ///
    /// Todo se calcula con el MISMO mapper que usa el juego (GrowthCurveMapper), así lo que ves aquí es
    /// exactamente lo que pasará en partida.
    /// </summary>
    public sealed class GrowthCurveEditorWindow : ContentEditorWindow<GrowthCurveData>
    {
        [MenuItem(EditorMenus.Creatures + "Curvas de experiencia", false, EditorMenus.CreaturesOrder + 5)]
        public static void Open() => OpenWindow<GrowthCurveEditorWindow>("Curvas de XP");

        protected override string Category => ContentFolders.Curves;
        protected override string Noun => "curva";
        protected override string Intro =>
            "Una curva define cuánta experiencia TOTAL necesita un monstruo para alcanzar cada nivel. " +
            "Cada especie elige la suya en su ficha (campo «Curva de experiencia», con desplegable).";

        protected override string Title => "Curvas de experiencia";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear las 6 curvas clásicas».",
            "Compara curvas en el gráfico y ajusta la «Velocidad global» para que tu juego sea más rápido o lento.",
            "Para algo propio elige «Tabla propia» (XP por nivel) o «Fórmulas por tramos» (ej.: n^3).",
            "Cada especie elige su curva en su ficha.",
        };

        // Los 6 grupos clásicos: fórmula, id, nombre en español y color en el gráfico.
        internal static readonly (GrowthCurveFormula formula, string id, string name, Color color)[] Classic =
        {
            (GrowthCurveFormula.Fast,        "fast",        "Rápida",         new Color(0.35f, 0.78f, 0.98f)),
            (GrowthCurveFormula.MediumFast,  "medium_fast", "Media (normal)", new Color(0.55f, 0.85f, 0.45f)),
            (GrowthCurveFormula.MediumSlow,  "medium_slow", "Parabólica",     new Color(0.98f, 0.80f, 0.30f)),
            (GrowthCurveFormula.Slow,        "slow",        "Lenta",          new Color(0.95f, 0.50f, 0.35f)),
            (GrowthCurveFormula.Erratic,     "erratic",     "Errática",       new Color(0.80f, 0.55f, 0.95f)),
            (GrowthCurveFormula.Fluctuating, "fluctuating", "Fluctuante",     new Color(0.95f, 0.45f, 0.70f)),
        };

        private static readonly int[] MultiplierPresets = { 50, 75, 100, 150, 200 };

        // Plantillas de fórmula propias (para inspirar al autor).
        private static readonly (string name, (int from, string expr)[] segments)[] FormulaTemplates =
        {
            ("Cúbica (n³)",            new[] { (2, "n^3") }),
            ("Lineal (sube parejo)",   new[] { (2, "500*n") }),
            ("Cuadrática",             new[] { (2, "40*n^2") }),
            ("Exponencial suave",      new[] { (2, "100*1.08^n") }),
            ("Por tramos: rápida hasta el 50, dura después", new[] { (2, "0.6*n^3"), (51, "0.6*n^3 + 4*(n-50)^3") }),
        };

        private int _formulaTemplate;

        private bool _compare = true;
        private int _enemyYield = 64;
        private int _startLevel = 5;

        // Caché del cálculo de ritmo (solo se recalcula si cambian las entradas).
        private string _paceKey;
        private List<string> _paceLines = new List<string>();

        // ---------------- Set clásico ----------------

        /// <summary>Crea las curvas clásicas que falten. Público: también lo usa el Centro de Contenido.</summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var c in Classic)
            {
                var formula = c.formula;
                if (ContentAssets.CreateIfMissing<GrowthCurveData>(ContentFolders.Curves, c.id, c.name,
                        so => { so.FindProperty("formula").enumValueIndex = (int)formula; }))
                    created++;
            }
            return created;
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Classic.Select(c => (c.id, c.name, "Curvas clásicas")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            foreach (var c in Classic)
            {
                if (c.id != id) continue;
                so.FindProperty("formula").enumValueIndex = (int)c.formula;
                so.FindProperty("displayName").stringValue = c.name;
            }
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear las 6 curvas clásicas")) FinishBulk(CreateClassicSet(), "curvas");
        }

        // ---------------- Plantillas para la curva seleccionada ----------------

        protected override void DrawPresets(GrowthCurveData d)
        {
            EditorGUILayout.LabelField("Forma de la curva (plantillas clásicas)", EditorStyles.boldLabel);
            int a = ButtonRow(Classic[0].name, Classic[1].name, Classic[2].name);
            int b = ButtonRow(Classic[3].name, Classic[4].name, Classic[5].name);
            int pick = a >= 0 ? a : (b >= 0 ? b + 3 : -1);
            if (pick >= 0)
            {
                var f = Classic[pick].formula;
                EditSelected(so => so.FindProperty("formula").enumValueIndex = (int)f);
            }

            EditorGUILayout.LabelField("Velocidad global (multiplica la XP necesaria de la plantilla)", EditorStyles.miniLabel);
            int m = ButtonRow("×0.5 (doble de rápido)", "×0.75", "×1 (clásico)", "×1.5", "×2 (doble de lento)");
            if (m >= 0)
            {
                int pct = MultiplierPresets[m];
                EditSelected(so => so.FindProperty("xpMultiplierPercent").intValue = pct);
            }

            if (GUILayout.Button("Convertir en tabla personalizada (para afinar nivel a nivel)"))
                ConvertToCustom(d);

            // --- Fórmulas propias por tramos ---
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fórmulas propias (por tramos)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var names = new string[FormulaTemplates.Length];
            for (int i = 0; i < names.Length; i++) names[i] = FormulaTemplates[i].name;
            _formulaTemplate = EditorGUILayout.Popup(_formulaTemplate, names);
            if (GUILayout.Button("Usar plantilla", GUILayout.Width(110)))
                SetFormulas(FormulaTemplates[_formulaTemplate].segments);
            EditorGUILayout.EndHorizontal();

            if (d.Formula != GrowthCurveFormula.Custom && d.Formula != GrowthCurveFormula.Expression &&
                GUILayout.Button("Convertir la curva actual en su fórmula oficial (para retocarla)"))
                SetFormulas(GrowthCurveMapper.OfficialFormula(d.Formula));

            if (d.Formula == GrowthCurveFormula.Expression) DrawFormulaCheck(d);
            EditorGUILayout.Space();
        }

        private void SetFormulas((int from, string expr)[] segments)
        {
            EditSelected(so =>
            {
                var arr = so.FindProperty("formulaSegments");
                arr.arraySize = segments.Length;
                for (int i = 0; i < segments.Length; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("fromLevel").intValue = segments[i].from;
                    e.FindPropertyRelative("expression").stringValue = segments[i].expr;
                }
                so.FindProperty("formula").enumValueIndex = (int)GrowthCurveFormula.Expression;
            });
            ShowNotification(new GUIContent("Fórmulas listas: edítalas en «Tramos de fórmula»"));
        }

        // Comprobación en vivo de cada tramo: ✔ con un valor de ejemplo, o el error exacto.
        private static void DrawFormulaCheck(GrowthCurveData d)
        {
            EditorGUILayout.HelpBox(
                "Escribe la XP TOTAL para ser nivel n. Operadores + - * / ^ ( )   Funciones: " +
                string.Join(", ", MathExpression.FunctionNames) + ".   Decimales con punto: 0.8", MessageType.None);

            var segs = d.FormulaSegments;
            if (segs == null || segs.Length == 0)
            {
                EditorGUILayout.HelpBox("No hay tramos: se usará la curva Media. Añade uno en «Tramos de fórmula».", MessageType.Warning);
                return;
            }
            for (int i = 0; i < segs.Length; i++)
            {
                var seg = segs[i];
                if (seg == null) continue;
                int nextFrom = i + 1 < segs.Length && segs[i + 1] != null ? segs[i + 1].fromLevel - 1 : d.MaxLevel;
                string range = $"Tramo {i + 1} (Nv.{seg.fromLevel}–{nextFrom})";
                if (MathExpression.TryParse(seg.expression, GrowthCurveMapper.CurveVariables, out var expr, out var error))
                {
                    double atStart = expr.Evaluate("n", seg.fromLevel);
                    EditorGUILayout.LabelField($"✔ {range}:  {seg.expression}   →  n={seg.fromLevel}: {Math.Floor(atStart):N0} XP");
                }
                else
                {
                    EditorGUILayout.HelpBox($"{range}: {error}", MessageType.Error);
                }
            }
        }

        // Copia la curva actual (preset × multiplicador) a la tabla editable y cambia a Custom.
        private void ConvertToCustom(GrowthCurveData d)
        {
            var curve = Map(d);
            if (curve == null) return;
            EditSelected(so =>
            {
                var table = so.FindProperty("customTable");
                table.arraySize = curve.MaxLevel + 1;
                for (int level = 0; level <= curve.MaxLevel; level++)
                    table.GetArrayElementAtIndex(level).intValue = level <= 1 ? 0 : curve.XpToReachLevel(level);
                so.FindProperty("formula").enumValueIndex = (int)GrowthCurveFormula.Custom;
                so.FindProperty("xpMultiplierPercent").intValue = 100;
            });
            ShowNotification(new GUIContent("Tabla creada: edítala en «Tabla propia»"));
        }

        // ---------------- Vista previa ----------------

        protected override void DrawPreview(GrowthCurveData d)
        {
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                EditorGUILayout.HelpBox("Ponle un Id a la curva para ver el gráfico.", MessageType.Warning);
                return;
            }
            var curve = Map(d);
            if (curve == null)
            {
                EditorGUILayout.HelpBox("No se pudo generar la curva con estos datos (revisa la validación).", MessageType.Error);
                return;
            }

            int max = curve.MaxLevel;
            var normal = GrowthCurveMapper.Preset(GrowthCurveFormula.MediumFast, new Id<GrowthCurve>("ref"), "Media", max);

            EditorGUILayout.LabelField("Gráfico: XP total necesaria por nivel", EditorStyles.boldLabel);
            _compare = EditorGUILayout.ToggleLeft("Comparar con las 6 curvas clásicas", _compare);

            var series = new List<ChartSeries>();
            if (_compare)
            {
                foreach (var c in Classic)
                {
                    var preset = GrowthCurveMapper.Preset(c.formula, new Id<GrowthCurve>(c.id), c.name, max);
                    var col = c.color; col.a = 0.55f;
                    series.Add(new ChartSeries(c.name, col, 1.3f, x => preset.XpToReachLevel(x)));
                }
            }
            series.Add(new ChartSeries("Esta curva", EditorGUIUtility.isProSkin ? Color.white : Color.black, 3f,
                x => curve.XpToReachLevel(x)));
            LineChart.Draw(250f, series, 1, max, "Nivel", LineChart.Compact);

            long total = curve.XpToReachLevel(max);
            long normalTotal = normal.XpToReachLevel(max);
            EditorGUILayout.HelpBox(
                $"Para llegar al Nv.{max} hacen falta {total:N0} XP en total " +
                $"({Percent(total, normalTotal)} de lo que pide la curva Media).", MessageType.Info);

            DrawMilestones(curve, normal);
            DrawPace(d, curve);
        }

        private static void DrawMilestones(GrowthCurve curve, GrowthCurve normal)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hitos", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Nivel", EditorStyles.miniLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("XP total", EditorStyles.miniLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField("XP para subir al siguiente", EditorStyles.miniLabel, GUILayout.Width(170));
            EditorGUILayout.LabelField("vs. Media", EditorStyles.miniLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            var levels = new List<int> { 5 };
            for (int l = 10; l < curve.MaxLevel; l += 10) levels.Add(l);
            levels.Add(curve.MaxLevel);

            foreach (int level in levels)
            {
                int xp = curve.XpToReachLevel(level);
                string next = level < curve.MaxLevel
                    ? (curve.XpToReachLevel(level + 1) - xp).ToString("N0")
                    : "— (nivel máximo)";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(level.ToString(), GUILayout.Width(50));
                EditorGUILayout.LabelField(xp.ToString("N0"), GUILayout.Width(110));
                EditorGUILayout.LabelField(next, GUILayout.Width(170));
                EditorGUILayout.LabelField(Percent(xp, normal.XpToReachLevel(level)), GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }
        }

        // Ritmo de juego: cuántos combates contra rivales DE SU MISMO NIVEL hacen falta para llegar a
        // ciertos niveles, usando la fórmula plana clásica (rendimiento × nivel / 7; ×1.5 entrenador).
        // Es una estimación de diseño, no una promesa: en partida influyen el nivel de los rivales,
        // cuántos participan, objetos, etc. Pero sirve MUCHO para comparar curvas y detectar extremos.
        private void DrawPace(GrowthCurveData d, GrowthCurve curve)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ritmo de juego estimado", EditorStyles.boldLabel);
            _enemyYield = EditorGUILayout.IntSlider("Rendimiento de XP de los rivales", _enemyYield, 10, 400);
            _startLevel = EditorGUILayout.IntSlider("Nivel inicial", _startLevel, 1, Math.Max(1, curve.MaxLevel - 1));

            string key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d)}|{d.Formula}|{d.XpMultiplierPercent}|{curve.MaxLevel}|{curve.XpToReachLevel(curve.MaxLevel)}|{_enemyYield}|{_startLevel}";
            if (key != _paceKey)
            {
                _paceKey = key;
                _paceLines.Clear();
                foreach (int target in new[] { 20, 36, 50, 75, 100 })
                {
                    if (target <= _startLevel || target > curve.MaxLevel) continue;
                    int wild = Battles(curve, _startLevel, target, _enemyYield, trainer: false);
                    int trainer = Battles(curve, _startLevel, target, _enemyYield, trainer: true);
                    _paceLines.Add($"Nv.{_startLevel} → Nv.{target}: ~{wild:N0} combates contra salvajes (~{trainer:N0} contra entrenadores)");
                }
            }

            if (_paceLines.Count == 0)
                EditorGUILayout.LabelField("Sube el nivel máximo para ver estimaciones.", EditorStyles.miniLabel);
            foreach (var line in _paceLines)
                EditorGUILayout.LabelField(line);
            EditorGUILayout.LabelField("Supone rivales de tu mismo nivel y un solo participante (fórmula plana clásica).", EditorStyles.miniLabel);
        }

        private static int Battles(GrowthCurve curve, int from, int to, int yield, bool trainer)
        {
            int level = from, count = 0;
            long xp = curve.XpToReachLevel(from);
            while (level < to && count < 200000)
            {
                long gain = Math.Max(1, (long)yield * level / 7);
                if (trainer) gain = gain * 3 / 2;
                xp += gain;
                count++;
                while (level < curve.MaxLevel && xp >= curve.XpToReachLevel(level + 1)) level++;
            }
            return count;
        }

        // ---------------- Helpers ----------------

        private static GrowthCurve Map(GrowthCurveData d)
        {
            try { return GrowthCurveMapper.ToDomain(d); }
            catch (Exception) { return null; }
        }

        private static string Percent(long value, long reference)
            => reference <= 0 ? "—" : $"{value * 100 / reference}%";
    }
}
