using System.Collections.Generic;
using System.Linq;
using CTEditor.Art.Domain;

namespace CTEditor.World.Domain
{
    /// <summary>
    /// Keeps autotiles joined: after any change of tiles (pencil, rectangle, fill, eraser, paste...), each changed cell and
    /// its 8 neighbours that are autotiles get the piece that matches their neighbours. The map edge counts as "the same"
    /// (water that reaches the edge has no shore there), as in RPG Maker.
    /// </summary>
    public static class AutotileResolver
    {
        private static readonly (int dx, int dy, int bit)[] Around =
        {
            (0, -1, AutotileLayout.N), (1, -1, AutotileLayout.NE), (1, 0, AutotileLayout.E), (1, 1, AutotileLayout.SE),
            (0, 1, AutotileLayout.S), (-1, 1, AutotileLayout.SW), (-1, 0, AutotileLayout.W), (-1, -1, AutotileLayout.NW),
        };

        /// <summary>The same changes (their cells with the right piece) plus the neighbours that had to change.</summary>
        public static List<TileChange> Resolve(MapDefinition map, MapTilesets sets, IReadOnlyList<TileChange> changes)
        {
            var result = changes as List<TileChange> ?? changes.ToList();
            if (map == null || sets == null || changes.Count == 0 || !sets.Slots.Any(t => t?.Free != null && t.Free.Pieces.Any(p => p.IsAutotile)))
                return result;

            var pending = new Dictionary<(int l, int x, int y), int>();
            foreach (var c in changes) pending[(c.Layer, c.X, c.Y)] = c.After;
            int Get(int l, int x, int y) => pending.TryGetValue((l, x, y), out var v) ? v : map.Layers[l].Get(x, y);

            (int slot, FreePiece piece)? Info(int cell)
            {
                if (cell < 0) return null;
                var a = sets.For(cell)?.AutotileOf(MapTile.Index(cell));
                return a == null ? ((int, FreePiece)?)null : (MapTile.Slot(cell), a);
            }

            var cells = new HashSet<(int l, int x, int y)>();
            foreach (var c in changes)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (map.Contains(c.X + dx, c.Y + dy)) cells.Add((c.Layer, c.X + dx, c.Y + dy));

            var fixedCells = new Dictionary<(int l, int x, int y), int>();
            foreach (var (l, x, y) in cells)
            {
                int cell = Get(l, x, y);
                var info = Info(cell);
                if (info == null) continue;
                int mask = 0;
                foreach (var (dx, dy, bit) in Around)
                {
                    int nx = x + dx, ny = y + dy;
                    bool same = !map.Contains(nx, ny) || Equals(Info(Get(l, nx, ny)), info);
                    if (same) mask |= bit;
                }
                var ts = sets.For(cell);
                int want = MapTile.Encode(info.Value.slot, ts.AutotileTile(info.Value.piece, AutotileLayout.VariantOf(mask)));
                if (want != cell) fixedCells[(l, x, y)] = want;
            }
            if (fixedCells.Count == 0) return result;

            var output = new List<TileChange>(changes.Count + fixedCells.Count);
            foreach (var c in changes)
            {
                var key = (c.Layer, c.X, c.Y);
                if (fixedCells.TryGetValue(key, out var after)) { output.Add(new TileChange(c.Layer, c.X, c.Y, c.Before, after)); fixedCells.Remove(key); }
                else output.Add(c);
            }
            foreach (var kv in fixedCells)
                output.Add(new TileChange(kv.Key.l, kv.Key.x, kv.Key.y, map.Layers[kv.Key.l].Get(kv.Key.x, kv.Key.y), kv.Value));
            return output;
        }
    }
}
