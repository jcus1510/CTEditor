using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// INTERFAZ (puro): teclas, repetición de flechas, cursor de menús en lista y rejilla, navegación por
    /// posición, páginas y tecleo de la caja de texto, variables y los menús clásicos.
    /// </summary>
    public class InterfaceTests
    {
        // ---------------- Teclas ----------------

        [Test]
        public void Classic_bindings_have_no_problems_and_menu_may_share_cancel_keys()
        {
            var b = ClassicInterface.Bindings();
            Assert.AreEqual(0, b.Problems().Count, string.Join(" | ", b.Problems()));
            Assert.IsTrue(b.KeysOf(GameButton.Confirm).Contains("Z"));
            b.Bind(GameButton.Confirm, "X"); // X en Confirmar y Cancelar: conflicto
            Assert.IsTrue(b.Problems().Any(p => p.Contains("«X»")));
        }

        [Test]
        public void A_button_without_keys_is_reported()
        {
            var b = ClassicInterface.Bindings();
            b.Clear(GameButton.Up);
            Assert.IsTrue(b.Problems().Any(p => p.Contains("Arriba")));
        }

        [Test]
        public void Keyboard_and_pad_bindings_are_separate()
        {
            var b = ClassicInterface.Bindings();
            CollectionAssert.Contains(b.Keyboard(GameButton.Confirm), "Z");
            CollectionAssert.AreEqual(new[] { "Pad/South" }, b.Pad(GameButton.Confirm));
            b.Replace(GameButton.Confirm, true, new[] { "Pad/East", "Z" });   // "Z" no es de mando: se ignora
            CollectionAssert.AreEqual(new[] { "Pad/East" }, b.Pad(GameButton.Confirm));
            CollectionAssert.Contains(b.Keyboard(GameButton.Confirm), "Z");     // el teclado no se toca
            Assert.IsTrue(PadControls.IsPad("pad/south"));
            Assert.AreEqual("A / Cruz", PadControls.Short("Pad/South"));
            Assert.AreEqual("↑", PadControls.Friendly("UpArrow"));
            Assert.IsTrue(PadControls.All.All(p => p.id.StartsWith(PadControls.Prefix)));
        }

        [Test]
        public void Player_overrides_apply_over_the_author_and_survive_a_round_trip()
        {
            var defaults = ClassicInterface.Bindings();
            var o = new ControlOverrides();
            o.Set(GameButton.Confirm, false, new[] { "K" });
            var active = o.Apply(defaults);
            CollectionAssert.AreEqual(new[] { "K" }, active.Keyboard(GameButton.Confirm));
            CollectionAssert.AreEqual(new[] { "Pad/South" }, active.Pad(GameButton.Confirm)); // el mando sigue igual
            CollectionAssert.Contains(defaults.Keyboard(GameButton.Confirm), "Z");            // el del autor intacto

            var back = ControlOverrides.Parse(o.Serialize());
            CollectionAssert.AreEqual(new[] { "K" }, back.Get(GameButton.Confirm, false));
            Assert.IsNull(back.Get(GameButton.Confirm, true));
            Assert.IsTrue(ControlOverrides.Parse("basura;;Confirm=Z;Nada.teclado=Q").IsEmpty);
        }

        [Test]
        public void Rebinding_a_used_key_takes_it_from_the_other_button_except_cancel_and_menu()
        {
            var defaults = ClassicInterface.Bindings();
            var o = new ControlOverrides();
            string note = o.Rebind(GameButton.Confirm, "X", defaults);   // X era de Cancelar y Menú
            var active = o.Apply(defaults);
            CollectionAssert.AreEqual(new[] { "X" }, active.Keyboard(GameButton.Confirm));
            CollectionAssert.DoesNotContain(active.Keyboard(GameButton.Cancel), "X");
            CollectionAssert.DoesNotContain(active.Keyboard(GameButton.Menu), "X");
            Assert.IsTrue(note.Contains("Cancelar"));

            var o2 = new ControlOverrides();
            o2.Rebind(GameButton.Menu, "Backspace", defaults);             // Menú puede compartir con Cancelar
            CollectionAssert.Contains(o2.Apply(defaults).Keyboard(GameButton.Cancel), "Backspace");

            // Intercambio: Confirmar = solo Z; luego Z a Cancelar → Confirmar recibe las de Cancelar (nunca vacío).
            var o4 = new ControlOverrides();
            o4.Rebind(GameButton.Confirm, "Z", defaults);
            o4.Rebind(GameButton.Cancel, "Z", defaults);
            var a4 = o4.Apply(defaults);
            CollectionAssert.AreEqual(new[] { "Z" }, a4.Keyboard(GameButton.Cancel));
            Assert.IsTrue(a4.Keyboard(GameButton.Confirm).Count > 0);
            CollectionAssert.DoesNotContain(a4.Keyboard(GameButton.Confirm), "Z");

            // Red de seguridad: aunque lo guardado deje Confirmar vacío, vuelve a sus teclas.
            var broken = ControlOverrides.Parse("Confirm.teclado=");
            Assert.IsTrue(broken.Apply(defaults).Keyboard(GameButton.Confirm).Count > 0);

            var o3 = new ControlOverrides();
            o3.Rebind(GameButton.Cancel, "Pad/South", defaults);           // mando: A era lo único de Confirmar → se intercambian
            var a3 = o3.Apply(defaults);
            CollectionAssert.AreEqual(new[] { "Pad/South" }, a3.Pad(GameButton.Cancel));
            CollectionAssert.AreEqual(new[] { "Pad/East" }, a3.Pad(GameButton.Confirm));
        }

        // ---------------- Pulsar y repetir ----------------

        [Test]
        public void Pressed_lasts_one_frame_and_holding_repeats_after_the_delay()
        {
            var s = new InputState { RepeatDelay = 0.4f, RepeatInterval = 0.1f };
            bool down = true;
            s.Update(0.016f, b => b == GameButton.Down && down);
            Assert.IsTrue(s.Pressed(GameButton.Down));
            Assert.IsTrue(s.Repeated(GameButton.Down));
            s.Update(0.2f, b => b == GameButton.Down && down);
            Assert.IsFalse(s.Pressed(GameButton.Down));
            Assert.IsFalse(s.Repeated(GameButton.Down));   // aún no llega al retraso
            s.Update(0.25f, b => b == GameButton.Down && down);
            Assert.IsTrue(s.Repeated(GameButton.Down));    // 0,45 s ≥ 0,4
            s.Update(0.05f, b => b == GameButton.Down && down);
            Assert.IsFalse(s.Repeated(GameButton.Down));
            s.Update(0.06f, b => b == GameButton.Down && down);
            Assert.IsTrue(s.Repeated(GameButton.Down));    // cada 0,1 s
            down = false;
            s.Update(0.016f, b => b == GameButton.Down && down);
            Assert.IsTrue(s.Released(GameButton.Down));
            Assert.AreEqual(Direction.None, s.RepeatedDirection());
        }

        [Test]
        public void A_consumed_button_is_not_seen_again_this_frame()
        {
            var s = new InputState();
            s.Update(0.016f, b => b == GameButton.Confirm);
            Assert.IsTrue(s.Pressed(GameButton.Confirm));
            s.Consume(GameButton.Confirm);
            Assert.IsFalse(s.Pressed(GameButton.Confirm));
            s.Update(0.016f, b => false);
            s.Update(0.016f, b => b == GameButton.Menu);
            s.ConsumeAll();
            Assert.IsFalse(s.Pressed(GameButton.Menu));
        }

        // ---------------- Cursor de menús ----------------

        [Test]
        public void List_cursor_wraps_and_skips_disabled_options()
        {
            var c = new MenuCursor(4, 1, wrap: true, enabled: i => i != 1);
            Assert.AreEqual(0, c.Index);
            c.Move(Direction.Down); Assert.AreEqual(2, c.Index);   // salta la 1
            c.Move(Direction.Down); Assert.AreEqual(3, c.Index);
            c.Move(Direction.Down); Assert.AreEqual(0, c.Index);   // vuelve arriba
            c.Move(Direction.Up); Assert.AreEqual(3, c.Index);
            Assert.IsFalse(c.Move(Direction.Left));                // en lista no hay columnas
        }

        [Test]
        public void List_cursor_without_wrap_stops_at_the_edges()
        {
            var c = new MenuCursor(3, 1, wrap: false);
            Assert.IsFalse(c.Move(Direction.Up));
            c.Move(Direction.Down); c.Move(Direction.Down);
            Assert.AreEqual(2, c.Index);
            Assert.IsFalse(c.Move(Direction.Down));
        }

        [Test]
        public void Grid_cursor_moves_like_the_battle_menu()
        {
            // LUCHAR  MOCHILA
            // POKéMON HUIR
            var c = new MenuCursor(4, 2);
            c.Move(Direction.Right); Assert.AreEqual(1, c.Index);
            c.Move(Direction.Down); Assert.AreEqual(3, c.Index);
            c.Move(Direction.Left); Assert.AreEqual(2, c.Index);
            c.Move(Direction.Up); Assert.AreEqual(0, c.Index);
            c.Move(Direction.Left); Assert.AreEqual(1, c.Index);   // vuelta en la fila
        }

        [Test]
        public void Grid_cursor_goes_to_the_last_cell_of_a_short_row()
        {
            // 0 1 2
            // 3 4
            var c = new MenuCursor(5, 3, start: 2);
            c.Move(Direction.Down);
            Assert.AreEqual(4, c.Index);
        }

        [Test]
        public void Start_position_skips_to_an_enabled_option()
        {
            var c = new MenuCursor(3, 1, enabled: i => i == 2, start: 0);
            Assert.AreEqual(2, c.Index);
        }

        // ---------------- Navegación por posición ----------------

        [Test]
        public void Spatial_navigation_picks_the_nearest_button_in_that_direction()
        {
            // Rejilla 2×2 (y hacia arriba) + un botón «Atrás» abajo a la derecha.
            var pos = new List<(float x, float y)> { (0, 100), (200, 100), (0, 0), (200, 0), (320, -80) };
            Assert.AreEqual(1, SpatialNavigator.Next(0, Direction.Right, pos));
            Assert.AreEqual(2, SpatialNavigator.Next(0, Direction.Down, pos));
            Assert.AreEqual(3, SpatialNavigator.Next(2, Direction.Right, pos));
            Assert.AreEqual(4, SpatialNavigator.Next(3, Direction.Down, pos));
            Assert.AreEqual(0, SpatialNavigator.Next(1, Direction.Left, pos));
            // Desactivado: se lo salta y va al siguiente en esa dirección.
            Assert.AreEqual(4, SpatialNavigator.Next(1, Direction.Down, pos, i => i != 3));
        }

        [Test]
        public void Spatial_navigation_wraps_to_the_other_side()
        {
            var pos = new List<(float x, float y)> { (0, 200), (0, 100), (0, 0) };
            Assert.AreEqual(0, SpatialNavigator.Next(2, Direction.Down, pos));
            Assert.AreEqual(2, SpatialNavigator.Next(2, Direction.Down, pos, wrap: false));
            Assert.AreEqual(0, SpatialNavigator.Next(-1, Direction.Down, pos)); // sin selección: el primero
        }

        // ---------------- Texto ----------------

        [Test]
        public void Variables_are_replaced_ignoring_case_and_unknown_ones_are_kept()
        {
            var v = new Dictionary<string, string> { ["jugador"] = "Rojo", ["dinero"] = "3000" };
            Assert.AreEqual("¡Hola, Rojo! Tienes 3000₽. {Rival}", TextTokens.Replace("¡Hola, {JUGADOR}! Tienes {dinero}₽. {Rival}", v));
            CollectionAssert.AreEqual(new[] { "jugador", "rival" }, TextTokens.Find("{jugador} y {rival} y {jugador}"));
        }

        [Test]
        public void Pager_wraps_by_words_and_a_blank_line_starts_a_new_page()
        {
            var pages = TextPager.Paginate("Hola, soy el Profesor Encina y estudio a los monstruos.\n\nAdiós.", 20, 2);
            Assert.AreEqual(3, pages.Count, string.Join(" || ", pages));
            Assert.IsTrue(pages[0].Split('\n').All(l => l.Length <= 20));
            Assert.AreEqual("Adiós.", pages[2]);
            Assert.AreEqual(5, TextPager.VisibleLength("<color=red>Rojo</color>!"));
        }

        [Test]
        public void Typewriter_shows_tags_whole_and_can_be_completed()
        {
            var t = new Typewriter("<b>Hola</b> mundo", 10f);
            Assert.AreEqual(10, t.TotalVisible);
            t.Tick(0.25f); // 2,5 → 2 caracteres
            Assert.AreEqual("<b>Ho", t.Visible);
            Assert.IsFalse(t.IsDone);
            t.Complete();
            Assert.AreEqual("<b>Hola</b> mundo", t.Visible);
            Assert.IsTrue(new Typewriter("instantáneo", 0f).IsDone);
        }

        // ---------------- Menús clásicos ----------------

        [Test]
        public void Classic_menus_are_valid_and_submenus_exist()
        {
            var ids = ClassicInterface.Menus().Select(m => m.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
            foreach (var m in ClassicInterface.Menus())
                Assert.AreEqual(0, m.Problems(ids.Contains).Count, m.Id + ": " + string.Join(" | ", m.Problems(ids.Contains)));
        }

        [Test]
        public void Every_screen_action_of_the_classic_menus_is_known_by_its_screen()
        {
            var known = ScreenActions.Known.Select(k => k.id).ToList();
            foreach (var m in ClassicInterface.Menus())
                foreach (var o in m.Options.Where(o => o.Action == MenuActionKind.ScreenAction))
                    Assert.IsTrue(known.Contains(o.Target), $"{m.Id}/{o.Id}: acción «{o.Target}» desconocida");
            Assert.AreEqual(4, ClassicInterface.Battle().Options.Count);
            Assert.AreEqual(2, ClassicInterface.Battle().Columns);
        }

        [Test]
        public void Copying_bindings_keeps_keys_and_repeat_timing()
        {
            var b = ClassicInterface.Bindings();
            b.RepeatDelay = 0.3f;
            var c = b.Copy();
            c.Bind(GameButton.Confirm, "J");
            Assert.AreEqual(0.3f, c.RepeatDelay, 1e-5);
            Assert.IsFalse(b.KeysOf(GameButton.Confirm).Contains("J")); // la copia no toca el original
            CollectionAssert.AreEqual(new[] { GameButton.Cancel, GameButton.Menu }, b.ButtonsOf("Escape"));
            var classicPad = new InputBindings().Bind(GameButton.Confirm, "Z").WithClassicPad();
            CollectionAssert.AreEqual(new[] { "Pad/South" }, classicPad.Pad(GameButton.Confirm));
        }

        [Test]
        public void Options_hide_or_show_with_game_flags()
        {
            var pause = ClassicInterface.Pause();
            Assert.IsFalse(pause.VisibleOptions(f => false).Any(o => o.Id == "pokedex"));
            Assert.IsTrue(pause.VisibleOptions(f => f == "tiene_pokedex").Any(o => o.Id == "pokedex"));
            var m = new MenuDefinition("x", "x").Add(new MenuOption("a", "A", MenuActionKind.Close, hiddenByFlag: "fin"));
            Assert.AreEqual(0, m.VisibleOptions(f => f == "fin").Count);
        }

        [Test]
        public void Menu_problems_are_explained()
        {
            var m = new MenuDefinition("m", "m") { CancelCloses = false, CancelOptionId = "zz" }
                .Add(new MenuOption("a", "A", MenuActionKind.OpenMenu, ""))
                .Add(new MenuOption("a", "", MenuActionKind.ScreenAction, ""));
            var p = m.Problems(id => false);
            Assert.IsTrue(p.Any(x => x.Contains("repetido")));
            Assert.IsTrue(p.Any(x => x.Contains("no dice cuál")));
            Assert.IsTrue(p.Any(x => x.Contains("sin id")));
            Assert.IsTrue(p.Any(x => x.Contains("zz")));
        }
    }
}
