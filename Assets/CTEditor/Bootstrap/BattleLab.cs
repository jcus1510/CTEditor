using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Adventure.Domain;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// LABORATORIO DE COMBATE: el menú de la escena de pruebas. Desde aquí se prueba TODO con la partida
    /// real del jugador (la que vive en PartyHolder):
    ///
    ///   • Combate salvaje en la zona elegida (para capturar).
    ///   • Combate contra el entrenador elegido (premio, no se huye, no se captura).
    ///   • Centro: cura a todo el equipo.
    ///   • Ordenar el equipo (quién sale primero), dejar en el PC y sacar del PC.
    ///   • MOCHILA Y EQUIPO fuera del combate: usar objetos (curar, revivir, piedras evolutivas...),
    ///     dar y quitar objetos equipados y ver el RESUMEN de cada monstruo (stats, IV/EV, PP...).
    ///   • Reiniciar la partida (vuelve a empezar con el mismo equipo inicial).
    ///
    /// Tras cada combate vuelve aquí y muestra el equipo ACTUALIZADO (niveles, PS, experiencia, capturas,
    /// dinero): así se comprueba que la partida persiste de verdad.
    /// </summary>
    public sealed class BattleLab : MonoBehaviour
    {
        [Header("Qué se puede probar")]
        [SerializeField] private TrainerData[] trainers;
        [SerializeField] private EncounterZoneData[] zones;

        [Header("Escena")]
        [SerializeField] private BattleScreen battleScreen;
        [Tooltip("El panel del laboratorio (se oculta durante el combate).")]
        [SerializeField] private GameObject labPanel;
        [Tooltip("La interfaz del combate (se oculta fuera del combate).")]
        [SerializeField] private GameObject battleRoot;

        [Header("Interfaz del laboratorio")]
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private TMP_Text teamText;
        [SerializeField] private Button wildButton;
        [SerializeField] private Button trainerButton;
        [SerializeField] private Button prevTrainerButton;
        [SerializeField] private Button nextTrainerButton;
        [SerializeField] private Button prevZoneButton;
        [SerializeField] private Button nextZoneButton;
        [SerializeField] private Button healButton;
        [SerializeField] private Button resetButton;
        [Tooltip("Un botón por hueco del equipo: ELIGE a ese miembro (para usarle objetos, verle el resumen, ponerlo primero o dejarlo en el PC). " +
                 "En escenas antiguas sin «Poner primero», lo pone directamente el primero.")]
        [SerializeField] private Button[] leadButtons;
        [Tooltip("Pone al miembro elegido el primero (el que sale a combatir).")]
        [SerializeField] private Button makeLeadButton;
        [SerializeField] private Button depositButton;
        [SerializeField] private Button withdrawButton;

        [Header("Mochila y equipo (fuera del combate)")]
        [Tooltip("Muestra el objeto de la mochila elegido.")]
        [SerializeField] private TMP_Text itemText;
        [SerializeField] private Button prevItemButton;
        [SerializeField] private Button nextItemButton;
        [Tooltip("Usa el objeto elegido sobre el miembro elegido (Poción, Revivir, piedras...).")]
        [SerializeField] private Button useItemButton;
        [Tooltip("Da el objeto elegido al miembro para que lo lleve equipado.")]
        [SerializeField] private Button giveItemButton;
        [Tooltip("Le quita el objeto equipado al miembro (vuelve a la mochila).")]
        [SerializeField] private Button takeItemButton;
        [Tooltip("Muestra u oculta el resumen completo del miembro elegido.")]
        [SerializeField] private Button summaryButton;
        [Tooltip("Aparece cuando un monstruo quiere aprender un movimiento y ya sabe 4 (por ejemplo, al evolucionar).")]
        [SerializeField] private GameObject learnPanel;
        [Tooltip("Un botón por movimiento que sabe: olvida ese para aprender el nuevo.")]
        [SerializeField] private Button[] forgetButtons;
        [SerializeField] private Button skipLearnButton;

        private int _trainer, _zone, _selected, _item;
        private bool _showSummary;
        private string _lastMessage = "";
        // Movimientos que esperan respuesta (quién y cuál), tras evolucionar con una piedra.
        private readonly Queue<(MonsterInstance mon, Id<CTEditor.GameDefinition.Domain.Moves.Move> move)> _pendingLearn =
            new Queue<(MonsterInstance, Id<CTEditor.GameDefinition.Domain.Moves.Move>)>();

        private PartyHolder Party => PartyHolder.Current;

        private void Start()
        {
            Wire(wildButton, StartWild);
            Wire(trainerButton, StartTrainer);
            Wire(prevTrainerButton, () => Cycle(ref _trainer, trainers, -1));
            Wire(nextTrainerButton, () => Cycle(ref _trainer, trainers, +1));
            Wire(prevZoneButton, () => Cycle(ref _zone, zones, -1));
            Wire(nextZoneButton, () => Cycle(ref _zone, zones, +1));
            Wire(healButton, Heal);
            Wire(resetButton, ResetGame);
            Wire(depositButton, Deposit);
            Wire(withdrawButton, Withdraw);
            Wire(makeLeadButton, () => MakeLead(_selected));
            // Con «Poner primero» en la escena, los botones de hueco ELIGEN; sin él (escena antigua), ponen primero.
            if (leadButtons != null)
                for (int i = 0; i < leadButtons.Length; i++)
                {
                    int k = i;
                    Wire(leadButtons[i], makeLeadButton ? (UnityEngine.Events.UnityAction)(() => Select(k)) : () => MakeLead(k));
                }
            Wire(prevItemButton, () => CycleItem(-1));
            Wire(nextItemButton, () => CycleItem(+1));
            Wire(useItemButton, UseItem);
            Wire(giveItemButton, GiveItem);
            Wire(takeItemButton, TakeItem);
            Wire(summaryButton, () => { _showSummary = !_showSummary; Refresh(); });
            if (forgetButtons != null)
                for (int i = 0; i < forgetButtons.Length; i++) { int k = i; Wire(forgetButtons[i], () => AnswerLearn(k)); }
            Wire(skipLearnButton, () => AnswerLearn(-1));
            if (battleScreen != null) battleScreen.Finished += OnBattleFinished;
            SetupKeyboard();
            TextSafety.FixTree(transform);   // quita los símbolos que la fuente no tiene (escenas antiguas)
            ShowLab();
        }

        // ---------------- Teclado y menú de pausa ----------------

        private PauseMenu _pause;

        // Sin tocar la escena: flechas y Confirmar para todos los botones del Laboratorio, Cancelar =
        // «No aprenderlo», y el MENÚ DE PAUSA (botón Menú: Esc/X) con equipo, mochila, resumen y opciones.
        private void SetupKeyboard()
        {
            var nav = GetComponent<KeyboardNavigator>();
            if (nav == null) nav = gameObject.AddComponent<KeyboardNavigator>();
            nav.Configure(new[] { skipLearnButton }, new Button[0]);

            _pause = GetComponent<PauseMenu>();
            if (_pause == null) _pause = gameObject.AddComponent<PauseMenu>();
            _pause.Configure(battleScreen);
            _pause.Closed += Refresh; // el equipo o la mochila pueden haber cambiado
        }

        private void OnDestroy()
        {
            if (battleScreen != null) battleScreen.Finished -= OnBattleFinished;
        }

        private static void Wire(Button b, UnityEngine.Events.UnityAction a)
        {
            if (!b) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(a);
        }

        private void Cycle<T>(ref int index, T[] list, int delta)
        {
            if (list == null || list.Length == 0) return;
            index = (index + delta + list.Length) % list.Length;
            Refresh();
        }

        // ---------------- Acciones ----------------

        private void StartWild()
        {
            if (zones == null || zones.Length == 0 || zones[_zone] == null) { Message("No hay zonas salvajes: créalas en CTEditor → Mundo → Zonas salvajes."); return; }
            Launch(BattleRequest.InZone(TrainerMapper.ToDomain(zones[_zone])));
        }

        private void StartTrainer()
        {
            if (trainers == null || trainers.Length == 0 || trainers[_trainer] == null) { Message("No hay entrenadores: créalos en CTEditor → Personajes → Entrenadores."); return; }
            Launch(BattleRequest.AgainstTrainer(TrainerMapper.ToDomain(trainers[_trainer])));
        }

        private void Launch(BattleRequest request)
        {
            if (Party == null || battleScreen == null) { Message("Falta la partida del jugador o la pantalla de combate en la escena."); return; }
            if (!Party.Save.CanBattle) { Message("Tu equipo no puede luchar: pulsa «Centro» para curarlo."); return; }
            ShowBattle();
            if (!battleScreen.Begin(request)) { ShowLab(); Message("No se pudo empezar el combate (mira la consola)."); }
        }

        private void OnBattleFinished(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.PlayerWon: _lastMessage = "¡Ganaste el combate!"; break;
                case BattleOutcome.PlayerLost: _lastMessage = "Perdiste el combate y volviste al Centro."; break;
                case BattleOutcome.Fled: _lastMessage = "Huiste del combate."; break;
                case BattleOutcome.Caught: _lastMessage = "¡Captura conseguida! Mira tu equipo o el PC."; break;
            }
            ShowLab();
        }

        private void Heal()
        {
            Party?.Save.HealAll();
            Message("Tu equipo se ha recuperado por completo (PS, estados y PP).");
        }

        private void ResetGame()
        {
            Party?.ResetGame();
            _selected = 0; _item = 0; _showSummary = false;
            _pendingLearn.Clear();
            Message("Partida reiniciada: equipo, dinero y mochila como al principio.");
        }

        private void MakeLead(int index)
        {
            if (Party == null) return;
            if (index > 0 && index < Party.Save.Party.Count)
            {
                var r = FieldActions.Swap(Party.Data, Party.Save, 0, index);
                _lastMessage = r.Done ? $"{Party.Data.NameOf(Party.Save.Party.Members[0])} saldrá el primero." : r.ToString();
            }
            _selected = 0; // el elegido sigue siendo el mismo monstruo, ahora en el hueco 1
            Refresh();
        }

        private void Select(int index)
        {
            if (Party == null || index < 0 || index >= Party.Save.Party.Count) return;
            _selected = index;
            Refresh();
        }

        private void Deposit()
        {
            if (Party == null) return;
            string name = Party.Save.Party.Count > 0 ? Party.Data.NameOf(Party.Save.Party.Members[SelectedIndex]) : "";
            var r = Party.Save.Deposit(SelectedIndex);
            if (r.IsSuccess) _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, Party.Save.Party.Count - 1));
            Message(r.IsSuccess ? $"Has dejado a {name} en el PC." : r.Error);
        }

        private int SelectedIndex => Party == null ? 0 : Mathf.Clamp(_selected, 0, Mathf.Max(0, Party.Save.Party.Count - 1));

        // ---------------- Mochila y equipo (fuera del combate) ----------------

        // Los objetos de la mochila, siempre en el mismo orden (por nombre).
        private List<(string id, int count)> BagItems()
        {
            if (Party == null) return new List<(string, int)>();
            var data = Party.Data;
            return Party.Save.Bag.Contents().Where(c => c.count > 0)
                .OrderBy(c => data != null ? data.ItemName(c.itemId) : c.itemId)
                .Select(c => (c.itemId, c.count)).ToList();
        }

        private string SelectedItem()
        {
            var items = BagItems();
            if (items.Count == 0) return null;
            _item = Mathf.Clamp(_item, 0, items.Count - 1);
            return items[_item].id;
        }

        private void CycleItem(int delta)
        {
            var items = BagItems();
            if (items.Count == 0) return;
            _item = ((_item + delta) % items.Count + items.Count) % items.Count;
            Refresh();
        }

        private void UseItem()
        {
            if (Party == null) return;
            var id = SelectedItem();
            if (id == null) { Message("La mochila está vacía."); return; }
            var r = FieldActions.UseItem(Party.Data, Party.Save, SelectedIndex, id);
            foreach (var move in r.PendingMoves) _pendingLearn.Enqueue((Party.Save.Party.Members[SelectedIndex], move));
            Message(r.ToString());
        }

        private void GiveItem()
        {
            if (Party == null) return;
            var id = SelectedItem();
            if (id == null) { Message("La mochila está vacía."); return; }
            Message(FieldActions.GiveItem(Party.Data, Party.Save, SelectedIndex, id).ToString());
        }

        private void TakeItem()
        {
            if (Party == null) return;
            Message(FieldActions.TakeItem(Party.Data, Party.Save, SelectedIndex).ToString());
        }

        // Respuesta a "quiere aprender X": olvidar el del hueco 'slot' o (-1) no aprenderlo.
        private void AnswerLearn(int slot)
        {
            if (Party == null || _pendingLearn.Count == 0) return;
            var (mon, move) = _pendingLearn.Dequeue();
            int index = Party.Save.Party.IndexOf(mon.Id);
            if (index < 0) { Message("Ese monstruo ya no está en el equipo."); return; }
            Message(FieldActions.LearnMove(Party.Data, Party.Save, index, move, slot).ToString());
        }

        private void Withdraw()
        {
            if (Party == null) return;
            if (Party.Save.Box.Count == 0) { Message("El PC está vacío."); return; }
            var r = Party.Save.Withdraw(0);
            Message(r.IsSuccess ? "Lo has sacado del PC al equipo." : r.Error);
        }

        private static string Key(CTEditor.Adventure.Domain.Interface.InputBindings b, CTEditor.Adventure.Domain.Interface.GameButton button)
            => b.KeysOf(button).Count > 0 ? b.KeysOf(button)[0] : "?";

        private void Message(string text)
        {
            _lastMessage = text;
            Refresh();
        }

        // ---------------- Mostrar ----------------

        private void ShowLab()
        {
            if (battleRoot) battleRoot.SetActive(false);
            if (labPanel) labPanel.SetActive(true);
            Refresh();
        }

        private void ShowBattle()
        {
            if (labPanel) labPanel.SetActive(false);
            if (battleRoot) battleRoot.SetActive(true);
        }

        private void Refresh()
        {
            if (Party == null) { if (infoText) infoText.text = "Falta la partida del jugador (PartyHolder) en la escena."; return; }
            var save = Party.Save;
            var data = Party.Data;
            if (data == null) { if (infoText) infoText.text = "Falta contenido (tabla de tipos y reglas)."; return; }

            // --- Información general ---
            var info = new StringBuilder();
            info.AppendLine($"<b>{save.PlayerName}</b>   ·   {save.Money} ₽   ·   entrenadores vencidos: {save.DefeatedTrainers.Count}");
            string trainerLine = trainers != null && trainers.Length > 0 && trainers[_trainer] != null
                ? $"{trainers[_trainer].TrainerClass} {trainers[_trainer].DisplayName}  · IA {AiLabel(trainers[_trainer].Ai)}{(save.HasDefeated(trainers[_trainer].Id) ? "  (ya vencido)" : "")}  ({_trainer + 1}/{trainers.Length})"
                : "(ninguno)";
            string zoneLine = zones != null && zones.Length > 0 && zones[_zone] != null ? $"{zones[_zone].DisplayName}  ({_zone + 1}/{zones.Length})" : "(ninguna)";
            info.AppendLine($"Entrenador: <b>{trainerLine}</b>");
            info.AppendLine($"Zona salvaje: <b>{zoneLine}</b>");
            var bag = save.Bag.Contents().Where(c => c.count > 0).Select(c => $"{data.ItemName(c.itemId)} ×{c.count}").ToList();
            info.AppendLine("Mochila: " + (bag.Count == 0 ? "vacía" : string.Join(", ", bag)));
            var keys = GameInput.Settings.Bindings;
            info.AppendLine($"<size=85%><color=#9FB4D9>Teclado: flechas para moverte · {Key(keys, CTEditor.Adventure.Domain.Interface.GameButton.Confirm)} = elegir · " +
                            $"{Key(keys, CTEditor.Adventure.Domain.Interface.GameButton.Menu)} = menú de pausa (equipo, mochila, opciones)</color></size>");
            if (!string.IsNullOrEmpty(_lastMessage)) info.AppendLine().Append("<color=#FFD966>").Append(_lastMessage).Append("</color>");
            TextSafety.Set(infoText, info.ToString());

            // --- Equipo y PC (o el resumen del elegido, o la pregunta de aprender) ---
            var team = new StringBuilder();
            bool selecting = makeLeadButton != null;
            if (_pendingLearn.Count > 0)
            {
                var (mon, move) = _pendingLearn.Peek();
                team.AppendLine($"<b>{data.NameOf(mon)} quiere aprender {data.MoveName(move)}.</b>");
                team.AppendLine("Pero ya sabe 4 movimientos. ¿Cuál olvida? (o pulsa «No aprenderlo»)").AppendLine();
                for (int i = 0; i < mon.Moves.Count; i++) team.AppendLine($"  {i + 1}. {data.MoveName(mon.Moves[i])}");
            }
            else if (_showSummary && save.Party.Count > 0)
            {
                var summary = FieldActions.Summary(data, save.Party.Members[SelectedIndex]);
                team.AppendLine("<b>RESUMEN</b>  <size=80%>(pulsa «Resumen» otra vez para volver al equipo)</size>");
                foreach (var line in summary.Lines()) team.AppendLine(line);
            }
            else
            {
                team.AppendLine($"<b>EQUIPO ({save.Party.Count}/{save.Party.MaxSize})</b>{(selecting ? "  <size=80%>▶ sale primero · ◆ elegido</size>" : "")}");
                for (int i = 0; i < save.Party.Count; i++)
                {
                    var m = save.Party.Members[i];
                    string mark = (i == 0 ? "▶" : " ") + (selecting && i == SelectedIndex ? "◆" : " ");
                    team.AppendLine($"{mark} {i + 1}. {Describe(m, data)}");
                }
            }
            if (_pendingLearn.Count == 0 && !_showSummary && save.Box.Count > 0)
            {
                team.AppendLine().AppendLine($"<b>PC ({save.Box.Count})</b>");
                foreach (var m in save.Box.Take(8)) team.AppendLine($"   {data.NameOf(m)} Nv.{m.Level.Value}");
                if (save.Box.Count > 8) team.AppendLine($"   ... y {save.Box.Count - 8} más");
            }
            TextSafety.Set(teamText, team.ToString());

            // Botones de orden: solo los huecos ocupados.
            if (leadButtons != null)
                for (int i = 0; i < leadButtons.Length; i++)
                    if (leadButtons[i]) leadButtons[i].gameObject.SetActive(i < save.Party.Count);
            if (withdrawButton) withdrawButton.interactable = save.Box.Count > 0 && !save.Party.IsFull;

            RefreshField(save, data);
        }

        // El panel de mochila y equipo: objeto elegido, qué botones sirven y la pregunta de aprender.
        private void RefreshField(PlayerSave save, GameData data)
        {
            var items = BagItems();
            string id = SelectedItem();
            ItemDefinitionRef(data, id, out var item);
            var member = save.Party.Count > 0 ? save.Party.Members[SelectedIndex] : null;

            if (itemText)
            {
                if (id == null) TextSafety.Set(itemText, "Mochila vacía");
                else
                {
                    string what = item == null ? "" : item.UsableOutsideBattle ? "se usa aquí" : FieldActions.CanBeHeld(item) ? "para equipar" : "";
                    TextSafety.Set(itemText, $"<b>{data.ItemName(id)}</b> ×{items[_item].count}  <size=75%>({_item + 1}/{items.Count}{(what.Length > 0 ? " · " + what : "")})</size>");
                }
            }
            bool pending = _pendingLearn.Count > 0;
            if (useItemButton) useItemButton.interactable = !pending && member != null && item != null && item.UsableOutsideBattle;
            if (giveItemButton) giveItemButton.interactable = !pending && member != null && item != null && FieldActions.CanBeHeld(item);
            if (takeItemButton) takeItemButton.interactable = !pending && member != null && !string.IsNullOrEmpty(member.HeldItem);
            if (summaryButton) summaryButton.interactable = !pending && member != null;
            if (makeLeadButton) makeLeadButton.interactable = !pending && SelectedIndex > 0;

            if (learnPanel) learnPanel.SetActive(pending);
            if (pending && forgetButtons != null)
            {
                var (mon, _) = _pendingLearn.Peek();
                for (int i = 0; i < forgetButtons.Length; i++)
                {
                    if (!forgetButtons[i]) continue;
                    bool has = i < mon.Moves.Count;
                    forgetButtons[i].gameObject.SetActive(has);
                    var label = forgetButtons[i].GetComponentInChildren<TMP_Text>();
                    if (has && label) TextSafety.Set(label, "Olvidar " + data.MoveName(mon.Moves[i]));
                }
            }
        }

        private static void ItemDefinitionRef(GameData data, string id, out CTEditor.GameDefinition.Domain.Items.ItemDefinition item)
        {
            item = null;
            if (id != null) data.TryGetItem(id, out item);
        }

        private static string AiLabel(CTEditor.GameDefinition.Domain.Trainers.TrainerAi ai)
            => ai == CTEditor.GameDefinition.Domain.Trainers.TrainerAi.Expert ? "🧠 Experto"
             : ai == CTEditor.GameDefinition.Domain.Trainers.TrainerAi.Smart ? "🎯 Listo" : "🎲 Novato";

        // "Charmander Nv.7  PS 22/24  EXP 45%  [Quemado]  · Placaje, Gruñido, Ascuas"
        private static string Describe(MonsterInstance m, GameData data)
        {
            var species = data.SpeciesOf(m);
            string xp = "";
            if (species != null)
            {
                var curve = data.CurveFor(species);
                int lvl = m.Level.Value;
                if (lvl < curve.MaxLevel)
                {
                    int from = curve.XpToReachLevel(lvl), to = curve.XpToReachLevel(lvl + 1);
                    int pct = to > from ? Mathf.Clamp(100 * (m.Experience.Value - from) / (to - from), 0, 100) : 100;
                    xp = $"  EXP {pct}%";
                }
            }
            string hp = m.IsFainted ? "<color=#FF6B6B>debilitado</color>" : $"PS {m.CurrentHp}/{m.MaxHp}";
            string status = !m.Status.HasValue ? ""
                : data.Statuses.TryGet(new Id<CTEditor.GameDefinition.Domain.Status.StatusConditionDefinition>(m.Status.Value.Value), out var st)
                    ? $"  [{st.DisplayName}]" : $"  [{m.Status.Value.Value}]";
            string held = string.IsNullOrEmpty(m.HeldItem) ? "" : $"  🎒{data.ItemName(m.HeldItem)}";
            string moves = string.Join(", ", m.Moves.Select(data.MoveName));
            return $"<b>{data.NameOf(m)}</b> Nv.{m.Level.Value}  {hp}{xp}{status}{held}\n      <size=80%>{moves}</size>";
        }
    }
}
