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

        /// <summary>A rectangular block of a tileset (what the palette selects), for the tileset in hueco 'slot' of the map.</summary>
        public static TileStamp FromTileset(Tileset ts, int column, int row, int width, int height, int slot = 0)
        {
            var tiles = new int[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tiles[y * width + x] = MapTile.Encode(slot, (row + y) * ts.Columns + column + x);
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

        // ── Automatic layers ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// La capa a la que va un tile con las capas automáticas: la del papel de su pieza (suelo, detalle, encima). Si el
        /// mapa no tiene esa capa o está bloqueada, la de reserva (la activa).
        /// </summary>
        public static int AutoLayerOf(MapDefinition map, MapTilesets tilesets, int cell, int fallback)
        {
            var role = tilesets.PieceOf(cell) switch
            {
                TilePiece.Ground => LayerRole.Ground,
                TilePiece.Detail => LayerRole.Detail,
                _ => LayerRole.Above,
            };
            int i = map.LayerFor(role);
            return i >= 0 && !map.Layers[i].Locked ? i : fallback;
        }

        /// <summary>Lápiz con capas automáticas: cada tile del sello va a su capa; -1 borra lo más alto de la casilla.</summary>
        public static List<TileChange> PencilAuto(MapDefinition map, MapTilesets tilesets, int x, int y, TileStamp stamp, int fallback)
        {
            var list = new List<TileChange>();
            for (int sy = 0; sy < stamp.Height; sy++)
            for (int sx = 0; sx < stamp.Width; sx++)
                AddAuto(list, map, tilesets, x + sx, y + sy, stamp[sx, sy], fallback);
            return list;
        }

        public static List<TileChange> RectangleAuto(MapDefinition map, MapTilesets tilesets, int x0, int y0, int x1, int y1, TileStamp stamp, int fallback)
        {
            var list = new List<TileChange>();
            int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1), minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                AddAuto(list, map, tilesets, x, y, stamp.Tiled(x, y, minX, minY), fallback);
            return list;
        }

        /// <summary>Relleno con capas automáticas: rellena en la capa del primer tile del sello.</summary>
        public static List<TileChange> FillAuto(MapDefinition map, MapTilesets tilesets, int x, int y, TileStamp stamp, int fallback)
        {
            int first = stamp[0, 0];
            int layer = first < 0 ? TopLayerAt(map, x, y, fallback) : AutoLayerOf(map, tilesets, first, fallback);
            return Fill(map, layer, x, y, stamp);
        }

        /// <summary>The highest unlocked layer with something at (x, y) (fallback if the cell is empty).</summary>
        public static int TopLayerAt(MapDefinition map, int x, int y, int fallback)
        {
            for (int i = map.Layers.Count - 1; i >= 0; i--)
                if (!map.Layers[i].Locked && map.Layers[i].Get(x, y) >= 0) return i;
            return fallback;
        }

        private static void AddAuto(List<TileChange> list, MapDefinition map, MapTilesets tilesets, int x, int y, int cell, int fallback)
        {
            if (!map.Contains(x, y)) return;
            int layer = cell < 0 ? TopLayerAt(map, x, y, -1) : AutoLayerOf(map, tilesets, cell, fallback);
            if (layer < 0 || layer >= map.Layers.Count || map.Layers[layer].Locked) return;
            Add(list, map.Layers[layer], layer, x, y, cell);
        }

        // ── Selection: copy, paste, delete ────────────────────────────────────────────────────────

        /// <summary>Borra todas las capas (no bloqueadas) dentro del rectángulo.</summary>
        public static List<TileChange> Clear(MapDefinition map, int x0, int y0, int x1, int y1)
        {
            var list = new List<TileChange>();
            int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1), minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
            for (int i = 0; i < map.Layers.Count; i++)
            {
                if (map.Layers[i].Locked) continue;
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    Add(list, map.Layers[i], i, x, y, MapLayer.Empty);
            }
            return list;
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
        private readonly List<string> _tilesets;
        private readonly List<MapObject> _objects;
        private readonly List<EncounterArea> _encounters;
        private readonly int _width, _height, _worldX, _worldY;
        private readonly string _name, _music, _weather;
        private readonly bool _bicycle, _outdoor, _inWorld, _onRegionMap;
        private readonly MapKind _kind;
        private readonly SectionCategory _category;

        public MapSnapshot(MapDefinition map)
        {
            _layers = map.Layers.Select(l => l.Clone()).ToList();
            _tilesets = map.TilesetIds.ToList();
            _objects = map.Objects.Select(o => o.Clone()).ToList();
            _encounters = map.Encounters.Select(a => a.Clone()).ToList();
            (_width, _height, _name, _music, _bicycle, _outdoor) = (map.Width, map.Height, map.Name, map.Music, map.Bicycle, map.Outdoor);
            (_worldX, _worldY, _inWorld, _onRegionMap, _kind, _category, _weather) = (map.WorldX, map.WorldY, map.InWorld, map.ShowOnRegionMap, map.Kind, map.Category, map.Weather);
        }

        public void RestoreInto(MapDefinition map)
        {
            if (map.Width != _width || map.Height != _height)
            {
                // Resize an empty copy of the lists so only the size changes (the saved lists are put back below).
                map.Layers.Clear();
                map.Objects.Clear();
                map.Encounters.Clear();
                map.Resize(_width, _height);
            }
            map.Layers.Clear();
            map.Layers.AddRange(_layers.Select(l => l.Clone()));
            map.TilesetIds.Clear();
            map.TilesetIds.AddRange(_tilesets);
            map.Objects.Clear();
            map.Objects.AddRange(_objects.Select(o => o.Clone()));
            map.Encounters.Clear();
            map.Encounters.AddRange(_encounters.Select(a => a.Clone()));
            (map.Name, map.Music, map.Bicycle, map.Outdoor) = (_name, _music, _bicycle, _outdoor);
            (map.WorldX, map.WorldY, map.InWorld, map.ShowOnRegionMap, map.Kind, map.Category, map.Weather) = (_worldX, _worldY, _inWorld, _onRegionMap, _kind, _category, _weather);
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
            a.Blocked == b.Blocked && a.Priority == b.Priority && a.TerrainTag == b.TerrainTag && a.Bush == b.Bush && a.Counter == b.Counter && a.Piece == b.Piece;
    }
}

namespace CTEditor.World.Domain
{
    /// <summary>
    /// Un trozo de mapa copiado con TODAS sus capas (Ctrl+C). Al pegar se ponen solo las casillas que tenían algo, capa
    /// por capa (las vacías no borran lo de debajo). Los tiles se traducen si el mapa de destino tiene sus tilesets en
    /// otros huecos.
    /// </summary>
    public sealed class MapClipboard
    {
        public int Width { get; }
        public int Height { get; }
        /// <summary>Por capa (por índice): las casillas, fila a fila.</summary>
        public IReadOnlyList<int[]> Layers { get; }
        public IReadOnlyList<LayerRole> Roles { get; }
        /// <summary>Tilesets del mapa de origen por hueco.</summary>
        public IReadOnlyList<string> TilesetIds { get; }

        private MapClipboard(int w, int h, List<int[]> layers, List<LayerRole> roles, List<string> tilesets)
        {
            Width = w; Height = h; Layers = layers; Roles = roles; TilesetIds = tilesets;
        }

        public static MapClipboard Copy(MapDefinition map, int x0, int y0, int x1, int y1)
        {
            int minX = Math.Max(0, Math.Min(x0, x1)), maxX = Math.Min(map.Width - 1, Math.Max(x0, x1));
            int minY = Math.Max(0, Math.Min(y0, y1)), maxY = Math.Min(map.Height - 1, Math.Max(y0, y1));
            int w = Math.Max(1, maxX - minX + 1), h = Math.Max(1, maxY - minY + 1);
            var layers = new List<int[]>();
            foreach (var l in map.Layers)
            {
                var a = new int[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) a[y * w + x] = l.Get(minX + x, minY + y);
                layers.Add(a);
            }
            return new MapClipboard(w, h, layers, map.Layers.Select(l => l.Role).ToList(), map.TilesetIds.ToList());
        }

        /// <summary>
        /// Los cambios de pegar con la esquina en (x, y). Cada capa copiada va a la capa del mismo papel en el destino (o
        /// a la misma posición si no hay papeles). Añade al mapa los tilesets que falten (antes de calcular los cambios).
        /// </summary>
        public List<TileChange> PasteChanges(MapDefinition map, int x, int y)
        {
            var slotMap = TilesetIds.Select(id => string.IsNullOrEmpty(id) ? 0 : map.SlotOf(id)).ToArray();
            var list = new List<TileChange>();
            for (int li = 0; li < Layers.Count; li++)
            {
                int target = Roles[li] != LayerRole.Custom ? map.LayerFor(Roles[li]) : -1;
                if (target < 0) target = li < map.Layers.Count ? li : map.Layers.Count - 1;
                if (target < 0 || map.Layers[target].Locked) continue;
                var layer = map.Layers[target];
                for (int yy = 0; yy < Height; yy++)
                for (int xx = 0; xx < Width; xx++)
                {
                    int cell = Layers[li][yy * Width + xx];
                    if (cell < 0 || !layer.Contains(x + xx, y + yy)) continue;
                    int slot = MapTile.Slot(cell);
                    int translated = slot < slotMap.Length && slotMap[slot] >= 0 ? MapTile.Encode(slotMap[slot], MapTile.Index(cell)) : cell;
                    int before = layer.Get(x + xx, y + yy);
                    if (before != translated) list.Add(new TileChange(target, x + xx, y + yy, before, translated));
                }
            }
            return list;
        }
    }

    /// <summary>Pintar o borrar casillas de una zona de encuentros (deshacible).</summary>
    public sealed class EncounterPaintCommand : IEditCommand
    {
        private readonly EncounterArea _area;
        private readonly List<(int x, int y)> _added, _removed;
        public string Label { get; }

        public EncounterPaintCommand(EncounterArea area, IEnumerable<(int x, int y)> added, IEnumerable<(int x, int y)> removed)
        {
            _area = area;
            _added = added.ToList();
            _removed = removed.ToList();
            Label = "zona «" + area.Name + "»";
        }

        public bool IsEmpty => _added.Count == 0 && _removed.Count == 0;
        public void Do() { foreach (var c in _added) _area.Paint(c.x, c.y); foreach (var c in _removed) _area.Erase(c.x, c.y); }
        public void Undo() { foreach (var c in _added) _area.Erase(c.x, c.y); foreach (var c in _removed) _area.Paint(c.x, c.y); }
    }
}
