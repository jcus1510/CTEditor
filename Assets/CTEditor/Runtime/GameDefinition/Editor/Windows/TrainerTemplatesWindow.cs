using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// 📋 PLANTILLAS DE ENTRENADORES: todos los entrenadores del pack elegido (o de tu Excel) como plantillas.
    /// Eliges cuáles (todos, unos pocos o los 7 sugeridos, uno por nivel de IA), ves la VISTA PREVIA de cada uno
    /// (equipo con movimientos, objeto y naturaleza, su IA, mochila y frases) y los añades. Antes se crea lo que
    /// necesitan (movimientos, habilidades y las especies de sus equipos con su familia) y se guarda copia.
    /// </summary>
    public sealed class TrainerTemplatesWindow : EditorWindow
    {
        private sealed class Row
        {
            public Dictionary<string, string> Cells;
            public string Id, Name, Class;
            public int Level;
            public List<CsvTeamCodecs.ParsedMember> Team = new List<CsvTeamCodecs.ParsedMember>();
            public string TeamError;
        }

        private List<Row> _rows = new List<Row>();
        private readonly HashSet<string> _picked = new HashSet<string>();
        private string _source, _search = "", _focus;
        private int _levelFilter;   // 0 = todos
        private int _classFilter;   // 0 = todas
        private ImportMode _mode = ImportMode.CreateOnly;
        private Vector2 _listScroll, _previewScroll;
        private Dictionary<string, string> _speciesNames = new Dictionary<string, string>(), _moveNames = new Dictionary<string, string>();

        [MenuItem(EditorMenus.Characters + "Plantillas de entrenadores (del pack)", false, EditorMenus.CharactersOrder + 1)]
        public static void Open() => OpenWith(null);

        /// <summary>Abre la ventana con esos entrenadores ya marcados (null = ninguno).</summary>
        public static void OpenWith(IEnumerable<string> preselect)
        {
            var w = GetWindow<TrainerTemplatesWindow>();
            w.titleContent = new GUIContent("Plantillas de entrenadores");
            w.minSize = new Vector2(860, 520);
            w.Load();
            if (preselect != null) { w._picked.Clear(); foreach (var id in preselect) w._picked.Add(id); w._focus = w._picked.FirstOrDefault(); }
            w.Show();
        }

        private void OnEnable() => Load();

        private void Load()
        {
            _source = PackTools.Folder;
            _rows = new List<Row>();
            string path = Path.Combine(_source ?? "", PackTools.TrainersFile);
            if (!File.Exists(path)) return;
            foreach (var cells in CsvTable.Load(path).Rows)
            {
                var r = new Row { Cells = cells, Id = Get(cells, "id"), Name = Get(cells, "nombre"), Class = Get(cells, "clase") };
                if (r.Id.Length == 0) continue;
                int.TryParse(Get(cells, "nivel_ia"), out r.Level);
                try { r.Team = CsvTeamCodecs.ParseTeam(Get(cells, "equipo")); }
                catch (Exception e) { r.TeamError = e.Message; }
                _rows.Add(r);
            }
            _speciesNames = Names(PackTools.SpeciesFile);
            _moveNames = Names(PackTools.MovesFile);
        }

        private Dictionary<string, string> Names(string file)
        {
            var map = new Dictionary<string, string>();
            string path = Path.Combine(_source ?? "", file);
            if (!File.Exists(path)) return map;
            foreach (var r in CsvTable.Load(path).Rows) { string id = Get(r, "id"); if (id.Length > 0) map[id] = Get(r, "nombre"); }
            return map;
        }

        private static string Get(Dictionary<string, string> cells, string key) => cells.TryGetValue(key, out var v) ? (v ?? "").Trim() : "";

        /// <summary>
        /// Los 7 SUGERIDOS: un entrenador por nivel de IA (1-7), preferiendo los más representativos de cada nivel
        /// (entrenadores de ruta, líderes, Alto Mando, Campeones, Ases). Sin los del Laboratorio de IA.
        /// </summary>
        public static List<string> Suggested(IEnumerable<(string id, string cls, int level)> rows)
        {
            string[][] prefer =
            {
                new[] { "Joven", "Cazabichos", "Chica", "Campista" }, new[] { "Líder" }, new[] { "Líder", "Especialista" },
                new[] { "Líder" }, new[] { "Alto Mando", "Campeón" }, new[] { "Campeón", "As del Frente", "Maestr" }, new[] { "Maestro", "Castellana", "Entrenador" },
            };
            var list = rows.Where(r => !r.id.StartsWith("prueba_nivel_")).ToList();
            var result = new List<string>();
            for (int lvl = 1; lvl <= AiProfile.MaxLevel; lvl++)
            {
                var candidates = list.Where(r => r.level == lvl).ToList();
                if (candidates.Count == 0) continue;
                var best = candidates.OrderBy(r =>
                {
                    int i = Array.FindIndex(prefer[lvl - 1], k => (r.cls ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                    return i < 0 ? 99 : i;
                }).First();
                result.Add(best.id);
            }
            return result;
        }

        private void OnGUI()
        {
            EditorTheme.TitleBand("Plantillas de entrenadores", "Todos los entrenadores del pack, con vista previa antes de añadirlos.", EditorTheme.ForCategory(ContentFolders.Trainers));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Fuente: " + PackTools.SourceName + $"  ·  {_rows.Count} entrenadores", EditorStyles.boldLabel);
            ContentHubWindow.DrawPackPicker();
            if (GUILayout.Button("↻", GUILayout.Width(28))) Load();
            EditorGUILayout.EndHorizontal();
            if (_source != PackTools.Folder) Load();
            if (_rows.Count == 0) { EditorGUILayout.HelpBox("La fuente no tiene entrenadores.csv.", MessageType.Warning); return; }

            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawPreview();
            EditorGUILayout.EndHorizontal();
            DrawActions();
        }

        private List<Row> Visible()
        {
            var classes = ClassList();
            return _rows.Where(r =>
                (_levelFilter == 0 || r.Level == _levelFilter) &&
                (_classFilter == 0 || (_classFilter - 1 < classes.Count && r.Class == classes[_classFilter - 1])) &&
                (_search.Length == 0 || (r.Name + " " + r.Id + " " + r.Class).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 r.Team.Any(m => m.Species.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
        }

        private List<string> ClassList() => _rows.Select(r => r.Class).Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList();

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(360));
            _search = EditorGUILayout.TextField("Buscar", _search);
            _levelFilter = EditorGUILayout.Popup("Nivel de IA", _levelFilter,
                new[] { "Todos" }.Concat(Enumerable.Range(1, AiProfile.MaxLevel).Select(AiLevelEditorWindow.LevelLabel)).ToArray());
            _classFilter = EditorGUILayout.Popup("Clase", Mathf.Clamp(_classFilter, 0, ClassList().Count), new[] { "Todas" }.Concat(ClassList()).ToArray());
            var visible = Visible();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Marcar los visibles", EditorStyles.miniButton)) foreach (var r in visible) _picked.Add(r.Id);
            if (GUILayout.Button("Ninguno", EditorStyles.miniButton)) _picked.Clear();
            if (GUILayout.Button(new GUIContent("⭐ 7 sugeridos", "Uno por nivel de IA (1-7): un buen punto de partida."), EditorStyles.miniButton))
            {
                _picked.Clear();
                foreach (var id in Suggested(_rows.Select(r => (r.Id, r.Class, r.Level)))) _picked.Add(id);
                _focus = _picked.FirstOrDefault();
            }
            EditorGUILayout.EndHorizontal();

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            foreach (var r in visible)
            {
                EditorGUILayout.BeginHorizontal();
                bool on = _picked.Contains(r.Id);
                bool now = EditorGUILayout.Toggle(on, GUILayout.Width(18));
                if (now != on) { if (now) _picked.Add(r.Id); else _picked.Remove(r.Id); }
                var old = GUI.backgroundColor;
                GUI.backgroundColor = r.Id == _focus ? AiLevelEditorWindow.LevelColor(Math.Max(1, r.Level)) : old;
                bool exists = ContentAssets.FindById<TrainerData>(r.Id) != null;
                if (GUILayout.Button($"{(r.Level > 0 ? r.Level.ToString() : "·")}  {r.Class} {r.Name}  ({r.Team.Count}){(exists ? "  ✔" : "")}", EditorStyles.miniButtonLeft))
                    _focus = r.Id;
                GUI.backgroundColor = old;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"{_picked.Count} marcados · ✔ = ya está en tu proyecto", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawPreview()
        {
            EditorGUILayout.BeginVertical();
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);
            var r = _rows.FirstOrDefault(x => x.Id == _focus);
            if (r == null) { EditorTheme.Paragraph("Pulsa un entrenador de la lista para ver su vista previa."); EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical(); return; }

            int level = Math.Max(1, r.Level);
            var color = AiLevelEditorWindow.LevelColor(level);
            EditorTheme.Section($"{r.Class} {r.Name}  ·  IA {AiLevelEditorWindow.LevelLabel(level)}", color);
            var existing = ContentAssets.FindById<TrainerData>(r.Id);
            EditorTheme.Tip(existing == null ? "Nuevo: se creará en tu proyecto."
                : _mode == ImportMode.CreateOnly ? "Ya existe en tu proyecto: con «solo los que faltan» no se toca (salvo referencias rotas)."
                : "Ya existe en tu proyecto: se ACTUALIZARÁ con estos datos (el id y las referencias se mantienen).",
                existing == null ? EditorTheme.Ok : EditorTheme.Warn, existing == null ? "✚" : "↻");
            var profile = TrainerEditorWindow.ProfileFor(level);
            EditorTheme.Tip(profile.Description, color, "🧠");
            if (r.TeamError != null) EditorGUILayout.HelpBox("Equipo con formato no válido: " + r.TeamError, MessageType.Error);

            foreach (var m in r.Team)
            {
                string sp = _speciesNames.TryGetValue(m.Species, out var n) ? n : m.Species;
                bool inProject = ContentAssets.FindById<SpeciesData>(m.Species) != null;
                EditorTheme.BeginCard(color, $"{(string.IsNullOrEmpty(m.Nickname) ? sp : $"{m.Nickname} ({sp})")}  ·  Nv. {m.Level}");
                var pills = new List<(string, Color)>();
                pills.Add((m.Held.Length > 0 ? "🎒 " + m.Held : profile.HeldItems != HeldItemStyle.None ? "🎒 automático" : "sin objeto", EditorTheme.Items));
                pills.Add((m.Nature.Length > 0 ? m.Nature : profile.CompetitiveTraining ? "naturaleza automática" : "naturaleza al azar", EditorTheme.Natures));
                if (m.Iv >= 0) pills.Add(($"IV {m.Iv}", EditorTheme.Tools));
                if (m.Gender == "m" || m.Gender == "h") pills.Add((m.Gender == "m" ? "♂" : "♀", EditorTheme.Tools));
                if (!inProject) pills.Add(("se creará la especie", EditorTheme.Warn));
                EditorTheme.Pills(pills.ToArray());
                EditorTheme.Paragraph("Movimientos: " + (m.Moves.Count > 0
                    ? string.Join(", ", m.Moves.Select(x => _moveNames.TryGetValue(x, out var mn) ? mn : x))
                    : "automáticos según su IA (" + Etiquetas.Enum(profile.Moveset.ToString()).ToLowerInvariant() + ")"));
                EditorTheme.EndCard();
            }
            string bag = Get(r.Cells, "mochila");
            EditorTheme.Paragraph("Mochila: " + (bag.Length > 0 ? bag.Replace("|", ", ") : "la de su nivel de IA") +
                                  $"  ·  premio base {Get(r.Cells, "dinero_base")} ₽");
            foreach (var (col, icon) in new[] { ("frase_inicio", "💬"), ("frase_derrota", "🏳"), ("frase_victoria", "🏆") })
                if (Get(r.Cells, col).Length > 0) EditorTheme.Tip($"«{Get(r.Cells, col)}»", color, icon);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();
            int mode = EditorGUILayout.Popup("Si ya existe", _mode == ImportMode.CreateOnly ? 0 : 1,
                new[] { "Solo crear los que faltan (no tocar lo tuyo)", "Crear los nuevos y actualizar los que ya tengo" });
            _mode = mode == 0 ? ImportMode.CreateOnly : ImportMode.CreateAndUpdate;
            GUI.enabled = _picked.Count > 0;
            var old = GUI.backgroundColor;
            if (_picked.Count > 0) GUI.backgroundColor = EditorTheme.Ok;
            if (GUILayout.Button($"✚ Añadir {_picked.Count} entrenador(es) a mi proyecto", GUILayout.Height(30)))
            {
                var species = PackTools.SpeciesForTrainers(_picked).Where(s => ContentAssets.FindById<SpeciesData>(s) == null).ToList();
                if (EditorUtility.DisplayDialog("Añadir entrenadores",
                        $"Se {(_mode == ImportMode.CreateOnly ? "crearán los que falten de" : "crearán o actualizarán")} {_picked.Count} entrenador(es)." +
                        (species.Count > 0 ? $"\n\nAntes se crearán {species.Count} especie(s) que usan sus equipos (con su familia evolutiva) y los movimientos que falten." : "") +
                        "\n\nSe guarda una copia de seguridad en Excel/copias.", "Añadir", "Cancelar"))
                {
                    string report = PackTools.ImportTrainers(_picked.ToList(), _mode);
                    EditorUtility.DisplayDialog("Añadir entrenadores", report ?? "No se pudo leer la fuente.", "Vale");
                    GUIUtility.ExitGUI();
                }
            }
            GUI.backgroundColor = old;
            GUI.enabled = true;
        }
    }
}
