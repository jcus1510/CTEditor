using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Editing;

namespace CTEditor.World.Domain
{
    /// <summary>Un bloque de tiles para pintar (uno o varios, elegidos en la paleta o con el cuentagotas). -1 = borrar.</summary>
    public sealed class TileStamp
    {
        public int Width { get; }
        public int Height { get; }
        private readonly int[] _tiles;

        public TileStamp(int width, int height, int[] tiles)
        {
            if (width <= 0 || height <= 0 || tiles == null || tiles.Length != width * height)
                throw new ArgumentException("Sello de tiles no válido.");
            Width = width; Height = height; _tiles = (int[])tiles.Clone();
        }

        public static TileStamp Single(int tile) => new TileStamp(1, 1, new[] { tile });
        public static readonly TileStamp Eraser = Single(MapLayer.Empty);

        public int this[int x, int y] => _tiles[y * Width + x];

        /// <summary>Tile en (x, y) repitiendo el sello desde (originX, originY) (para rellenar áreas).</summary>
        public int Tiled(int x, int y, int originX, int originY) => this[Mod(x - originX, Width), Mod(y - originY, Height)];

        /// <summary>A rectangular block of a tileset (what the palette selects).</summary>
        public static TileStamp FromTileset(Tileset ts, int column, int row, int width, int height)
        {
            var tiles = new int[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tiles[y * width + x] = (row + y) * ts.Columns + column + x;
            return new TileStamp(width, height, tiles);
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;
    }

    /// <summary>Un cambio de una casilla de una capa.</summary>
    public readonly struct TileChange
    {
        public readonly int Layer, X, Y, Before, After;
        public TileChange(int layer, int x, int y, int before, int after) { Layer = layer; X = x; Y = y; Before = before; After = after; }
    }

    /// <summary>Las herramientas de pintar mapas. Solo CALCULAN los cambios; aplicarlos (y deshacerlos) es cosa de los comandos.</summary>
    public static class MapTools
    {
        private static bool Editable(MapDefinition map, int layer) =>
            layer >= 0 && layer < map.Layers.Count && !map.Layers[layer].Locked;

        /// <summary>Lápiz: el sello con su esquina de arriba a la izquierda en (x, y).</summary>
        public static List<TileChange> Pencil(MapDefinition map, int layer, int x, int y, TileStamp stamp)
        {
            var list = new List<TileChange>();
            if (!Editable(map, layer)) return list;
            var l = map.Layers[layer];
            for (int sy = 0; sy < stamp.Height; sy++)
            for (int sx = 0; sx < stamp.Width; sx++)
                Add(list, l, layer, x + sx, y + sy, stamp[sx, sy]);
            return list;
        }

        /// <summary>Rectángulo: rellena el área repitiendo el sello desde su esquina.</summary>
        public static List<TileChange> Rectangle(MapDefinition map, int layer, int x0, int y0, int x1, int y1, TileStamp stamp)
        {
            var list = new List<TileChange>();
            if (!Editable(map, layer)) return list;
            var l = map.Layers[layer];
            int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1), minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                Add(list, l, layer, x, y, stamp.Tiled(x, y, minX, minY));
            return list;
        }

        /// <summary>Cubo: rellena la zona contigua (arriba, abajo, izquierda, derecha) del mismo tile que (x, y).</summary>
        public static List<TileChange> Fill(MapDefinition map, int layer, int x, int y, TileStamp stamp)
        {
            var list = new List<TileChange>();
            if (!Editable(map, layer) || !map.Contains(x, y)) return list;
            var l = map.Layers[layer];
            int target = l.Get(x, y);
            var seen = new bool[map.Width * map.Height];
            var queue = new Queue<(int, int)>();
            queue.Enqueue((x, y));
            seen[y * map.Width + x] = true;
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                Add(list, l, layer, cx, cy, stamp.Tiled(cx, cy, x, y));
                foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                {
                    if (!map.Contains(nx, ny) || seen[ny * map.Width + nx] || l.Get(nx, ny) != target) continue;
                    seen[ny * map.Width + nx] = true;
                    queue.Enqueue((nx, ny));
                }
            }
            return list;
        }

        /// <summary>Cuentagotas: copia un área como sello (de una capa, o de lo visible más alto si layer &lt; 0).</summary>
        public static TileStamp Pick(MapDefinition map, int layer, int x0, int y0, int x1, int y1)
        {
            int minX = Math.Max(0, Math.Min(x0, x1)), maxX = Math.Min(map.Width - 1, Math.Max(x0, x1));
            int minY = Math.Max(0, Math.Min(y0, y1)), maxY = Math.Min(map.Height - 1, Math.Max(y0, y1));
            if (minX > maxX || minY > maxY) return TileStamp.Eraser;
            int w = maxX - minX + 1, h = maxY - minY + 1;
            var tiles = new int[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tiles[y * w + x] = layer >= 0 && layer < map.Layers.Count
                    ? map.Layers[layer].Get(minX + x, minY + y)
                    : map.TopTile(minX + x, minY + y, out _);
            return new TileStamp(w, h, tiles);
        }

        private static void Add(List<TileChange> list, MapLayer l, int layer, int x, int y, int tile)
        {
            if (!l.Contains(x, y)) return;
            int before = l.Get(x, y);
            if (before != tile) list.Add(new TileChange(layer, x, y, before, tile));
        }

        public static void Apply(MapDefinition map, IEnumerable<TileChange> changes, bool undo = false)
        {
            foreach (var c in undo ? changes.Reverse() : changes)
                if (c.Layer < map.Layers.Count) map.Layers[c.Layer].Set(c.X, c.Y, undo ? c.Before : c.After);
        }
    }

    /// <summary>Pintar tiles (deshacible).</summary>
    public sealed class TilePaintCommand : IEditCommand
    {
        private readonly MapDefinition _map;
        public IReadOnlyList<TileChange> Changes { get; }
        public string Label { get; }

        public TilePaintCommand(MapDefinition map, IReadOnlyList<TileChange> changes, string label = null)
        {
            _map = map;
            Changes = changes;
            Label = label ?? $"pintar {changes.Count} tiles";
        }

        public void Do() => MapTools.Apply(_map, Changes);
        public void Undo() => MapTools.Apply(_map, Changes, undo: true);
    }

    /// <summary>
    /// Un trazo de lápiz o goma mientras se arrastra: cada punto se aplica al momento y, al soltar, todo el trazo es UN
    /// solo paso de deshacer (con el tile que había al principio en cada casilla).
    /// </summary>
    public sealed class TileStroke
    {
        private readonly MapDefinition _map;
        private readonly Dictionary<(int, int, int), (int before, int after)> _cells = new Dictionary<(int, int, int), (int, int)>();
        private readonly List<(int, int, int)> _order = new List<(int, int, int)>();

        public TileStroke(MapDefinition map) { _map = map; }

        /// <summary>Applies the changes now and remembers them.</summary>
        public void Add(IEnumerable<TileChange> changes)
        {
            foreach (var c in changes)
            {
                var key = (c.Layer, c.X, c.Y);
                if (_cells.TryGetValue(key, out var v)) _cells[key] = (v.before, c.After);
                else { _cells[key] = (c.Before, c.After); _order.Add(key); }
                _map.Layers[c.Layer].Set(c.X, c.Y, c.After);
            }
        }

        public bool IsEmpty => _cells.Values.All(v => v.before == v.after);

        public TilePaintCommand ToCommand(string label = null)
        {
            var list = _order.Select(k => new TileChange(k.Item1, k.Item2, k.Item3, _cells[k].before, _cells[k].after))
                .Where(c => c.Before != c.After).ToList();
            return new TilePaintCommand(_map, list, label);
        }
    }

    /// <summary>A frozen copy of a map's layers, size and properties (for structural changes: layers, resize, tileset).</summary>
    public sealed class MapSnapshot
    {
        private readonly List<MapLayer> _layers;
        private readonly int _width, _height;
        private readonly string _name, _tileset, _music;
        private readonly bool _bicycle, _outdoor;

        public MapSnapshot(MapDefinition map)
        {
            _layers = map.Layers.Select(l => l.Clone()).ToList();
            (_width, _height, _name, _tileset, _music, _bicycle, _outdoor) = (map.Width, map.Height, map.Name, map.TilesetId, map.Music, map.Bicycle, map.Outdoor);
        }

        public void RestoreInto(MapDefinition map)
        {
            if (map.Width != _width || map.Height != _height)
            {
                // Resize with no layers so the size changes, then put the saved layers back.
                var keep = new List<MapLayer>(map.Layers);
                map.Layers.Clear();
                map.Resize(_width, _height);
                map.Layers.AddRange(keep);
            }
            map.Layers.Clear();
            map.Layers.AddRange(_layers.Select(l => l.Clone()));
            (map.Name, map.TilesetId, map.Music, map.Bicycle, map.Outdoor) = (_name, _tileset, _music, _bicycle, _outdoor);
        }
    }

    /// <summary>Any change to the structure of a map (add / remove / move / rename a layer, resize, tileset, properties), deshacible.</summary>
    public sealed class MapStructureCommand : IEditCommand
    {
        private readonly MapDefinition _map;
        private readonly MapSnapshot _before, _after;
        public string Label { get; }

        /// <summary>Runs 'change' now and records the map before and after.</summary>
        public static MapStructureCommand Run(MapDefinition map, string label, Action<MapDefinition> change)
        {
            var before = new MapSnapshot(map);
            change(map);
            return new MapStructureCommand(map, label, before, new MapSnapshot(map));
        }

        private MapStructureCommand(MapDefinition map, string label, MapSnapshot before, MapSnapshot after)
        {
            _map = map; Label = label; _before = before; _after = after;
        }

        public void Do() => _after.RestoreInto(_map);
        public void Undo() => _before.RestoreInto(_map);
    }

    /// <summary>Cambiar las propiedades de un tile del tileset (deshacible).</summary>
    public sealed class TileAttributeCommand : IEditCommand
    {
        private readonly TileAttributes _attributes;
        private readonly IReadOnlyList<(int tile, TileProperties before, TileProperties after)> _changes;
        public string Label { get; }

        public TileAttributeCommand(TileAttributes attributes, IEnumerable<(int tile, TileProperties after)> changes, string label)
        {
            _attributes = attributes;
            _changes = changes.Select(c => (c.tile, attributes.Get(c.tile), c.after.Clone())).ToList();
            Label = label;
        }

        public bool IsEmpty => _changes.All(c => Same(c.before, c.after));

        public void Do() { foreach (var c in _changes) _attributes.Set(c.tile, c.after); }
        public void Undo() { foreach (var c in _changes) _attributes.Set(c.tile, c.before); }

        private static bool Same(TileProperties a, TileProperties b) =>
            a.Blocked == b.Blocked && a.Priority == b.Priority && a.TerrainTag == b.TerrainTag && a.Bush == b.Bush && a.Counter == b.Counter;
    }
}
