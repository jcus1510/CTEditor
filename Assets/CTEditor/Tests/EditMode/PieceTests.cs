using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Editing;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Piezas reutilizables (fase 6, B6): una selección con todas sus capas se guarda como pieza y se coloca en otros
    /// mapas como una copia (con los tilesets que necesite); cambiar o quitar la pieza no toca los mapas.
    /// </summary>
    public class PieceTests
    {
        [Test]
        public void The_same_house_goes_in_two_towns_and_editing_the_piece_does_not_touch_them()
        {
            var root = Path.Combine(Path.GetTempPath(), "cteditor_piece_" + Guid.NewGuid().ToString("N"));
            try
            {
                var session = new MapEditorSession(new MemoryMaps(), new NoTilesets()) { PieceRepository = new JsonMapPieceRepository(root) };
                session.CreateMap("Pueblo A", width: 8, height: 8, tilesetId: "casas");
                var a = session.Map;
                a.Layers[0].Set(1, 1, 5);
                a.Layers[0].Set(2, 1, MapTile.Encode(0, 6, MapTile.FlipH));
                a.Layers[2].Set(1, 0, 9); // the roof, on the layer above
                session.SetTool(MapTool.Select);
                session.PointerDown(1, 0);
                session.PointerDrag(2, 1);
                session.PointerUp(2, 1);
                var house = session.SavePieceFromSelection("Casa");
                Assert.AreEqual((2, 2), (house.Width, house.Height));

                // Another town with another main tileset: the piece brings its own.
                session.CreateMap("Pueblo B", width: 8, height: 8, tilesetId: "campo");
                var b = session.Map;
                session.PlacePiece(house);
                Assert.AreEqual(MapTool.Paste, session.Tool);
                session.PointerDown(4, 4);
                session.PointerUp(4, 4);
                int slot = b.TilesetIds.IndexOf("casas");
                Assert.AreEqual(1, slot, "the tileset was added to the map");
                Assert.AreEqual(MapTile.Encode(slot, 5), b.Layers[0].Get(4, 5));
                Assert.AreEqual(MapTile.Encode(slot, 6, MapTile.FlipH), b.Layers[0].Get(5, 5), "flipped tiles stay flipped");
                Assert.AreEqual(MapTile.Encode(slot, 9), b.Layers[2].Get(4, 4), "each layer goes to the layer of its kind");

                session.RenamePiece(house, "Casa roja");
                session.DeletePiece(house);
                Assert.AreEqual(MapTile.Encode(slot, 5), b.Layers[0].Get(4, 5), "removing the piece does not touch the maps");
                session.Undo();
                Assert.AreEqual(MapLayer.Empty, b.Layers[0].Get(4, 5), "placing is one undo");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void Pieces_are_saved_in_the_project()
        {
            var root = Path.Combine(Path.GetTempPath(), "cteditor_piece_" + Guid.NewGuid().ToString("N"));
            try
            {
                var content = MapClipboard.FromData(2, 1, new[] { (LayerRole.Ground, new[] { 3, MapTile.Encode(1, 4, MapTile.FlipV) }), (LayerRole.Above, new[] { -1, 7 }) },
                    new[] { "a", "b" });
                var repo = new JsonMapPieceRepository(root);
                repo.Save(new[] { new MapPiece("fuente", "Fuente", content) });
                var back = repo.Load().Single();
                Assert.AreEqual(("fuente", "Fuente", 2, 1), (back.Id, back.Name, back.Width, back.Height));
                CollectionAssert.AreEqual(new[] { 3, MapTile.Encode(1, 4, MapTile.FlipV) }, back.Content.Layers[0]);
                CollectionAssert.AreEqual(new[] { LayerRole.Ground, LayerRole.Above }, back.Content.Roles);
                CollectionAssert.AreEqual(new[] { "a", "b" }, back.Content.TilesetIds);
                Assert.Throws<ArgumentException>(() => MapClipboard.FromData(2, 2, new[] { (LayerRole.Ground, new[] { 1 }) }, new string[0]));
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
