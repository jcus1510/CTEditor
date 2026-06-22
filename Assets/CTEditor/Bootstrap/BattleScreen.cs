using System.Collections;
using System.Collections.Generic;
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
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Rules;
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
    /// Pantalla de combate por turnos estilo clásico, con EQUIPOS (hasta 6v6).
    ///
    /// ESTRUCTURA (máquina de estados por fases):
    ///   COMANDO   -> menú visible (Luchar/Cambiar/Mochila/Capturar/Huir), caja = "¿Qué hará X?"
    ///   RESOLUCIÓN-> menús OCULTOS; la caja de mensajes reproduce una COLA DE BEATS:
    ///                 mensaje (con tecleo + avanzar con tecla/clic, luego se limpia),
    ///                 animación de movimiento (hook por movimiento), bajada PROGRESIVA de barra+número.
    ///   RELEVO    -> si un activo cae con reservas, se pide relevo (rival auto, jugador a mano).
    /// Al vaciarse la cola, vuelve COMANDO. Coincide con el flujo de un RPG por turnos real.
    ///
    /// Sigue siendo PRESENTACIÓN (M.2/N): el dominio resuelve al instante y emite eventos; esta capa
    /// es la ÚNICA que sabe de tiempos, tecleo y animaciones. El dominio nunca ve un frame.
    /// </summary>
    public sealed class BattleScreen : MonoBehaviour
    {
        [Header("Contenido — arrastra tus assets")]
        [SerializeField] private MoveData[] moves;
        [SerializeField] private TypeChartData typeChart;
        [SerializeField] private RulesetData ruleset;
        [SerializeField] private StatusConditionData[] statuses;
        [SerializeField] private AbilityData[] abilities;

        [Header("Equipos (6v6). Si los dejas vacíos, se usan las especies sueltas de abajo como equipo de 1.")]
        [SerializeField] private SpeciesData[] playerTeam;
        [SerializeField] private int[] playerLevels;
        [SerializeField] private SpeciesData[] enemyTeam;
        [SerializeField] private int[] enemyLevels;

        [Header("1v1 (respaldo si los equipos están vacíos)")]
        [SerializeField] private SpeciesData playerSpecies;
        [SerializeField] private SpeciesData enemySpecies;
        [SerializeField] private int playerLevel = 10;
        [SerializeField] private int enemyLevel = 10;
        [SerializeField] private int seed = 12345;

        [Header("Dificultad")]
        [SerializeField] private AiLevel aiLevel = AiLevel.Hard;

        [Header("Objeto y captura (demo simple)")]
        [SerializeField] private int potionCount = 3;
        [SerializeField] private int potionHeal = 50;
        [SerializeField] private bool potionCuresStatus = false;
        [SerializeField] private int ballCount = 5;
        [SerializeField] private float catchBonus = 1.5f;

        [Header("UI — datos de los activos")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text enemyNameText;
        [SerializeField] private Slider playerHpBar;
        [SerializeField] private Slider enemyHpBar;
        [SerializeField] private TMP_Text playerHpText;
        [SerializeField] private TMP_Text enemyHpText;

        [Header("UI — caja de mensajes")]
        [Tooltip("La caja de mensajes del combate (un mensaje a la vez, con tecleo).")]
        [SerializeField] private TMP_Text messageText;
        [Tooltip("Opcional: historial acumulado (si lo asignas, además se va apilando ahí).")]
        [SerializeField] private TMP_Text logText;
        [Tooltip("Botón/zona para AVANZAR el mensaje (clic o toque). Recomendado: un botón transparente sobre la caja.")]
        [SerializeField] private Button advanceButton;
        [Tooltip("Velocidad de tecleo (caracteres por segundo).")]
        [SerializeField] private float charsPerSecond = 40f;
        [Tooltip("Si > 0, los mensajes avanzan solos tras estos segundos (además del clic). 0 = solo manual.")]
        [SerializeField] private float autoAdvanceSeconds = 0f;
        [Tooltip("Permitir avanzar con teclado (Espacio). Desactívalo si usas el nuevo Input System.")]
        [SerializeField] private bool useKeyboardAdvance = true;
        [Tooltip("Segundos que tarda una barra de PS en bajar/subir progresivamente.")]
        [SerializeField] private float hpDrainSeconds = 0.4f;

        [Header("UI — menús (paneles que se muestran/ocultan)")]
        [SerializeField] private GameObject actionPanel;   // Luchar/Cambiar/Mochila/Capturar/Huir
        [SerializeField] private GameObject movePanel;     // lista de movimientos
        [SerializeField] private GameObject switchPanel;   // lista de miembros del equipo
        [SerializeField] private Button fightButton;
        [SerializeField] private Button switchButton;
        [SerializeField] private Button bagButton;
        [SerializeField] private Button catchButton;
        [SerializeField] private Button fleeButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button[] moveButtons;
        [SerializeField] private Button[] switchButtons;

        // --- Estado de runtime ---
        private CTEditor.Battle.Domain.Battle _battle;
        private TurnResolver _resolver;
        private ICatalog<Move> _moveCatalog;
        private IBattleAI _ai;
        private readonly StringBuilder _log = new StringBuilder();
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _moveAnim = new Dictionary<string, float>();

        private BattleAction _pendingAction;
        private Id<BattleParticipant>? _pendingReplacement;
        private bool _choosingReplacement;
        private bool _advance;                 // lo fija el clic/tecla de avanzar
        private int _potions, _balls;
        private int _shownP, _shownE;          // PS mostrados ahora en cada barra (para interpolar)

        private void Start()
        {
            if (!Compose()) return;
            StartCoroutine(BattleLoop());
        }

        private void Update()
        {
            // Avanzar con teclado (legacy Input). Si usas el nuevo Input System, desactiva la casilla.
            if (useKeyboardAdvance && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
                _advance = true;
        }

        private bool Compose()
        {
            if (moves == null || moves.Length == 0) { Debug.LogError("[CTEditor] Asigna MoveData en 'moves'."); return false; }
            if (typeChart == null || ruleset == null) { Debug.LogError("[CTEditor] Asigna typeChart y ruleset."); return false; }

            _moveCatalog = new ScriptableObjectCatalog<MoveData, Move>(moves, d => d.Id, MoveMapper.ToDomain);
            foreach (var d in moves) if (d != null) _moveAnim[d.Id] = d.AnimationSeconds; // timing de presentación por movimiento

            var chart = TypeChartMapper.ToDomain(typeChart);
            var rules = RulesetMapper.ToDomain(ruleset);
            IRng rng = new SystemRng(seed);
            var growth = new ClassicStatGrowthFormula();

            _ai = aiLevel == AiLevel.Hard
                ? (IBattleAI)new AggressiveBattleAI(rng, _moveCatalog, chart)
                : new SimpleBattleAI(rng);

            var playerParts = BuildTeam(playerTeam, playerLevels, playerSpecies, playerLevel, "p", rules, growth);
            var enemyParts = BuildTeam(enemyTeam, enemyLevels, enemySpecies, enemyLevel, "e", rules, growth);
            if (playerParts.Count == 0 || enemyParts.Count == 0)
            {
                Debug.LogError("[CTEditor] Necesitas al menos 1 especie por bando (equipo o respaldo 1v1).");
                return false;
            }

            ICatalog<StatusConditionDefinition> statusCatalog =
                (statuses != null && statuses.Length > 0)
                    ? new ScriptableObjectCatalog<StatusConditionData, StatusConditionDefinition>(statuses, d => d.Id, StatusMapper.ToDomain)
                    : null;
            ICatalog<AbilityDefinition> abilityCatalog =
                (abilities != null && abilities.Length > 0)
                    ? new ScriptableObjectCatalog<AbilityData, AbilityDefinition>(abilities, d => d.Id, AbilityMapper.ToDomain)
                    : null;

            _battle = new CTEditor.Battle.Domain.Battle(playerParts, enemyParts);
            _resolver = new TurnResolver(_moveCatalog, chart, new ClassicDamageFormula(), rng, statusCatalog, abilities: abilityCatalog);

            _potions = potionCount;
            _balls = ballCount;

            WireMenuButtons();
            RefreshActiveVisuals(true);
            RefreshActiveVisuals(false);
            HideMenus();
            return true;
        }

        private List<BattleParticipant> BuildTeam(
            SpeciesData[] team, int[] levels, SpeciesData fallback, int fallbackLevel,
            string prefix, Ruleset rules, IStatGrowthFormula growth)
        {
            var list = new List<BattleParticipant>();
            bool useTeam = team != null && team.Length > 0;
            var src = useTeam ? team : new[] { fallback };

            for (int i = 0; i < src.Length; i++)
            {
                var sp = src[i];
                if (sp == null) continue;

                int lvl = (useTeam && levels != null && i < levels.Length && levels[i] > 0) ? levels[i] : fallbackLevel;
                var def = SpeciesMapper.ToDomain(sp);
                string idv = prefix + (i + 1);

                var mon = MonsterFactory.Create(new Id<MonsterInstance>(idv), def, lvl, rules, growth);
                list.Add(new BattleParticipant(
                    new Id<BattleParticipant>(idv), mon.SpeciesId, mon.Level.Value,
                    mon.Stats, mon.CurrentHp, def.Types, mon.Moves, mon.Status, def.Ability));
                _names[idv] = def.DisplayName;
            }
            return list;
        }

        // ---------- Cableado de menús ----------

        private void WireMenuButtons()
        {
            if (fightButton)  { fightButton.onClick.RemoveAllListeners();  fightButton.onClick.AddListener(OpenMovePanel); }
            if (switchButton) { switchButton.onClick.RemoveAllListeners(); switchButton.onClick.AddListener(() => OpenSwitchPanel(false)); }
            if (bagButton)    { bagButton.onClick.RemoveAllListeners();    bagButton.onClick.AddListener(ChooseBag); }
            if (catchButton)  { catchButton.onClick.RemoveAllListeners();  catchButton.onClick.AddListener(ChooseCatch); }
            if (fleeButton)   { fleeButton.onClick.RemoveAllListeners();   fleeButton.onClick.AddListener(() => _pendingAction = new Flee()); }
            if (backButton)   { backButton.onClick.RemoveAllListeners();   backButton.onClick.AddListener(OpenActionMenu); }
            if (advanceButton){ advanceButton.onClick.RemoveAllListeners(); advanceButton.onClick.AddListener(() => _advance = true); }

            if (moveButtons != null)
                for (int i = 0; i < moveButtons.Length; i++)
                {
                    var b = moveButtons[i]; if (!b) continue;
                    int index = i;
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() => OnMoveButton(index));
                }

            if (switchButtons != null)
                for (int i = 0; i < switchButtons.Length; i++)
                {
                    var b = switchButtons[i]; if (!b) continue;
                    int index = i;
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() => OnSwitchButton(index));
                }
        }

        private void OnMoveButton(int index)
        {
            var active = _battle.Player;
            if (index < 0 || index >= active.Moves.Count) return;
            _pendingAction = new UseMove(active.Moves[index]);
        }

        private void OnSwitchButton(int index)
        {
            var members = _battle.PlayerTeam.Members;
            if (index < 0 || index >= members.Count) return;
            var m = members[index];
            if (m.IsFainted) return;

            if (_choosingReplacement) _pendingReplacement = m.Id;
            else
            {
                if (ReferenceEquals(m, _battle.PlayerTeam.Active)) return;
                _pendingAction = new SwitchMonster(m.Id);
            }
        }

        private void ChooseBag()
        {
            if (_potions <= 0) { SetMessageInstant("No te quedan pociones."); return; }
            _potions--;
            _pendingAction = new UseItemAction(_battle.Player.Id, new BattleItemEffect(potionHeal, potionCuresStatus, false));
        }

        private void ChooseCatch()
        {
            if (_balls <= 0) { SetMessageInstant("No te quedan bolas."); return; }
            _balls--;
            _pendingAction = new Capturar(catchBonus);
        }

        // ---------- Mostrar/ocultar paneles ----------

        private void OpenActionMenu()
        {
            _choosingReplacement = false;
            if (fightButton == null && actionPanel == null) { OpenMovePanel(); return; } // compat. escena vieja
            SetPanel(actionPanel, true);
            SetPanel(movePanel, false);
            SetPanel(switchPanel, false);
            if (bagButton) bagButton.interactable = _potions > 0;
            if (catchButton) catchButton.interactable = _balls > 0;
        }

        private void OpenMovePanel()
        {
            RefreshMoveButtons();
            SetPanel(actionPanel, false);
            SetPanel(movePanel, true);
            SetPanel(switchPanel, false);
            if (backButton) backButton.interactable = true;
        }

        private void OpenSwitchPanel(bool forced)
        {
            _choosingReplacement = forced;
            RefreshSwitchButtons();
            SetPanel(actionPanel, false);
            SetPanel(movePanel, false);
            SetPanel(switchPanel, true);
            if (backButton) backButton.interactable = !forced;
        }

        private void HideMenus()
        {
            SetPanel(actionPanel, false);
            SetPanel(movePanel, false);
            SetPanel(switchPanel, false);
        }

        private static void SetPanel(GameObject go, bool on) { if (go) go.SetActive(on); }

        // Los botones siguen el orden FIJO de los slots del moveset del activo (1..N). No se reordenan.
        private void RefreshMoveButtons()
        {
            if (moveButtons == null) return;
            var active = _battle.Player;
            for (int i = 0; i < moveButtons.Length; i++)
            {
                var b = moveButtons[i]; if (!b) continue;
                if (i < active.Moves.Count)
                {
                    var label = b.GetComponentInChildren<TMP_Text>();
                    if (label) label.text = _moveCatalog.Get(active.Moves[i]).DisplayName;
                    b.gameObject.SetActive(true);
                    b.interactable = true;
                }
                else b.gameObject.SetActive(false);
            }
        }

        private void RefreshSwitchButtons()
        {
            if (switchButtons == null) return;
            var members = _battle.PlayerTeam.Members;
            var active = _battle.PlayerTeam.Active;
            for (int i = 0; i < switchButtons.Length; i++)
            {
                var b = switchButtons[i]; if (!b) continue;
                if (i < members.Count)
                {
                    var m = members[i];
                    var label = b.GetComponentInChildren<TMP_Text>();
                    if (label)
                    {
                        string tag = m.IsFainted ? " (debil.)" : ReferenceEquals(m, active) ? " (en campo)" : "";
                        label.text = $"{NameOf(m.Id)}  {m.CurrentHp}/{m.MaxHp}{tag}";
                    }
                    b.gameObject.SetActive(true);
                    b.interactable = !m.IsFainted && !ReferenceEquals(m, active);
                }
                else b.gameObject.SetActive(false);
            }
        }

        // ---------- Bucle de combate (fases) ----------

        private IEnumerator BattleLoop()
        {
            // Mensaje de apertura + habilidades al entrar.
            yield return Say($"¡Te desafia un combate! [IA: {aiLevel}]");
            foreach (var ev in _resolver.ResolveBattleStart(_battle))
                yield return StartCoroutine(PlayEvent(ev));

            while (!_battle.IsOver)
            {
                // --- FASE COMANDO ---
                _pendingAction = null;
                SetMessageInstant($"¿Que hara {NameOf(_battle.Player.Id)}?");
                OpenActionMenu();
                yield return new WaitUntil(() => _pendingAction != null);
                HideMenus();

                // --- FASE RESOLUCIÓN ---
                var enemyAction = _ai.ChooseAction(_battle.Enemy, _battle.Player);
                foreach (var ev in _resolver.ResolveTurn(_battle, _pendingAction, enemyAction))
                    yield return StartCoroutine(PlayEvent(ev));

                // --- FASE RELEVO ---
                yield return StartCoroutine(HandleReplacements());
            }

            HideMenus();
            yield return Say(OutcomeText(_battle.Outcome));
        }

        private IEnumerator HandleReplacements()
        {
            // Rival: auto-relevo (primer vivo en banca).
            if (!_battle.IsOver && _battle.NeedsReplacement(false))
            {
                var target = FirstReserve(_battle.EnemyTeam);
                if (target.HasValue && _battle.SendReplacement(false, target.Value))
                {
                    RefreshActiveVisuals(false);
                    yield return Say("El rival envia otro monstruo.");
                    foreach (var ev in _resolver.ResolveReplacementEntry(_battle, false))
                        yield return StartCoroutine(PlayEvent(ev));
                }
            }

            // Jugador: relevo manual forzado.
            if (!_battle.IsOver && _battle.NeedsReplacement(true))
            {
                yield return Say("Tu monstruo se debilito. Elige un relevo.");
                _pendingReplacement = null;
                OpenSwitchPanel(true);
                yield return new WaitUntil(() => _pendingReplacement.HasValue);
                HideMenus();

                if (_battle.SendReplacement(true, _pendingReplacement.Value))
                {
                    RefreshActiveVisuals(true);
                    foreach (var ev in _resolver.ResolveReplacementEntry(_battle, true))
                        yield return StartCoroutine(PlayEvent(ev));
                }
            }
        }

        private Id<BattleParticipant>? FirstReserve(BattleTeam team)
        {
            foreach (var m in team.Members)
                if (!m.IsFainted && !ReferenceEquals(m, team.Active)) return m.Id;
            return null;
        }

        // ---------- Reproducción de cada evento como "beats" ----------

        private IEnumerator PlayEvent(IDomainEvent e)
        {
            switch (e)
            {
                case MoveUsedEvent mu:
                    yield return Say($"{NameOf(mu.Attacker)} uso {_moveCatalog.Get(mu.Move).DisplayName}!");
                    yield return PlayMoveAnimation(mu.Move);   // HOOK: aquí enganchas la animación real
                    break;
                case MoveMissedEvent mm: yield return Say($"{NameOf(mm.Attacker)} fallo el ataque."); break;
                case CriticalHitEvent _: yield return Say("¡Un golpe critico!"); break;
                case DamageDealtEvent dd:
                    yield return AnimateBarFor(dd.Target);
                    if (EffNote(dd.Effectiveness) is string note && note.Length > 0) yield return Say(note);
                    break;
                case MonsterFaintedEvent mf:
                    yield return AnimateBarFor(mf.Combatant);
                    yield return Say($"¡{NameOf(mf.Combatant)} se debilito!");
                    break;
                case StatusInflictedEvent si: yield return Say($"¡{NameOf(si.Target)} quedo {si.Status}!"); break;
                case StatusDamageEvent sd:
                    yield return AnimateBarFor(sd.Target);
                    yield return Say($"{NameOf(sd.Target)} sufrio por {sd.Status}.");
                    break;
                case ActionPreventedEvent ap: yield return Say($"{NameOf(ap.Combatant)} no pudo moverse ({ap.Status})."); break;
                case StatusFadedEvent sf: yield return Say($"{NameOf(sf.Combatant)} se recupero de {sf.Status}."); break;
                case HpRestoredEvent hr:
                    yield return AnimateBarFor(hr.Combatant);
                    yield return Say($"{NameOf(hr.Combatant)} recupero PS.");
                    break;
                case RecoilDamageEvent rc:
                    yield return AnimateBarFor(rc.Combatant);
                    yield return Say($"{NameOf(rc.Combatant)} sufrio retroceso.");
                    break;
                case StatStageChangedEvent ss:
                    yield return Say($"{NameOf(ss.Combatant)} {(ss.Delta > 0 ? "subio" : "bajo")} su {ss.Stat.Value}.");
                    break;
                case FlinchedEvent fl: yield return Say($"¡{NameOf(fl.Combatant)} se amedrento!"); break;
                case ChargingStartedEvent cg: yield return Say($"{NameOf(cg.Combatant)} acumula energia..."); break;
                case RechargingEvent rch: yield return Say($"{NameOf(rch.Combatant)} debe recargar."); break;
                case MonsterWithdrawnEvent mw: yield return Say($"¡Vuelve, {NameOf(mw.Combatant)}!"); break;
                case MonsterSentEvent mse:
                    RefreshActiveVisualsFor(mse.Combatant);
                    yield return Say($"¡Adelante, {NameOf(mse.Combatant)}!");
                    break;
                case ReplacementRequiredEvent _: break; // se gestiona en HandleReplacements
                case ItemUsedInBattleEvent iu:
                    yield return AnimateBarFor(iu.Target);
                    yield return Say($"Usaste un objeto en {NameOf(iu.Target)}.");
                    break;
                case MonsterCapturedEvent mc: yield return Say($"¡{NameOf(mc.Target)} fue capturado!"); break;
                case CaptureFailedEvent _: yield return Say("¡Casi! El monstruo se solto."); break;
                case BattleEndedEvent be: yield return Say(OutcomeText(be.Outcome)); break;
            }
        }

        // Espera la "animación" de un movimiento. Por ahora solo es una pausa por movimiento (dato de
        // presentación en MoveData). AQUÍ es donde dispararías la animación real y esperarías su fin.
        private IEnumerator PlayMoveAnimation(Id<Move> move)
        {
            float secs = _moveAnim.TryGetValue(move.Value, out var s) ? s : 0f;
            if (secs > 0f) yield return new WaitForSeconds(secs);
        }

        // ---------- Caja de mensajes con tecleo + avanzar ----------

        private IEnumerator Say(string line)
        {
            if (string.IsNullOrEmpty(line)) yield break;
            _advance = false;

            // 1) Tecleo: revela caracter a caracter; un clic/tecla completa la línea de golpe.
            if (messageText)
            {
                float cps = charsPerSecond <= 0f ? 9999f : charsPerSecond;
                float t = 0f; int shown = 0;
                while (shown < line.Length)
                {
                    if (_advance) { _advance = false; break; }
                    t += Time.deltaTime * cps;
                    shown = Mathf.Min(line.Length, Mathf.FloorToInt(t));
                    messageText.text = line.Substring(0, shown);
                    yield return null;
                }
                messageText.text = line;
            }

            // 2) Esperar a AVANZAR (clic/tecla). Respaldo: auto-avance si está configurado o si no hay
            //    forma manual cableada (para no quedarse colgado).
            bool canManual = advanceButton != null || useKeyboardAdvance;
            float autoWait = autoAdvanceSeconds > 0f ? autoAdvanceSeconds : (canManual ? 0f : 1.2f);
            if (autoWait > 0f)
            {
                float w = 0f;
                while (w < autoWait && !_advance) { w += Time.deltaTime; yield return null; }
            }
            else
            {
                while (!_advance) yield return null;
            }
            _advance = false;

            if (logText) { _log.AppendLine(line); logText.text = _log.ToString(); }
            Debug.Log(line);
        }

        private void SetMessageInstant(string line)
        {
            if (messageText) messageText.text = line;
        }

        // ---------- Barras de PS progresivas ----------

        // Anima la barra del bando cuyo ACTIVO sea 'target'. Si target no es un activo en campo, no hace nada.
        private IEnumerator AnimateBarFor(Id<BattleParticipant> target)
        {
            if (_battle.Player.Id.Equals(target)) { yield return AnimateBar(true); yield break; }
            if (_battle.Enemy.Id.Equals(target)) { yield return AnimateBar(false); yield break; }
        }

        private IEnumerator AnimateBar(bool playerSide)
        {
            var active = playerSide ? _battle.Player : _battle.Enemy;
            var bar = playerSide ? playerHpBar : enemyHpBar;
            var txt = playerSide ? playerHpText : enemyHpText;
            int from = playerSide ? _shownP : _shownE;
            int to = active.CurrentHp;
            int max = active.MaxHp;

            float dur = hpDrainSeconds <= 0f ? 0f : hpDrainSeconds;
            if (dur > 0f && from != to)
            {
                float t = 0f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    int cur = Mathf.RoundToInt(Mathf.Lerp(from, to, t / dur));
                    SetBar(bar, txt, cur, max);
                    yield return null;
                }
            }
            SetBar(bar, txt, to, max);
            if (playerSide) _shownP = to; else _shownE = to;
        }

        // Snapshot inmediato (sin animar) del bando: nombre + barra. Tras un cambio/relevo.
        private void RefreshActiveVisuals(bool playerSide)
        {
            var active = playerSide ? _battle.Player : _battle.Enemy;
            if (playerSide) { if (playerNameText) playerNameText.text = NameOf(active.Id); _shownP = active.CurrentHp; SetBar(playerHpBar, playerHpText, active.CurrentHp, active.MaxHp); }
            else { if (enemyNameText) enemyNameText.text = NameOf(active.Id); _shownE = active.CurrentHp; SetBar(enemyHpBar, enemyHpText, active.CurrentHp, active.MaxHp); }
        }

        private void RefreshActiveVisualsFor(Id<BattleParticipant> sentId)
        {
            if (_battle.PlayerTeam.Find(sentId) != null) RefreshActiveVisuals(true);
            else RefreshActiveVisuals(false);
        }

        private static void SetBar(Slider bar, TMP_Text text, int current, int max)
        {
            if (bar) bar.value = max > 0 ? (float)current / max : 0f;
            if (text) text.text = $"{current}/{max}";
        }

        // ---------- Utilidades ----------

        private string NameOf(Id<BattleParticipant> id)
            => _names.TryGetValue(id.Value, out var n) ? n : id.Value;

        private static string EffNote(float eff)
        {
            if (eff <= 0f) return "No tuvo efecto.";
            if (eff > 1f) return "¡Es muy eficaz!";
            if (eff < 1f) return "No es muy eficaz...";
            return "";
        }

        private static string OutcomeText(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerWon: return "¡Ganaste el combate!";
                case BattleOutcome.PlayerLost: return "Perdiste el combate...";
                case BattleOutcome.Fled: return "Huiste del combate.";
                case BattleOutcome.Caught: return "¡Capturaste al monstruo!";
                default: return "";
            }
        }
    }
}
