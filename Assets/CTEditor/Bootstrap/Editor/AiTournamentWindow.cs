using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.Adventure.Domain;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Bootstrap.Editor
{
    /// <summary>
    /// 🏆 TORNEO DE IAs (menú CTEditor → Pruebas → Torneo de IAs): enfrenta los niveles de IA entre sí con el MISMO
    /// equipo, muchas veces y con el motor real, y enseña una tabla de % de victorias. Así se comprueba con
    /// números que cada nivel es más fuerte que el anterior (y que una IA personalizada hace lo que esperas).
    /// </summary>
    public sealed class AiTournamentWindow : EditorWindow
    {
        private int _trainer;
        private readonly bool[] _levels = { true, true, true, true, true, true, true };
        private readonly Dictionary<string, bool> _custom = new Dictionary<string, bool>();
        private int _games = 40, _seed = 1;
        private bool _ladderOnly = true;
        private Vector2 _scroll;

        private List<(string label, int level, string profileId)> _entrants;
        private DuelResult[,] _table;
        private List<string> _problems = new List<string>();

        [MenuItem(EditorMenus.Testing + "🏆 Torneo de IAs", false, EditorMenus.TestingOrder + 5)]
        public static void Open()
        {
            var w = GetWindow<AiTournamentWindow>();
            w.titleContent = new GUIContent("Torneo de IAs");
            w.minSize = new Vector2(560, 420);
            w.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorTheme.TitleBand("Torneo de IAs", "¿De verdad cada nivel gana al anterior? Compruébalo con cientos de combates reales.", EditorTheme.Tools);
            EditorTheme.Paragraph("Las dos IAs usan el MISMO equipo (el del entrenador que elijas); cada una lo arma según su bloque 🎒 " +
                                  "(movimientos, objetos, entrenamiento). En cada combate una IA es el rival completo (objetos, cambios, predicción) " +
                                  "y la otra juega con su decisión de movimientos; la mitad de los combates se juegan con los lados cambiados.");

            var trainers = ContentAssets.LoadAll<TrainerData>()
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Id) && (t.Team ?? new TeamMemberData[0]).Any(m => m != null && m.SpeciesKey.Length > 0))
                .OrderBy(t => t.DisplayName).ToList();
            if (trainers.Count == 0) { EditorGUILayout.HelpBox("No hay entrenadores con equipo. Importa el pack o crea uno.", MessageType.Warning); EditorGUILayout.EndScrollView(); return; }
            _trainer = EditorGUILayout.Popup("Equipo de", Mathf.Clamp(_trainer, 0, trainers.Count - 1),
                trainers.Select(t => $"{t.TrainerClass} {t.DisplayName} ({t.Id})").ToArray());

            EditorGUILayout.LabelField("Niveles que compiten", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (int l = 1; l <= AiProfile.MaxLevel; l++)
                _levels[l - 1] = GUILayout.Toggle(_levels[l - 1], AiLevelEditorWindow.LevelLabel(l), EditorStyles.miniButton);
            EditorGUILayout.EndHorizontal();
            var customs = ContentAssets.LoadAll<AiLevelData>().Where(a => a.Custom && !string.IsNullOrWhiteSpace(a.Id)).ToList();
            if (customs.Count > 0)
            {
                EditorGUILayout.LabelField("IAs personalizadas", EditorStyles.boldLabel);
                foreach (var c in customs)
                {
                    _custom.TryGetValue(c.Id, out bool on);
                    _custom[c.Id] = EditorGUILayout.ToggleLeft($"{c.DisplayName} ({c.Id}, base nivel {c.Level})", on);
                }
            }

            _ladderOnly = EditorGUILayout.ToggleLeft("Solo la escalera (cada uno contra el siguiente): mucho más rápido", _ladderOnly);
            _games = EditorGUILayout.IntSlider("Combates por duelo", _games, 2, 400);
            _seed = EditorGUILayout.IntField(new GUIContent("Semilla", "Mismo número = mismos combates (resultados repetibles)."), _seed);

            if (GUILayout.Button("▶ Jugar el torneo", GUILayout.Height(30))) Run(trainers[Mathf.Clamp(_trainer, 0, trainers.Count - 1)], customs);

            DrawResults();
            EditorGUILayout.EndScrollView();
        }

        private void Run(TrainerData teamSource, List<AiLevelData> customs)
        {
            ContentLibrary.Reload();
            var data = ContentLibrary.GameData;
            if (data == null) { EditorUtility.DisplayDialog("Torneo de IAs", "Falta la tabla de tipos o las reglas: créalas en el Centro de Contenido.", "Vale"); return; }
            TrainerDefinition team;
            try { team = TrainerMapper.ToDomain(teamSource); }
            catch (System.Exception e) { EditorUtility.DisplayDialog("Torneo de IAs", "No se pudo leer el entrenador: " + e.Message, "Vale"); return; }

            _entrants = new List<(string, int, string)>();
            for (int l = 1; l <= AiProfile.MaxLevel; l++)
                if (_levels[l - 1]) _entrants.Add((AiLevelEditorWindow.LevelLabel(l), l, ""));
            foreach (var c in customs)
                if (_custom.TryGetValue(c.Id, out bool on) && on) _entrants.Add(("✨ " + c.DisplayName, c.Level, c.Id));
            if (_entrants.Count < 2) { EditorUtility.DisplayDialog("Torneo de IAs", "Elige al menos dos IAs.", "Vale"); return; }

            int n = _entrants.Count;
            _table = new DuelResult[n, n];
            _problems = new List<string>();
            var pairs = new List<(int, int)>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (!_ladderOnly || j == i + 1) pairs.Add((i, j));
            try
            {
                for (int p = 0; p < pairs.Count; p++)
                {
                    var (i, j) = pairs[p];
                    var a = _entrants[i];
                    var b = _entrants[j];
                    int done = p;
                    var r = AiTournament.Duel(data, team, (a.level, a.profileId), (b.level, b.profileId), _games, _seed + p,
                        f => EditorUtility.DisplayCancelableProgressBar("Torneo de IAs", $"{a.label} contra {b.label}", (done + f) / pairs.Count));
                    _table[i, j] = r;
                    _table[j, i] = new DuelResult { Wins = r.Losses, Losses = r.Wins, Draws = r.Draws, Turns = r.Turns };
                    _problems.AddRange(r.Problems.Select(x => $"{a.label} vs {b.label}: {x}"));
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private void DrawResults()
        {
            if (_table == null || _entrants == null) return;
            int n = _entrants.Count;
            EditorGUILayout.Space();
            EditorTheme.Section("Resultados (% de victorias de la FILA contra la COLUMNA)", EditorTheme.Tools);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(140));
            for (int j = 0; j < n; j++) GUILayout.Label(_entrants[j].label, EditorStyles.miniBoldLabel, GUILayout.Width(80));
            GUILayout.Label("Total", EditorStyles.miniBoldLabel, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
            for (int i = 0; i < n; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(_entrants[i].label, EditorStyles.boldLabel, GUILayout.Width(140));
                int wins = 0, games = 0;
                for (int j = 0; j < n; j++)
                {
                    var r = _table[i, j];
                    if (i == j || r == null) { GUILayout.Label("—", GUILayout.Width(80)); continue; }
                    var old = GUI.color;
                    GUI.color = r.WinRate >= 0.55 ? EditorTheme.Ok : r.WinRate <= 0.45 ? EditorTheme.Bad : EditorTheme.Warn;
                    GUILayout.Label(new GUIContent($"{r.WinRate * 100:0}%", $"{r.Wins} victorias, {r.Losses} derrotas, {r.Draws} empates · {r.AverageTurns:0.#} turnos de media"), GUILayout.Width(80));
                    GUI.color = old;
                    wins += r.Wins * 2 + r.Draws; games += r.Games * 2;
                }
                GUILayout.Label(games == 0 ? "—" : $"{100.0 * wins / games:0}%", EditorStyles.boldLabel, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }

            // ¿La escalera sube? Cada nivel debería ganar al anterior.
            var inverted = new List<string>();
            for (int i = 0; i + 1 < n; i++)
            {
                var r = _table[i + 1, i];
                if (r != null && _entrants[i + 1].level > _entrants[i].level && r.WinRate < 0.5)
                    inverted.Add($"{_entrants[i + 1].label} solo gana el {r.WinRate * 100:0}% a {_entrants[i].label}");
            }
            if (inverted.Count > 0)
                EditorGUILayout.HelpBox("La escalera NO sube en:\n• " + string.Join("\n• ", inverted) +
                                        "\n\nAjusta esos niveles (Niveles de IA) o juega más combates: con pocos, el azar pesa mucho.", MessageType.Warning);
            else EditorGUILayout.HelpBox("✔ Cada nivel gana al anterior.", MessageType.Info);
            if (_problems.Count > 0)
                EditorGUILayout.HelpBox("Combates que fallaron (cuentan como empate):\n• " + string.Join("\n• ", _problems.Take(8)), MessageType.Warning);
        }
    }
}
