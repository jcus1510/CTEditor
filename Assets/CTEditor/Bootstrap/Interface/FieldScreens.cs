using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// LAS PANTALLAS DEL MENÚ DE PAUSA (equipo, mochila, resumen, ficha, opciones), hechas con los
    /// menús del editor y la caja de texto. Toda la lógica es la de FieldActions (dominio): aquí solo se
    /// pregunta y se enseña.
    ///
    /// Equipo: elegir miembro → menú «equipo_miembro» (DATOS · MOVER · OBJETO → «equipo_objeto»: DAR/QUITAR).
    /// Mochila: elegir objeto → menú «mochila_objeto» (USAR · DAR) → elegir miembro.
    /// Los textos, el orden y las opciones de esos menús se cambian en el editor de Menús.
    /// </summary>
    public static class FieldScreens
    {
        private static PartyHolder Holder => PartyHolder.Current;
        private static PlayerSave Save => Holder != null ? Holder.Save : null;
        private static GameData Data => Holder != null ? Holder.Data : null;
        private static TextBox Box => UiRoot.Instance.Text;
        private static Dictionary<string, string> Vars => UiContent.Variables(Save);

        private static bool Ready(out string why)
        {
            why = Holder == null ? "No hay partida (falta «Partida del jugador» en la escena)." : Data == null ? "No se pudo cargar el contenido." : null;
            return why == null;
        }

        // ---------------- Equipo ----------------

        public static IEnumerator Party()
        {
            if (!Ready(out var why)) { yield return Box.Say(why); yield break; }
            if (Save.Party.Count == 0) { yield return Box.Say("No tienes a nadie en el equipo."); yield break; }

            while (true)
            {
                var list = MenuOpener.OpenList("equipo_lista", "EQUIPO", MemberLabels(), MenuAnchor.Center, null, MemberHelp(), 700f, 6, remember: true);
                yield return list.Wait();
                int who = list.ResultIndex;
                list.Close();
                if (who < 0) yield break;

                yield return RunMenu(UiContent.Menu(ClassicInterface.PartyMember), action => MemberAction(who, action));
            }
        }

        private static IEnumerator MemberAction(int who, string action)
        {
            switch (action)
            {
                case ScreenActions.Summary: yield return Summary(who); break;
                case ScreenActions.Swap:
                {
                    int other = -1;
                    yield return ChooseMember($"¿Con quién cambias a {Name(who)}?", i => other = i, i => i != who);
                    if (other >= 0) yield return Show(FieldActions.Swap(Data, Save, who, other));
                    break;
                }
                case ScreenActions.Give:
                {
                    string item = null;
                    yield return ChooseItem("¿Qué objeto le das?", FieldActions.GivableItems(Data, Save), id => item = id);
                    if (item != null) yield return Show(FieldActions.GiveItem(Data, Save, who, item));
                    break;
                }
                case ScreenActions.Take: yield return Show(FieldActions.TakeItem(Data, Save, who)); break;
                default: yield return Box.Say($"(La acción «{action}» no hace nada en el equipo.)"); break;
            }
        }

        private static List<string> MemberLabels()
        {
            var l = new List<string>();
            for (int i = 0; i < Save.Party.Count; i++)
            {
                var m = Save.Party.Members[i];
                string status = m.Status.HasValue ? $"  [{(Data.Statuses.TryGet(new Id<CTEditor.GameDefinition.Domain.Status.StatusConditionDefinition>(m.Status.Value.Value), out var st) ? st.DisplayName : m.Status.Value.Value)}]" : "";
                string item = string.IsNullOrEmpty(m.HeldItem) ? "" : "  •";
                l.Add($"{Data.NameOf(m)}  Nv.{m.Level.Value}   PS {m.CurrentHp}/{m.MaxHp}{status}{item}{(m.IsFainted ? "  (debilitado)" : "")}");
            }
            return l;
        }

        private static List<string> MemberHelp()
            => Save.Party.Members.Select(m => string.IsNullOrEmpty(m.HeldItem) ? "No lleva objeto." : "Lleva: " + Data.ItemName(m.HeldItem)).ToList();

        private static string Name(int i) => i >= 0 && i < Save.Party.Count ? Data.NameOf(Save.Party.Members[i]) : "?";

        /// <summary>Elige un miembro (-1 si cancela). 'enabled' marca los que se pueden elegir.</summary>
        public static IEnumerator ChooseMember(string prompt, Action<int> result, Func<int, bool> enabled = null)
        {
            Box.Show(prompt, Vars);
            var list = MenuOpener.OpenList("equipo_lista", "", MemberLabels(), MenuAnchor.Center, enabled, null, 700f, 6);
            yield return list.Wait();
            int i = list.ResultIndex;
            list.Close();
            Box.Hide();
            result(i);
        }

        // ---------------- Resumen ----------------

        public static IEnumerator Summary(int who)
        {
            if (!Ready(out var why)) { yield return Box.Say(why); yield break; }
            if (who < 0 || who >= Save.Party.Count) yield break;
            var s = FieldActions.Summary(Data, Save.Party.Members[who]);
            var text = new List<string>
            {
                (s.Dex.Length > 0 ? s.Dex + "\n" : "") +
                $"{s.Name}{(s.Name != s.SpeciesName ? $" ({s.SpeciesName})" : "")} · Nv. {s.Level} · {string.Join("/", s.Types)}\n" +
                $"PS {s.Hp}/{s.MaxHp}{(s.StatusName.Length > 0 ? " · " + s.StatusName : "")} · Objeto: {(s.HeldItemName.Length > 0 ? s.HeldItemName : "nada")}",
                $"Naturaleza {s.NatureName} · Habilidad {s.AbilityName}\n" +
                (s.ExperienceToNext > 0 ? $"Experiencia {s.Experience} (faltan {s.ExperienceToNext})" : $"Experiencia {s.Experience} (nivel máximo)"),
                string.Join("\n", s.Stats.Take(3).Select(Stat)),
                string.Join("\n", s.Stats.Skip(3).Select(Stat)),
            };
            for (int i = 0; i < s.Moves.Count; i += 2)
                text.Add(string.Join("\n", s.Moves.Skip(i).Take(2).Select(m => $"{m.Name}  {m.TypeName}  PP {m.Pp}/{m.MaxPp}{(m.Power > 0 ? $"  Pot. {m.Power}" : "")}")));
            // Cada bloque en su caja (la caja reparte más si no cabe).
            yield return Box.Say(string.Join("\n\n", text), Vars);
        }

        private static string Stat(MonsterSummary.StatLine l)
            => $"{l.Name} {l.Value}{(l.NatureEffect > 0 ? " ▲" : l.NatureEffect < 0 ? " ▼" : "")}  (IV {l.Iv} · EV {l.Ev})";

        // ---------------- Mochila ----------------

        public static IEnumerator Bag()
        {
            if (!Ready(out var why)) { yield return Box.Say(why); yield break; }
            while (true)
            {
                var items = Save.Bag.Contents().Where(c => c.count > 0)
                    .Select(c => Data.TryGetItem(c.itemId, out var it) ? (it, c.count) : (null, 0))
                    .Where(x => x.it != null).OrderBy(x => x.it.Category).ThenBy(x => x.it.DisplayName).ToList();
                if (items.Count == 0) { yield return Box.Say("La mochila está vacía."); yield break; }

                var list = MenuOpener.OpenList("mochila_lista", "MOCHILA", items.Select(x => $"{x.it.DisplayName}  ×{x.count}").ToList(), MenuAnchor.Center,
                    null, items.Select(x => x.it.Description ?? "").ToList(), 640f, 7, remember: true);
                yield return list.Wait();
                int pick = list.ResultIndex;
                list.Close();
                if (pick < 0) yield break;

                var item = items[pick].it;
                yield return RunMenu(UiContent.Menu(ClassicInterface.BagItem), action => ItemAction(item, action));
            }
        }

        private static IEnumerator ItemAction(CTEditor.GameDefinition.Domain.Items.ItemDefinition item, string action)
        {
            switch (action)
            {
                case ScreenActions.Use:
                {
                    if (!item.UsableOutsideBattle) { yield return Box.Say($"{item.DisplayName} no se puede usar aquí."); break; }
                    int who = -1;
                    yield return ChooseMember($"¿En quién usas {item.DisplayName}?", i => who = i);
                    if (who < 0) break;
                    var r = FieldActions.UseItem(Data, Save, who, item.Id);
                    yield return Show(r);
                    foreach (var move in r.PendingMoves.ToList()) yield return LearnFlow(who, move);
                    break;
                }
                case ScreenActions.GiveFromBag:
                {
                    if (!FieldActions.CanBeHeld(item)) { yield return Box.Say($"{item.DisplayName} es un objeto clave: no se puede llevar."); break; }
                    int who = -1;
                    yield return ChooseMember($"¿A quién le das {item.DisplayName}?", i => who = i);
                    if (who >= 0) yield return Show(FieldActions.GiveItem(Data, Save, who, item.Id));
                    break;
                }
                default: yield return Box.Say($"(La acción «{action}» no hace nada en la mochila.)"); break;
            }
        }

        private static IEnumerator ChooseItem(string prompt, List<(CTEditor.GameDefinition.Domain.Items.ItemDefinition item, int count)> items, Action<string> result)
        {
            if (items.Count == 0) { yield return Box.Say("No tienes objetos para eso."); result(null); yield break; }
            Box.Show(prompt, Vars);
            var list = MenuOpener.OpenList("mochila_lista", "", items.Select(x => $"{x.item.DisplayName}  ×{x.count}").ToList(), MenuAnchor.Center, null, null, 640f, 7);
            yield return list.Wait();
            int i = list.ResultIndex;
            list.Close();
            Box.Hide();
            result(i >= 0 ? items[i].item.Id : null);
        }

        // ---------------- Aprender movimiento (tras una piedra, una MT...) ----------------

        public static IEnumerator LearnFlow(int who, Id<Move> move)
        {
            var mon = Save.Party.Members[who];
            string name = Data.NameOf(mon), mv = Data.MoveName(move);
            bool yes = false;
            yield return Box.Say($"{name} quiere aprender {mv}, pero ya conoce {mon.Moves.Count} movimientos.");
            yield return Box.Ask($"¿Olvidar uno para aprender {mv}?", a => yes = a);
            int slot = -1;
            if (yes)
            {
                Box.Show("¿Qué movimiento olvida?");
                var list = MenuOpener.OpenList("olvidar", "", mon.Moves.Select(m => Data.MoveName(m)).ToList(), MenuAnchor.Center, null, null, 480f, 4);
                yield return list.Wait();
                slot = list.ResultIndex;
                list.Close();
                Box.Hide();
            }
            yield return Show(FieldActions.LearnMove(Data, Save, who, move, slot));
        }

        // ---------------- Ficha, opciones ----------------

        public static IEnumerator TrainerCard()
        {
            if (!Ready(out var why)) { yield return Box.Say(why); yield break; }
            yield return Box.Say($"FICHA DE ENTRENADOR\n{Save.PlayerName} · Dinero: {Save.Money}₽ · Equipo: {Save.Party.Count} · Hora: {Save.World.Hour:00}:00");
        }

        /// <summary>OPCIONES: velocidad del texto y CONTROLES (el jugador cambia teclas y botones del mando).</summary>
        public static IEnumerator Options()
        {
            while (true)
            {
                var def = new MenuDefinition("opciones", "Opciones") { Title = "OPCIONES", Anchor = MenuAnchor.Center }
                    .Add(new MenuOption("texto", "VELOCIDAD DEL TEXTO", MenuActionKind.ScreenAction, "texto"))
                    .Add(new MenuOption("controles", "CONTROLES", MenuActionKind.ScreenAction, "controles"))
                    .Add(new MenuOption("salir", "SALIR", MenuActionKind.Close));
                var menu = MenuOpener.Open("opciones", def);
                yield return menu.Wait();
                var r = menu.Result;
                menu.Close();
                if (r == null || r.Action == MenuActionKind.Close) yield break;
                if (r.Target == "texto") yield return TextSpeed();
                else yield return Controls();
            }
        }

        private static IEnumerator TextSpeed()
        {
            var speeds = new[] { ("LENTO", 20f), ("NORMAL", 40f), ("RÁPIDO", 80f), ("INSTANTÁNEO", 0f) };
            float current = GameInput.Settings.TextSpeed;
            int start = Array.FindIndex(speeds, s => Math.Abs(s.Item2 - current) < 0.1f);
            var def = new MenuDefinition("opciones_texto", "Velocidad del texto") { Title = "VELOCIDAD DEL TEXTO", Anchor = MenuAnchor.Center, RememberCursor = false };
            for (int i = 0; i < speeds.Length; i++) def.Add(new MenuOption(i.ToString(), speeds[i].Item1, MenuActionKind.ScreenAction, i.ToString()));
            var menu = MenuView.Open(def, null, null, null, 8, 0f, Math.Max(0, start));
            yield return menu.Wait();
            int pick = menu.ResultIndex;
            menu.Close();
            if (pick >= 0)
            {
                GameInput.SetPlayerTextSpeed(speeds[pick].Item2);
                yield return Box.Say("Así se verá el texto a partir de ahora.");
            }
        }

        // ---------------- Controles (el jugador los cambia) ----------------

        /// <summary>
        /// CONTROLES: cada botón del juego con sus teclas y su botón de mando. Al elegir uno:
        /// CAMBIAR TECLA / CAMBIAR BOTÓN DEL MANDO (se pulsa el nuevo) / POR DEFECTO. Se guarda solo.
        /// </summary>
        public static IEnumerator Controls()
        {
            var buttons = GameButtons.All;
            while (true)
            {
                var b = GameInput.Settings.Bindings;
                var labels = buttons.Select(x =>
                    $"{GameButtons.NameOf(x)}:  {Join(b.Keyboard(x))}   |   {Join(b.Pad(x))}{(GameInput.PlayerChanged(x) ? "  *" : "")}").ToList();
                labels.Add("RESTAURAR TODO POR DEFECTO");
                var list = MenuOpener.OpenList("controles", "CONTROLES   (teclado | mando)", labels, MenuAnchor.Center, null, null, 1100f, 8);
                yield return list.Wait();
                int pick = list.ResultIndex;
                list.Close();
                if (pick < 0) yield break;
                if (pick == buttons.Length)
                {
                    bool yes = false;
                    yield return Box.Ask("¿Volver a los controles por defecto?", a => yes = a);
                    if (yes) { GameInput.ResetPlayer(); yield return Box.Say("Controles restaurados."); }
                    continue;
                }

                var button = buttons[pick];
                var def = new MenuDefinition("control_boton", GameButtons.NameOf(button)) { Title = GameButtons.NameOf(button).ToUpperInvariant(), Anchor = MenuAnchor.Center, RememberCursor = false }
                    .Add(new MenuOption("tecla", "CAMBIAR TECLA", MenuActionKind.ScreenAction, "tecla"))
                    .Add(new MenuOption("mando", "CAMBIAR BOTÓN DEL MANDO", MenuActionKind.ScreenAction, "mando"))
                    .Add(new MenuOption("defecto", "POR DEFECTO", MenuActionKind.ScreenAction, "defecto"))
                    .Add(new MenuOption("salir", "SALIR", MenuActionKind.Close));
                var sub = MenuView.Open(def);
                yield return sub.Wait();
                var r = sub.Result;
                sub.Close();
                if (r == null || r.Action == MenuActionKind.Close) continue;
                if (r.Target == "defecto") { GameInput.ResetPlayer(button); yield return Box.Say($"«{GameButtons.NameOf(button)}» vuelve a sus controles por defecto."); continue; }

                bool pad = r.Target == "mando";
                Box.Show(pad ? $"Pulsa en el MANDO el botón para «{GameButtons.NameOf(button)}»... (5 s)"
                             : $"Pulsa la TECLA para «{GameButtons.NameOf(button)}»... (5 s)");
                UiRoot.Register(Box);         // nadie más lee las teclas mientras tanto
                string got = null;
                yield return GameInput.Capture(pad, 5f, x => got = x);
                UiRoot.Unregister(Box);
                Box.Hide();
                if (got == null) { yield return Box.Say("No se pulsó nada: sin cambios."); continue; }
                string note = GameInput.Rebind(button, got);
                yield return Box.Say($"«{GameButtons.NameOf(button)}» = {PadControls.Friendly(got)}." + (note.Length > 0 ? "\n" + note : ""));
            }
        }

        private static string Join(List<string> bindings) => bindings.Count == 0 ? "—" : string.Join(", ", bindings.Take(3).Select(PadControls.Short));

        // ---------------- Utilidades ----------------

        /// <summary>
        /// Ejecuta un submenú (del editor): abre otros menús, cierra, o pasa las acciones de pantalla a 'onAction'.
        /// Vuelve cuando el jugador cierra o cancela el menú.
        /// </summary>
        public static IEnumerator RunMenu(MenuDefinition def, Func<string, IEnumerator> onAction)
        {
            var stack = new List<MenuDefinition> { def };
            while (stack.Count > 0)
            {
                var top = stack[stack.Count - 1];
                var menu = MenuOpener.Open(top.Id, top, f => Save != null && Save.World.HasFlag(f), null, Vars);
                yield return menu.Wait();
                var r = menu.Result;
                menu.Close();
                if (r == null) { stack.RemoveAt(stack.Count - 1); continue; }
                switch (r.Action)
                {
                    case MenuActionKind.Close: stack.Clear(); break;
                    case MenuActionKind.OpenMenu:
                        var sub = MenuOpener.Definition(r.Target);
                        if (sub != null) stack.Add(sub); else yield return Box.Say($"(El menú «{r.Target}» no existe.)");
                        break;
                    case MenuActionKind.ScreenAction:
                        yield return onAction(r.Target);
                        stack.Clear(); // tras la acción se vuelve a la lista (como en los juegos)
                        break;
                    default:
                        yield return Common(r);
                        break;
                }
            }
        }

        /// <summary>Las acciones que valen desde cualquier menú.</summary>
        public static IEnumerator Common(MenuOption r)
        {
            switch (r.Action)
            {
                case MenuActionKind.OpenParty: yield return Party(); break;
                case MenuActionKind.OpenBag: yield return Bag(); break;
                case MenuActionKind.OpenSummary: yield return Summary(0); break;
                case MenuActionKind.OpenTrainerCard: yield return TrainerCard(); break;
                case MenuActionKind.Options: yield return Options(); break;
                case MenuActionKind.Save: yield return Box.Say("Guardar la partida llegará pronto (bloque «Guardar y cargar»)."); break;
                case MenuActionKind.OpenPokedex: yield return Box.Say("La Pokédex llegará pronto."); break;
            }
        }

        private static IEnumerator Show(FieldResult r)
        {
            if (r == null) yield break;
            yield return Box.SayAll(r.Messages, Vars);
        }
    }
}
