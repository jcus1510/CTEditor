using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Art.Domain;
using CTEditor.SharedKernel.Editing;

namespace CTEditor.World.Domain
{
    /// <summary>
    /// DOORS that link themselves: a door is a map object («puerta») that points to ANOTHER door by map id and object id.
    /// Doors are created in pairs (the house door outside and the mat inside), so going in and coming out always match;
    /// moving either one keeps the link (it is by id, not by position). Stepping on a door takes the player one step out
    /// of the other one, in its exit direction (outside: down, away from the house; inside: up, off the mat).
    /// </summary>
    public static class Doors
    {
        public const string Kind = "puerta";
        public const string LinkMapKey = "enlace_mapa";
        public const string LinkDoorKey = "enlace_puerta";
        public const string ExitKey = "salida";

        private static readonly string[] ExitNames = { "abajo", "izquierda", "derecha", "arriba" };

        public static bool IsDoor(MapObject o) => o != null && o.Kind == Kind;

        public static IEnumerable<MapObject> In(MapDefinition map) => map?.Objects.Where(IsDoor) ?? Enumerable.Empty<MapObject>();

        public static MapObject At(MapDefinition map, int x, int y) => In(map).FirstOrDefault(d => d.Covers(x, y));

        public static (string map, string door) LinkOf(MapObject door) =>
            (door.Properties.TryGetValue(LinkMapKey, out var m) ? m : "", door.Properties.TryGetValue(LinkDoorKey, out var d) ? d : "");

        public static bool IsLinked(MapObject door) => !string.IsNullOrEmpty(LinkOf(door).map) && !string.IsNullOrEmpty(LinkOf(door).door);

        /// <summary>Where the player comes out when arriving through this door: one step away from it.</summary>
        public static FacingDirection ExitOf(MapObject door)
        {
            if (door.Properties.TryGetValue(ExitKey, out var name))
            {
                int i = Array.IndexOf(ExitNames, name);
                if (i >= 0) return (FacingDirection)i;
            }
            return FacingDirection.Down;
        }

        public static void SetExit(MapObject door, FacingDirection exit) => door.Properties[ExitKey] = ExitNames[(int)exit];

        public static string ExitName(FacingDirection d) => ExitNames[(int)d];

        /// <summary>The natural exit for a door in this kind of map: outside you leave the house downwards; inside, up from the mat.</summary>
        public static FacingDirection DefaultExit(MapDefinition map) => map.Kind == MapKind.Interior ? FacingDirection.Up : FacingDirection.Down;

        public static void Link(MapDefinition mapA, MapObject a, MapDefinition mapB, MapObject b)
        {
            a.Properties[LinkMapKey] = mapB.Id;
            a.Properties[LinkDoorKey] = b.Id;
            b.Properties[LinkMapKey] = mapA.Id;
            b.Properties[LinkDoorKey] = a.Id;
        }

        public static void Unlink(MapObject door)
        {
            door.Properties.Remove(LinkMapKey);
            door.Properties.Remove(LinkDoorKey);
        }

        /// <summary>A new id for a door in that map («puerta», «puerta_2»...).</summary>
        public static string NewId(MapDefinition map)
        {
            var used = new HashSet<string>(map.Objects.Select(o => o.Id));
            if (!used.Contains(Kind)) return Kind;
            for (int i = 2; ; i++) if (!used.Contains(Kind + "_" + i)) return Kind + "_" + i;
        }

        /// <summary>
        /// Creates the two doors at once and links them (they can be in the same map). Returns them; nothing is added if
        /// either cell is outside its map or already has a door.
        /// </summary>
        public static (MapObject a, MapObject b)? CreatePair(MapDefinition mapA, int ax, int ay, MapDefinition mapB, int bx, int by)
        {
            if (mapA == null || mapB == null || !mapA.Contains(ax, ay) || !mapB.Contains(bx, by)) return null;
            if (At(mapA, ax, ay) != null || At(mapB, bx, by) != null) return null;
            if (mapA == mapB && ax == bx && ay == by) return null;
            var a = new MapObject(NewId(mapA), Kind, ax, ay, "Puerta");
            mapA.Objects.Add(a);
            var b = new MapObject(NewId(mapB), Kind, bx, by, "Puerta");
            mapB.Objects.Add(b);
            SetExit(a, DefaultExit(mapA));
            SetExit(b, DefaultExit(mapB));
            a.Name = "A " + mapB.Name;
            b.Name = "A " + mapA.Name;
            Link(mapA, a, mapB, b);
            return (a, b);
        }

        /// <summary>The other end of a door (null if it is not linked or the other end is gone).</summary>
        public static (MapDefinition map, MapObject door)? Partner(MapObject door, Func<string, MapDefinition> find)
        {
            var (mapId, doorId) = LinkOf(door);
            if (string.IsNullOrEmpty(mapId)) return null;
            var map = find(mapId);
            var other = map?.Objects.FirstOrDefault(o => o.Id == doorId && IsDoor(o));
            return other == null ? ((MapDefinition, MapObject)?)null : (map, other);
        }

        /// <summary>Where stepping on this door takes the player (map, cell, facing). Null if it leads nowhere.</summary>
        public static (MapDefinition map, int x, int y, FacingDirection facing)? Destination(MapObject door, Func<string, MapDefinition> find)
        {
            var p = Partner(door, find);
            if (p == null) return null;
            var (map, other) = p.Value;
            var exit = ExitOf(other);
            var (x, y) = Passability.Step(other.X, other.Y, exit);
            if (!map.Contains(x, y)) (x, y) = (other.X, other.Y);
            return (map, x, y, exit);
        }

        /// <summary>What is wrong with the doors of a map: not linked, the other end gone or not pointing back, the way out off the map.</summary>
        public static IEnumerable<(MapObject door, string text, bool error)> Check(MapDefinition map, Func<string, MapDefinition> find)
        {
            foreach (var d in In(map))
            {
                if (!IsLinked(d)) { yield return (d, $"«{map.Name}»: la puerta «{d.Name}» no lleva a ningún sitio.", true); continue; }
                var (mapId, _) = LinkOf(d);
                var target = find(mapId);
                if (target == null) { yield return (d, $"«{map.Name}»: la puerta «{d.Name}» lleva a un mapa que ya no existe ({mapId}).", true); continue; }
                var p = Partner(d, find);
                if (p == null) { yield return (d, $"«{map.Name}»: la otra puerta de «{d.Name}» (en «{target.Name}») se borró.", true); continue; }
                var (pm, pd) = p.Value;
                var back = LinkOf(pd);
                if (back.map != map.Id || back.door != d.Id)
                    yield return (d, $"«{map.Name}»: «{d.Name}» lleva a «{pd.Name}» ({pm.Name}), pero esa no vuelve aquí.", false);
                var (sx, sy) = Passability.Step(d.X, d.Y, ExitOf(d));
                if (!map.Contains(sx, sy))
                    yield return (d, $"«{map.Name}»: al llegar por «{d.Name}» se saldría del mapa: cambia su salida.", false);
            }
        }
    }

    /// <summary>Undo for changes to the objects of one or more maps (a pair of doors, moving or removing one).</summary>
    public sealed class MapObjectsCommand : IEditCommand
    {
        private readonly List<(MapDefinition map, List<MapObject> before, List<MapObject> after)> _maps =
            new List<(MapDefinition, List<MapObject>, List<MapObject>)>();

        public string Label { get; }
        public IEnumerable<MapDefinition> Maps => _maps.Select(m => m.map);

        private MapObjectsCommand(string label) { Label = label; }

        /// <summary>Runs the change and keeps what each map had before and after.</summary>
        public static MapObjectsCommand Run(string label, IEnumerable<MapDefinition> maps, Action change)
        {
            var cmd = new MapObjectsCommand(label);
            var list = maps.Where(m => m != null).Distinct().ToList();
            var before = list.Select(m => m.Objects.Select(o => o.Clone()).ToList()).ToList();
            change();
            for (int i = 0; i < list.Count; i++)
                cmd._maps.Add((list[i], before[i], list[i].Objects.Select(o => o.Clone()).ToList()));
            return cmd;
        }

        public void Do() { foreach (var (m, _, after) in _maps) Put(m, after); }
        public void Undo() { foreach (var (m, before, _) in _maps) Put(m, before); }

        private static void Put(MapDefinition map, List<MapObject> objects)
        {
            map.Objects.Clear();
            map.Objects.AddRange(objects.Select(o => o.Clone()));
        }
    }
}
