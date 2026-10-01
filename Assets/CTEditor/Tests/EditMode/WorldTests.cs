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
    /// Fase 4.5: varios tilesets por mapa, capas automáticas, copiar y pegar, tramos del mundo continuo (vecinos, solapes,
    /// cruzar andando, mover), encuentros como en los juegos originales (métodos, zonas pintadas, tablas, horas,
    /// interruptores), mapa de la región, ids fijos de los tilesets y lectura de archivos antiguos.
    /// </summary>
    public class WorldTests
    {
        private static string TempDir() => Path.Combine(Path.GetTempPath(), "cteditor_world_" + Guid.NewGuid().ToString("N"));

        private static Tileset Sheet(string id, Action<TileAttributes> setup = null, TileCoverage[] coverage = null)
        {
            var a = new TileAttributes();
            setup?.Invoke(a);
            return new Tileset(id, id, SliceSettings.Square(32), 256, 64, a) { Coverage = coverage };
        }

        [Test]
        public void A_map_cell_says_which_tileset_and_which_tile()
        {
            int cell = MapTile.Encode(2, 37);
            Assert.AreEqual(2, MapTile.Slot(cell));
            Assert.AreEqual(37, MapTile.Index(cell));
            Assert.AreEqual(37, MapTile.Encode(0, 37), "slot 0 = the old single-tileset numbers");
            Assert.AreEqual(-1, MapTile.Slot(MapTile.Empty));

            var grass = Sheet("cesped", a => a.Set(3, new TileProperties { Blocked = PassageBlock.All }));
            var city = Sheet("ciudad", a => a.Set(3, new TileProperties { TerrainTag = 2 }));
            var sets = new MapTilesets(new[] { grass, city });
            Assert.AreEqual(PassageBlock.All, sets.Properties(MapTile.Encode(0, 3)).Blocked);
            Assert.AreEqual(2, sets.Properties(MapTile.Encode(1, 3)).TerrainTag);
            Assert.AreSame(city, sets.For(MapTile.Encode(1, 0)));

            var map = new MapDefinition("m", "m", 4, 4, "cesped");
            Assert.AreEqual(1, map.SlotOf("ciudad"), "adding a second tileset gives it slot 1");
            Assert.AreEqual(1, map.SlotOf("ciudad"));
            Assert.AreEqual(-1, map.SlotOf("playa", add: false));
        }

        [Test]
        public void Automatic_layers_send_each_tile_to_its_layer_and_the_eraser_takes_the_top_one()
        {
            // Tile 0 = full ground; tile 1 = a flower (transparent parts); tile 2 = tree top (priority).
            var ts = Sheet("t", a => a.Set(2, new TileProperties { Priority = 1 }),
                new[] { TileCoverage.Full, TileCoverage.Partial, TileCoverage.Partial }.Concat(Enumerable.Repeat(TileCoverage.Full, 13)).ToArray());
            var map = new MapDefinition("m", "m", 4, 4, "t");
            MapTilesets sets = ts;

            MapTools.Apply(map, MapTools.PencilAuto(map, sets, 1, 1, TileStamp.Single(0), 0));
            MapTools.Apply(map, MapTools.PencilAuto(map, sets, 1, 1, TileStamp.Single(1), 0));
            MapTools.Apply(map, MapTools.PencilAuto(map, sets, 1, 1, TileStamp.Single(2), 0));
            Assert.AreEqual(0, map.Layers[0].Get(1, 1), "ground stays under the flower");
            Assert.AreEqual(1, map.Layers[1].Get(1, 1), "the flower goes to Detalles");
            Assert.AreEqual(2, map.Layers[2].Get(1, 1), "the tree top goes to Encima");

            MapTools.Apply(map, MapTools.PencilAuto(map, sets, 1, 1, TileStamp.Eraser, 0));
            Assert.AreEqual(MapLayer.Empty, map.Layers[2].Get(1, 1), "the eraser takes the top tile");
            Assert.AreEqual(1, map.Layers[1].Get(1, 1));

            // A tile set by hand to «suelo» goes to the ground even with transparency.
            ts.Attributes.Set(1, new TileProperties { Piece = TilePiece.Ground });
            Assert.AreEqual(0, MapTools.AutoLayerOf(map, sets, 1, 2));
            Assert.AreEqual(0, MapTools.AutoLayerOf(map, sets, 5, 2), "a full tile goes to the ground");
            ts.Attributes.Set(4, new TileProperties { Piece = TilePiece.Detail });
            map.Layers[1].Locked = true;
            Assert.AreEqual(2, MapTools.AutoLayerOf(map, sets, 4, 2), "its layer is locked: the fallback (active) layer");
        }

        [Test]
        public void Copy_and_paste_keep_every_layer_and_do_not_erase_with_empty_cells()
        {
            var a = new MapDefinition("a", "A", 5, 5, "t");
            a.Layers[0].Set(0, 0, 4);
            a.Layers[1].Set(1, 0, 9);
            var clip = MapClipboard.Copy(a, 0, 0, 1, 0);
            Assert.AreEqual((2, 1), (clip.Width, clip.Height));

            var b = new MapDefinition("b", "B", 5, 5, "otro");
            b.Layers[0].Set(3, 3, 7);
            b.Layers[0].Set(4, 3, 7);
            MapTools.Apply(b, clip.PasteChanges(b, 3, 3));
            Assert.AreEqual(MapTile.Encode(1, 4), b.Layers[0].Get(3, 3), "tileset «t» is added to B as slot 1");
            Assert.AreEqual(7, b.Layers[0].Get(4, 3), "an empty copied cell does not erase");
            Assert.AreEqual(MapTile.Encode(1, 9), b.Layers[1].Get(4, 3));
            CollectionAssert.AreEqual(new[] { "otro", "t" }, b.TilesetIds);

            var clear = MapTools.Clear(b, 3, 3, 4, 3);
            MapTools.Apply(b, clear);
            Assert.AreEqual(MapLayer.Empty, b.Layers[0].Get(4, 3));
        }

        [Test]
        public void Sections_touching_each_other_are_neighbours_and_overlaps_are_reported()
        {
            var town = new MapDefinition("pueblo", "Pueblo", 20, 15) { InWorld = true, WorldX = 0, WorldY = 0, Category = SectionCategory.Town };
            var route = new MapDefinition("ruta1", "Ruta 1", 10, 30) { InWorld = true, WorldX = 5, WorldY = -30 };
            var far = new MapDefinition("lejos", "Lejos", 5, 5) { InWorld = true, WorldX = 100, WorldY = 100 };
            var house = new MapDefinition("casa", "Casa", 8, 6) { Kind = MapKind.Interior };
            var world = new WorldLayout(new[] { town, route, far, house });

            Assert.AreEqual(3, world.Sections.Count, "interiors are not in the world");
            CollectionAssert.AreEquivalent(new[] { route }, world.Neighbors(town).ToArray());
            Assert.IsEmpty(world.Neighbors(far));
            Assert.AreSame(route, world.MapAt(7, -1));
            Assert.IsNull(world.MapAt(0, -1));
            Assert.IsEmpty(world.Overlaps());

            var place = world.PlaceNextTo(town, FacingDirection.Right, 10, 15);
            Assert.AreEqual((20, 0), place);
            var below = world.PlaceNextTo(town, FacingDirection.Down, 20, 10);
            Assert.AreEqual((0, 15), below);

            var clash = new MapDefinition("x", "X", 10, 10) { InWorld = true, WorldX = 15, WorldY = 5 };
            Assert.AreEqual(1, new WorldLayout(new[] { town, clash }).Overlaps().Count());
        }

        [Test]
        public void The_player_walks_from_one_section_into_the_next_without_loading()
        {
            var town = new MapDefinition("pueblo", "Pueblo", 4, 4) { InWorld = true, WorldX = 0, WorldY = 0 };
            var route = new MapDefinition("ruta", "Ruta", 4, 6) { InWorld = true, WorldX = 1, WorldY = -6 };
            var world = new WorldLayout(new[] { town, route });
            var sim = new OverworldSim(town, MapTilesets.None, 2, 0) { World = world, TilesetsOf = _ => MapTilesets.None };
            MapDefinition entered = null;
            sim.SectionChanged += (from, to) => entered = to;

            sim.Update(0.01f, FacingDirection.Up, false);
            Assert.AreSame(route, sim.Map);
            Assert.AreSame(route, entered);
            Assert.AreEqual((1, 5), (sim.Player.X, sim.Player.Y), "world cell (2,-1) is (1,5) of the route");
            Assert.IsTrue(sim.Player.Moving, "the step keeps animating across the border");

            // Walking off the west edge of the town where there is no section: blocked.
            var sim2 = new OverworldSim(town, MapTilesets.None, 0, 2) { World = world };
            sim2.Update(0.01f, FacingDirection.Left, false);
            Assert.AreSame(town, sim2.Map);
            Assert.IsFalse(sim2.Player.Moving);
        }

        [Test]
        public void Moving_a_section_is_undoable_and_resizing_keeps_the_world_in_place()
        {
            var town = new MapDefinition("pueblo", "Pueblo", 10, 10) { InWorld = true, WorldX = 3, WorldY = 4 };
            var h = new CommandHistory();
            h.Execute(new MoveSectionCommand(town, 30, 40));
            Assert.AreEqual((30, 40), (town.WorldX, town.WorldY));
            h.Undo();
            Assert.AreEqual((3, 4), (town.WorldX, town.WorldY));

            town.Objects.Add(new MapObject("cartel_1", "cartel", 2, 2));
            var area = new EncounterArea("z", "Z");
            area.Paint(1, 1);
            town.Encounters.Add(area);
            town.Resize(12, 10, 2, 0); // two columns on the left
            Assert.AreEqual(1, town.WorldX, "grows to the left: the old tiles stay where they were in the world");
            Assert.AreEqual(4, town.Objects[0].X);
            Assert.IsTrue(area.Contains(3, 1));
        }

        [Test]
        public void The_species_picker_sorts_by_pokedex_and_filters_by_types_egg_groups_and_forms()
        {
            var root = Path.Combine(Path.GetTempPath(), "ct_especies_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "datos"));
            try
            {
                File.WriteAllText(Path.Combine(root, "datos", "especies.csv"),
                    "id;nombre;tipos;numero;grupos_huevo;forma_de;legendario\n" +
                    "raichu_alola;Raichu de Alola;electric|psychic;26;field|fairy;raichu;no\n" +
                    "pikachu;Pikachu;electric;25;field|fairy;;no\n" +
                    "raichu;Raichu;electric;26;field|fairy;;no\n" +
                    "bulbasaur;Bulbasaur;grass|poison;1;monster|grass;;no\n" +
                    "mewtwo;Mewtwo;psychic;150;undiscovered;;si\n");
                File.WriteAllText(Path.Combine(root, "datos", "tipos.csv"), "id;nombre;color\nelectric;Eléctrico;F7D02C\n");
                var dir = new CsvSpeciesDirectory(root);
                var all = dir.Entries();
                Assert.AreEqual(5, all.Count);
                Assert.AreEqual(("Eléctrico", "F7D02C"), dir.Types()["electric"]);

                var f = new SpeciesFilter();
                CollectionAssert.AreEqual(new[] { "bulbasaur", "pikachu", "raichu", "raichu_alola", "mewtwo" }, f.Apply(all).Select(s => s.Id).ToArray(),
                    "Pokédex order, forms right after their species");
                f.Types.Add("electric");
                Assert.AreEqual(3, f.Apply(all).Count);
                f.ExactTypes = true;
                CollectionAssert.AreEqual(new[] { "pikachu", "raichu" }, f.Apply(all).Select(s => s.Id).ToArray(), "exactly electric");
                f = new SpeciesFilter { Forms = 2 };
                Assert.AreEqual("raichu_alola", f.Apply(all).Single().Id);
                f = new SpeciesFilter { EggGroup = "monster" };
                Assert.AreEqual("bulbasaur", f.Apply(all).Single().Id);
                f = new SpeciesFilter { OnlyLegendary = true, Generation = 1 };
                Assert.AreEqual("mewtwo", f.Apply(all).Single().Id);
                f = new SpeciesFilter { Text = "#25" };
                Assert.AreEqual("pikachu", f.Apply(all).Single().Id);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void Encounters_follow_the_original_games()
        {
            var map = new MapDefinition("ruta1", "Ruta 1", 10, 10);
            var general = new EncounterArea("todo", "Toda la ruta", wholeMap: true);
            var grass = general.GetOrAddTable("hierba");
            grass.Rate = 20;
            grass.Slots.Add(new EncounterSlot("pidgey", 2, 4, 50));
            grass.Slots.Add(new EncounterSlot("rattata", 2, 4, 50));
            grass.Slots.Add(new EncounterSlot("hoothoot", 3, 3, 30, TimeOfDay.Night));
            var special = new EncounterArea("claro", "Claro del bosque");
            special.Paint(5, 5);
            special.GetOrAddTable("hierba").Slots.Add(new EncounterSlot("pikachu", 5, 5, 1) { RequiredFlag = "tiene_pokedex" });
            map.Encounters.Add(general);
            map.Encounters.Add(special);
            var methods = EncounterMethodCatalog.Classic();
            var hierba = methods.Find("hierba");

            Assert.AreSame(general, EncounterResolver.Find(map, 0, 0, "hierba").Value.area);
            Assert.AreSame(special, EncounterResolver.Find(map, 5, 5, "hierba").Value.area, "the painted area wins");
            Assert.IsNull(EncounterResolver.Find(map, 0, 0, "surf"), "no surf table on this route");

            // Percentages shown in the editor: at day the owl is gone.
            var day = grass.Chances(TimeOfDay.Day, _ => false);
            Assert.AreEqual(2, day.Count);
            Assert.AreEqual(50.0, day[0].percent, 0.01);
            var night = grass.Chances(TimeOfDay.Night, _ => false);
            Assert.AreEqual(30.0 / 130 * 100, night[2].percent, 0.01);
            var allHours = grass.BaseChances();
            Assert.AreEqual(grass.Slots.Count(x => x.Weight > 0), allHours.Count, "«all hours»: every species with weight");
            Assert.AreEqual(100.0, allHours.Sum(c => c.percent), 0.01);

            // Rolls with a scripted dice: first roll = rate check, second = slot, third = level.
            Queue<int> dice = null;
            int Rng(int n) => dice.Dequeue() % n;
            dice = new Queue<int>(new[] { 25 });
            Assert.IsNull(EncounterResolver.Roll(map, 0, 0, hierba, TimeOfDay.Day, _ => false, Rng), "25 ≥ 20 %: nothing");
            dice = new Queue<int>(new[] { 5, 60, 2 });
            var r = EncounterResolver.Roll(map, 0, 0, hierba, TimeOfDay.Day, _ => false, Rng);
            Assert.AreEqual(("rattata", 4), (r.SpeciesId, r.Level));
            dice = new Queue<int>(new[] { 5, 0, 0 });
            Assert.IsNull(EncounterResolver.Roll(map, 5, 5, hierba, TimeOfDay.Day, _ => false, Rng), "pikachu needs its switch");
            dice = new Queue<int>(new[] { 5, 0, 0 });
            Assert.AreEqual("pikachu", EncounterResolver.Roll(map, 5, 5, hierba, TimeOfDay.Day, f => f == "tiene_pokedex", Rng).SpeciesId);

            Assert.AreEqual(TimeOfDay.Night, EncounterResolver.TimeAt(23));
            Assert.AreEqual(TimeOfDay.Morning, EncounterResolver.TimeAt(7));
            CollectionAssert.Contains(EncounterResolver.StepMethods(methods, 2, false).Select(m => m.Id).ToList(), "hierba");
            CollectionAssert.DoesNotContain(EncounterResolver.StepMethods(methods, 0, false).Select(m => m.Id).ToList(), "hierba");
            CollectionAssert.Contains(EncounterResolver.StepMethods(methods, 0, false).Select(m => m.Id).ToList(), "cueva");
            Assert.IsFalse(methods.Remove("hierba"), "built-in methods stay");
            methods.Add(new EncounterMethod("volando", "Volando", EncounterTrigger.Script, 100));
            Assert.IsTrue(methods.Remove("volando"));
        }

        [Test]
        public void Painted_areas_are_saved_as_rectangles()
        {
            var a = new EncounterArea("z", "Z");
            for (int y = 2; y <= 4; y++) for (int x = 1; x <= 3; x++) a.Paint(x, y);
            a.Paint(10, 10);
            var rects = a.ToRectangles();
            CollectionAssert.AreEquivalent(new[] { (1, 2, 3, 3), (10, 10, 1, 1) }, rects);
            var b = new EncounterArea("b", "B");
            b.SetRectangles(rects);
            Assert.AreEqual(10, b.Cells.Count);
        }

        [Test]
        public void The_region_map_adapts_to_the_world()
        {
            var town = new MapDefinition("pueblo", "Pueblo", 8, 8) { InWorld = true, WorldX = 0, WorldY = 0, Category = SectionCategory.Town };
            var route = new MapDefinition("ruta", "Ruta", 8, 16) { InWorld = true, WorldX = 0, WorldY = -16 };
            var hidden = new MapDefinition("secreto", "Secreto", 4, 4) { InWorld = true, WorldX = 20, WorldY = 0, ShowOnRegionMap = false };
            var (img, areas) = RegionMapBuilder.Build(new WorldLayout(new[] { town, route, hidden }), RegionMapStyle.Schematic, 4, margin: 1);
            Assert.AreEqual((24 / 4 + 2, 24 / 4 + 2), (img.Width, img.Height));
            Assert.AreEqual(2, areas.Count, "hidden sections are left out of the areas");
            var t = areas.Single(a => a.MapId == "pueblo");
            Assert.AreEqual(new PixelRect(1, 5, 2, 2), t.Rect);
            Assert.AreEqual(new PixelRect(1, 1, 2, 4), areas.Single(a => a.MapId == "ruta").Rect);
            Assert.AreNotEqual(img[0, 0], img[1, 5], "sections are drawn over the sea");
        }

        [Test]
        public void Tilesets_get_a_fixed_id_and_old_maps_that_saved_the_path_still_open()
        {
            var root = TempDir();
            try
            {
                ProjectLayout.CreateFolders(root);
                var img = Path.Combine(root, "graficos", "tilesets", "Pueblo Raíz.png");
                Png.Write(new PixelImage(64, 32), img);
                new SliceFile(SliceSettings.Square(32)).SaveFor(img);
                var repo = new FolderTilesetRepository(root);
                CollectionAssert.AreEqual(new[] { "pueblo_raiz" }, repo.List());
                Assert.AreEqual("pueblo_raiz", repo.Load("graficos/tilesets/Pueblo Raíz.png").Id, "the path still finds it");
                Assert.AreEqual(TileCoverage.Empty, repo.Load("pueblo_raiz").Coverage[0]);

                // An old (format 1) map file pointing to the path.
                Directory.CreateDirectory(Path.Combine(root, "mapas"));
                File.WriteAllText(Path.Combine(root, "mapas", "viejo.mapa.json"),
                    "{\"nombre\":\"Viejo\",\"ancho\":2,\"alto\":1,\"tileset\":\"graficos/tilesets/Pueblo Raíz.png\"," +
                    "\"capas\":[{\"nombre\":\"Suelo\",\"filas\":[\"0 1\"]},{\"nombre\":\"Detalles\",\"filas\":[\"-1 -1\"]},{\"nombre\":\"Encima\",\"filas\":[\"-1 -1\"]}]}");
                var session = new MapEditorSession(new JsonMapRepository(root), repo, new JsonEncounterMethodRepository(root), new CsvSpeciesDirectory(root));
                session.LoadTree();
                Assert.IsTrue(session.OpenMap("viejo"));
                Assert.AreEqual("pueblo_raiz", session.Map.TilesetId, "migrated to the id");
                Assert.AreEqual(LayerRole.Ground, session.Map.Layers[0].Role, "old default layers get their roles");
                Assert.AreEqual(MapKind.Exterior, session.Map.Kind);
                Assert.IsFalse(session.Map.InWorld, "old maps are not placed in the world until the author does");

                // Renaming the image keeps the map working.
                File.Move(img, Path.Combine(root, "graficos", "tilesets", "otro_nombre.png"));
                File.Move(ProjectLayout.SlicePathFor(img), ProjectLayout.SlicePathFor(Path.Combine(root, "graficos", "tilesets", "otro_nombre.png")));
                session.ReloadTilesets();
                Assert.IsNotNull(session.Tileset);
                Assert.AreEqual("graficos/tilesets/otro_nombre.png", session.Tileset.ImagePath);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void The_session_creates_sections_side_by_side_and_saves_encounters_objects_and_methods()
        {
            var root = TempDir();
            try
            {
                ProjectLayout.CreateFolders(root);
                File.WriteAllText(Path.Combine(root, "datos", "especies.csv"), "﻿id;nombre;tipos\npidgey;Pidgey;normal|flying\n\"rattata\";\"Rattata\";normal\n");
                var s = new MapEditorSession(new JsonMapRepository(root), new FolderTilesetRepository(root),
                    new JsonEncounterMethodRepository(root), new CsvSpeciesDirectory(root));
                s.LoadTree();
                CollectionAssert.AreEqual(new[] { ("pidgey", "Pidgey"), ("rattata", "Rattata") }, s.Species.All());

                s.CreateMap("Pueblo", width: 20, height: 15, category: SectionCategory.Town);
                s.CreateMap("Ruta 1", width: 10, height: 30, nextTo: "pueblo", side: FacingDirection.Up);
                var route = s.Find("ruta_1");
                Assert.AreEqual((5, -30), (route.WorldX, route.WorldY), "placed on top of the town, centred");
                Assert.AreEqual(("pueblo", 10, 7), s.PlayerStart);
                Assert.IsNotNull(s.Find("pueblo").FindObject(MapObject.PlayerStartKind), "the start is an object of the map");

                var area = s.AddArea("Toda la ruta", wholeMap: true);
                s.AddTable(area.Id, "hierba");
                s.AddSlot(area.Id, "hierba", new EncounterSlot("pidgey", 2, 4, 1));
                s.AddSlot(area.Id, "hierba", new EncounterSlot("rattata", 2, 4, 1, TimeOfDay.Night));
                s.ApplyClassicWeights(area.Id, "hierba");
                Assert.AreEqual(20, area.TableFor("hierba").Slots[1].Weight);
                s.Undo();
                Assert.AreEqual(1, s.Map.Encounters[0].TableFor("hierba").Slots[1].Weight, "undo works on tables too");

                var patch = s.AddArea("Hierba del lago", wholeMap: false);
                Assert.AreEqual(MapTool.EncounterPaint, s.Tool);
                s.PointerDown(1, 1);
                s.PointerDrag(2, 1);
                s.PointerUp(2, 1);
                Assert.AreEqual(2, s.ActiveArea.Cells.Count);
                s.PointerDown(1, 1, secondary: true);
                s.PointerUp(1, 1);
                Assert.AreEqual(1, s.ActiveArea.Cells.Count, "right click removes");
                s.StopAreaPainting();
                Assert.AreEqual(MapTool.Pencil, s.Tool, "stop painting goes back to the pencil");
                int changes = 0;
                s.EncountersChanged += () => changes++;
                s.SetHighlightAllAreas(true);
                Assert.IsTrue(s.HighlightAllAreas);
                Assert.AreEqual(1, changes);

                // Extras of a species and of a method, and ordering.
                s.UpdateSlot(area.Id, "hierba", 0, x => { x.Form = 1; x.HeldItem = "baya_aranja"; x.ShinyOdds = 512; });
                s.SetTableDoubles(area.Id, "hierba", 15);
                s.DuplicateSlot(area.Id, "hierba", 0);
                Assert.AreEqual(3, s.Map.Encounters[0].TableFor("hierba").Slots.Count);
                s.MoveSlot(area.Id, "hierba", 1, +1);
                Assert.AreEqual("rattata", s.Map.Encounters[0].TableFor("hierba").Slots[1].SpeciesId, "the copy went down one");
                s.Undo(); s.Undo();
                Assert.AreEqual(2, s.Map.Encounters[0].TableFor("hierba").Slots.Count);

                s.SaveMethod(new EncounterMethod("volando", "Volando", EncounterTrigger.Script, 100));
                s.MoveSection("pueblo", 50, 50);
                Assert.IsTrue(s.World().Overlaps().Count() == 0);
                s.Save();

                var again = new MapEditorSession(new JsonMapRepository(root), new FolderTilesetRepository(root), new JsonEncounterMethodRepository(root));
                again.LoadTree();
                Assert.IsNotNull(again.Methods.Find("volando"), "custom methods are saved in the project");
                again.OpenMap("ruta_1");
                Assert.AreEqual(2, again.Map.Encounters.Count);
                Assert.AreEqual(TimeOfDay.Night, again.Map.Encounters[0].TableFor("hierba").Slots[1].Times);
                var first = again.Map.Encounters[0].TableFor("hierba").Slots[0];
                Assert.AreEqual((1, "baya_aranja", 512), (first.Form, first.HeldItem, first.ShinyOdds), "species extras are saved");
                Assert.AreEqual(15, again.Map.Encounters[0].TableFor("hierba").DoublePercent);
                Assert.AreEqual(200, EncounterTable.StepsToFind(10, 5), 0.001, "10 % per step and 5 % of the table: about 200 steps");
                Assert.AreEqual(1, again.Map.Encounters[1].Cells.Count);
                Assert.AreEqual((50, 50), (again.Find("pueblo").WorldX, again.Find("pueblo").WorldY));

                again.Solo("ruta_1");
                Assert.IsTrue(again.Tree.Find("pueblo").HiddenInWorld);
                again.Solo("ruta_1");
                Assert.IsFalse(again.Tree.Find("pueblo").HiddenInWorld, "solo again shows everything");
                again.SetSectionView("pueblo", locked: true);
                string warn = null;
                again.Message += (t, _) => warn = t;
                again.MoveSection("pueblo", 0, 0);
                StringAssert.Contains("bloqueado", warn);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void Selection_copy_paste_and_delete_through_the_session()
        {
            var s = new MapEditorSession(new MemoryMaps(), new NoTilesets());
            s.CreateMap("M", width: 6, height: 6, tilesetId: "");
            s.SetAutoLayers(false);
            s.SetStamp(TileStamp.Single(3));
            s.PointerDown(0, 0); s.PointerUp(0, 0);
            s.SetTool(MapTool.Select);
            s.PointerDown(0, 0); s.PointerDrag(1, 1); s.PointerUp(1, 1);
            Assert.AreEqual((0, 0, 1, 1), s.Selection);
            Assert.IsTrue(s.Copy());
            s.BeginPaste();
            s.PointerDown(4, 4); s.PointerUp(4, 4);
            Assert.AreEqual(3, s.Map.Layers[0].Get(4, 4));
            s.SetTool(MapTool.Select);
            s.PointerDown(0, 0); s.PointerUp(0, 0);
            s.Cut();
            Assert.AreEqual(MapLayer.Empty, s.Map.Layers[0].Get(0, 0));
            s.Undo();
            Assert.AreEqual(3, s.Map.Layers[0].Get(0, 0));
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
