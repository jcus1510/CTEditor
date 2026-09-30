using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.AI;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// SIMULADOR DE COMBATE (menú CTEditor → Pruebas → Simulador de combate). Arma dos equipos con tus
    /// especies, elige la IA de cada bando y lanza N combates con el MOTOR REAL. Sirve para EQUILIBRAR
    /// sin abrir escenas: "¿mi inicial de fuego gana demasiado?", "¿esta habilidad está rota?".
    ///
    /// Cada combate usa una semilla distinta pero predecible: misma semilla = mismos resultados, así
    /// puedes comparar un cambio de contenido contra el anterior en igualdad de condiciones.
    /// </summary>
    public sealed class BattleSimulatorWindow : EditorWindow
    {
        private enum AiKind { Aleatoria, Inteligente }

        [Serializable] private sealed class Row { public int species; public int level = 50; }

        private List<Row> _teamA = new List<Row> { new Row() };
        private List<Row> _teamB = new List<Row> { new Row { species = 1 } };
        private Preparation _prepA = Preparation.Normal, _prepB = Preparation.Normal;
        private AiKind _aiA = AiKind.Inteligente, _aiB = AiKind.Inteligente;
        private int _battles = 200, _seed = 1234, _maxTurns = 200;
        private bool _showLog;

        private Summary _summary;
        private Vector2 _scroll, _logScroll;

        private sealed class Summary
        {
            public int WinsA, WinsB, Draws, Battles, Turns, Crits, Misses, Moves;
            public float WinnerHpPercent; public int WinnerHpSamples;
            public readonly Dictionary<string, int> MovesA = new Dictionary<string, int>();
            public readonly Dictionary<string, int> MovesB = new Dictionary<string, int>();
            public string Log;
            public List<string> Problems = new List<string>();
        }

        [MenuItem(EditorMenus.Testing + "Simulador de combate", false, EditorMenus.TestingOrder + 2)]
        public static void Open()
        {
            var w = GetWindow<BattleSimulatorWindow>();
            w.titleContent = new GUIContent("Simulador de combate");
            w.minSize = new Vector2(560, 560);
            w.Show();
        }

        private void OnGUI()
        {
            EditorTheme.TitleBand("Simulador de combate",
                "Arma dos equipos y lanza cientos de combates con el motor real para equilibrar tu juego sin abrir escenas.",
                EditorTheme.Tools, "🎲");
            EditorTheme.Guide("BattleSimulator", new[]
            {
                "Arma el Equipo A y el Equipo B (especie y nivel de cada miembro, hasta 6).",
                "Elige su preparación y la IA que los controla.",
                "Pulsa «Simular»: verás el % de victorias, los movimientos más usados y un combate de ejemplo.",
                "Misma semilla = mismos resultados: úsala para comparar antes y después de un cambio.",
            }, EditorTheme.Tools);
            var species = ContentAssets.LoadAll<SpeciesData>();
            species.Sort((a, b) => string.Compare(ContentAssets.Label(a), ContentAssets.Label(b), StringComparison.OrdinalIgnoreCase));
            if (species.Count == 0)
            {
                EditorGUILayout.HelpBox("Necesitas especies con movimientos (CTEditor → Criaturas → Especies).", MessageType.Info);
                return;
            }
            var names = species.ConvertAll(s => ContentAssets.Label(s)).ToArray();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.BeginHorizontal();
            DrawTeam("🔴 Equipo A", _teamA, ref _prepA, ref _aiA, names, EditorTheme.Moves);
            DrawTeam("🔵 Equipo B", _teamB, ref _prepB, ref _aiB, names, EditorTheme.Weathers);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            _battles = EditorGUILayout.IntSlider("Número de combates", _battles, 1, 2000);
            _maxTurns = EditorGUILayout.IntSlider("Máx. turnos (luego empate)", _maxTurns, 10, 500);
            EditorGUILayout.BeginHorizontal();
            _seed = EditorGUILayout.IntField("Semilla", _seed);
            if (GUILayout.Button("Nueva", GUILayout.Width(60))) _seed = new System.Random().Next(1, 999999);
            EditorGUILayout.EndHorizontal();

            var old = GUI.backgroundColor;
            GUI.backgroundColor = EditorTheme.Ok;
            if (GUILayout.Button("▶ Simular", GUILayout.Height(30))) _summary = Run(species);
            GUI.backgroundColor = old;

            if (_summary != null) DrawSummary();
            EditorGUILayout.EndScrollView();
        }

        private static readonly GUIContent[] AiLabels =
        {
            new GUIContent("Aleatoria", "Elige un movimiento al azar."),
            new GUIContent("Inteligente", "Busca el golpe que más daño hace y evita lo que no afecta."),
        };

        private static void DrawTeam(string title, List<Row> team, ref Preparation prep, ref AiKind ai, string[] names, Color color)
        {
            EditorGUILayout.BeginVertical();
            EditorTheme.BeginCard(color, title);
            for (int i = 0; i < team.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                team[i].species = Mathf.Clamp(EditorGUILayout.Popup(team[i].species, names), 0, names.Length - 1);
                team[i].level = EditorGUILayout.IntField(team[i].level, GUILayout.Width(40));
                team[i].level = Mathf.Clamp(team[i].level, 1, 100);
                if (team.Count > 1 && GUILayout.Button("✕", GUILayout.Width(22))) { team.RemoveAt(i); break; }
                EditorGUILayout.EndHorizontal();
            }
            if (team.Count < 6 && GUILayout.Button("+ Añadir")) team.Add(new Row { species = team[team.Count - 1].species, level = team[team.Count - 1].level });
            prep = DamageCalculatorWindow.PrepPopup(prep);
            ai = (AiKind)EditorGUILayout.Popup(new GUIContent("Inteligencia", "Cómo elige sus acciones este equipo."), (int)ai, AiLabels);
            EditorTheme.EndCard();
            EditorGUILayout.EndVertical();
        }

        // ---------------- Simulación ----------------

        private Summary Run(List<SpeciesData> species)
        {
            var s = new Summary { Battles = _battles };
            var sb = BattleSandbox.Load();
            s.Problems.AddRange(sb.Problems);

            var names = new Dictionary<string, string>();
            string Name(Id<BattleParticipant> id) => names.TryGetValue(id.Value, out var n) ? n : id.Value;
            string MoveName(Id<Move> id) => sb.Moves.TryGet(id, out var m) ? m.DisplayName : id.Value;

            try
            {
                for (int i = 0; i < _battles; i++)
                {
                    var rng = new EditorRng(unchecked(_seed + i * 7919));
                    var teamA = BuildTeam(sb, species, _teamA, _prepA, "a", names);
                    var teamB = BuildTeam(sb, species, _teamB, _prepB, "b", names);
                    if (teamA.Any(p => p.Moves.Count == 0) || teamB.Any(p => p.Moves.Count == 0))
                    {
                        s.Problems.Add("Alguna especie no tiene movimientos (revisa su learnset al nivel elegido): no puede luchar.");
                        s.Battles = i;
                        break;
                    }

                    var battle = new CTEditor.Battle.Domain.Battle(teamA, teamB);
                    var resolver = sb.Resolver(rng);
                    var aiA = MakeAi(_aiA, rng, sb);
                    var aiB = MakeAi(_aiB, rng, sb);
                    var log = i == 0 ? new StringBuilder() : null;

                    void Take(IReadOnlyList<IDomainEvent> events)
                    {
                        foreach (var e in events)
                        {
                            if (e is CriticalHitEvent) s.Crits++;
                            else if (e is MoveMissedEvent) s.Misses++;
                            else if (e is MoveUsedEvent mu)
                            {
                                s.Moves++;
                                var dict = mu.Attacker.Value.StartsWith("a") ? s.MovesA : s.MovesB;
                                string mn = MoveName(mu.Move);
                                dict.TryGetValue(mn, out var c); dict[mn] = c + 1;
                            }
                            if (log != null)
                            {
                                string line = Describe(e, Name, MoveName);
                                if (line != null) log.AppendLine(line);
                            }
                        }
                    }

                    Take(resolver.ResolveBattleStart(battle));
                    int turns = 0;
                    while (!battle.IsOver && turns < _maxTurns)
                    {
                        turns++;
                        log?.AppendLine($"— Turno {turns} —");
                        var actA = aiA.ChooseAction(battle.Player, battle.Enemy);
                        var actB = aiB.ChooseAction(battle.Enemy, battle.Player);
                        Take(resolver.ResolveTurn(battle, actA, actB));
                        if (battle.IsOver) break;

                        foreach (bool playerSide in new[] { true, false })
                        {
                            if (!battle.NeedsReplacement(playerSide)) continue;
                            var team = playerSide ? battle.PlayerTeam : battle.EnemyTeam;
                            var next = team.Members.First(m => m != team.Active && !m.IsFainted);
                            battle.SendReplacement(playerSide, next.Id);
                            Take(resolver.ResolveReplacementEntry(battle, playerSide));
                        }
                    }

                    s.Turns += turns;
                    if (battle.Outcome == BattleOutcome.PlayerWon) { s.WinsA++; AddWinnerHp(s, battle.PlayerTeam); }
                    else if (battle.Outcome == BattleOutcome.PlayerLost) { s.WinsB++; AddWinnerHp(s, battle.EnemyTeam); }
                    else s.Draws++;

                    if (log != null)
                    {
                        log.AppendLine(battle.Outcome == BattleOutcome.PlayerWon ? "RESULTADO: gana el equipo A"
                            : battle.Outcome == BattleOutcome.PlayerLost ? "RESULTADO: gana el equipo B"
                            : $"RESULTADO: empate (se alcanzó el máximo de {_maxTurns} turnos)");
                        s.Log = log.ToString();
                    }
                }
            }
            catch (Exception e)
            {
                s.Problems.Add("La simulación se detuvo: " + e.Message);
            }
            return s;
        }

        private static List<BattleParticipant> BuildTeam(BattleSandbox sb, List<SpeciesData> species, List<Row> rows,
            Preparation prep, string prefix, Dictionary<string, string> names)
        {
            var list = new List<BattleParticipant>();
            for (int i = 0; i < rows.Count; i++)
            {
                var data = species[Mathf.Clamp(rows[i].species, 0, species.Count - 1)];
                // En modo "Maxed", EVs en PS y en su mejor stat de ataque.
                var bestAtk = data.Attack >= data.SpAttack ? StatId.Attack : StatId.SpAttack;
                string id = prefix + (i + 1);
                list.Add(sb.Participant(data, rows[i].level, id, prep, new[] { bestAtk, StatId.Hp }));
                names[id] = $"{prefix.ToUpper()}·{data.DisplayName}";
            }
            return list;
        }

        private static IBattleAI MakeAi(AiKind kind, EditorRng rng, BattleSandbox sb)
            => kind == AiKind.Inteligente ? (IBattleAI)new AggressiveBattleAI(rng, sb.Moves, sb.Chart) : new SimpleBattleAI(rng);

        private static void AddWinnerHp(Summary s, BattleTeam team)
        {
            int hp = 0, max = 0;
            foreach (var m in team.Members) { hp += m.CurrentHp; max += m.MaxHp; }
            if (max > 0) { s.WinnerHpPercent += hp * 100f / max; s.WinnerHpSamples++; }
        }

        // ---------------- Resultados ----------------

        private void DrawSummary()
        {
            var s = _summary;
            EditorGUILayout.Space();
            foreach (var p in s.Problems.Distinct()) EditorGUILayout.HelpBox(p, MessageType.Warning);
            if (s.Battles == 0) return;

            EditorGUILayout.LabelField($"Resultados de {s.Battles} combates", EditorStyles.boldLabel);
            DrawWinBar(s);
            EditorGUILayout.LabelField("Victorias", $"A: {Pct(s.WinsA, s.Battles)}   ·   B: {Pct(s.WinsB, s.Battles)}   ·   Empates: {Pct(s.Draws, s.Battles)}");
            EditorGUILayout.LabelField("Turnos por combate", (s.Turns / (float)s.Battles).ToString("0.0"));
            if (s.WinnerHpSamples > 0)
                EditorGUILayout.LabelField("PS que le quedan al ganador", (s.WinnerHpPercent / s.WinnerHpSamples).ToString("0") + "% (bajo = combate reñido)");
            if (s.Moves > 0)
                EditorGUILayout.LabelField("Críticos / Fallos", $"{Pct(s.Crits, s.Moves)} / {Pct(s.Misses, s.Moves)} de los ataques");

            EditorGUILayout.LabelField("Movimientos más usados", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Equipo A", Top(s.MovesA));
            EditorGUILayout.LabelField("Equipo B", Top(s.MovesB));

            float diff = Math.Abs(s.WinsA - s.WinsB) * 100f / s.Battles;
            EditorGUILayout.HelpBox(diff < 15 ? "Bastante equilibrado." :
                diff < 45 ? "Hay un favorito claro, pero el otro gana a veces." :
                "Muy desequilibrado: un equipo gana casi siempre.", MessageType.Info);

            _showLog = EditorGUILayout.Foldout(_showLog, "Registro del primer combate", true);
            if (_showLog && !string.IsNullOrEmpty(s.Log))
            {
                _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(260));
                EditorGUILayout.SelectableLabel(s.Log, EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawWinBar(Summary s)
        {
            Rect r = GUILayoutUtility.GetRect(100f, 4000f, 20f, 20f);
            if (Event.current.type != EventType.Repaint) return;
            float a = r.width * s.WinsA / s.Battles, d = r.width * s.Draws / s.Battles;
            EditorGUI.DrawRect(r, new Color(0.9f, 0.4f, 0.35f));                                  // B
            EditorGUI.DrawRect(new Rect(r.x, r.y, a, r.height), new Color(0.35f, 0.6f, 0.95f));    // A
            EditorGUI.DrawRect(new Rect(r.x + a, r.y, d, r.height), new Color(0.6f, 0.6f, 0.6f));  // empates
        }

        private static string Pct(int part, int total) => total <= 0 ? "—" : $"{part * 100f / total:0.#}%";

        private static string Top(Dictionary<string, int> d)
            => d.Count == 0 ? "—" : string.Join(", ", d.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} ({kv.Value})"));

        /// <summary>Una línea legible por evento (solo los relevantes para entender el combate).</summary>
        internal static string Describe(IDomainEvent e, Func<Id<BattleParticipant>, string> name, Func<Id<Move>, string> move)
        {
            switch (e)
            {
                case MoveUsedEvent x: return $"{name(x.Attacker)} usa {move(x.Move)}";
                case DamageDealtEvent x: return $"   {name(x.Target)} pierde {x.Amount} PS" + (x.Effectiveness > 1f ? " (¡muy eficaz!)" : x.Effectiveness < 1f ? " (poco eficaz)" : "");
                case CriticalHitEvent _: return "   ¡Golpe crítico!";
                case MoveMissedEvent x: return $"   ¡{name(x.Attacker)} falla!";
                case MoveHadNoEffectEvent x: return $"   No afecta a {name(x.Target)}";
                case ChargingStartedEvent x: return $"   {name(x.Combatant)} se prepara (carga)";
                case RechargingEvent x: return $"   {name(x.Combatant)} tiene que recargar";
                case MoveBlockedEvent x: return $"   {name(x.Target)} se protege";
                case HeldItemActivatedEvent x: return $"   {name(x.Combatant)} usa su objeto {x.ItemId}" + (x.Consumed ? " (se gasta)" : "");
                case EnduredEvent x: return $"   {name(x.Combatant)} aguanta con 1 PS";
                case SwitchPreventedEvent x: return $"   {name(x.Combatant)} no puede escapar ({x.Status})";
                case StatusFailedEvent x: return "   ¡pero falla!";
                case WeatherStartedEvent x: return $"   Clima: {x.WeatherId} ({x.Turns} turnos)";
                case WeatherEndedEvent x: return $"   Termina el clima {x.WeatherId}";
                case WeatherDamageEvent x: return $"   {name(x.Combatant)} pierde {x.Amount} PS por el clima";
                case MonsterFaintedEvent x: return $"   {name(x.Combatant)} se debilita";
                case StatusInflictedEvent x: return $"   {name(x.Target)} sufre {x.Status}";
                case StatusDamageEvent x: return $"   {name(x.Target)} pierde {x.Amount} PS por {x.Status}";
                case ActionPreventedEvent x: return $"   {name(x.Combatant)} no puede moverse ({x.Status})";
                case StatStageChangedEvent x: return $"   {name(x.Combatant)}: {StatLabels.NameOf(x.Stat.Value)} {(x.Delta > 0 ? "+" : "")}{x.Delta}";
                case HpRestoredEvent x: return $"   {name(x.Combatant)} recupera {x.Amount} PS";
                case RecoilDamageEvent x: return $"   {name(x.Combatant)} sufre {x.Amount} PS de retroceso";
                case FlinchedEvent x: return $"   {name(x.Combatant)} retrocede";
                case StatusFadedEvent x: return $"   {name(x.Combatant)} ya no sufre {x.Status}";
                case MonsterSentEvent x: return $"Sale {name(x.Combatant)}";
                case StruggleEvent x: return $"{name(x.Combatant)} no tiene PP: ¡usa Forcejeo!";
                case OutOfPpEvent x: return $"   ¡A {name(x.Combatant)} no le quedan PP de {move(x.Move)}!";
                default: return null;
            }
        }
    }
}
