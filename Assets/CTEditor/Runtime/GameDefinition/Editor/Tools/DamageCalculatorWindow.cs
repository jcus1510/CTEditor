using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Turn;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// CALCULADORA DE DAÑO (menú CTEditor → Pruebas → Calculadora de daño). Elige atacante, defensor y movimiento
    /// y ve cuánto hace: rango exacto (tirada 85–100%), crítico, % de PS y golpes para debilitar.
    ///
    /// Usa el MOTOR REAL (TurnResolver.PreviewDamage): tipos, STAB, stats, habilidades (Sebo, Mar Llamas,
    /// Adaptable, Levitación...) y la misma fórmula que el combate. Lo que ves es lo que pasa en partida.
    /// </summary>
    public sealed class DamageCalculatorWindow : EditorWindow
    {
        private int _atk, _def, _move;
        private int _atkLevel = 50, _defLevel = 50;
        private Preparation _atkPrep = Preparation.Normal, _defPrep = Preparation.Normal;
        private int _defHpPercent = 100;
        private Vector2 _scroll;

        // Caché del motor (releer todo el contenido en cada repintado sería lento con muchas fichas).
        private BattleSandbox _sandbox;
        private double _sandboxTime;
        private BattleSandbox Sandbox()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_sandbox == null || now - _sandboxTime > 2.0) { _sandbox = BattleSandbox.Load(); _sandboxTime = now; }
            return _sandbox;
        }

        private string _pendingMoveId; // movimiento a preseleccionar (al abrir desde el editor de movimientos)

        /// <summary>Abre la calculadora con un movimiento ya elegido.</summary>
        public static void OpenWithMove(MoveData move)
        {
            Open();
            var w = GetWindow<DamageCalculatorWindow>();
            w._pendingMoveId = move != null ? move.Id : null;
            w.Repaint();
        }

        [MenuItem(EditorMenus.Testing + "Calculadora de daño", false, EditorMenus.TestingOrder + 1)]
        public static void Open()
        {
            var w = GetWindow<DamageCalculatorWindow>();
            w.titleContent = new GUIContent("Calculadora de daño");
            w.minSize = new Vector2(520, 520);
            w.Show();
        }

        private static readonly string[] PrepLabels = { "Sin entrenar (IV 0)", "Normal (IV al máximo)", "Al máximo (IV + EV + naturaleza)" };

        // Preparación del ejemplar, con nombres en español.
        internal static Preparation PrepPopup(Preparation current)
            => (Preparation)EditorGUILayout.Popup(new GUIContent("Preparación",
                "Sin entrenar: IV 0 y sin EVs (el peor caso).\nNormal: IV 31, sin EVs.\nAl máximo: IV 31, 252 EVs y naturaleza favorable en su estadística clave."),
                (int)current, Array.ConvertAll(PrepLabels, s => new GUIContent(s)));

        // Pastillas con los tipos de la especie (con su color).
        private static void TypePills(SpeciesData s)
        {
            if (s?.Types == null) return;
            var pills = new List<(string, Color)>();
            foreach (var ty in s.Types) if (ty != null) pills.Add((ty.DisplayName, ty.Color));
            EditorTheme.Pills(pills.ToArray());
        }

        private void OnGUI()
        {
            EditorTheme.TitleBand("Calculadora de daño",
                "Elige atacante, defensor y movimiento: verás el daño exacto con el motor real del combate.", EditorTheme.Tools, "🧮");
            EditorTheme.Guide("DamageCalculator", new[]
            {
                "Elige la especie, el nivel y la preparación de cada lado.",
                "Elige el movimiento (★ = lo aprende el atacante).",
                "Lee el resultado: rango de daño, crítico, eficacia y cuántos golpes necesita.",
            }, EditorTheme.Tools);
            var species = ContentAssets.LoadAll<SpeciesData>();
            species.Sort((a, b) => string.Compare(ContentAssets.Label(a), ContentAssets.Label(b), StringComparison.OrdinalIgnoreCase));
            var moves = ContentAssets.LoadAll<MoveData>();
            moves.Sort((a, b) => string.Compare(ContentAssets.Label(a), ContentAssets.Label(b), StringComparison.OrdinalIgnoreCase));

            if (species.Count == 0 || moves.Count == 0)
            {
                EditorGUILayout.HelpBox("Necesitas al menos una especie y un movimiento (CTEditor → Centro de Contenido).", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var names = species.ConvertAll(s => ContentAssets.Label(s)).ToArray();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorTheme.BeginCard(EditorTheme.Moves, "⚔ Atacante");
            _atk = Mathf.Clamp(EditorGUILayout.Popup("Especie", _atk, names), 0, species.Count - 1);
            TypePills(species[_atk]);
            _atkLevel = EditorGUILayout.IntSlider("Nivel", _atkLevel, 1, 100);
            _atkPrep = PrepPopup(_atkPrep);
            EditorTheme.EndCard();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            EditorTheme.BeginCard(EditorTheme.Species, "🛡 Defensor");
            _def = Mathf.Clamp(EditorGUILayout.Popup("Especie", _def, names), 0, species.Count - 1);
            TypePills(species[_def]);
            _defLevel = EditorGUILayout.IntSlider("Nivel", _defLevel, 1, 100);
            _defPrep = PrepPopup(_defPrep);
            _defHpPercent = EditorGUILayout.IntSlider("PS actuales (%)", _defHpPercent, 1, 100);
            EditorTheme.EndCard();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("⇄ Intercambiar atacante y defensor"))
            {
                (_atk, _def) = (_def, _atk);
                (_atkLevel, _defLevel) = (_defLevel, _atkLevel);
                (_atkPrep, _defPrep) = (_defPrep, _atkPrep);
            }

            // Movimiento: primero los del learnset del atacante (★), luego el resto.
            var attacker = species[_atk];
            var learn = new HashSet<MoveData>();
            if (attacker.Learnset != null) foreach (var e in attacker.Learnset) if (e.move != null) learn.Add(e.move);
            var ordered = new List<MoveData>();
            foreach (var m in moves) if (learn.Contains(m)) ordered.Add(m);
            foreach (var m in moves) if (!learn.Contains(m)) ordered.Add(m);
            var moveNames = ordered.ConvertAll(m => (learn.Contains(m) ? "★ " : "") + ContentAssets.Label(m)).ToArray();

            if (!string.IsNullOrEmpty(_pendingMoveId))
            {
                int found = ordered.FindIndex(m => m.Id == _pendingMoveId);
                if (found >= 0) _move = found;
                _pendingMoveId = null;
            }

            EditorGUILayout.Space();
            _move = Mathf.Clamp(EditorGUILayout.Popup("Movimiento (★ = lo aprende)", _move, moveNames), 0, ordered.Count - 1);

            EditorGUILayout.Space();
            DrawResult(species[_atk], species[_def], ordered[_move], ordered, learn);

            EditorGUILayout.EndScrollView();
        }

        private void DrawResult(SpeciesData atkData, SpeciesData defData, MoveData moveData, List<MoveData> allMoves, HashSet<MoveData> learnset)
        {
            BattleSandbox sb;
            CTEditor.Battle.Domain.Battle battle;
            TurnResolver resolver;
            try
            {
                sb = Sandbox();
                bool physical = moveData.Category == MoveCategory.Physical;
                var atkKeys = new[] { physical ? StatId.Attack : StatId.SpAttack };
                var defKeys = new[] { StatId.Hp, physical ? StatId.Defense : StatId.SpDefense };

                var a = sb.Participant(atkData, _atkLevel, "atk", _atkPrep, atkKeys);
                var d = sb.Participant(defData, _defLevel, "def", _defPrep, defKeys);
                int hp = Math.Max(1, d.Stats.Of(StatId.Hp) * _defHpPercent / 100);
                d = new BattleParticipant(d.Id, d.SpeciesId, d.Level, d.Stats, hp, d.Types, d.Moves, null, d.Ability, d.BaseExpYield, d.EvYield, null, d.Friendship);

                battle = new CTEditor.Battle.Domain.Battle(a, d);
                resolver = sb.Resolver(new EditorRng(1));
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox("No se pudo preparar el cálculo: " + e.Message + "\nRevisa la validación del contenido.", MessageType.Error);
                return;
            }

            if (!sb.Moves.TryGet(new Id<Move>(moveData.Id), out _))
            {
                EditorGUILayout.HelpBox($"El movimiento '{moveData.Id}' tiene errores y no se puede usar.", MessageType.Error);
                return;
            }

            var p = resolver.PreviewDamage(battle.Player, battle.Enemy, new Id<Move>(moveData.Id));
            // Potencia REAL ahora mismo (fórmula, modificadores con condiciones, habilidades y clima).
            int realPower = sb.Moves.TryGet(new Id<Move>(moveData.Id), out var domMove)
                ? resolver.EffectivePower(battle.Player, battle.Enemy, domMove) : moveData.Power;
            EditorTheme.Section("Resultado", EditorTheme.Moves);
            EditorTheme.Paragraph($"<b>{moveData.DisplayName}</b>: {moveData.Power} de potencia" +
                (realPower != moveData.Power ? $" → {realPower} ahora (fórmula/condiciones/habilidades)" : "") +
                $" · {Category(moveData.Category)}", small: false);

            if (!p.DealsDamage)
            {
                EditorGUILayout.HelpBox("Es un movimiento de estado: no hace daño directo.", MessageType.Info);
            }
            else if (p.Max == 0)
            {
                EditorGUILayout.HelpBox(p.ImmuneByAbility ? "No le afecta: su HABILIDAD lo hace inmune a este tipo." : "No le afecta (×0 por tipo).", MessageType.Warning);
            }
            else
            {
                var (best, worst) = p.UsesToKo();
                string ko = best == worst ? $"{best} uso(s) — seguro" : $"entre {best} y {worst} usos";
                if (best == 1 && worst == 1) ko = "¡de un solo golpe, seguro!";
                else if (best == 1) ko = $"puede debilitar de un golpe (con buena tirada); seguro en {worst}";

                EditorTheme.Chip(Effect(p.Effectiveness) + (p.Stab ? "  ·  mismo tipo (+50%)" : ""), EffectColor(p.Effectiveness));
                EditorGUILayout.HelpBox(
                    $"Daño: {p.Min}–{p.Max} PS  ({p.PercentOfMaxHp(p.Min):0.#}% – {p.PercentOfMaxHp(p.Max):0.#}% de sus PS)\n" +
                    $"Crítico: {p.CritMin}–{p.CritMax} PS  ({p.PercentOfMaxHp(p.CritMin):0.#}% – {p.PercentOfMaxHp(p.CritMax):0.#}%)\n" +
                    (p.MaxHits > 1 ? $"Golpea {p.MinHits}–{p.MaxHits} veces (el daño de arriba es por golpe).\n" : "") +
                    $"Para debilitarlo desde {p.TargetHp}/{p.TargetMaxHp} PS: {ko}\n" +
                    $"Precisión: {(p.AccuracyPercent.HasValue ? p.AccuracyPercent.Value.ToString("0") + "%" : "nunca falla")}",
                    MessageType.Info);
                DrawHpBar(p);
            }

            // Todos los movimientos que aprende el atacante contra este defensor.
            EditorGUILayout.Space();
            EditorTheme.Section("Todos sus movimientos (los que aprende) contra este rival", EditorTheme.Moves);
            var rows = new List<(string name, DamagePreview pv)>();
            foreach (var m in allMoves)
            {
                if (!learnset.Contains(m) || !sb.Moves.TryGet(new Id<Move>(m.Id), out _)) continue;
                rows.Add((m.DisplayName, resolver.PreviewDamage(battle.Player, battle.Enemy, new Id<Move>(m.Id))));
            }
            rows.Sort((x, y) => (y.pv.Max * y.pv.MaxHits).CompareTo(x.pv.Max * x.pv.MaxHits));
            if (rows.Count == 0) EditorGUILayout.LabelField("No aprende movimientos todavía.", EditorStyles.miniLabel);
            foreach (var (name, pv) in rows)
            {
                string text = !pv.DealsDamage ? "estado (sin daño)" :
                              pv.Max == 0 ? "no le afecta" :
                              $"{pv.PercentOfMaxHp(pv.Min):0.#}–{pv.PercentOfMaxHp(pv.Max * pv.MaxHits):0.#}%  {Effect(pv.Effectiveness)}";
                EditorGUILayout.LabelField(new GUIContent(name, name), new GUIContent(text, text));
            }

            foreach (var problem in sb.Problems)
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            EditorTheme.Tip("No incluye cambios de etapa (±1 Ataque…) ni el clima. Para eso, usa el Simulador de combate.", EditorTheme.Tools);
        }

        // Barra de PS: verde = lo que queda seguro; amarillo = franja que depende de la tirada; rojo = daño seguro.
        private static void DrawHpBar(DamagePreview p)
        {
            Rect r = GUILayoutUtility.GetRect(100f, 4000f, 22f, 22f);
            if (Event.current.type != EventType.Repaint) return;
            float W(int hp) => r.width * Mathf.Clamp01(hp / (float)p.TargetMaxHp);
            EditorGUI.DrawRect(r, new Color(0.2f, 0.2f, 0.2f));
            int afterMax = Math.Max(0, p.TargetHp - p.Max * p.MaxHits);
            int afterMin = Math.Max(0, p.TargetHp - p.Min * p.MinHits);
            EditorGUI.DrawRect(new Rect(r.x, r.y, W(afterMax), r.height), new Color(0.35f, 0.8f, 0.4f));
            EditorGUI.DrawRect(new Rect(r.x + W(afterMax), r.y, W(afterMin) - W(afterMax), r.height), new Color(0.95f, 0.8f, 0.3f));
            EditorGUI.DrawRect(new Rect(r.x + W(afterMin), r.y, W(p.TargetHp) - W(afterMin), r.height), new Color(0.9f, 0.35f, 0.3f));
            GUI.Label(new Rect(r.x + 4, r.y + 3, r.width, 16), $"PS tras el golpe: {afterMax}–{afterMin} / {p.TargetMaxHp}", EditorStyles.miniLabel);
        }

        private static string Effect(float m)
            => m <= 0f ? "No le afecta" : m >= 4f ? "¡Súper eficaz! (×4)" : m >= 2f ? "¡Es muy eficaz! (×2)" :
               m <= 0.25f ? "Apenas le afecta (×¼)" : m < 1f ? "No es muy eficaz (×½)" : "Eficacia normal";

        private static Color EffectColor(float m)
            => m <= 0f ? new Color(0.5f, 0.5f, 0.5f) : m >= 2f ? EditorTheme.Ok : m < 1f ? EditorTheme.Warn : EditorTheme.Tools;

        private static string Category(MoveCategory c)
            => c == MoveCategory.Physical ? "Físico" : c == MoveCategory.Special ? "Especial" : "Estado";
    }
}
