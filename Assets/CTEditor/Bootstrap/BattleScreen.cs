using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;   // Slider, Button
using TMPro;            // TextMeshPro
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.SharedKernel.Events;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Events;
using CTEditor.Adventure.Domain;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;
using BattleAggregate = CTEditor.Battle.Domain.Battle;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// PANTALLA DE COMBATE. Toda la lógica y las reglas viven en la sesión pura (BattleSession): esta
    /// pantalla solo MUESTRA lo que pasa (mensajes con tecleo, barras de PS que bajan poco a poco) y
    /// PREGUNTA al jugador (menús). Así las reglas se prueban sin Unity y la pantalla se puede
    /// rediseñar sin tocarlas.
    ///
    ///   Menú:  Luchar · Mochila · Equipo · Capturar · Huir
    ///   Si la elección no vale ("no puedes huir de un entrenador"), se explica y NO se pierde el turno.
    ///   Cuando cae tu monstruo eliges quién sale; si quiere aprender un movimiento y no hay hueco,
    ///   eliges cuál olvidar; si va a evolucionar, puedes dejarlo o cancelarlo.
    ///
    /// Se lanza sola al pulsar Play (con el rival del Inspector o el que dejó un NPC en PendingBattle), o
    /// desde código con Begin(petición) — así la usa el Laboratorio de pruebas.
    /// </summary>
    public sealed class BattleScreen : MonoBehaviour
    {
        public enum OpponentMode { Trainer, Zone, Wild, Legacy }

        [Header("Rival (si el combate empieza al pulsar Play)")]
        [Tooltip("Empieza el combate solo al pulsar Play. Desmárcalo si otro componente lo lanza (Laboratorio de pruebas).")]
        [SerializeField] private bool startOnPlay = true;
        [Tooltip("Contra quién: un entrenador, un salvaje de una zona, una especie concreta o la lista antigua.")]
        [SerializeField] private OpponentMode opponentMode = OpponentMode.Trainer;
        [SerializeField] private TrainerData trainer;
        [SerializeField] private EncounterZoneData zone;
        [SerializeField] private SpeciesData wildSpecies;
        [SerializeField, Min(1)] private int wildLevel = 5;
        [Tooltip("Formato antiguo: lista de combatientes + si es salvaje.")]
        [SerializeField] private OpponentSpec opponent;

        [Header("Equipo del jugador")]
        [Tooltip("Normalmente se toma sola de la partida actual (PartyHolder). Asigna una aquí solo para escenas de prueba.")]
        [SerializeField] private PartyHolder partyOverride;
        [Tooltip("Semilla del azar del combate. 0 = usa el de la partida.")]
        [SerializeField] private int seed = 0;

        [Header("Interfaz — datos de los monstruos en combate")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text enemyNameText;
        [SerializeField] private TMP_Text playerLevelText;
        [SerializeField] private TMP_Text enemyLevelText;
        [SerializeField] private Slider playerHpBar;
        [SerializeField] private Slider enemyHpBar;
        [SerializeField] private TMP_Text playerHpText;
        [SerializeField] private TMP_Text enemyHpText;

        [Header("Depuración (solo lectura): se llena al pulsar Play para comprobar que cargó bien")]
        [TextArea(2, 8)] [SerializeField] private string playerDebug;
        [TextArea(2, 8)] [SerializeField] private string enemyDebug;

        [Header("Interfaz — caja de mensajes")]
        [Tooltip("La caja de mensajes del combate (un mensaje a la vez, con tecleo).")]
        [SerializeField] private TMP_Text messageText;
        [Tooltip("Opcional: historial acumulado (si lo asignas, además se va apilando ahí).")]
        [SerializeField] private TMP_Text logText;
        [Tooltip("Botón/zona para AVANZAR el mensaje (clic o toque). Recomendado: un botón transparente sobre la caja.")]
        [SerializeField] private Button advanceButton;
        [Tooltip("Usar la velocidad de texto de los Ajustes de interfaz (editor de Controles y caja de texto). Si lo desmarcas, usa la de abajo.")]
        [SerializeField] private bool useInterfaceTextSpeed = true;
        [Tooltip("Velocidad de tecleo (caracteres por segundo).")]
        [SerializeField] private float charsPerSecond = 40f;
        [Tooltip("Si > 0, los mensajes avanzan solos tras estos segundos (además del clic). 0 = solo manual.")]
        [SerializeField] private float autoAdvanceSeconds = 0f;
        [Tooltip("Segundos que tarda una barra de PS en bajar/subir progresivamente.")]
        [SerializeField] private float hpDrainSeconds = 0.4f;

        [Header("Interfaz — menús (paneles que se muestran/ocultan)")]
        [SerializeField] private GameObject actionPanel;   // Luchar / Mochila / Equipo / Capturar / Huir
        [SerializeField] private GameObject movePanel;     // movimientos
        [SerializeField] private GameObject switchPanel;   // miembros del equipo
        [SerializeField] private GameObject bagPanel;      // objetos de la mochila
        [SerializeField] private GameObject confirmPanel;  // Sí / No
        [SerializeField] private Button fightButton;
        [SerializeField] private Button switchButton;
        [SerializeField] private Button bagButton;
        [SerializeField] private Button catchButton;
        [SerializeField] private Button fleeButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button[] moveButtons;
        [SerializeField] private Button[] switchButtons;
        [SerializeField] private Button[] bagButtons;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;

        /// <summary>Se llama al terminar el combate (con el desenlace), cuando ya se aplicó todo a la partida.</summary>
        public event Action<BattleOutcome> Finished;

        /// <summary>¿Hay un combate en marcha?</summary>
        public bool IsRunning { get; private set; }

        /// <summary>La sesión en curso (null si no hay combate).</summary>
        public BattleSession Session { get; private set; }

        // --- Estado de la interfaz ---
        private enum Mode { None, Action, Moves, Switch, ItemTarget, Bag, Replacement, LearnMove, Confirm }
        private Mode _mode;
        private PlayerChoice _choice;        // lo elegido en el menú de acción
        private int? _index;                 // lo elegido en listas (relevo, movimiento a olvidar)
        private bool? _yesNo;                // lo elegido en Sí/No
        private string _itemForTarget;       // objeto esperando a que elijas a quién dárselo
        private readonly List<ItemDefinition> _bagShown = new List<ItemDefinition>();
        private bool _advance;
        private bool _endedByForce;          // un Rugido terminó el combate salvaje (no "escapaste")
        private bool _itemNamed;             // el objeto ya se nombró (BagItemUsedEvent): no repetir la frase genérica
        private int _shownP, _shownE;
        private readonly StringBuilder _log = new StringBuilder();
        private PartyHolder _party;

        private BattleAggregate B => Session.Battle;

        // ---------------- Arranque ----------------

        private void Start()
        {
            WireButtons();
            SetupKeyboard();
            TextSafety.FixTree(transform);   // quita los símbolos que la fuente no tiene
            HideMenus();
            if (!startOnPlay) return;
            var request = PendingBattle.Request ?? RequestFromInspector();
            PendingBattle.Clear(); // consumido: que no se filtre al siguiente combate
            if (request == null) { Debug.LogError("[CTEditor] No hay rival: elige uno en «Rival» del Inspector de la pantalla de combate."); return; }
            Begin(request);
        }

        private BattleRequest RequestFromInspector()
        {
            var legacy = opponent != null && opponent.Members != null && opponent.Members.Any(m => m != null && m.Species != null)
                ? BattleRequest.FromSpec(opponent) : null;
            switch (opponentMode)
            {
                // Si el modo elegido no tiene datos pero la lista antigua sí (escenas de antes), se usa la antigua.
                case OpponentMode.Trainer: return trainer != null ? BattleRequest.AgainstTrainer(TrainerMapper.ToDomain(trainer)) : legacy;
                case OpponentMode.Zone: return zone != null ? BattleRequest.InZone(TrainerMapper.ToDomain(zone)) : legacy;
                case OpponentMode.Wild: return wildSpecies != null ? BattleRequest.WildOf(new Id<SpeciesDef>(wildSpecies.Id), wildLevel) : legacy;
                default: return legacy;
            }
        }

        /// <summary>
        /// Lanza un combate con la partida actual. Devuelve false (y lo explica en la consola y en la caja
        /// de mensajes) si no se puede: sin contenido, sin monstruos en pie, rival vacío...
        /// </summary>
        public bool Begin(BattleRequest request)
        {
            if (IsRunning) return false;
            WireButtons();
            _party = partyOverride != null ? partyOverride : PartyHolder.Current;
#if UNITY_2022_2_OR_NEWER
            if (_party == null) _party = FindFirstObjectByType<PartyHolder>();
#else
            if (_party == null) _party = FindObjectOfType<PartyHolder>();
#endif
            if (_party == null) return Fail("No hay partida del jugador (PartyHolder) en la escena.");
            var data = _party.Data;
            if (data == null) return Fail("Falta contenido: tabla de tipos y reglas del juego (Centro de Contenido).");
            var save = _party.Save;
            if (!save.CanBattle) return Fail("No tienes ningún monstruo en condiciones de luchar. ¡Ve al Centro!");

            var rng = seed != 0 ? new CTEditor.Bootstrap.Platform.SystemRng(seed) : _party.Rng;
            try
            {
                Session = CreateSession(request, data, save, rng);
            }
            catch (Exception e)
            {
                return Fail(e.Message);
            }
            if (Session == null) return Fail("El rival no tiene ningún monstruo válido.");
            foreach (var p in Session.Problems) Debug.LogWarning("[CTEditor] " + p);

            IsRunning = true;
            _endedByForce = false;
            _log.Clear();
            _hpShown.Clear();
            SnapshotHp();
            _onScreenP = B.Player.Id;
            _onScreenE = B.Enemy.Id;
            RefreshActiveVisuals(true);
            RefreshActiveVisuals(false);
            StartCoroutine(Loop());
            return true;
        }

        private static BattleSession CreateSession(BattleRequest r, GameData data, PlayerSave save, CTEditor.SharedKernel.Abstractions.IRng rng)
        {
            if (r.Trainer != null) return BattleSession.Against(data, save, r.Trainer, rng);
            MonsterInstance wild = null;
            var problems = new List<string>();
            if (r.Zone != null) wild = TeamBuilder.Wild(r.Zone, save.NewMonsterId(), data, rng, problems);
            else if (r.WildSpecies.HasValue) wild = TeamBuilder.Build(r.WildSpecies.Value, r.WildLevel, save.NewMonsterId(), data, rng, problems);
            else if (r.Legacy != null) return FromLegacy(r.Legacy, data, save, rng);
            foreach (var p in problems) Debug.LogWarning("[CTEditor] " + p);
            return wild != null ? BattleSession.Wild(data, save, wild, rng) : null;
        }

        // Formato antiguo: la lista del Inspector se convierte en un entrenador improvisado (o un salvaje).
        private static BattleSession FromLegacy(OpponentSpec spec, GameData data, PlayerSave save, CTEditor.SharedKernel.Abstractions.IRng rng)
        {
            var members = new List<CTEditor.GameDefinition.Domain.Trainers.TeamMemberSpec>();
            foreach (var b in spec.Members)
            {
                if (b == null || b.Species == null) continue;
                members.Add(new CTEditor.GameDefinition.Domain.Trainers.TeamMemberSpec(new Id<SpeciesDef>(b.Species.Id), b.Level, b.MoveIds(),
                    b.HeldItem, null, b.FixedIvs));
            }
            if (members.Count == 0) return null;
            if (!spec.IsWild)
                return BattleSession.Against(data, save, new CTEditor.GameDefinition.Domain.Trainers.TrainerDefinition("rival", "Rival", members), rng);
            var wild = TeamBuilder.Build(members[0], save.NewMonsterId(), data, rng);
            return wild != null ? BattleSession.Wild(data, save, wild, rng) : null;
        }

        private bool Fail(string why)
        {
            Debug.LogError("[CTEditor] " + why);
            SetMessageInstant(why);
            return false;
        }

        // ---------------- Bucle del combate ----------------

        private IEnumerator Loop()
        {
            SnapshotHp();
            yield return Play(Session.Begin());

            while (Session.Phase != SessionPhase.Finished)
            {
                switch (Session.Phase)
                {
                    case SessionPhase.ChooseAction:
                    {
                        if (Session.PlayerIsLocked)   // cargando, recargando, Saña o Venganza: no hay menú
                        {
                            SnapshotHp();
                            yield return Play(Session.Submit(PlayerChoice.Fight(0)).Events);
                            break;
                        }
                        _choice = null;
                        SetMessageInstant($"¿Qué hará {Session.NameOf(B.Player.Id)}?");
                        OpenActionMenu();
                        yield return new WaitUntil(() => _choice != null);
                        HideMenus();
                        SnapshotHp();
                        var step = Session.Submit(_choice);
                        if (!step.Accepted) { yield return Say(step.Message); break; }
                        yield return Play(step.Events);
                        break;
                    }

                    case SessionPhase.ChooseReplacement:
                    {
                        _index = null;
                        SetMessageInstant(Session.IsSelfSwitchPending ? "¿Quién entra en su lugar?" : "¿Quién sale ahora?");
                        OpenSwitchPanel(Mode.Replacement);
                        yield return new WaitUntil(() => _index.HasValue);
                        HideMenus();
                        SnapshotHp();
                        var step = Session.ChooseReplacement(_index.Value);
                        if (!step.Accepted) { yield return Say(step.Message); break; }
                        yield return Play(step.Events);
                        break;
                    }

                    case SessionPhase.LearnMove:
                    {
                        var (who, move) = Session.PendingLearn.Value;
                        string mon = Session.NameOf(who), mv = Session.Data.MoveName(move);
                        yield return Say($"{mon} quiere aprender {mv}.");
                        yield return Say($"Pero {mon} ya conoce {Session.MonsterOf(who).Moves.Count} movimientos. ¿Olvida uno para aprender {mv}?");
                        _index = null;
                        SetMessageInstant($"¿Qué movimiento olvida? (Atrás = no aprender {mv})");
                        OpenLearnPanel(Session.MonsterOf(who));
                        yield return new WaitUntil(() => _index.HasValue);
                        HideMenus();
                        SnapshotHp();
                        yield return Play(Session.AnswerLearnMove(_index.Value).Events);
                        break;
                    }

                    case SessionPhase.Evolution:
                    {
                        var (mon, target) = Session.PendingEvolution.Value;
                        string name = Session.Data.NameOf(mon);
                        yield return Say($"¿Qué? ¡{name} está evolucionando!");
                        bool accept = true;
                        if (confirmPanel != null && yesButton != null && noButton != null)
                        {
                            _yesNo = null;
                            SetMessageInstant($"¿Dejar que {name} evolucione a {target.DisplayName}?");
                            OpenConfirm();
                            yield return new WaitUntil(() => _yesNo.HasValue);
                            HideMenus();
                            accept = _yesNo.Value;
                        }
                        SnapshotHp();
                        yield return Play(Session.AnswerEvolution(accept).Events);
                        break;
                    }
                }
                UpdateDebug();
            }

            HideMenus();
            IsRunning = false;
            var outcome = Session.Outcome;
            Finished?.Invoke(outcome);
        }

        private IEnumerator Play(IReadOnlyList<IDomainEvent> events)
        {
            foreach (var e in events)
            {
                ApplyHpDelta(e);
                yield return StartCoroutine(PlayEvent(e));
            }
            // Si algo cambió los PS sin evento propio (un objeto equipado...), las barras se ponen al día al final.
            yield return SyncBars();
            SnapshotHp();
        }

        // ---------------- Barras de PS paso a paso ----------------
        // El turno se resuelve entero de golpe (reglas puras) y luego se NARRA. Para que las barras bajen en
        // ORDEN (primero el golpe del más rápido, luego el otro, luego quemaduras...), se guarda cuántos PS se
        // VEN de cada monstruo al empezar el turno y cada evento resta o suma lo suyo. Al debilitarse, la barra
        // llega a 0 antes del mensaje.
        private readonly Dictionary<Id<BattleParticipant>, int> _hpShown = new Dictionary<Id<BattleParticipant>, int>();

        // QUIÉN SE VE en cada lado. El turno ya está resuelto: B.Enemy puede ser el SIGUIENTE monstruo del rival
        // aunque aún se esté narrando el golpe que debilitó al anterior. Por eso las barras siguen al que está
        // en pantalla (cambia con «¡Sale X!»), no al activo final.
        private Id<BattleParticipant>? _onScreenP, _onScreenE;

        private Combatant Find(Id<BattleParticipant> id)
            => B.PlayerTeam.Find(id) ?? B.EnemyTeam.Find(id);

        private Combatant OnScreen(bool playerSide)
        {
            var id = playerSide ? _onScreenP : _onScreenE;
            var c = id.HasValue ? Find(id.Value) : null;
            return c ?? (playerSide ? B.Player : B.Enemy);
        }

        private void PutOnScreen(Id<BattleParticipant> id, bool playerSide)
        {
            if (playerSide) _onScreenP = id; else _onScreenE = id;
            RefreshActiveVisuals(playerSide);
        }

        private void SnapshotHp()
        {
            if (Session == null || Session.Battle == null) return;
            foreach (var c in B.PlayerTeam.Members) _hpShown[c.Id] = c.CurrentHp;
            foreach (var c in B.EnemyTeam.Members) _hpShown[c.Id] = c.CurrentHp;
        }

        private int MaxHpOf(Id<BattleParticipant> id)
        {
            foreach (var c in B.PlayerTeam.Members) if (c.Id.Equals(id)) return c.MaxHp;
            foreach (var c in B.EnemyTeam.Members) if (c.Id.Equals(id)) return c.MaxHp;
            return 1;
        }

        private void ChangeShown(Id<BattleParticipant> id, int delta)
        {
            if (!_hpShown.TryGetValue(id, out int hp)) return;
            _hpShown[id] = Mathf.Clamp(hp + delta, 0, MaxHpOf(id));
        }

        private void ApplyHpDelta(IDomainEvent e)
        {
            switch (e)
            {
                case DamageDealtEvent dd: ChangeShown(dd.Target, -dd.Amount); break;
                case StatusDamageEvent sd: ChangeShown(sd.Target, -sd.Amount); break;
                case RecoilDamageEvent rc: ChangeShown(rc.Combatant, -rc.Amount); break;
                case WeatherDamageEvent wd: ChangeShown(wd.Combatant, -wd.Amount); break;
                case HazardDamageEvent hd: ChangeShown(hd.Combatant, -hd.Amount); break;
                case SubstituteCreatedEvent sc: ChangeShown(sc.Combatant, -sc.Cost); break;
                case HpRestoredEvent hr: ChangeShown(hr.Combatant, hr.Amount); break;
                case MonsterFaintedEvent mf: if (_hpShown.ContainsKey(mf.Combatant)) _hpShown[mf.Combatant] = 0; break;
                // Al subir de nivel también suben sus PS actuales (lo que gana de PS máximos).
                case HpChangedEvent hc: ChangeShown(hc.Combatant, hc.Delta); break;
                case LevelUpEvent lu: if (lu.StatGains != null && lu.StatGains.TryGetValue(CTEditor.GameDefinition.Domain.Stats.StatId.Hp, out int gain)) ChangeShown(lu.Combatant, gain); break;
            }
        }

        private IEnumerator SyncBars()
        {
            if (Session == null) yield break;
            // Al acabar de narrar, en pantalla debe estar el activo real (y con sus PS reales).
            if (!_onScreenP.Equals(B.Player.Id)) PutOnScreen(B.Player.Id, true);
            if (!_onScreenE.Equals(B.Enemy.Id)) PutOnScreen(B.Enemy.Id, false);
            if (_shownP != B.Player.CurrentHp) { _hpShown[B.Player.Id] = B.Player.CurrentHp; yield return AnimateBar(true); }
            if (_shownE != B.Enemy.CurrentHp) { _hpShown[B.Enemy.Id] = B.Enemy.CurrentHp; yield return AnimateBar(false); }
        }

        // ---------------- Teclado y menú de combate del editor ----------------

        private string _fleeLabel = "Huir";

        // Flechas + Confirmar/Cancelar en todos los paneles (sin tocar la escena), y el menú «combate»
        // del editor de Menús si el autor lo creó: textos, qué botones salen y en qué ORDEN.
        private void SetupKeyboard()
        {
            var nav = GetComponent<KeyboardNavigator>();
            if (nav == null) nav = gameObject.AddComponent<KeyboardNavigator>();
            nav.Configure(new[] { backButton, noButton }, new[] { advanceButton });
            ApplyBattleMenu();
        }

        private void ApplyBattleMenu()
        {
            if (!UiContent.HasAuthored(CTEditor.Adventure.Domain.Interface.ClassicInterface.BattleMenu)) return;
            var menu = UiContent.Menu(CTEditor.Adventure.Domain.Interface.ClassicInterface.BattleMenu);
            var byTarget = new Dictionary<string, Button> { ["fight"] = fightButton, ["bag"] = bagButton, ["switch"] = switchButton, ["run"] = fleeButton, ["catch"] = catchButton };
            var all = byTarget.Values.Where(b => b != null).Distinct().ToList();
            // Huecos: las posiciones originales en orden de lectura (arriba→abajo, izquierda→derecha).
            var slots = all.Select(b => (RectTransform)b.transform)
                .OrderByDescending(r => Mathf.Round(r.position.y)).ThenBy(r => r.position.x)
                .Select(r => (pos: r.anchoredPosition, size: r.sizeDelta)).ToList();
            var used = new List<Button>();
            foreach (var o in menu.Options)
            {
                if (!byTarget.TryGetValue(o.Target ?? "", out var b) || b == null || used.Contains(b)) continue;
                string label = CTEditor.Adventure.Domain.Interface.TextTokens.Replace(o.Label, UiContent.Variables(PartyHolder.Current != null ? PartyHolder.Current.Save : null));
                used.Add(b);
                SetButtonLabel(b, label);
                if (b == fleeButton) _fleeLabel = label;
            }
            for (int i = 0; i < used.Count && i < slots.Count; i++)
            {
                var rt = (RectTransform)used[i].transform;
                rt.anchoredPosition = slots[i].pos;
                rt.sizeDelta = slots[i].size;
            }
            foreach (var b in all) b.gameObject.SetActive(used.Contains(b));
        }

        private void Update()
        {
            // Avanzar el texto con Confirmar (o Cancelar, según los Ajustes) cuando no hay un menú abierto.
            if (!IsRunning || _mode != Mode.None) return;
            if (GameInput.AdvancePressed())
            {
                GameInput.Consume(CTEditor.Adventure.Domain.Interface.GameButton.Confirm);
                GameInput.Consume(CTEditor.Adventure.Domain.Interface.GameButton.Cancel);
                _advance = true;
            }
        }

        // ---------------- Menús ----------------

        private void WireButtons()
        {
            Wire(fightButton, OpenMovePanel);
            Wire(switchButton, () => OpenSwitchPanel(Mode.Switch));
            Wire(bagButton, OpenBagPanel);
            Wire(catchButton, QuickBall);
            Wire(fleeButton, () => _choice = PlayerChoice.Run());
            Wire(backButton, OnBack);
            Wire(advanceButton, () => _advance = true);
            Wire(yesButton, () => _yesNo = true);
            Wire(noButton, () => _yesNo = false);
            if (moveButtons != null) for (int i = 0; i < moveButtons.Length; i++) { int k = i; Wire(moveButtons[i], () => OnMoveButton(k)); }
            if (switchButtons != null) for (int i = 0; i < switchButtons.Length; i++) { int k = i; Wire(switchButtons[i], () => OnSwitchButton(k)); }
            if (bagButtons != null) for (int i = 0; i < bagButtons.Length; i++) { int k = i; Wire(bagButtons[i], () => OnBagButton(k)); }
        }

        private static void Wire(Button b, UnityEngine.Events.UnityAction action)
        {
            if (!b) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(action);
        }

        private void OnBack()
        {
            switch (_mode)
            {
                case Mode.LearnMove: _index = -1; break;           // no aprender
                case Mode.Replacement: break;                      // obligatorio: no se puede volver
                case Mode.ItemTarget: OpenBagPanel(); break;
                default: OpenActionMenu(); break;
            }
        }

        private void OnMoveButton(int i)
        {
            if (_mode == Mode.LearnMove) { _index = i; return; }
            _choice = PlayerChoice.Fight(i);
        }

        private void OnSwitchButton(int i)
        {
            switch (_mode)
            {
                case Mode.Replacement: _index = i; break;
                case Mode.ItemTarget: _choice = PlayerChoice.UseItem(_itemForTarget, i); break;
                default: _choice = PlayerChoice.Switch(i); break;
            }
        }

        private void OnBagButton(int i)
        {
            if (i < 0 || i >= _bagShown.Count) return;
            var item = _bagShown[i];
            if (item.IsBall) { _choice = PlayerChoice.ThrowBall(item.Id); return; }
            // Objetos de combate (Ataque X): siempre al que está en el campo.
            if (!string.IsNullOrEmpty(item.BattleStatId) && item.BattleStages != 0 && !item.HealsHp && !item.CuresStatus && !item.Revives)
            {
                _choice = PlayerChoice.UseItem(item.Id, IndexOfActive());
                return;
            }
            _itemForTarget = item.Id;
            SetMessageInstant($"¿En quién usas {item.DisplayName}?");
            OpenSwitchPanel(Mode.ItemTarget);
        }

        // "Capturar": lanza la bola más sencilla que tengas (para elegir otra, usa la Mochila).
        private void QuickBall()
        {
            var balls = Session.Save.Bag.Contents()
                .Select(c => Session.Data.TryGetItem(c.itemId, out var it) ? it : null)
                .Where(it => it != null && it.IsBall && Session.Save.Bag.Has(it.Id))
                .OrderBy(it => it.CatchMultiplier).ToList();
            if (balls.Count == 0) { SetMessageInstant("No te quedan bolas."); return; }
            _choice = PlayerChoice.ThrowBall(balls[0].Id);
        }

        private int IndexOfActive()
        {
            var members = B.PlayerTeam.Members;
            for (int i = 0; i < members.Count; i++) if (ReferenceEquals(members[i], B.Player)) return i;
            return 0;
        }

        private void OpenActionMenu()
        {
            _mode = Mode.Action;
            if (actionPanel == null && fightButton == null) { OpenMovePanel(); return; }
            ShowOnly(actionPanel);
            bool wild = Session.Kind == BattleKind.Wild;
            if (catchButton) catchButton.interactable = wild || Session.Resolver.Rules.CanCatchTrainerMonsters;
            if (bagButton) bagButton.interactable = Session.Save.Bag.Contents().Any();
            SetButtonLabel(fleeButton, wild ? _fleeLabel : _fleeLabel + " ×");
        }

        private void OpenMovePanel()
        {
            _mode = Mode.Moves;
            ShowOnly(movePanel);
            var active = B.Player;
            // ¿Queda alguno elegible? Si todos están bloqueados (Mofa, Tormento, sin PP...), se deja pulsar
            // cualquiera y el motor lo convierte en Forcejeo.
            bool anyUsable = false;
            for (int i = 0; i < active.Moves.Count; i++) if (Session.Resolver.CanChooseMove(active, i)) { anyUsable = true; break; }
            for (int i = 0; moveButtons != null && i < moveButtons.Length; i++)
            {
                var b = moveButtons[i]; if (!b) continue;
                if (i >= active.Moves.Count) { b.gameObject.SetActive(false); continue; }
                b.gameObject.SetActive(true);
                var move = Session.Data.Moves.TryGet(active.Moves[i], out var m) ? m : null;
                var (pp, max) = Session.PpOf(i);
                string type = move != null ? Session.Data.TypeName(move.Type) : "";
                SetButtonLabel(b, Session.Resolver.UsesPp ? $"{Session.Data.MoveName(active.Moves[i])}\n<size=70%>{type} · PP {pp}/{max}</size>"
                                                          : Session.Data.MoveName(active.Moves[i]));
                string blockedBy = Session.Resolver.RestrictionFor(active, active.Moves[i]);
                if (blockedBy != null)
                    SetButtonLabel(b, $"{Session.Data.MoveName(active.Moves[i])}\n<size=70%><color=#c0392b>bloqueado</color></size>");
                b.interactable = anyUsable ? Session.Resolver.CanChooseMove(active, i)
                                           : (!Session.Resolver.UsesPp || pp > 0 || !active.HasAnyPp);
            }
            if (backButton) { backButton.gameObject.SetActive(true); backButton.interactable = true; SetButtonLabel(backButton, "Atrás"); }
        }

        private void OpenLearnPanel(MonsterInstance mon)
        {
            _mode = Mode.LearnMove;
            ShowOnly(movePanel);
            for (int i = 0; moveButtons != null && i < moveButtons.Length; i++)
            {
                var b = moveButtons[i]; if (!b) continue;
                if (i >= mon.Moves.Count) { b.gameObject.SetActive(false); continue; }
                b.gameObject.SetActive(true);
                b.interactable = true;
                SetButtonLabel(b, $"Olvidar\n{Session.Data.MoveName(mon.Moves[i])}");
            }
            if (backButton) { backButton.gameObject.SetActive(true); backButton.interactable = true; SetButtonLabel(backButton, "No aprender"); }
        }

        private void OpenSwitchPanel(Mode mode)
        {
            _mode = mode;
            ShowOnly(switchPanel);
            var members = B.PlayerTeam.Members;
            for (int i = 0; switchButtons != null && i < switchButtons.Length; i++)
            {
                var b = switchButtons[i]; if (!b) continue;
                if (i >= members.Count) { b.gameObject.SetActive(false); continue; }
                var m = members[i];
                b.gameObject.SetActive(true);
                string tag = m.IsFainted ? "  (debilitado)" : ReferenceEquals(m, B.Player) ? "  (en el campo)" : "";
                string status = m.Status.HasValue ? $"  [{StatusName(m.Status.Value)}]" : "";
                SetButtonLabel(b, $"{Session.NameOf(m.Id)}  Nv.{m.Level}   PS {m.CurrentHp}/{m.MaxHp}{status}{tag}");
                b.interactable = mode == Mode.ItemTarget || (!m.IsFainted && !ReferenceEquals(m, B.Player));
            }
            if (backButton)
            {
                backButton.gameObject.SetActive(mode != Mode.Replacement);
                backButton.interactable = mode != Mode.Replacement;
                SetButtonLabel(backButton, "Atrás");
            }
        }

        private void OpenBagPanel()
        {
            _mode = Mode.Bag;
            ShowOnly(bagPanel != null ? bagPanel : switchPanel);
            _bagShown.Clear();
            foreach (var (id, count) in Session.Save.Bag.Contents())
                if (count > 0 && Session.Data.TryGetItem(id, out var it) && (it.UsableInBattle || it.IsBall)) _bagShown.Add(it);
            // Primero medicinas, luego bolas; dentro, por nombre.
            _bagShown.Sort((a, b) => a.IsBall != b.IsBall ? a.IsBall.CompareTo(b.IsBall) : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture));

            var buttons = bagButtons ?? new Button[0];
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i]; if (!b) continue;
                if (i >= _bagShown.Count) { b.gameObject.SetActive(false); continue; }
                var it = _bagShown[i];
                b.gameObject.SetActive(true);
                b.interactable = true;
                string extra = it.IsBall ? $"  ({Session.CatchChance(it.Id) * 100:0}%)" : "";
                SetButtonLabel(b, $"{it.DisplayName} ×{Session.Save.Bag.Count(it.Id)}{extra}");
            }
            if (_bagShown.Count == 0) SetMessageInstant("La mochila no tiene nada que se pueda usar en combate.");
            else if (_bagShown.Count > buttons.Length) SetMessageInstant($"Mostrando {buttons.Length} de {_bagShown.Count} objetos.");
            else SetMessageInstant("¿Qué objeto usas?");
            if (backButton) { backButton.gameObject.SetActive(true); backButton.interactable = true; SetButtonLabel(backButton, "Atrás"); }
        }

        private void OpenConfirm()
        {
            _mode = Mode.Confirm;
            ShowOnly(confirmPanel);
        }

        private void ShowOnly(GameObject panel)
        {
            SetPanel(actionPanel, panel == actionPanel);
            SetPanel(movePanel, panel == movePanel);
            SetPanel(switchPanel, panel == switchPanel);
            SetPanel(bagPanel, panel == bagPanel);
            SetPanel(confirmPanel, panel == confirmPanel);
            if (backButton) backButton.gameObject.SetActive(panel != actionPanel && panel != confirmPanel && panel != null);
        }

        private void HideMenus()
        {
            _mode = Mode.None;
            SetPanel(actionPanel, false);
            SetPanel(movePanel, false);
            SetPanel(switchPanel, false);
            SetPanel(bagPanel, false);
            SetPanel(confirmPanel, false);
            if (backButton) backButton.gameObject.SetActive(false);
        }

        private static void SetPanel(GameObject go, bool on) { if (go) go.SetActive(on); }

        private static void SetButtonLabel(Button b, string text)
        {
            if (!b) return;
            var label = b.GetComponentInChildren<TMP_Text>(true);
            if (label) TextSafety.Set(label, text);
        }

        // ---------------- Narración de cada evento ----------------

        private string Who(Id<BattleParticipant> id)
        {
            string name = Session.NameOf(id);
            if (Session.IsPlayerSide(id)) return name;
            return Session.Kind == BattleKind.Wild ? $"el {name} salvaje" : $"el {name} enemigo";
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private string StatusName(StatusId id)
            => Session.Data.Statuses.TryGet(new Id<StatusConditionDefinition>(id.Value), out var d) ? d.DisplayName : id.Value;

        private string WeatherName(string id)
            => id != null && Session.Data.Weathers.TryGet(new Id<CTEditor.GameDefinition.Domain.Weather.WeatherDefinition>(id), out var w) ? w.DisplayName : id;

        private static string StatName(string id)
        {
            switch (id)
            {
                case "hp": return "Los PS";
                case "attack": return "El Ataque";
                case "defense": return "La Defensa";
                case "sp_attack": return "El Ataque Especial";
                case "sp_defense": return "La Defensa Especial";
                case "speed": return "La Velocidad";
                case "accuracy": return "La Precisión";
                case "evasion": return "La Evasión";
                default: return "La estadística " + id;
            }
        }

        private static string ShortStat(string id)
        {
            switch (id)
            {
                case "hp": return "PS";
                case "attack": return "Ataque";
                case "defense": return "Defensa";
                case "sp_attack": return "Atq. Esp.";
                case "sp_defense": return "Def. Esp.";
                case "speed": return "Velocidad";
                default: return id;
            }
        }

        // Qué se dice cuando salta una habilidad (Detail = qué hizo). Vacío = no se narra.
        private string AbilityLine(AbilityTriggeredEvent at)
        {
            var data = Session.Data;
            string who = Cap(Who(at.Combatant)), ab = data.AbilityName(at.AbilityId), d = at.Detail ?? "";
            if (d.StartsWith("objeto:")) { string it = d.Substring(7); return string.IsNullOrEmpty(it) ? $"{who} ({ab}): el rival no lleva objeto." : $"¡{who} ({ab}) descubrió que el rival lleva {data.ItemName(it)}!"; }
            if (d == "peligro:si") return $"¡{who} ({ab}) se estremeció!";
            if (d == "peligro:no") return "";
            if (d.StartsWith("movimiento:")) return $"¡{who} ({ab}) detectó {data.MoveName(new Id<Move>(d.Substring(11)))}!";
            switch (d)
            {
                case "clima": return $"¡{ab} de {Who(at.Combatant)} cambió el clima!";
                case "protege": return $"¡{ab} de {Who(at.Combatant)} lo protege!";
                case "competitivo": return $"¡{ab} de {Who(at.Combatant)} se activó!";
                case "irascible": return $"¡{who} se enfureció y maximizó su Ataque!";
                case "resquicio": return $"¡{who} ({ab}) no se deja atrapar!";
                case "autoestima": return $"¡{who} ({ab}) ganó confianza!";
                case "piel_tosca": return $"¡{ab} de {Who(at.Combatant)} hirió al atacante!";
                case "viscosidad": return $"¡{ab} de {Who(at.Combatant)} evita que le quiten el objeto!";
                case "absorbe": return $"¡{who} absorbió el ataque con {ab}!";
                case "absorbe_potencia": return $"¡{ab} de {Who(at.Combatant)} potenció sus ataques!";
                case "robustez": return $"¡{who} aguantó gracias a {ab}!";
                case "inmune": return $"¡{ab} de {Who(at.Combatant)} lo hace inmune!";
                case "mutatipo": return $"¡{ab} de {Who(at.Combatant)} cambió su tipo!";
                case "lodo": return $"¡{ab}: el que drenaba sale perjudicado!";
                case "sincronia": return $"¡{ab} de {Who(at.Combatant)} le devolvió el estado!";
                case "clima_ps": return "";   // lo narra la barra de PS / el evento de PS
                case "cura": return $"¡{ab} de {Who(at.Combatant)} le curó el estado!";
                case "mal_sueño": return $"¡{who} sufre pesadillas por {ab}!";
                case "cosecha": return $"¡{who} ({ab}) recogió una baya!";
                case "veleta": return $"¡{who} cambió de forma por el clima!";
                case "gas": return $"¡Un gas reactivo inunda el campo! ({ab})";
                case "nervios": return $"¡El rival está demasiado nervioso para comer bayas! ({ab})";
                case "presion": return $"¡{who} ejerce presión!";
                case "rompemoldes": return $"¡{who} rompe los moldes!";
                case "roba": return $"¡{who} robó el objeto del rival con {ab}!";
                case "baba": return $"¡{ab} de {Who(at.Combatant)} frenó al atacante!";
                case "": return $"¡{ab} de {Who(at.Combatant)} se activó!";
                default: return $"¡{ab} de {Who(at.Combatant)}!";
            }
        }

        // El motivo de un movimiento bloqueado: un estado (Mofa, Tormento...) o una habilidad (Humedad).
        private string RestrictionName(string reason)
        {
            var data = Session.Data;
            if (string.IsNullOrEmpty(reason)) return "bloqueado";
            if (data.Abilities.TryGet(new Id<CTEditor.GameDefinition.Domain.Abilities.AbilityDefinition>(reason), out var a)) return a.DisplayName;
            // Objetos Elección / Chaleco Asalto: el motivo es el objeto equipado.
            if (data.TryGetItem(reason, out var it)) return it.DisplayName;
            return StatusName(new StatusId(reason));
        }

        // ♂ azul / ♀ rosa junto al nombre (nada si no tiene género).
        private static string GenderMark(Combatant c)
            => c.Gender == CTEditor.GameDefinition.Domain.Species.Gender.Male ? " <color=#4A90E2>♂</color>"
             : c.Gender == CTEditor.GameDefinition.Domain.Species.Gender.Female ? " <color=#E86AA8>♀</color>" : "";

        private IEnumerator PlayEvent(IDomainEvent e)
        {
            var data = Session.Data;
            switch (e)
            {
                // --- Presentación y cambios ---
                case BattleIntroEvent intro:
                    if (intro.Kind == BattleKind.Wild) yield return Say($"¡Un {Session.NameOf(intro.FirstEnemy)} salvaje apareció!");
                    else
                    {
                        yield return Say($"¡{intro.TrainerName} te desafía!");
                        if (!string.IsNullOrWhiteSpace(intro.IntroLine)) yield return Say($"{intro.TrainerName}: «{intro.IntroLine}»");
                    }
                    break;
                case SentOutEvent so:
                    PutOnScreen(so.Combatant, so.IsPlayer);
                    yield return Say(so.IsPlayer ? $"¡Adelante, {Session.NameOf(so.Combatant)}!" : $"¡{so.TrainerName} saca a {Session.NameOf(so.Combatant)}!");
                    break;
                case MonsterWithdrawnEvent mw:
                    yield return Say(Session.IsPlayerSide(mw.Combatant) || Session.Trainer == null
                        ? $"¡Vuelve, {Session.NameOf(mw.Combatant)}!"
                        : $"¡{Session.Trainer.FullName} retira a {Session.NameOf(mw.Combatant)}!");
                    break;
                case MonsterSentEvent mse:
                    PutOnScreen(mse.Combatant, Session.IsPlayerSide(mse.Combatant));
                    yield return Say(Session.IsPlayerSide(mse.Combatant) ? $"¡Adelante, {Session.NameOf(mse.Combatant)}!" : $"¡Sale {Session.NameOf(mse.Combatant)}!");
                    break;
                case ReplacementRequiredEvent _: break; // lo gestiona la sesión (fase de relevo)

                // --- Movimientos ---
                case MoveUsedEvent mu:
                    yield return Say($"¡{Cap(Who(mu.Attacker))} usó {data.MoveName(mu.Move)}!");
                    yield return PlayMoveAnimation(mu.Move);
                    break;
                case MoveMissedEvent mm: yield return Say($"¡El ataque de {Who(mm.Attacker)} falló!"); break;
                case MoveHadNoEffectEvent ne: yield return Say($"No afecta a {Who(ne.Target)}..."); break;
                case CriticalHitEvent _: yield return Say("¡Un golpe crítico!"); break;
                case DamageDealtEvent dd:
                    yield return AnimateBarFor(dd.Target);
                    if (EffNote(dd.Effectiveness) is string note && note.Length > 0) yield return Say(note);
                    break;
                case MonsterFaintedEvent mf:
                    yield return AnimateBarFor(mf.Combatant);           // la barra baja hasta 0 (se ve)
                    yield return new WaitForSeconds(0.25f);
                    yield return Say($"¡{Cap(Who(mf.Combatant))} se debilitó!");
                    break;
                case OutOfPpEvent op: yield return Say($"¡A {Who(op.Combatant)} no le quedan PP para ese movimiento!"); break;
                case StruggleEvent st: yield return Say($"¡A {Who(st.Combatant)} no le quedan movimientos! Usa su último recurso."); break;
                case MoveBlockedEvent mb: yield return Say($"¡{Cap(Who(mb.Target))} se protegió!"); break;
                case EnduredEvent en: yield return Say($"¡{Cap(Who(en.Combatant))} aguantó el golpe!"); break;
                case FlinchedEvent fl: yield return Say($"¡{Cap(Who(fl.Combatant))} retrocedió y no pudo moverse!"); break;
                case ChargingStartedEvent cg: yield return Say($"{Cap(Who(cg.Combatant))} acumula energía..."); break;
                case RechargingEvent rch: yield return Say($"{Cap(Who(rch.Combatant))} tiene que recuperarse."); break;
                case StatStageChangedEvent ss:
                    yield return Say($"¡{StatName(ss.Stat.Value)} de {Who(ss.Combatant)} {(ss.Delta > 0 ? "subió" : "bajó")}{(Math.Abs(ss.Delta) >= 2 ? " mucho" : "")}!");
                    break;

                // --- Estados, clima y objetos ---
                case StatusInflictedEvent si: yield return Say($"¡{Cap(Who(si.Target))}: {StatusName(si.Status)}!"); break;
                case StatusDamageEvent sd:
                    yield return AnimateBarFor(sd.Target);
                    yield return Say(sd.Status.Equals(CTEditor.Battle.Domain.Turn.TurnResolver.InjuryStatus) ? $"¡{Cap(Who(sd.Target))} resultó herido!"
                                                                           : $"{Cap(Who(sd.Target))} sufre por {StatusName(sd.Status)}.");
                    break;
                case ActionPreventedEvent ap: yield return Say($"{Cap(Who(ap.Combatant))} no puede moverse ({StatusName(ap.Status)})."); break;
                case StatusFadedEvent sf: yield return Say($"{Cap(Who(sf.Combatant))} ya no está afectado por {StatusName(sf.Status)}."); break;
                case StatusFailedEvent _: yield return Say("¡Pero falló!"); break;
                case SwitchPreventedEvent sp: yield return Say($"¡{Cap(Who(sp.Combatant))} no puede escapar ({StatusName(sp.Status)})!"); break;
                case WeatherStartedEvent ws: yield return Say($"El clima cambió: {WeatherName(ws.WeatherId)}."); break;
                case WeatherEndedEvent we: yield return Say($"Terminó {WeatherName(we.WeatherId)}."); break;
                case WeatherDamageEvent wd:
                    yield return AnimateBarFor(wd.Combatant);
                    yield return Say($"{Cap(Who(wd.Combatant))} sufre por {WeatherName(wd.WeatherId)}.");
                    break;
                case HpRestoredEvent hr:
                    yield return AnimateBarFor(hr.Combatant);
                    yield return Say($"{Cap(Who(hr.Combatant))} recuperó PS.");
                    break;
                case RecoilDamageEvent rc:
                    yield return AnimateBarFor(rc.Combatant);
                    yield return Say($"{Cap(Who(rc.Combatant))} también se hizo daño.");
                    break;
                case HeldItemActivatedEvent hi: yield return Say($"¡{Cap(Who(hi.Combatant))} usó su {data.ItemName(hi.ItemId)}!"); break;
                case BagItemUsedEvent bu:
                    yield return Say(bu.ByPlayer
                        ? $"Usaste {data.ItemName(bu.ItemId)} en {Session.NameOf(bu.Target)}."
                        : $"¡{bu.UserName} usó {data.ItemName(bu.ItemId)} en {Session.NameOf(bu.Target)}!");
                    _itemNamed = true;
                    break;
                case ItemUsedInBattleEvent iu:
                    yield return AnimateBarFor(iu.Target);
                    if (!_itemNamed) yield return Say($"Se usó un objeto en {Session.NameOf(iu.Target)}.");
                    _itemNamed = false;
                    break;

                // --- Trampas de campo y cambios forzados ---
                case HazardSetEvent hs:
                    yield return Say($"¡{data.HazardName(hs.HazardId)} {(hs.PlayerSide ? "rodea a tu equipo" : "rodea al equipo rival")}!" +
                                     (hs.Layers > 1 ? $" (capa {hs.Layers})" : ""));
                    break;
                case HazardsClearedEvent hc:
                    yield return Say($"¡Desapareció {string.Join(", ", hc.HazardIds.Select(data.HazardName))} del lado {(hc.PlayerSide ? "de tu equipo" : "rival")}!");
                    break;
                case HazardDamageEvent hd:
                    yield return AnimateBarFor(hd.Combatant);
                    yield return Say($"¡{Cap(Who(hd.Combatant))} se hirió con {data.HazardName(hd.HazardId)}!");
                    break;
                case HazardAbsorbedEvent ha: yield return Say($"¡{Cap(Who(ha.Combatant))} retiró {data.HazardName(ha.HazardId)}!"); break;
                case MoveFailedEvent _: yield return Say("¡Pero falló!"); break;
                case ForcedOutEvent fo:
                    if (fo.BattleEnded) _endedByForce = true;
                    yield return Say(fo.BattleEnded ? $"¡{Cap(Who(fo.Combatant))} salió huyendo! El combate ha terminado."
                                                    : $"¡{Cap(Who(fo.Combatant))} fue arrastrado fuera del combate!");
                    break;

                // --- Efectos de lado, sustituto y apoyo ---
                case SideConditionStartedEvent ss:
                    yield return Say($"¡{data.SideConditionName(ss.ConditionId)} protege {(ss.PlayerSide ? "a tu equipo" : "al equipo rival")}!");
                    break;
                case SideConditionEndedEvent se:
                    yield return Say($"¡{data.SideConditionName(se.ConditionId)} {(se.PlayerSide ? "de tu equipo" : "del equipo rival")} se ha disipado!");
                    break;
                case ProtectedBySideEvent pb: yield return Say($"¡{Cap(Who(pb.Combatant))} está protegido por {data.SideConditionName(pb.ConditionId)}!"); break;
                case SubstituteCreatedEvent sc:
                    yield return AnimateBarFor(sc.Combatant);
                    yield return Say($"¡{Cap(Who(sc.Combatant))} creó un sustituto!");
                    break;
                case SubstituteDamagedEvent sd: yield return Say($"¡El sustituto recibió el golpe en lugar de {Who(sd.Combatant)}!"); break;
                case SubstituteBrokeEvent sb: yield return Say($"¡El sustituto de {Who(sb.Combatant)} se rompió!"); break;
                case SubstituteBlockedEvent sbk: yield return Say($"¡El sustituto de {Who(sbk.Combatant)} lo ha bloqueado!"); break;
                case StagesResetEvent sr: yield return Say($"¡Las estadísticas de {Who(sr.Combatant)} volvieron a la normalidad!"); break;
                case CritBoostedEvent cb: yield return Say($"¡{Cap(Who(cb.Combatant))} se está concentrando!"); break;
                case MoveDisabledEvent md: yield return Say($"¡{data.MoveName(md.Move)} de {Who(md.Combatant)} ha sido anulado!"); break;
                case DisabledMoveTriedEvent dt: yield return Say($"¡{Cap(Who(dt.Combatant))} no puede usar {data.MoveName(dt.Move)}: está anulado!"); break;
                case DisableEndedEvent de: yield return Say($"¡{Cap(Who(de.Combatant))} ya no está anulado!"); break;
                case EncoreStartedEvent es: yield return Say($"¡{Cap(Who(es.Combatant))} tiene que repetir {data.MoveName(es.Move)}!"); break;
                case EncoreEndedEvent ee: yield return Say($"¡{Cap(Who(ee.Combatant))} ya no tiene que repetir!"); break;
                case RampageEndedEvent re: yield return Say($"¡{Cap(Who(re.Combatant))} se ha calmado... y está agotado!"); break;
                case RageBuildingEvent rb: yield return Say($"¡La furia de {Who(rb.Combatant)} aumenta!"); break;
                case BideStoringEvent bs: yield return Say(bs.Started ? $"¡{Cap(Who(bs.Combatant))} empieza a aguantar!" : $"¡{Cap(Who(bs.Combatant))} está aguantando!"); break;
                case BideUnleashedEvent bu2: yield return Say($"¡{Cap(Who(bu2.Combatant))} desató su energía!"); break;
                case MoveCalledEvent mcall: yield return Say($"¡{data.MoveName(mcall.Via)} se convirtió en {data.MoveName(mcall.Called)}!"); break;
                case MoveCopiedEvent mcop: yield return Say($"¡{Cap(Who(mcop.Combatant))} aprendió {data.MoveName(mcop.Move)}!"); break;
                case TransformedEvent tr:
                    RefreshActiveVisuals(Session.IsPlayerSide(tr.Combatant));
                    yield return Say($"¡{Cap(Who(tr.Combatant))} se transformó en {Session.NameOf(tr.Into)}!");
                    break;
                case TypeChangedEvent tc:
                    yield return Say($"¡{Cap(Who(tc.Combatant))} ahora es de tipo {string.Join("/", tc.Types.Select(data.TypeName))}!");
                    break;
                case SelfSwitchRequiredEvent _: break; // lo gestiona la sesión (elegir quién entra)
                case FormChangedEvent fc:
                    yield return Say(fc.To.Length == 0
                        ? $"¡{Cap(Who(fc.Combatant))} volvió a su forma normal!"
                        : $"¡{Cap(Who(fc.Combatant))} cambió a {(string.IsNullOrEmpty(fc.FormName) ? fc.To : fc.FormName)}!");
                    RefreshActiveVisuals(Session.IsPlayerSide(fc.Combatant));
                    break;
                case SelfSwitchedEvent ssw:
                    yield return Say(ssw.PassesBoosts ? $"¡{Cap(Who(ssw.Combatant))} pasa el relevo!" : $"¡{Cap(Who(ssw.Combatant))} vuelve con los suyos!");
                    break;
                case TeleportedEvent tp:
                    _endedByForce = true;
                    yield return Say($"¡{Cap(Who(tp.Combatant))} se teletransportó lejos del combate!");
                    break;

                // --- Captura y huida ---
                case MonsterCapturedEvent mc:
                    for (int i = 0; i < mc.Shakes; i++) yield return Say(i == 0 ? "¡La bola se mueve...!" : i == 1 ? "¡Se mueve otra vez...!" : "¡Y otra...!");
                    yield return Say($"¡Ya está! ¡Has capturado a {Session.NameOf(mc.Target)}!");
                    break;
                case CaptureFailedEvent cf:
                    for (int i = 0; i < cf.Shakes; i++) yield return Say(i == 0 ? "¡La bola se mueve...!" : "¡Se mueve otra vez...!");
                    yield return Say(cf.Shakes == 0 ? "¡Oh, no! ¡Se ha escapado!" : cf.Shakes == 1 ? "¡Vaya! ¡Parecía que lo tenías!"
                        : cf.Shakes == 2 ? "¡Ay! ¡Casi lo consigues!" : "¡Qué rabia! ¡Por muy poco!");
                    break;
                case CaptureBlockedEvent _: yield return Say("¡El entrenador ha bloqueado la bola! ¡No seas ladrón!"); break;
                case FleeFailedEvent _: yield return Say("¡No has podido escapar!"); break;
                case FleeBlockedEvent _: yield return Say("¡No puedes huir de un combate contra un entrenador!"); break;

                // --- 3.ª y 4.ª generación: habilidades, objetos, restricciones ---
                case AbilityTriggeredEvent at:
                {
                    string line = AbilityLine(at);
                    if (!string.IsNullOrEmpty(line)) yield return Say(line);
                    break;
                }
                case AbilityChangedEvent ac: yield return Say($"¡La habilidad de {Who(ac.Combatant)} ahora es {data.AbilityName(ac.AbilityId)}!"); break;
                case ItemTransferredEvent it:
                    yield return Say(it.To.HasValue
                        ? $"¡{Cap(Who(it.To.Value))} obtuvo {data.ItemName(it.ItemId)} de {Who(it.From)}!"
                        : $"¡{Cap(Who(it.From))} perdió su {data.ItemName(it.ItemId)}!");
                    break;
                case ItemRestoredEvent ir: yield return Say($"¡{Cap(Who(ir.Combatant))} recuperó su {data.ItemName(ir.ItemId)}!"); break;
                case MoveRestrictedEvent mr: yield return Say($"¡{Cap(Who(mr.Combatant))} no puede usar {data.MoveName(mr.Move)} ({RestrictionName(mr.Reason)})!"); break;
                case MoveReflectedEvent mref:
                    yield return Say(mref.Stolen ? $"¡{Cap(Who(mref.Combatant))} le robó {data.MoveName(mref.Move)}!"
                                                 : $"¡{Cap(Who(mref.Combatant))} devolvió {data.MoveName(mref.Move)}!");
                    break;
                case HpChangedEvent hpc:
                    yield return AnimateBarFor(hpc.Combatant);
                    break;
                case DelayedEffectSetEvent des:
                    yield return Say(des.Heals ? $"¡{Cap(Who(des.Combatant))} pidió un deseo!" : $"¡{Cap(Who(des.Combatant))} previó un ataque!");
                    break;
                case DelayedEffectTriggeredEvent det:
                    yield return Say(det.Heals ? $"¡El deseo se hizo realidad para {Who(det.Target)}!"
                                               : $"¡{Cap(Who(det.Target))} recibió {data.MoveName(det.Move)}!");
                    break;
                case StockpileEvent sp2: yield return Say($"¡{Cap(Who(sp2.Combatant))} reservó energía ({sp2.Count})!"); break;
                case LoafingEvent lf: yield return Say($"{Cap(Who(lf.Combatant))} está holgazaneando..."); break;
                case StatsSwappedEvent sw:
                    yield return Say(sw.Copied ? $"¡{Cap(Who(sw.Combatant))} copió los cambios de estadísticas!"
                                               : sw.Other.HasValue ? $"¡{Cap(Who(sw.Combatant))} intercambió sus cambios de estadísticas con {Who(sw.Other.Value)}!"
                                                                   : $"¡{Cap(Who(sw.Combatant))} cambió sus estadísticas!");
                    break;
                case DestinyBondEvent db:
                    yield return Say(db.Grudge ? $"¡{Cap(Who(db.Attacker))} perdió los PP de su movimiento por el rencor de {Who(db.Holder)}!"
                                               : $"¡{Cap(Who(db.Holder))} se llevó consigo a {Who(db.Attacker)}!");
                    break;
                case PerishCountEvent pc: yield return Say($"Contador de salida de {Who(pc.Combatant)}: {pc.Count}."); break;

                // --- Experiencia, niveles y movimientos ---
                case ExperienceAwardedEvent xp: yield return Say($"¡{Session.NameOf(xp.Recipient)} ganó {xp.Amount} puntos de experiencia!"); break;
                case LevelUpEvent lu:
                    if (lu.Combatant.Equals(_onScreenP)) RefreshActiveVisuals(true);
                    yield return Say($"¡{Session.NameOf(lu.Combatant)} subió al nivel {lu.NewLevel}!");
                    var gains = lu.StatGains.Where(g => g.Value != 0).Select(g => $"{ShortStat(g.Key.Value)} +{g.Value}").ToList();
                    if (gains.Count > 0) yield return Say(string.Join("  ·  ", gains));
                    break;
                case MoveLearnedEvent ml:
                    if (ml.Forgotten.HasValue)
                        yield return Say($"1, 2 y... ¡puf! {Session.NameOf(ml.Combatant)} olvidó {data.MoveName(ml.Forgotten.Value)} y aprendió {data.MoveName(ml.Move)}.");
                    else yield return Say($"¡{Session.NameOf(ml.Combatant)} aprendió {data.MoveName(ml.Move)}!");
                    break;
                case MoveNotLearnedEvent nl: yield return Say($"{Session.NameOf(nl.Combatant)} no aprendió {data.MoveName(nl.Move)}."); break;
                case MoveLearnPromptEvent _: break;   // lo pregunta la fase LearnMove
                case EvolutionPromptEvent _: break;   // lo pregunta la fase Evolution

                // --- Fin ---
                case BattleEndedEvent be:
                    if (be.Outcome == BattleOutcome.PlayerWon && Session.Kind == BattleKind.Trainer) yield return Say($"¡Has vencido a {Session.Trainer.FullName}!");
                    else if (be.Outcome == BattleOutcome.Fled && !_endedByForce) yield return Say("¡Escapaste sin problemas!");
                    break;
                case TrainerSaysEvent ts: yield return Say($"{ts.TrainerName}: «{ts.Line}»"); break;
                case MoneyWonEvent mw2: yield return Say($"¡Has ganado {mw2.Amount} ₽!"); break;
                case BlackoutEvent bo:
                    yield return Say("¡No te quedan monstruos en pie!");
                    if (bo.MoneyLost > 0) yield return Say($"Has perdido {bo.MoneyLost} ₽...");
                    if (bo.Healed) yield return Say("Vuelves corriendo al Centro, donde curan a tu equipo.");
                    break;
                case CaptureStoredEvent cs:
                    yield return Say(cs.Where == StoredIn.Party ? $"¡{cs.Name} se ha unido a tu equipo!"
                        : cs.Where == StoredIn.Box ? $"Tu equipo está lleno: {cs.Name} se ha enviado al PC."
                        : $"No había sitio: {cs.Name} ha sido liberado.");
                    break;
                case EvolutionResultEvent er:
                    string from = data.Species.TryGet(er.From, out var f) ? f.DisplayName : er.From.Value;
                    string to = data.Species.TryGet(er.To, out var t) ? t.DisplayName : er.To.Value;
                    yield return Say(er.Evolved ? $"¡Enhorabuena! ¡Tu {from} ha evolucionado a {to}!" : $"¿Eh? {from} ha dejado de evolucionar.");
                    break;
            }
        }

        // Espera la "animación" de un movimiento (dato de presentación de la ficha). AQUÍ engancharías la real.
        private IEnumerator PlayMoveAnimation(Id<Move> move)
        {
            float secs = ContentLibrary.MoveAnimationSeconds(move);
            if (secs > 0f) yield return new WaitForSeconds(secs);
        }

        // ---------------- Caja de mensajes con tecleo + avanzar ----------------

        private IEnumerator Say(string line)
        {
            if (string.IsNullOrEmpty(line)) yield break;
            line = TextSafety.Clean(line, messageText != null ? messageText.font : null);
            _advance = false;

            if (messageText)
            {
                float speed = useInterfaceTextSpeed ? GameInput.Settings.TextSpeed : charsPerSecond;
                float cps = speed <= 0f ? 99999f : speed;
                float t = 0f; int shown = 0;
                while (shown < line.Length)
                {
                    if (_advance) { _advance = false; break; }
                    // Mantener Confirmar acelera el texto (como en los juegos).
                    bool fast = GameInput.Settings.HoldToSpeedUp && GameInput.Held(CTEditor.Adventure.Domain.Interface.GameButton.Confirm);
                    t += Time.deltaTime * cps * (fast ? 3f : 1f);
                    shown = Mathf.Min(line.Length, Mathf.FloorToInt(t));
                    messageText.text = line.Substring(0, shown);
                    yield return null;
                }
                messageText.text = line;
            }

            bool canManual = true; // siempre se puede avanzar con el teclado o el mando (y con clic si hay botón)
            float auto = autoAdvanceSeconds > 0f ? autoAdvanceSeconds : GameInput.Settings.AutoAdvanceSeconds;
            float autoWait = auto > 0f ? auto : (canManual ? 0f : 1.2f);
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
            if (messageText) TextSafety.Set(messageText, line);
        }

        // ---------------- Barras de PS ----------------

        private IEnumerator AnimateBarFor(Id<BattleParticipant> target)
        {
            if (target.Equals(_onScreenP)) { yield return AnimateBar(true); yield break; }
            if (target.Equals(_onScreenE)) { yield return AnimateBar(false); yield break; }
        }

        private IEnumerator AnimateBar(bool playerSide)
        {
            var active = OnScreen(playerSide);
            var bar = playerSide ? playerHpBar : enemyHpBar;
            var txt = playerSide ? playerHpText : enemyHpText;
            int from = playerSide ? _shownP : _shownE;
            int to = _hpShown.TryGetValue(active.Id, out var shown) ? shown : active.CurrentHp;
            int max = active.MaxHp;

            // Como en los juegos: un golpe grande tarda más en bajar que uno pequeño.
            float dur = hpDrainSeconds <= 0f ? 0f : hpDrainSeconds * (0.6f + 0.9f * Mathf.Abs(to - from) / Mathf.Max(1f, max));
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

        private void RefreshActiveVisuals(bool playerSide)
        {
            if (Session == null) return;
            var active = OnScreen(playerSide);
            string status = active.Status.HasValue ? $"  <size=70%>[{StatusName(active.Status.Value)}]</size>" : "";
            if (playerSide)
            {
                if (playerNameText) playerNameText.text = Session.NameOf(active.Id) + GenderMark(active) + status;
                if (playerLevelText) playerLevelText.text = $"Nv.{active.Level}";
                _shownP = _hpShown.TryGetValue(active.Id, out var hpP) ? hpP : active.CurrentHp;
                SetBar(playerHpBar, playerHpText, _shownP, active.MaxHp);
            }
            else
            {
                if (enemyNameText) enemyNameText.text = Session.NameOf(active.Id) + GenderMark(active) + status;
                if (enemyLevelText) enemyLevelText.text = $"Nv.{active.Level}";
                _shownE = _hpShown.TryGetValue(active.Id, out var hpE) ? hpE : active.CurrentHp;
                SetBar(enemyHpBar, enemyHpText, _shownE, active.MaxHp);
            }
            UpdateDebug();
        }

        private static void SetBar(Slider bar, TMP_Text text, int current, int max)
        {
            if (bar) bar.value = max > 0 ? (float)current / max : 0f;
            if (text) text.text = $"{current}/{max}";
        }

        // Campos de depuración del Inspector: nivel, PS, estadísticas y genética de ambos activos.
        private void UpdateDebug()
        {
            if (Session == null) return;
            playerDebug = DescribeActive(B.Player);
            enemyDebug = DescribeActive(B.Enemy);
        }

        private string DescribeActive(Combatant c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Session.NameOf(c.Id)}  Nv.{c.Level}   PS {c.CurrentHp}/{c.MaxHp}{(c.Status.HasValue ? "  [" + c.Status.Value + "]" : "")}");
            foreach (var stat in c.Stats.Stats)
            {
                int bas = c.Stats.Of(stat);
                int cur = Session.Resolver.CurrentStat(c, stat);
                sb.AppendLine(bas == cur ? $"{stat.Value}: {bas}" : $"{stat.Value}: {bas} -> {cur}");
            }
            var mon = Session.MonsterOf(c.Id);
            if (mon != null) sb.Append(BattlerBuilder.DescribeGenetics(mon));
            return sb.ToString();
        }

        private static string EffNote(float eff)
        {
            if (eff <= 0f) return "No tuvo efecto.";
            if (eff > 1f) return "¡Es muy eficaz!";
            if (eff < 1f) return "No es muy eficaz...";
            return "";
        }
    }
}
