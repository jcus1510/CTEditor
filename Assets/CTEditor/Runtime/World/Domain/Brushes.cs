using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.World.Domain
{
    /// <summary>One tile of a random brush and how often it comes out (weight 3 = three times as often as weight 1).</summary>
    public sealed class BrushTile
    {
        public string TilesetId { get; }
        public int Index { get; }
        public int Weight { get; set; }

        public BrushTile(string tilesetId, int index, int weight = 1)
        {
            TilesetId = tilesetId ?? "";
            Index = index;
            Weight = Math.Max(0, weight);
        }

        public BrushTile Clone() => new BrushTile(TilesetId, Index, Weight);
    }

    /// <summary>
    /// RANDOM BRUSH: grass with flowers here and there, rocks of several shapes... Each painted cell gets one of its tiles at
    /// random, more often the heavier ones. Works with the pencil, the rectangle and the fill, and saved in the project.
    /// Tiles are kept by tileset id, so a brush works in any map that uses that tileset.
    /// </summary>
    public sealed class RandomBrush
    {
        public string Id { get; }
        public string Name { get; set; }
        public List<BrushTile> Tiles { get; } = new List<BrushTile>();

        public RandomBrush(string id, string name)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("El pincel necesita un id.", nameof(id)) : id;
            Name = name ?? id;
        }

        public int TotalWeight => Tiles.Sum(t => t.Weight);

        /// <summary>The tile for a roll between 0 and TotalWeight − 1 (null if every weight is 0).</summary>
        public BrushTile Pick(int roll)
        {
            if (TotalWeight <= 0) return null;
            roll = Math.Max(0, Math.Min(TotalWeight - 1, roll));
            foreach (var t in Tiles)
            {
                if (roll < t.Weight) return t;
                roll -= t.Weight;
            }
            return Tiles.Last(t => t.Weight > 0);
        }

        /// <summary>How likely each tile is, from 0 to 1.</summary>
        public double Chance(BrushTile t) => TotalWeight <= 0 ? 0 : (double)t.Weight / TotalWeight;

        /// <summary>Adds a tile (or more weight to it if it is there already).</summary>
        public void Add(string tilesetId, int index, int weight = 1)
        {
            var same = Tiles.FirstOrDefault(t => t.TilesetId == tilesetId && t.Index == index);
            if (same != null) same.Weight += weight;
            else Tiles.Add(new BrushTile(tilesetId, index, weight));
        }

        public RandomBrush Clone(string id = null)
        {
            var b = new RandomBrush(id ?? Id, Name);
            b.Tiles.AddRange(Tiles.Select(t => t.Clone()));
            return b;
        }

        /// <summary>The map cell of a brush tile in that map (-1 if the map does not use its tileset).</summary>
        public static int CellIn(MapDefinition map, BrushTile t)
        {
            int slot = map.TilesetIds.IndexOf(t.TilesetId);
            return slot < 0 ? MapTile.Empty : MapTile.Encode(slot, t.Index);
        }

        /// <summary>The tiles this map can paint (its tilesets), with their weights.</summary>
        public bool UsableIn(MapDefinition map) => Tiles.Any(t => t.Weight > 0 && CellIn(map, t) >= 0);

        /// <summary>A cell that is never a real tile: what the tools paint before the brush picks each cell.</summary>
        public static readonly int Placeholder = MapTile.Encode(MapTile.SlotMask, MapTile.IndexMask);

        /// <summary>Puts a random tile of the brush in every change that paints the placeholder; drops the ones that end equal.</summary>
        public List<TileChange> Randomize(MapDefinition map, IEnumerable<TileChange> changes, Func<int, int> random)
        {
            var usable = Tiles.Where(t => t.Weight > 0 && CellIn(map, t) >= 0).ToList();
            int total = usable.Sum(t => t.Weight);
            var list = new List<TileChange>();
            foreach (var c in changes)
            {
                if (c.After != Placeholder) { list.Add(c); continue; }
                if (total <= 0) continue;
                int roll = random(total);
                BrushTile pick = usable[usable.Count - 1];
                foreach (var t in usable)
                {
                    if (roll < t.Weight) { pick = t; break; }
                    roll -= t.Weight;
                }
                int after = CellIn(map, pick);
                if (after != c.Before) list.Add(new TileChange(c.Layer, c.X, c.Y, c.Before, after));
            }
            return list;
        }
    }

    /// <summary>Where the project's brushes are kept («datos/pinceles.json»).</summary>
    public interface IBrushRepository
    {
        IReadOnlyList<RandomBrush> Load();
        void Save(IReadOnlyList<RandomBrush> brushes);
    }
}
