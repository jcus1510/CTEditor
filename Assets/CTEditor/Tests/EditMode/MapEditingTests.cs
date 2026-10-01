using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.Project;
using CTEditor.SharedKernel.Editing;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Mapas (fase 3): capas, herramientas (lápiz, rectángulo, relleno, cuentagotas), trazos que se deshacen de una vez,
    /// cambios de estructura, reglas de paso de RPG Maker XP, el jugador andando, guardar en JSON y la sesión de edición.
    /// </summary>
    public class MapEditingTests
    {
        private static Tileset Tiles(int count = 16, Action<TileAttributes> setup = null)
        {
            var attrs = new TileAttributes();
            setup?.Invoke(attrs);
            return new Tileset("graficos/tilesets/t.png", "t", SliceSettings.Square(32), 32 * 8, 32 * (count / 8), attrs);
        }

        private static string TempDir() => Path.Combine(Path.GetTempPath(), "cteditor_maps_" + Guid.NewGuid().ToString("N"));

        [Test]
        public void New_maps_have_three_layers_like_rpg_maker_xp_and_can_be_resized_with_an_anchor()
        {
            var map = new MapDefinition("pueblo", "Pueblo", 20, 15, "t");
            CollectionAssert.AreEqual(new[] { "Suelo", "Detalles", "Encima" }, map.Layers.Select(l => l.Name).ToArray());
            map.Layers[0].Set(0, 0, 5);
            map.Layers[0].Set(19, 14, 6);

            map.Resize(22, 16, 2, 1); // two columns added on the left, one row on top
            Assert.AreEqual((22, 16), (map.Width, map.Height));
            Assert.AreEqual(5, map.Layers[0].Get(2, 1));
            Assert.AreEqual(6, map.Layers[0].Get(21, 15));
            Assert.AreEqual(MapLayer.Empty, map.Layers[0].Get(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.Resize(0, 5));
        }

        [Test]
        public void Pencil_rectangle_fill_and_picker_compute_only_real_changes()
        {
            var map = new MapDefinition("m", "m", 6, 4);
            var stamp = new TileStamp(2, 1, new[] { 1, 2 });

            var pencil = MapTools.Pencil(map, 0, 5, 0, stamp); // the second tile falls outside
            Assert.AreEqual(1, pencil.Count);
            MapTools.Apply(map, pencil);
            Assert.AreEqual(1, map.Layers[0].Get(5, 0));
            Assert.AreEqual(0, MapTools.Pencil(map, 0, 5, 0, TileStamp.Single(1)).Count, "same tile: nothing to change");

            MapTools.Apply(map, MapTools.Rectangle(map, 0, 0, 1, 3, 2, stamp)); // 4×2 area tiled 1,2,1,2
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, Enumerable.Range(0, 4).Select(x => map.Layers[0].Get(x, 1)).ToArray());

            // Fill the empty area connected to (0,3): the whole bottom row and the empty cells of row 0.
            var fill = MapTools.Fill(map, 0, 0, 3, TileStamp.Single(9));
            MapTools.Apply(map, fill);
            Assert.AreEqual(9, map.Layers[0].Get(5, 3));
            Assert.AreEqual(9, map.Layers[0].Get(0, 0), "row 0 connects through column 4-5");
            Assert.AreEqual(1, map.Layers[0].Get(0, 1), "painted cells are not flooded");

            var picked = MapTools.Pick(map, -1, 0, 1, 1, 2);
            Assert.AreEqual((2, 2), (picked.Width, picked.Height));
            Assert.AreEqual(2, picked[1, 0]);

            map.Layers[0].Locked = true;
            Assert.IsEmpty(MapTools.Pencil(map, 0, 0, 0, TileStamp.Single(3)), "locked layers are not touched");
        }

        [Test]
        public void A_stroke_is_one_undo_step_that_restores_the_original_tiles()
        {
            var map = new MapDefinition("m", "m", 5, 5);
            map.Layers[0].Set(1, 1, 7);
            var history = new CommandHistory();
            var stroke = new TileStroke(map);
            foreach (var (x, y) in new[] { (0, 1), (1, 1), (2, 1), (1, 1) }) stroke.Add(MapTools.Pencil(map, 0, x, y, TileStamp.Single(3)));
            history.Record(stroke.ToCommand());

            Assert.AreEqual(3, map.Layers[0].Get(1, 1));
            Assert.IsTrue(history.Undo());
            Assert.AreEqual(7, map.Layers[0].Get(1, 1), "the tile that was there before the stroke");
            Assert.AreEqual(MapLayer.Empty, map.Layers[0].Get(0, 1));
            Assert.IsTrue(history.Redo());
            Assert.AreEqual(3, map.Layers[0].Get(2, 1));
            Assert.IsFalse(history.CanRedo);
        }

        [Test]
        public void Structure_changes_undo_layers_size_and_tileset_together()
        {
            var map = new MapDefinition("m", "m", 4, 4, "a");
            map.Layers[0].Set(3, 3, 1);
            var history = new CommandHistory();
            history.Record(MapStructureCommand.Run(map, "x", m =>
            {
                m.AddLayer("Nueva");
                m.Resize(2, 2);
                m.TilesetId = "b";
            }));
            Assert.AreEqual((4, 2, "b"), (map.Layers.Count, map.Width, map.TilesetId));

            history.Undo();
            Assert.AreEqual((3, 4, 4, "a"), (map.Layers.Count, map.Width, map.Height, map.TilesetId));
            Assert.AreEqual(1, map.Layers[0].Get(3, 3), "the tiles cut by the resize come back");
            history.Redo();
            Assert.AreEqual((4, 2, 2, "b"), (map.Layers.Count, map.Width, map.Height, map.TilesetId));
        }

        [Test]
        public void Passage_follows_rpg_maker_xp_rules()
        {
            // Tile 1 = wall (blocks everything). Tile 2 = fence blocking only its top side. Tile 3 = tree top above
            // the player (priority 1, passable). Tile 4 = floor.
            var ts = Tiles(16, a =>
            {
                a.Set(1, new TileProperties { Blocked = PassageBlock.All });
                a.Set(2, new TileProperties { Blocked = PassageBlock.Up });
                a.Set(3, new TileProperties { Priority = 1 });
                a.Set(5, new TileProperties { TerrainTag = 2, Bush = true });
            });
            var map = new MapDefinition("m", "m", 5, 5);
            for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) map.Layers[0].Set(x, y, 4);
            map.Layers[1].Set(2, 0, 1);
            map.Layers[1].Set(1, 2, 2);
            map.Layers[2].Set(3, 3, 3);
            map.Layers[1].Set(0, 4, 5);

            Assert.IsFalse(Passability.CanMove(map, ts, 2, 1, FacingDirection.Up), "wall");
            Assert.IsFalse(Passability.CanMove(map, ts, 1, 1, FacingDirection.Down), "entering the fence from above crosses its top side");
            Assert.IsTrue(Passability.CanMove(map, ts, 1, 3, FacingDirection.Up), "entering from below is fine");
            Assert.IsTrue(Passability.CanMove(map, ts, 3, 2, FacingDirection.Down), "a passable tile above the player does not block");
            Assert.IsFalse(Passability.CanMove(map, ts, 0, 0, FacingDirection.Left), "map edge");
            Assert.AreEqual(2, Passability.TerrainAt(map, ts, 0, 4));
            Assert.IsTrue(Passability.BushAt(map, ts, 0, 4));
            Assert.AreEqual(0, Passability.TerrainAt(map, ts, 1, 1));
        }

        [Test]
        public void The_player_walks_tile_by_tile_turns_against_walls_and_animates()
        {
            var ts = Tiles(16, a => a.Set(1, new TileProperties { Blocked = PassageBlock.All }));
            var map = new MapDefinition("m", "m", 4, 4);
            map.Layers[0].Set(1, 0, 1);
            var sim = new OverworldSim(map, ts, 0, 0);
            var steps = new List<(int, int)>();
            int bumps = 0;
            sim.StepFinished += (x, y, _) => steps.Add((x, y));
            sim.Bumped += _ => bumps++;

            sim.Update(0.01f, FacingDirection.Right, false);
            Assert.AreEqual(1, bumps, "wall on the right: only turns");
            Assert.AreEqual(FacingDirection.Right, sim.Player.Facing);
            Assert.IsFalse(sim.Player.Moving);

            sim.Update(0.01f, FacingDirection.Down, false);
            Assert.IsTrue(sim.Player.Moving);
            Assert.AreEqual((0, 1), (sim.Player.X, sim.Player.Y));
            Assert.AreEqual(0f, sim.Player.DrawY, 0.1f, "drawn still near the start");
            Assert.AreEqual(1, sim.Player.WalkFrame(4));
            sim.Update(0.3f, FacingDirection.Down, false); // 4 tiles/s: the step ends and the next one starts
            CollectionAssert.AreEqual(new[] { (0, 1) }, steps);
            Assert.AreEqual((0, 2), (sim.Player.X, sim.Player.Y), "holding the key keeps walking");
            sim.Update(0.3f, null, true);
            Assert.IsFalse(sim.Player.Moving);
            Assert.AreEqual(-1, sim.Player.WalkFrame(4), "standing");
            Assert.AreEqual(2, steps.Count);
        }

        [Test]
        public void Maps_and_the_tree_are_saved_as_readable_json()
        {
            var root = TempDir();
            try
            {
                var repo = new JsonMapRepository(root);
                var map = new MapDefinition("pueblo", "Pueblo Raíz", 3, 2, "graficos/tilesets/t.png") { Music = "pueblo.ogg", Bicycle = false };
                map.Layers[0].Set(2, 1, 12);
                map.Layers[2].Visible = false;
                repo.Save(map);
                var tree = new MapTree();
                tree.Add(new MapEntry("region", "Región"));
                tree.Add(new MapEntry("pueblo", "Pueblo Raíz", "region"));
                repo.SaveTree(tree);

                var text = File.ReadAllText(Path.Combine(root, "mapas", "pueblo.mapa.json"));
                StringAssert.Contains("\"-1 -1 12\"", text, "one text line per row");

                var back = repo.Load("pueblo");
                Assert.AreEqual((3, 2, "Pueblo Raíz", "pueblo.ogg", false), (back.Width, back.Height, back.Name, back.Music, back.Bicycle));
                Assert.AreEqual(12, back.Layers[0].Get(2, 1));
                Assert.IsFalse(back.Layers[2].Visible);
                var t = repo.LoadTree();
                Assert.AreEqual("region", t.Find("pueblo").ParentId);
                CollectionAssert.AreEqual(new[] { ("region", 0), ("pueblo", 1) }, t.Walk().Select(w => (w.entry.Id, w.depth)).ToArray());

                // A map file copied by hand shows up at the root.
                File.Copy(Path.Combine(root, "mapas", "pueblo.mapa.json"), Path.Combine(root, "mapas", "copia.mapa.json"));
                Assert.IsTrue(repo.LoadTree().Contains("copia"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void Map_tree_ids_are_readable_and_moves_cannot_make_loops()
        {
            var tree = new MapTree();
            Assert.AreEqual("pueblo_paleta", tree.NewId("Pueblo Paleta"));
            tree.Add(new MapEntry("ruta_1", "Ruta 1"));
            Assert.AreEqual("ruta_1_2", tree.NewId("Ruta 1"));
            Assert.AreEqual("camion", tree.NewId("  Camión!! "));
            tree.Add(new MapEntry("a", "A"));
            tree.Add(new MapEntry("b", "B", "a"));
            Assert.IsFalse(tree.Move("a", "b"), "a map cannot go inside its own child");
            Assert.IsTrue(tree.Move("b", ""));
            tree.Move("b", "a");
            tree.Remove("a");
            Assert.AreEqual("", tree.Find("b").ParentId, "children move up");
        }

        [Test]
        public void The_editor_session_paints_undoes_saves_and_edits_tile_properties()
        {
            var root = TempDir();
            try
            {
                ProjectLayout.CreateFolders(root);
                // A 64×32 tileset of two 32-px tiles, sliced.
                var tileset = Path.Combine(root, "graficos", "tilesets", "casa.png");
                var img = new PixelImage(64, 32);
                img[0, 0] = new Rgba32(255, 0, 0);
                img[40, 0] = new Rgba32(0, 255, 0);
                Png.Write(img, tileset);
                new SliceFile(SliceSettings.Square(32)).SaveFor(tileset);

                var session = new MapEditorSession(new JsonMapRepository(root), new FolderTilesetRepository(root));
                session.LoadTree();
                CollectionAssert.AreEqual(new[] { "casa" }, session.AvailableTilesets(), "a fixed id, not the path");

                var entry = session.CreateMap("Casa del héroe");
                Assert.AreEqual("casa_del_heroe", entry.Id);
                Assert.AreEqual("casa", session.Map.TilesetId, "the first tileset by default");
                Assert.AreEqual(("casa_del_heroe", 10, 7), session.PlayerStart, "the first map gets the start in its middle");

                session.SetAutoLayers(false); // this test paints by hand on the active layer
                int redraws = 0;
                session.TilesChanged += (x, y, w, h) => redraws++;
                session.SetStamp(TileStamp.Single(1));
                session.PointerDown(0, 0);
                session.PointerDrag(1, 0);
                session.PointerDrag(2, 0);
                session.PointerUp(2, 0);
                Assert.AreEqual(3, redraws, "every point of the stroke redraws at once");
                Assert.IsTrue(session.IsDirty);
                Assert.AreEqual(1, session.Map.Layers[0].Get(2, 0));

                session.SetTool(MapTool.Rectangle);
                session.PointerDown(0, 5);
                session.PointerDrag(3, 6);
                Assert.AreEqual((0, 5, 3, 6), session.DragRect);
                session.PointerUp(3, 6);
                Assert.AreEqual(1, session.Map.Layers[0].Get(3, 6));

                session.Undo();
                Assert.AreEqual(MapLayer.Empty, session.Map.Layers[0].Get(3, 6));
                session.Undo();
                Assert.AreEqual(MapLayer.Empty, session.Map.Layers[0].Get(1, 0), "the whole stroke at once");
                session.Redo();

                session.AddLayer("Sombras");
                Assert.AreEqual(4, session.Map.Layers.Count);
                Assert.AreEqual(1, session.ActiveLayer);
                session.Undo();
                Assert.AreEqual(3, session.Map.Layers.Count);

                session.ChangeTileProperties(null, new[] { 1 }, p => p.Blocked = PassageBlock.All, "bloquear");
                Assert.AreEqual(PassageBlock.All, session.Tileset.Properties(1).Blocked);
                session.Save();
                Assert.IsFalse(session.IsDirty);

                // Everything is on disk: a new session sees the same.
                var again = new MapEditorSession(new JsonMapRepository(root), new FolderTilesetRepository(root));
                again.LoadTree();
                Assert.IsTrue(again.OpenMap("casa_del_heroe"));
                Assert.AreEqual(1, again.Map.Layers[0].Get(2, 0));
                Assert.AreEqual(PassageBlock.All, again.Tileset.Properties(1).Blocked);

                again.DeleteMap("casa_del_heroe");
                Assert.IsNull(again.Map);
                Assert.IsFalse(File.Exists(Path.Combine(root, "mapas", "casa_del_heroe.mapa.json")));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void Picker_takes_a_block_of_tiles_and_returns_to_the_pencil()
        {
            var session = new MapEditorSession(new MemoryMaps(), new NoTilesets());
            session.CreateMap("M", width: 4, height: 4, tilesetId: "");
            session.SetStamp(new TileStamp(2, 2, new[] { 1, 2, 3, 4 }));
            session.PointerDown(0, 0);
            session.PointerUp(0, 0);
            session.SetTool(MapTool.Picker);
            session.PointerDown(1, 0);
            session.PointerDrag(1, 1);
            session.PointerUp(1, 1);
            Assert.AreEqual(MapTool.Pencil, session.Tool);
            Assert.AreEqual((1, 2), (session.Stamp.Width, session.Stamp.Height));
            Assert.AreEqual(4, session.Stamp[0, 1]);

            session.SetAutoLayers(false);
            session.SetActiveLayer(0);
            session.SetLayerView(0, locked: true);
            string warning = null;
            session.Message += (text, _) => warning = text;
            session.PointerDown(3, 3);
            StringAssert.Contains("bloqueada", warning);
        }

        [Test]
        public void Tile_flags_turn_and_flip_without_touching_slot_or_index()
        {
            int cell = MapTile.Encode(3, 1234);
            Assert.AreEqual(0, MapTile.Flags(cell), "old cells have no flags");
            int flipped = MapTile.WithFlags(cell, MapTile.FlipH | MapTile.FlipD);
            Assert.AreEqual((3, 1234), (MapTile.Slot(flipped), MapTile.Index(flipped)));
            Assert.Greater(flipped, 0, "flags never make a cell look empty");

            for (int f = 0; f < 8; f++)
            {
                int flags = (f & 1) * MapTile.FlipH | ((f >> 1) & 1) * MapTile.FlipV | ((f >> 2) & 1) * MapTile.FlipD;
                Assert.AreEqual(flags, MapTile.FlagsOf(MapTile.Matrix(flags)), "matrix round trip");
                int turned = flags;
                for (int i = 0; i < 4; i++) turned = MapTile.Rotated(turned, true);
                Assert.AreEqual(flags, turned, "four quarter turns = the same");
                Assert.AreEqual(flags, MapTile.Rotated(MapTile.Rotated(flags, true), false), "right then left = the same");
            }
            Assert.AreEqual(MapTile.Empty, MapTile.WithFlags(MapTile.Empty, MapTile.FlipH));
        }

        [Test]
        public void Stamps_flip_and_turn_as_a_block_and_tile_by_tile()
        {
            var stamp = new TileStamp(2, 1, new[] { 1, MapLayer.Empty });
            var h = stamp.FlippedHorizontally();
            Assert.AreEqual(MapLayer.Empty, h[0, 0]);
            Assert.AreEqual(1 | MapTile.FlipH, h[1, 0]);
            Assert.IsTrue(stamp.SameAs(h.FlippedHorizontally()));
            Assert.IsTrue(stamp.SameAs(stamp.FlippedVertically().FlippedVertically()));

            var r = stamp.Rotated();
            Assert.AreEqual((1, 2), (r.Width, r.Height));
            Assert.AreEqual(MapTile.Index(1), MapTile.Index(r[0, 0]));
            Assert.AreEqual(MapLayer.Empty, r[0, 1]);
            Assert.IsTrue(stamp.SameAs(r.Rotated().Rotated().Rotated()));
            Assert.IsTrue(stamp.SameAs(r.Rotated(false)));
        }

        [Test]
        public void Lines_include_both_ends_and_have_no_gaps()
        {
            var line = TileStamp.Line(0, 0, 5, 2).ToList();
            Assert.AreEqual((0, 0), line[0]);
            Assert.AreEqual((5, 2), line[line.Count - 1]);
            Assert.AreEqual(6, line.Count);
            for (int i = 1; i < line.Count; i++)
                Assert.LessOrEqual(Math.Max(Math.Abs(line[i].x - line[i - 1].x), Math.Abs(line[i].y - line[i - 1].y)), 1);
            Assert.AreEqual(new[] { (2, 2) }, TileStamp.Line(2, 2, 2, 2).ToArray());
        }

        [Test]
        public void Shift_click_paints_a_line_from_the_last_cell_as_one_undo_and_stamps_can_be_stored()
        {
            var session = new MapEditorSession(new MemoryMaps(), new NoTilesets());
            session.CreateMap("M", width: 6, height: 6, tilesetId: "");
            session.SetAutoLayers(false);
            session.SetActiveLayer(0);
            var layer = session.Map.Layers[0];
            session.SetStamp(TileStamp.Single(1));
            Assert.IsFalse(session.PaintLine(4, 0), "no line before painting something");
            session.PointerDown(0, 0);
            session.PointerUp(0, 0);
            Assert.IsTrue(session.PaintLine(4, 0));
            Assert.IsTrue(Enumerable.Range(0, 5).All(x => layer.Get(x, 0) == 1));
            session.Undo();
            Assert.AreEqual(1, layer.Get(0, 0), "the first click stays");
            Assert.IsTrue(Enumerable.Range(1, 4).All(x => layer.Get(x, 0) == MapLayer.Empty), "the line goes in one step");

            session.StoreStamp(3);
            session.RotateStamp();
            session.FlipStamp(true);
            Assert.AreNotEqual(1, session.Stamp[0, 0]);
            Assert.IsTrue(session.RecallStamp(3));
            Assert.AreEqual(1, session.Stamp[0, 0]);
            Assert.IsFalse(session.RecallStamp(5));
        }

        [Test]
        public void Any_unused_tileset_can_be_removed_and_later_tiles_keep_theirs()
        {
            var session = new MapEditorSession(new MemoryMaps(), new NoTilesets());
            session.CreateMap("M", width: 3, height: 3, tilesetId: "a");
            var m = session.Map;
            m.TilesetIds.Add("b");
            m.TilesetIds.Add("c");
            m.Layers[0].Set(1, 1, MapTile.Encode(2, 7)); // a tile of «c»
            m.Layers[0].Set(0, 0, MapTile.Encode(0, 3)); // a tile of «a»

            session.RemoveTilesetFromMap(1); // «b», in the middle and unused
            CollectionAssert.AreEqual(new[] { "a", "c" }, session.Map.TilesetIds);
            Assert.AreEqual(MapTile.Encode(1, 7), session.Map.Layers[0].Get(1, 1), "still a tile of «c»");
            Assert.AreEqual(MapTile.Encode(0, 3), session.Map.Layers[0].Get(0, 0));

            string warning = null;
            session.Message += (text, _) => warning = text;
            session.RemoveTilesetFromMap(1); // «c» is used
            StringAssert.Contains("se usa", warning);

            session.Undo();
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, session.Map.TilesetIds);
            Assert.AreEqual(MapTile.Encode(2, 7), session.Map.Layers[0].Get(1, 1));
        }

        [Test]
        public void The_problem_finder_points_at_what_is_missing()
        {
            var session = new MapEditorSession(new MemoryMaps(), new NoTilesets());
            Assert.AreEqual(ProblemLevel.Tip, ProblemFinder.Check(session).Single().Level, "no maps yet");

            session.CreateMap("Ruta", width: 4, height: 4, tilesetId: "falta");
            var area = session.AddArea("Hierba del lago", wholeMap: false);
            session.AddTable(area.Id, "hierba");
            var problems = ProblemFinder.Check(session);
            Assert.IsFalse(problems.Any(p => p.Text.Contains("inicio del jugador")), "the first map gets the start");
            Assert.IsTrue(problems.Any(p => p.Level == ProblemLevel.Error && p.Text.Contains("«falta»")));
            Assert.IsTrue(problems.Any(p => p.Text.Contains("no tiene casillas pintadas")));
            Assert.IsTrue(problems.Any(p => p.Text.Contains("sin especies")));
            Assert.IsTrue(problems.All(p => p.MapId == null || p.MapId == session.Map.Id));
            Assert.AreEqual(ProblemLevel.Error, problems[0].Level, "errors first");

            session.SetPlayerStart("", 0, 0);
            Assert.IsTrue(ProblemFinder.Check(session).Any(p => p.Level == ProblemLevel.Error && p.Text.Contains("inicio del jugador")));
        }

        private sealed class MemoryMaps : IMapRepository
        {
            private readonly Dictionary<string, MapDefinition> _maps = new Dictionary<string, MapDefinition>();
            private MapTree _tree = new MapTree();
            public MapTree LoadTree() => _tree;
            public void SaveTree(MapTree tree) => _tree = tree;
            public bool Exists(string id) => _maps.ContainsKey(id);
            public MapDefinition Load(string id) => _maps[id];
            public void Save(MapDefinition map) => _maps[map.Id] = map;
            public void Delete(string id) => _maps.Remove(id);
        }

        private sealed class NoTilesets : ITilesetRepository
        {
            public IReadOnlyList<string> List() => new string[0];
            public Tileset Load(string id) => null;
            public void SaveAttributes(Tileset tileset) { }
            public void Refresh() { }
        }
    }
}
