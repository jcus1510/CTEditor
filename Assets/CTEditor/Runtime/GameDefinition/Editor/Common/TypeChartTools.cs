using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Lógica de EDITOR para la tabla de tipos: leer y escribir multiplicadores, y el preset clásico
    /// (18 tipos, tabla de Gen VI en adelante). Lo comparten el editor de tipos y la matriz.
    ///
    /// La tabla guarda SOLO las relaciones distintas de ×1 (lo que no está escrito vale 1): por eso,
    /// poner una celda a ×1 BORRA su entrada en vez de guardar un 1 inútil.
    /// </summary>
    public static class TypeChartTools
    {
        // Los 18 tipos clásicos: id, nombre en español y color (hex).
        public static readonly (string id, string name, string hex)[] ClassicTypes =
        {
            ("normal", "Normal", "A8A77A"),   ("fire", "Fuego", "EE8130"),      ("water", "Agua", "6390F0"),
            ("electric", "Eléctrico", "F7D02C"), ("grass", "Planta", "7AC74C"), ("ice", "Hielo", "96D9D6"),
            ("fighting", "Lucha", "C22E28"),  ("poison", "Veneno", "A33EA1"),   ("ground", "Tierra", "E2BF65"),
            ("flying", "Volador", "A98FF3"),  ("psychic", "Psíquico", "F95587"), ("bug", "Bicho", "A6B91A"),
            ("rock", "Roca", "B6A136"),       ("ghost", "Fantasma", "735797"),  ("dragon", "Dragón", "6F35FC"),
            ("dark", "Siniestro", "705746"),  ("steel", "Acero", "B7B7CE"),     ("fairy", "Hada", "D685AD"),
        };

        // Tabla clásica (Gen VI+): atacante -> "defensor:multiplicador" (solo lo que no es ×1).
        private static readonly Dictionary<string, string> ClassicChart = new Dictionary<string, string>
        {
            ["normal"]   = "rock:0.5 ghost:0 steel:0.5",
            ["fire"]     = "fire:0.5 water:0.5 grass:2 ice:2 bug:2 rock:0.5 dragon:0.5 steel:2",
            ["water"]    = "fire:2 water:0.5 grass:0.5 ground:2 rock:2 dragon:0.5",
            ["electric"] = "water:2 electric:0.5 grass:0.5 ground:0 flying:2 dragon:0.5",
            ["grass"]    = "fire:0.5 water:2 grass:0.5 poison:0.5 ground:2 flying:0.5 bug:0.5 rock:2 dragon:0.5 steel:0.5",
            ["ice"]      = "fire:0.5 water:0.5 grass:2 ice:0.5 ground:2 flying:2 dragon:2 steel:0.5",
            ["fighting"] = "normal:2 ice:2 poison:0.5 flying:0.5 psychic:0.5 bug:0.5 rock:2 ghost:0 dark:2 steel:2 fairy:0.5",
            ["poison"]   = "grass:2 poison:0.5 ground:0.5 rock:0.5 ghost:0.5 steel:0 fairy:2",
            ["ground"]   = "fire:2 electric:2 grass:0.5 poison:2 flying:0 bug:0.5 rock:2 steel:2",
            ["flying"]   = "electric:0.5 grass:2 fighting:2 bug:2 rock:0.5 steel:0.5",
            ["psychic"]  = "fighting:2 poison:2 psychic:0.5 dark:0 steel:0.5",
            ["bug"]      = "fire:0.5 grass:2 fighting:0.5 poison:0.5 flying:0.5 psychic:2 ghost:0.5 dark:2 steel:0.5 fairy:0.5",
            ["rock"]     = "fire:2 ice:2 fighting:0.5 ground:0.5 flying:2 bug:2 steel:0.5",
            ["ghost"]    = "normal:0 psychic:2 ghost:2 dark:0.5",
            ["dragon"]   = "dragon:2 steel:0.5 fairy:0",
            ["dark"]     = "fighting:0.5 psychic:2 ghost:2 dark:0.5 fairy:0.5",
            ["steel"]    = "fire:0.5 water:0.5 electric:0.5 ice:2 rock:2 steel:0.5 fairy:2",
            ["fairy"]    = "fire:0.5 fighting:2 poison:0.5 dragon:2 dark:2 steel:0.5",
        };

        /// <summary>La tabla de tipos del proyecto (la primera que haya), o null.</summary>
        public static TypeChartData FindChart()
        {
            var all = ContentAssets.LoadAll<TypeChartData>();
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>Crea la tabla vacía si no existe y la devuelve.</summary>
        public static TypeChartData GetOrCreateChart()
        {
            var chart = FindChart();
            if (chart != null) return chart;
            string folder = ContentFolders.PathOf(ContentFolders.Types);
            ContentAssets.EnsureFolder(folder);
            chart = ScriptableObject.CreateInstance<TypeChartData>();
            AssetDatabase.CreateAsset(chart, folder + "/TypeChart.asset");
            ContentAssets.ClearCache();
            return chart;
        }

        /// <summary>Todos los multiplicadores escritos, para consultar rápido (una lectura por dibujado).</summary>
        public static Dictionary<(ElementTypeData, ElementTypeData), float> ReadAll(TypeChartData chart)
        {
            var map = new Dictionary<(ElementTypeData, ElementTypeData), float>();
            if (chart == null || chart.Matchups == null) return map;
            foreach (var m in chart.Matchups)
                if (m.attacking != null && m.defending != null)
                    map[(m.attacking, m.defending)] = m.multiplier;
            return map;
        }

        /// <summary>Fija un multiplicador (×1 = borrar la entrada). Con Deshacer.</summary>
        public static void Set(TypeChartData chart, ElementTypeData attacking, ElementTypeData defending, float value)
        {
            if (chart == null || attacking == null || defending == null) return;
            ContentAssets.Edit(chart, so => SetIn(so, attacking, defending, value));
        }

        private static void SetIn(SerializedObject so, ElementTypeData attacking, ElementTypeData defending, float value)
        {
            var arr = so.FindProperty("matchups");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("attacking").objectReferenceValue == attacking &&
                    e.FindPropertyRelative("defending").objectReferenceValue == defending)
                {
                    if (Mathf.Approximately(value, 1f)) arr.DeleteArrayElementAtIndex(i);
                    else e.FindPropertyRelative("multiplier").floatValue = value;
                    return;
                }
            }
            if (Mathf.Approximately(value, 1f)) return; // ×1 no se guarda
            arr.arraySize++;
            var added = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            added.FindPropertyRelative("attacking").objectReferenceValue = attacking;
            added.FindPropertyRelative("defending").objectReferenceValue = defending;
            added.FindPropertyRelative("multiplier").floatValue = value;
        }

        /// <summary>
        /// Crea los 18 tipos clásicos que falten y escribe la tabla clásica entre ellos. Si ya tenías
        /// tipos con esos ids, los reutiliza. Tus tipos inventados no se tocan. Devuelve cuántos tipos creó.
        /// </summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var (id, name, hex) in ClassicTypes)
            {
                var color = FromHex(hex);
                if (ContentAssets.CreateIfMissing<ElementTypeData>(ContentFolders.Types, id, name,
                        so => so.FindProperty("color").colorValue = color))
                    created++;
            }

            var byId = new Dictionary<string, ElementTypeData>();
            foreach (var t in ContentAssets.LoadAll<ElementTypeData>())
                if (!string.IsNullOrEmpty(t.Id)) byId[t.Id] = t;

            // Tipos clásicos que ya existían (de antes de tener color): si siguen con el gris por
            // defecto, reciben su color clásico. Si el autor eligió otro color, se respeta.
            foreach (var (id, _, hex) in ClassicTypes)
                if (byId.TryGetValue(id, out var existing) && IsDefaultGray(existing.Color))
                {
                    var classic = FromHex(hex);
                    ContentAssets.Edit(existing, so => so.FindProperty("color").colorValue = classic);
                }

            var chart = GetOrCreateChart();
            ContentAssets.Edit(chart, so =>
            {
                foreach (var kv in ClassicChart)
                {
                    if (!byId.TryGetValue(kv.Key, out var atk)) continue;
                    foreach (var pair in kv.Value.Split(' '))
                    {
                        var parts = pair.Split(':');
                        if (!byId.TryGetValue(parts[0], out var def)) continue;
                        SetIn(so, atk, def, float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            });
            AssetDatabase.SaveAssets();
            return created;
        }

        // ---------------- Tablas de cada época ----------------

        public enum ChartEra { Modern, Gen2To5, Gen1 }

        public static string EraName(ChartEra e)
            => e == ChartEra.Modern ? "Actual (6ª gen. en adelante, 18 tipos con Hada)"
             : e == ChartEra.Gen2To5 ? "2ª a 5ª gen. (17 tipos, sin Hada; Acero resiste Fantasma y Siniestro)"
             : "1ª gen. (15 tipos; Fantasma no afecta a Psíquico, Bicho y Veneno ×2 entre sí)";

        /// <summary>La tabla de una época: (atacante, defensor) → multiplicador (solo lo que no es ×1) y sus tipos.</summary>
        public static (List<string> types, Dictionary<(string, string), float> chart) Era(ChartEra era)
        {
            var map = new Dictionary<(string, string), float>();
            foreach (var kv in ClassicChart)
                foreach (var pair in kv.Value.Split(' '))
                {
                    var parts = pair.Split(':');
                    map[(kv.Key, parts[0])] = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                }
            var types = new List<string>();
            foreach (var t in ClassicTypes) types.Add(t.id);
            if (era == ChartEra.Modern) return (types, map);

            // 2ª-5ª: sin Hada; Acero resistía Fantasma y Siniestro.
            types.Remove("fairy");
            foreach (var k in new List<(string, string)>(map.Keys)) if (k.Item1 == "fairy" || k.Item2 == "fairy") map.Remove(k);
            map[("ghost", "steel")] = 0.5f;
            map[("dark", "steel")] = 0.5f;
            if (era == ChartEra.Gen2To5) return (types, map);

            // 1ª: sin Siniestro ni Acero; y sus famosas diferencias.
            types.Remove("dark"); types.Remove("steel");
            foreach (var k in new List<(string, string)>(map.Keys))
                if (k.Item1 == "dark" || k.Item2 == "dark" || k.Item1 == "steel" || k.Item2 == "steel") map.Remove(k);
            map[("bug", "poison")] = 2f;
            map[("poison", "bug")] = 2f;
            map[("ghost", "psychic")] = 0f;
            map.Remove(("ice", "fire"));
            return (types, map);
        }

        /// <summary>
        /// Pone la tabla de una época ENTRE SUS TIPOS: primero todo a ×1 (borra lo que hubiera, aunque estuviera
        /// mal) y luego sus valores. Crea los tipos que falten. Tus tipos inventados (y los que esa época no tiene,
        /// como Hada en la 1ª gen.) no se tocan.
        /// </summary>
        public static void ApplyEra(ChartEra era)
        {
            CreateClassicSet(); // asegura tipos y colores
            var (types, map) = Era(era);
            var byId = new Dictionary<string, ElementTypeData>();
            foreach (var t in ContentAssets.LoadAll<ElementTypeData>())
                if (!string.IsNullOrEmpty(t.Id)) byId[t.Id] = t;
            var chart = GetOrCreateChart();
            ContentAssets.Edit(chart, so =>
            {
                // Borrar las entradas entre tipos clásicos (las de tus tipos inventados se quedan).
                var arr = so.FindProperty("matchups");
                for (int i = arr.arraySize - 1; i >= 0; i--)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    var atk = e.FindPropertyRelative("attacking").objectReferenceValue as ElementTypeData;
                    var def = e.FindPropertyRelative("defending").objectReferenceValue as ElementTypeData;
                    // Solo las casillas entre tipos DE ESA ÉPOCA: con la 1ª gen., Acero/Siniestro/Hada conservan lo que tuvieran.
                    if (atk == null || def == null || (types.Contains(atk.Id) && types.Contains(def.Id))) arr.DeleteArrayElementAtIndex(i);
                }
                foreach (var kv in map)
                    if (byId.TryGetValue(kv.Key.Item1, out var a) && byId.TryGetValue(kv.Key.Item2, out var d))
                        SetIn(so, a, d, kv.Value);
            });
            AssetDatabase.SaveAssets();
        }

        public static bool IsClassic(string id) { foreach (var t in ClassicTypes) if (t.id == id) return true; return false; }

        /// <summary>Diferencias de la tabla del proyecto con la de una época: "Fuego → Agua: tienes ×1, debería ser ×0,5".</summary>
        public static List<string> Differences(TypeChartData chart, ChartEra era)
        {
            var list = new List<string>();
            if (chart == null) return list;
            var (types, map) = Era(era);
            var current = ReadAll(chart);
            var byId = new Dictionary<string, ElementTypeData>();
            foreach (var t in ContentAssets.LoadAll<ElementTypeData>())
                if (!string.IsNullOrEmpty(t.Id)) byId[t.Id] = t;
            foreach (var a in types)
                foreach (var d in types)
                {
                    if (!byId.TryGetValue(a, out var at) || !byId.TryGetValue(d, out var dt)) continue;
                    float want = map.TryGetValue((a, d), out var w) ? w : 1f;
                    float have = current.TryGetValue((at, dt), out var h) ? h : 1f;
                    if (!Mathf.Approximately(want, have))
                        list.Add($"{at.DisplayName} → {dt.DisplayName}: tienes ×{have:0.##}, debería ser ×{want:0.##}");
                }
            return list;
        }

        /// <summary>Texto corto de un multiplicador para las celdas: "2", "½", "0" o "".</summary>
        public static string Short(float m)
            => Mathf.Approximately(m, 1f) ? "" : Mathf.Approximately(m, 0.5f) ? "½" : Mathf.Approximately(m, 0.25f) ? "¼" : m.ToString("0.##");

        /// <summary>Color de fondo de una celda según su efecto.</summary>
        public static Color CellColor(float m)
        {
            if (Mathf.Approximately(m, 1f)) return Color.white;
            if (m <= 0f) return new Color(0.35f, 0.35f, 0.35f);
            return m > 1f ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.95f, 0.5f, 0.45f);
        }

        /// <summary>Siguiente valor al hacer clic en una celda: 1 → 2 → ½ → 0 → 1.</summary>
        public static float Cycle(float m)
            => Mathf.Approximately(m, 1f) ? 2f : Mathf.Approximately(m, 2f) ? 0.5f : Mathf.Approximately(m, 0.5f) ? 0f : 1f;

        private static bool IsDefaultGray(Color c)
            => Mathf.Approximately(c.r, 0.62f) && Mathf.Approximately(c.g, 0.62f) && Mathf.Approximately(c.b, 0.62f);

        public static Color FromHex(string hex)
        {
            int v = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
        }
    }
}
