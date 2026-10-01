using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Project;
using CTEditor.Workspace;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Aplicación: JSON propio, «proyecto.json» (tile de 32 px por defecto, base RPG Maker XP / Essentials), archivo de
    /// corte con propiedades de tile como RPG Maker XP, y el entorno de trabajo editable (tema, paneles, atajos).
    /// </summary>
    public class WorkspaceTests
    {
        private static string TempDir() => Path.Combine(Path.GetTempPath(), "cteditor_ws_" + Guid.NewGuid().ToString("N"));

        [Test]
        public void Json_round_trips_and_keeps_key_order()
        {
            var o = new JsonObject()
                .Set("z", 1).Set("a", "texto \"con\" comillas\ny salto").Set("n", 2.5).Set("b", true).Set("nada", null)
                .Set("lista", new List<object> { 1.0, "dos", false })
                .Set("anidado", new JsonObject().Set("x", -3));

            var text = Json.Write(o);
            var back = Json.ParseObject(text);

            CollectionAssert.AreEqual(new[] { "z", "a", "n", "b", "nada", "lista", "anidado" }, back.Keys.ToArray());
            Assert.AreEqual(1, back.GetInt("z"));
            Assert.AreEqual("texto \"con\" comillas\ny salto", back.GetString("a"));
            Assert.AreEqual(2.5, back.GetNumber("n"));
            Assert.IsTrue(back.GetBool("b"));
            Assert.IsNull(back["nada"]);
            Assert.AreEqual("dos", back.GetArray("lista")[1]);
            Assert.AreEqual(-3, back.GetObject("anidado").GetInt("x"));
            StringAssert.Contains("\"z\": 1,", text, "integers are written without decimals");
            Assert.AreEqual(text, Json.Write(back), "writing again gives the same text");
        }

        [Test]
        public void Json_errors_say_where()
        {
            var e = Assert.Throws<FormatException>(() => Json.Parse("{\n  \"a\": 1,\n  \"b\" 2\n}"));
            StringAssert.Contains("línea 3", e.Message);
            StringAssert.Contains("«:»", e.Message);
            Assert.Throws<FormatException>(() => Json.Parse("[1, 2"));
            Assert.Throws<FormatException>(() => Json.Parse("{} extra"));
            Assert.AreEqual("é", Json.Parse("\"\\u00e9\""));
        }

        [Test]
        public void New_project_defaults_to_rpg_maker_xp_and_the_tile_size_is_chosen_at_creation()
        {
            Assert.AreEqual(32, ProjectSettings.TileSizes[0].Size, "the recommended size comes first");
            var root = TempDir();
            var root16 = TempDir();
            try
            {
                var s = ProjectFile.Create(root, "Mi región");
                Assert.AreEqual(32, s.TileSize);
                Assert.AreEqual((512, 384), (s.ScreenWidth, s.ScreenHeight));
                Assert.IsTrue(Directory.Exists(Path.Combine(root, "graficos", "tilesets")));
                Assert.IsTrue(Directory.Exists(Path.Combine(root, "mapas")));

                var loaded = ProjectFile.Load(root);
                Assert.AreEqual("Mi región", loaded.Name);
                Assert.AreEqual(32, loaded.TileSize);
                Assert.Throws<InvalidOperationException>(() => ProjectFile.Create(root, "Otra"));

                Assert.AreEqual(16, ProjectFile.Create(root16, "GBA", 16).TileSize);
                Assert.AreEqual(16, ProjectFile.Load(root16).TileSize);
                Assert.Throws<ArgumentException>(() => ProjectFile.Create(TempDir(), "Mal", 3));
                Assert.IsNotEmpty(new ProjectSettings { Format = 99 }.Problems());
            }
            finally
            {
                foreach (var d in new[] { root, root16 }) if (Directory.Exists(d)) Directory.Delete(d, true);
            }
        }

        [Test]
        public void Slice_file_keeps_settings_and_rpg_maker_style_tile_properties()
        {
            var file = new SliceFile(new SliceSettings(32, 32, 1, 2, 3, 4));
            file.Set(5, new TileProperties { Blocked = PassageBlock.All, Priority = 1 });
            file.Set(9, new TileProperties { TerrainTag = 2, Bush = true });
            file.Set(12, new TileProperties { Blocked = PassageBlock.Down | PassageBlock.Up, Counter = true });
            file.Set(20, new TileProperties()); // default: not stored

            var back = SliceFile.FromJson(Json.ParseObject(Json.Write(file.ToJson())));

            Assert.AreEqual((32, 32, 1, 2, 3, 4), (back.Settings.TileWidth, back.Settings.TileHeight, back.Settings.OffsetX,
                back.Settings.OffsetY, back.Settings.SpacingX, back.Settings.SpacingY));
            CollectionAssert.AreEqual(new[] { 5, 9, 12 }, back.CustomizedTiles.ToArray());
            Assert.AreEqual(PassageBlock.All, back.Get(5).Blocked);
            Assert.AreEqual(1, back.Get(5).Priority);
            Assert.AreEqual(2, back.Get(9).TerrainTag);
            Assert.IsTrue(back.Get(9).Bush);
            Assert.AreEqual(PassageBlock.Down | PassageBlock.Up, back.Get(12).Blocked);
            Assert.IsTrue(back.Get(12).Counter);
            Assert.IsTrue(back.Get(100).IsDefault);
            Assert.AreEqual("hierba", TerrainCatalog.Essentials().Find(2).Key);
            Assert.IsTrue(TerrainCatalog.Essentials().Find(2).Encounters);

            // Get returns a copy: changing it without Set changes nothing.
            back.Get(5).Priority = 4;
            Assert.AreEqual(1, back.Get(5).Priority);
        }

        [Test]
        public void Dark_theme_by_default_and_any_color_can_be_changed()
        {
            var w = new WorkspaceSettings();
            Assert.AreEqual("Oscuro", w.Theme.Name);
            Assert.AreEqual("#1E1F22", w.Theme.Get("fondo"));
            foreach (var t in Theme.Tokens)
                foreach (var preset in Theme.Presets)
                    Assert.IsTrue(Theme.IsColor(preset().Get(t.Id)), $"{preset().Name} lacks {t.Id}");

            w.Theme.Set("acento", "#ff8800");
            Assert.AreEqual("#FF8800", w.Theme.Get("acento"));
            Assert.AreEqual("#4E8CF7", Theme.Dark().Get("acento"), "the preset itself is untouched");
            Assert.Throws<ArgumentException>(() => w.Theme.Set("acento", "rojo"));
        }

        [Test]
        public void Panels_can_be_docked_moved_closed_and_reopened()
        {
            var layout = DockLayout.Classic();
            var all = layout.OpenPanels().ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count(), "each panel once");
            Assert.IsTrue(layout.IsOpen(PanelCatalog.Map));

            // Drop «Capas» to the right of the map: the map group is split in two.
            var mapTabs = layout.TabsOf(PanelCatalog.Map);
            layout.Dock(PanelCatalog.Layers, mapTabs, DockSide.Right);
            var layersTabs = layout.TabsOf(PanelCatalog.Layers);
            Assert.AreNotSame(mapTabs, layersTabs);
            Assert.AreEqual(1, layersTabs.Panels.Count);
            AssertSizesAddUp(layout.Root);

            // Drop it into the map group as a tab.
            layout.Dock(PanelCatalog.Layers, layout.TabsOf(PanelCatalog.Map), DockSide.Center);
            Assert.AreSame(layout.TabsOf(PanelCatalog.Map), layout.TabsOf(PanelCatalog.Layers));
            Assert.AreEqual(PanelCatalog.Layers, layout.TabsOf(PanelCatalog.Map).ActivePanel);
            AssertSizesAddUp(layout.Root);

            // Closing every panel of a group removes the group; closing everything leaves the map.
            layout.Close(PanelCatalog.Inspector);
            Assert.IsFalse(layout.IsOpen(PanelCatalog.Inspector));
            layout.Open(PanelCatalog.Inspector, near: PanelCatalog.Palette);
            Assert.AreSame(layout.TabsOf(PanelCatalog.Palette), layout.TabsOf(PanelCatalog.Inspector));
            foreach (var p in layout.OpenPanels().ToList()) layout.Close(p);
            CollectionAssert.AreEqual(new[] { PanelCatalog.Map }, layout.OpenPanels().ToArray());
        }

        [Test]
        public void Separators_resize_within_limits_and_layouts_are_saved_with_a_name()
        {
            var w = new WorkspaceSettings();
            var root = (DockSplit)w.Layout.Root;
            w.Layout.Resize(root, 0, 0.99f);
            Assert.AreEqual(1f - DockLayout.MinSize, root.Children[0].Size, 1e-4);
            AssertSizesAddUp(root);

            w.Layout.Close(PanelCatalog.Messages);
            w.SaveLayoutAs("Mío");
            Assert.IsTrue(w.UseLayout("Arte"));
            Assert.IsTrue(w.Layout.IsOpen(PanelCatalog.PixelEditor));
            Assert.IsTrue(w.UseLayout("Mío"));
            Assert.IsFalse(w.Layout.IsOpen(PanelCatalog.Messages));
            Assert.IsFalse(w.UseLayout("No existe"));
            foreach (var preset in DockLayout.Presets)
            {
                var l = preset();
                var panels = l.OpenPanels().ToList();
                Assert.AreEqual(panels.Count, panels.Distinct().Count(), l.Name);
                Assert.IsTrue(panels.All(p => PanelCatalog.Find(p) != null), l.Name);
                AssertSizesAddUp(l.Root);
            }
        }

        [Test]
        public void Shortcuts_can_be_rebound_and_clashes_are_resolved()
        {
            var map = new ShortcutMap();
            Assert.AreEqual("F5", map.KeysFor("jugar"));
            Assert.AreEqual("Ctrl+Mayús+Z", ShortcutMap.Normalize("shift + ctrl + z"));

            var clashes = map.Rebind("rehacer", "ctrl+mayús+z");
            Assert.IsEmpty(clashes);
            Assert.AreEqual("rehacer", map.ActionFor("Ctrl+Mayús+Z"));

            clashes = map.Rebind("probar_aqui", "F5");
            CollectionAssert.AreEqual(new[] { "jugar" }, clashes.ToArray());
            Assert.AreEqual("", map.KeysFor("jugar"));
            Assert.Throws<ArgumentException>(() => map.Rebind("no_existe", "F1"));
            map.ResetToDefaults();
            Assert.AreEqual("F5", map.KeysFor("jugar"));
        }

        [Test]
        public void Default_shortcuts_are_unique_categorized_and_shown_nicely()
        {
            var map = new ShortcutMap();
            var used = ShortcutMap.Actions.Select(a => map.KeysFor(a.Id)).Where(k => k.Length > 0).ToList();
            CollectionAssert.AllItemsAreUnique(used, "two actions share a factory shortcut");
            Assert.IsTrue(ShortcutMap.Actions.All(a => !string.IsNullOrEmpty(a.Category)));
            Assert.AreEqual("Alpha1", ShortcutMap.Normalize("1"), "a typed digit is the number row");
            Assert.AreEqual("Ctrl+1", ShortcutMap.Pretty("Ctrl+Alpha1"));
            Assert.AreEqual("Av Pág", ShortcutMap.Pretty("PageDown"));
            Assert.AreEqual("modo_paso", map.ActionFor("2"));
            Assert.AreEqual("ventana_mundo", map.ActionFor("Ctrl+4"));
        }

        [Test]
        public void Each_window_remembers_its_own_scale()
        {
            var w = new WorkspaceSettings();
            Assert.AreEqual(1f, w.PanelScale("paleta"));
            w.SetPanelScale("paleta", 1.4f);
            w.SetPanelScale("mapa", 9f);
            w.SetPref("barra_mapa", "derecha");
            var back = WorkspaceSettings.FromJson(Json.ParseObject(Json.Write(w.ToJson())));
            Assert.AreEqual(1.4f, back.PanelScale("paleta"), 0.001f);
            Assert.AreEqual(WorkspaceSettings.MaxPanelScale, back.PanelScale("mapa"), 0.001f, "clamped");
            Assert.AreEqual("derecha", back.Pref("barra_mapa", "izquierda"));
            Assert.AreEqual("izquierda", back.Pref("otra", "izquierda"));
            back.SetPanelScale("paleta", 1f);
            Assert.AreEqual(1f, back.PanelScale("paleta"));
        }

        [Test]
        public void Workspace_is_saved_and_a_broken_file_falls_back_to_defaults()
        {
            var dir = TempDir();
            var path = Path.Combine(dir, WorkspaceSettings.FileName);
            try
            {
                var w = new WorkspaceSettings { UiScale = 1.25f, FontSize = 15 };
                w.Theme = Theme.Light();
                w.Theme.Set("acento", "#123456");
                w.Layout = DockLayout.Story();
                w.Layout.Dock(PanelCatalog.Palette, w.Layout.TabsOf(PanelCatalog.Events), DockSide.Bottom);
                w.SaveLayoutAs("Historia+tiles");
                w.Shortcuts.Rebind("guardar", "Ctrl+Alt+S");
                w.NoteRecentProject("C:/juegos/a");
                w.NoteRecentProject("C:/juegos/b");
                w.NoteRecentProject("C:/juegos/a");
                w.Save(path);

                var back = WorkspaceSettings.Load(path);
                Assert.AreEqual(1.25f, back.UiScale, 1e-4);
                Assert.AreEqual(15, back.FontSize);
                Assert.AreEqual("Claro", back.Theme.Name);
                Assert.AreEqual("#123456", back.Theme.Get("acento"));
                Assert.AreEqual("Historia", back.Layout.Name);
                CollectionAssert.AreEquivalent(w.Layout.OpenPanels().ToArray(), back.Layout.OpenPanels().ToArray());
                Assert.AreEqual(Json.Write(w.Layout.ToJson()), Json.Write(back.Layout.ToJson()), "same tree, same sizes");
                Assert.AreEqual("Historia+tiles", back.SavedLayouts.Single().Name);
                Assert.AreEqual("Ctrl+Alt+S", back.Shortcuts.KeysFor("guardar"));
                CollectionAssert.AreEqual(new[] { "C:/juegos/a", "C:/juegos/b" }, back.RecentProjects);

                File.WriteAllText(path, "{ roto");
                Assert.AreEqual("Oscuro", WorkspaceSettings.Load(path).Theme.Name);
                Assert.AreEqual("Oscuro", WorkspaceSettings.Load(Path.Combine(dir, "no_existe.json")).Theme.Name);
                Assert.AreEqual(WorkspaceSettings.MaxScale, new WorkspaceSettings { UiScale = 9 }.UiScale);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private static void AssertSizesAddUp(DockNode n)
        {
            if (!(n is DockSplit s)) return;
            Assert.GreaterOrEqual(s.Children.Count, 2, "no one-child splits");
            Assert.AreEqual(1f, s.Children.Sum(c => c.Size), 1e-3);
            foreach (var c in s.Children)
            {
                if (c is DockSplit cs) Assert.AreNotEqual(s.Direction, cs.Direction, "same-direction splits are merged");
                AssertSizesAddUp(c);
            }
        }
    }
}
