using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Pinceles aleatorios (fase 6, B5): cada casilla sale con uno de sus tiles al azar según los pesos; funcionan con el
    /// lápiz, el rectángulo y el relleno, se deshacen de una vez y se guardan en el proyecto.
    /// </summary>
    public class BrushTests
    {
        private static RandomBrush Grass()
        {
            var b = new RandomBrush("hierba", "Hierba");
            b.Tiles.Add(new BrushTile("t", 1, 3)); // plain grass, most of the time
            b.Tiles.Add(new BrushTile("t", 2, 1)); // flowers
            return b;
        }

        [Test]
        public void Weights_decide_how_often_each_tile_comes_out()
        {
            var b = Grass();
            Assert.AreEqual(4, b.TotalWeight);
            Assert.AreEqual(new[] { 1, 1, 1, 2 }, Enumerable.Range(0, 4).Select(r => b.Pick(r).Index).ToArray());
            Assert.AreEqual(0.75, b.Chance(b.Tiles[0]), 1e-9);
            b.Add("t", 2, 2);
            Assert.AreEqual(3, b.Tiles[1].Weight, "adding the same tile adds weight");
            b.Tiles.ForEach(t => t.Weight = 0);
            Assert.IsNull(b.Pick(0));
        }

        [Test]
        public void Only_the_maps_tilesets_are_used()
        {
            var b = Grass();
            b.Tiles.Add(new BrushTile("otro", 5, 100));
            var map = new MapDefinition("m", "m", 4, 1, "t");
            Assert.IsTrue(b.UsableIn(map));
            Assert.AreEqual(-1, RandomBrush.CellIn(map, b.Tiles[2]));
            var changes = Enumerable.Range(0, 4).Select(x => new TileChange(0, x, 0, MapLayer.Empty, RandomBrush.Placeholder)).ToList();
            var painted = b.Randomize(map, changes, n => n - 1); // the last of the usable ones: flowers
            Assert.IsTrue(painted.All(c => c.After == 2), "the tileset the map does not have is skipped");
            Assert.IsFalse(Grass().UsableIn(new MapDefinition("x", "x", 2, 2, "otro")));
        }

        [Test]
        public void The_pencil_rectangle_and_fill_paint_with_the_brush_as_one_undo()
        {
            var session = new MapEditorSession(new MemoryMaps(), new NoTilesets());
            session.CreateMap("M", width: 6, height: 4, tilesetId: "t");
            session.SetAutoLayers(false);
            session.SetActiveLayer(0);
            var layer = session.Map.Layers[0];
            int n = 0;
            session.Random = max => n++ % max; // 0, 1, 2, 3, 0, 1...: grass, grass, grass, flowers...
            session.Brushes.Add(Grass());
            session.UseBrush(session.Brushes[0]);

            session.SetTool(MapTool.Rectangle);
            session.PointerDown(0, 0);
            session.PointerDrag(3, 0);
            session.PointerUp(3, 0);
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 2 }, Enumerable.Range(0, 4).Select(x => layer.Get(x, 0)).ToArray());
            session.Undo();
            Assert.IsTrue(Enumerable.Range(0, 4).All(x => layer.Get(x, 0) == MapLayer.Empty), "one undo");

            session.SetTool(MapTool.Fill);
            session.PointerDown(5, 3);
            session.PointerUp(5, 3);
            Assert.IsTrue(Enumerable.Range(0, 6).All(x => layer.Get(x, 3) == 1 || layer.Get(x, 3) == 2), "the fill too");
            Assert.IsTrue(Enumerable.Range(0, 6).Any(x => layer.Get(x, 3) == 2), "with flowers here and there");

            session.SetStamp(TileStamp.Single(7));
            Assert.IsNull(session.ActiveBrush, "choosing a tile goes back to the stamp");
        }

        [Test]
        public void Brushes_are_made_from_the_stamp_and_saved_in_the_project()
        {
            var root = Path.Combine(Path.GetTempPath(), "cteditor_brush_" + Guid.NewGuid().ToString("N"));
            try
            {
                var session = new MapEditorSession(new MemoryMaps(), new NoTilesets()) { BrushRepository = new JsonBrushRepository(root) };
                session.CreateMap("M", width: 4, height: 4, tilesetId: "t");
                session.SetStamp(new TileStamp(3, 1, new[] { 4, 5, 4 }));
                var b = session.NewBrushFromStamp("Rocas");
                Assert.AreEqual("rocas", b.Id);
                Assert.AreEqual(new[] { (4, 2), (5, 1) }, b.Tiles.Select(t => (t.Index, t.Weight)).ToArray(), "repeated tiles weigh more");

                var again = new MapEditorSession(new MemoryMaps(), new NoTilesets()) { BrushRepository = new JsonBrushRepository(root) };
                again.LoadBrushes();
                var loaded = again.Brushes.Single();
                Assert.AreEqual(("rocas", "Rocas", "t"), (loaded.Id, loaded.Name, loaded.Tiles[0].TilesetId));
                Assert.AreEqual(2, loaded.Tiles[0].Weight);

                session.DeleteBrush(b);
                again.LoadBrushes();
                Assert.IsEmpty(again.Brushes);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
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
