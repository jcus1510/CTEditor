using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;   // Slider, Button
using TMPro;            // TextMeshPro
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Bootstrap.Platform;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// Pantalla de combate JUGABLE (TextMeshPro): el jugador elige movimiento cada turno con botones,
    /// el enemigo elige con la IA del nivel seleccionado, y la línea de tiempo se reproduce en barras
    /// de PS + un log.
    ///
    /// Es PRESENTACIÓN: se suscribe/observa los eventos del dominio y los pinta. "Elegir movimiento"
    /// es el patrón de SUSPENSIÓN (M.2/N): la corutina se DETIENE en WaitUntil hasta tu clic. El
    /// dominio nunca espera; el flujo sí.
    ///
    /// (Por ser demo, también COMPONE. En separación estricta eso sería del Bootstrap; lo juntamos
    /// para que sea fácil de poner en escena.)
    /// </summary>
    public sealed class BattleScreen : MonoBehaviour
    {
        [Header("Contenido — arrastra tus assets")]
        [SerializeField] private MoveData[] moves;
        [SerializeField] private SpeciesData playerSpecies;
        [SerializeField] private SpeciesData enemySpecies;
        [SerializeField] private TypeChartData typeChart;
        [SerializeField] private RulesetData ruleset;
        [SerializeField] private StatusConditionData[] statuses; // estados que el autor definió
        [SerializeField] private int playerLevel = 10;
        [SerializeField] private int enemyLevel = 10;
        [SerializeField] private int seed = 12345;

        [Header("Dificultad")]
        [SerializeField] private AiLevel aiLevel = AiLevel.Hard;

        [Header("UI — arrastra los elementos del Canvas")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text enemyNameText;
        [SerializeField] private Slider playerHpBar;
        [SerializeField] private Slider enemyHpBar;
        [SerializeField] private TMP_Text playerHpText;
        [SerializeField] private TMP_Text enemyHpText;
        [SerializeField] private TMP_Text logText;
        [SerializeField] private Button[] moveButtons; // arrastra tus botones (idealmente 4)
        [SerializeField] private float secondsPerBeat = 0.6f;

        // Estado de runtime
        private CTEditor.Battle.Domain.Battle _battle;
        private TurnResolver _resolver;
        private ICatalog<Move> _moveCatalog;
        private IBattleAI _ai;
        private readonly StringBuilder _log = new StringBuilder();
        private int _selectedMove = -1; // lo fija el clic en un botón

        private void Start()
        {
            if (!Compose()) return;
            StartCoroutine(BattleLoop());
        }

        private bool Compose()
        {
            if (moves == null || moves.Length == 0) { Debug.LogError("[CTEditor] Asigna MoveData en 'moves'."); return false; }
            if (playerSpecies == null || enemySpecies == null) { Debug.LogError("[CTEditor] Asigna playerSpecies y enemySpecies."); return false; }
            if (typeChart == null || ruleset == null) { Debug.LogError("[CTEditor] Asigna typeChart y ruleset."); return false; }

            _moveCatalog = new ScriptableObjectCatalog<MoveData, Move>(moves, d => d.Id, MoveMapper.ToDomain);
            var chart = TypeChartMapper.ToDomain(typeChart);
            var rules = RulesetMapper.ToDomain(ruleset);
            IRng rng = new SystemRng(seed);
            var growth = new ClassicStatGrowthFormula();

            // --- IA según el nivel elegido (mismo seam, distinta implementación) ---
            _ai = aiLevel == AiLevel.Hard
                ? (IBattleAI)new AggressiveBattleAI(rng, _moveCatalog, chart)
                : new SimpleBattleAI(rng);

            var playerDef = SpeciesMapper.ToDomain(playerSpecies);
            var enemyDef = SpeciesMapper.ToDomain(enemySpecies);

            var playerMon = MonsterFactory.Create(new Id<MonsterInstance>("player"), playerDef, playerLevel, rules, growth);
            var enemyMon = MonsterFactory.Create(new Id<MonsterInstance>("enemy"), enemyDef, enemyLevel, rules, growth);

            var playerSnap = new BattleParticipant(new Id<BattleParticipant>("p1"), playerMon.SpeciesId, playerMon.Level.Value,
                playerMon.Stats, playerMon.CurrentHp, playerDef.Types, playerMon.Moves);
            var enemySnap = new BattleParticipant(new Id<BattleParticipant>("p2"), enemyMon.SpeciesId, enemyMon.Level.Value,
                enemyMon.Stats, enemyMon.CurrentHp, enemyDef.Types, enemyMon.Moves);

            // Catálogo de estados (si el autor asignó alguno). Si no, el combate corre sin estados.
            ICatalog<StatusConditionDefinition> statusCatalog =
                (statuses != null && statuses.Length > 0)
                    ? new ScriptableObjectCatalog<StatusConditionData, StatusConditionDefinition>(
                        statuses, d => d.Id, StatusMapper.ToDomain)
                    : null;

            _battle = new CTEditor.Battle.Domain.Battle(playerSnap, enemySnap);
            _resolver = new TurnResolver(_moveCatalog, chart, new ClassicDamageFormula(), rng, statusCatalog);

            if (playerNameText) playerNameText.text = playerDef.DisplayName;
            if (enemyNameText) enemyNameText.text = enemyDef.DisplayName;

            SetupMoveButtons();
            UpdateHud();
            AppendLog($"Aparecio {enemyDef.DisplayName} (Nv.{enemyLevel}). [IA: {aiLevel}]");
            return true;
        }

        // Etiqueta cada botón con un movimiento del jugador y engancha su clic. Los botones de más se
        // ocultan. La copia local 'index' evita el clásico bug de closures en bucles.
        private void SetupMoveButtons()
        {
            if (moveButtons == null) return;
            for (int i = 0; i < moveButtons.Length; i++)
            {
                var button = moveButtons[i];
                if (button == null) continue;

                if (i < _battle.Player.Moves.Count)
                {
                    int index = i;
                    var move = _moveCatalog.Get(_battle.Player.Moves[i]);
                    var label = button.GetComponentInChildren<TMP_Text>();
                    if (label) label.text = move.DisplayName;

                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => _selectedMove = index);
                    button.gameObject.SetActive(true);
                }
                else
                {
                    button.gameObject.SetActive(false);
                }
            }
        }

        private IEnumerator BattleLoop()
        {
            while (!_battle.IsOver)
            {
                // --- SUSPENSIÓN: esperar a que el jugador elija (clic en un botón) ---
                _selectedMove = -1;
                SetButtonsInteractable(true);
                yield return new WaitUntil(() => _selectedMove >= 0);
                SetButtonsInteractable(false);

                var playerAction = new UseMove(_battle.Player.Moves[_selectedMove]);
                var enemyAction = _ai.ChooseAction(_battle.Enemy, _battle.Player);

                // El dominio resuelve el turno al instante; nosotros lo reproducimos con ritmo.
                var events = _resolver.ResolveTurn(_battle, playerAction, enemyAction);
                foreach (var ev in events)
                {
                    Present(ev);
                    UpdateHud();
                    yield return new WaitForSeconds(secondsPerBeat);
                }
            }
            AppendLog(OutcomeText(_battle.Outcome));
        }

        // La "presentación": traduce cada evento a texto del log. Cambiar esto por animaciones es lo
        // único que falta para un combate plenamente visual.
        private void Present(IDomainEvent e)
        {
            switch (e)
            {
                case MoveUsedEvent mu: AppendLog($"{NameOf(mu.Attacker)} uso {_moveCatalog.Get(mu.Move).DisplayName}."); break;
                case MoveMissedEvent mm: AppendLog($"{NameOf(mm.Attacker)} fallo."); break;
                case DamageDealtEvent dd: AppendLog($"{NameOf(dd.Target)} recibio {dd.Amount} de dano.{EffNote(dd.Effectiveness)}"); break;
                case MonsterFaintedEvent mf: AppendLog($"{NameOf(mf.Combatant)} se debilito!"); break;
                case StatusInflictedEvent si: AppendLog($"{NameOf(si.Target)} quedo afectado por {si.Status}!"); break;
                case StatusDamageEvent sd: AppendLog($"{NameOf(sd.Target)} sufrio {sd.Amount} por {sd.Status}."); break;
                case ActionPreventedEvent ap: AppendLog($"{NameOf(ap.Combatant)} no pudo moverse ({ap.Status})."); break;
                case BattleEndedEvent be: AppendLog(OutcomeText(be.Outcome)); break;
            }
        }

        private void UpdateHud()
        {
            SetBar(playerHpBar, playerHpText, _battle.Player.CurrentHp, _battle.Player.MaxHp);
            SetBar(enemyHpBar, enemyHpText, _battle.Enemy.CurrentHp, _battle.Enemy.MaxHp);
        }

        private static void SetBar(Slider bar, TMP_Text text, int current, int max)
        {
            if (bar) bar.value = max > 0 ? (float)current / max : 0f;
            if (text) text.text = $"{current}/{max}";
        }

        private void SetButtonsInteractable(bool value)
        {
            if (moveButtons == null) return;
            foreach (var b in moveButtons)
                if (b) b.interactable = value;
        }

        private void AppendLog(string line)
        {
            _log.AppendLine(line);
            if (logText) logText.text = _log.ToString();
            Debug.Log(line);
        }

        private string NameOf(Id<BattleParticipant> id)
        {
            if (id.Value == "p1") return playerNameText ? playerNameText.text : "Jugador";
            if (id.Value == "p2") return enemyNameText ? enemyNameText.text : "Rival";
            return id.Value;
        }

        private static string EffNote(float eff)
        {
            if (eff <= 0f) return " No tuvo efecto.";
            if (eff > 1f) return " Es muy eficaz!";
            if (eff < 1f) return " No es muy eficaz...";
            return "";
        }

        private static string OutcomeText(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerWon: return "Ganaste el combate!";
                case BattleOutcome.PlayerLost: return "Perdiste el combate...";
                case BattleOutcome.Fled: return "Huiste del combate.";
                default: return "";
            }
        }
    }
}
