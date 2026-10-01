using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Puertas que se enlazan solas (fase 6, B3): se crean por parejas, se enlazan por id (moverlas no rompe nada), al
    /// pisar una se sale de la otra, y borrar una avisa de la otra en la ventana Problemas. Todo se deshace de una vez.
    /// </summary>
    public class DoorTests
    {
        private static MapDefinition Map(string id, MapKind kind = MapKind.Exterior, int w = 10, int h = 8) =>
            new MapDefinition(id, id, w, h) { Kind = kind };

        [Test]
        public void A_pair_links_both_ways_and_each_leads_one_step_out_of_the_other()
        {
            var town = Map("pueblo");
            var house = Map("casa", MapKind.Interior);
            var pair = Doors.CreatePair(town, 4, 3, house, 5, 7);
            Assert.IsNotNull(pair);
            var (outside, inside) = pair.Value;
            Assert.AreEqual(("casa", inside.Id), Doors.LinkOf(outside));
            Assert.AreEqual(("pueblo", outside.Id), Doors.LinkOf(inside));
            Assert.AreEqual(FacingDirection.Down, Doors.ExitOf(outside), "outside: you leave the house downwards");
            Assert.AreEqual(FacingDirection.Up, Doors.ExitOf(inside), "inside: up, off the mat");

            MapDefinition Find(string id) => id == "pueblo" ? town : id == "casa" ? house : null;
            var into = Doors.Destination(outside, Find).Value;
            Assert.AreEqual((house, 5, 6, FacingDirection.Up), (into.map, into.x, into.y, into.facing));
            var back = Doors.Destination(inside, Find).Value;
            Assert.AreEqual((town, 4, 4), (back.map, back.x, back.y));

            outside.X = 7; // moving keeps the link: it is by id
            Assert.AreEqual(7, Doors.Destination(inside, Find).Value.x);
            Assert.IsEmpty(Doors.Check(town, Find).ToList());
            Assert.IsEmpty(Doors.Check(house, Find).ToList());

            Assert.IsNull(Doors.CreatePair(town, 7, 3, house, 1, 1), "a cell with a door already");
            Assert.IsNull(Doors.CreatePair(town, 20, 3, house, 1, 1), "outside the map");
            var second = Doors.CreatePair(town, 1, 1, town, 8, 1).Value;
            Assert.AreNotEqual(second.a.Id, second.b.Id, "two doors in the same map get different ids");
        }

        [Test]
        public void Problems_say_which_doors_lead_nowhere()
        {
            var town = Map("pueblo");
            var house = Map("casa", MapKind.Interior);
            var (outside, inside) = Doors.CreatePair(town, 4, 3, house, 5, 0).Value;
            MapDefinition Find(string id) => id == "pueblo" ? town : id == "casa" ? house : null;
            StringAssert.Contains("se saldría", Doors.Check(house, Find).Single().text, "arriving up from row 0 leaves the map");

            house.Objects.Remove(inside);
            var problem = Doors.Check(town, Find).Single();
            Assert.IsTrue(problem.error);
            StringAssert.Contains("se borró", problem.text);
            Assert.IsNull(Doors.Destination(outside, Find));

            Doors.Unlink(outside);
            StringAssert.Contains("no lleva", Doors.Check(town, Find).Single().text);
        }

        [Test]
        public void The_session_makes_doors_with_one_undo_and_keeps_the_other_end_when_one_is_removed()
        {
            var session = new MapEditorSession(new MemoryMaps(), new NoTilesets());
            session.CreateMap("Pueblo", width: 10, height: 8, tilesetId: "");
            var town = session.Map;

            // Door tool: first click marks the door, the second (here, the same map) makes its exit.
            session.SetTool(MapTool.Door);
            session.PointerDown(2, 2);
            session.PointerUp(2, 2);
            Assert.IsTrue(session.PendingDoor.HasValue);
            session.PointerDown(6, 5);
            session.PointerUp(6, 5);
            Assert.IsFalse(session.PendingDoor.HasValue);
            Assert.AreEqual(2, Doors.In(town).Count());
            session.Undo();
            Assert.AreEqual(0, Doors.In(town).Count(), "both doors go in one undo");
            session.Redo();
            Assert.AreEqual(2, Doors.In(town).Count());

            // Dragging a door moves it; the link stays.
            session.PointerDown(2, 2);
            session.PointerDrag(3, 2);
            session.PointerUp(3, 2);
            var moved = Doors.At(town, 3, 2);
            Assert.IsNotNull(moved);
            Assert.IsNotNull(Doors.Partner(moved, session.Find));

            // A new interior made together with its door.
            var inside = session.CreateDoorWithInterior(8, 1, "Casa", 13, 9);
            Assert.AreEqual(MapKind.Interior, inside.Kind);
            var mat = Doors.In(inside).Single();
            Assert.AreEqual((6, 8), (mat.X, mat.Y), "the exit mat at the bottom middle");
            Assert.AreEqual(town.Id, Doors.LinkOf(mat).map);

            // Removing the outside door leaves the mat unlinked, and Problems says so.
            var outside = Doors.At(town, 8, 1);
            session.RemoveDoor(outside);
            Assert.IsFalse(Doors.IsLinked(Doors.In(inside).Single()));
            Assert.IsTrue(ProblemFinder.Check(session).Any(p => p.MapId == inside.Id && p.Text.Contains("no lleva")));
            session.Undo();
            Assert.IsNotNull(Doors.At(town, 8, 1));
            Assert.IsTrue(Doors.IsLinked(Doors.In(inside).Single()), "undo puts the link back");

            session.CancelPendingDoor();
            session.SetTool(MapTool.Pencil);
            Assert.IsNull(session.SelectedObject);
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
