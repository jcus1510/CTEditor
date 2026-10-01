using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Art.Domain
{
    /// <summary>
    /// A piece cut by hand from anywhere in the image, with pixel precision (a big tree, a loose object from a ripped
    /// sheet that does not follow the grid). It is used as a block of whole tiles: as many as its size needs, with the
    /// piece standing on the bottom of the block.
    /// </summary>
    public sealed class FreePiece
    {
        public string Name { get; set; }
        /// <summary>Where it is in the original image (pixels, top-left origin).</summary>
        public PixelRect Source { get; set; }
        /// <summary>Where its block goes in the extra rows under the grid (in tiles). Fixed so the maps keep their tiles.</summary>
        public int Column { get; set; }
        public int Row { get; set; }
        /// <summary>An autotile (its 47 pieces take a block of 8 × 6 tiles) or a normal piece.</summary>
        public AutotileFormat Format { get; set; }
        /// <summary>The image it comes from, when it is not the tileset's own («graficos/autotiles/agua.png»; empty = this image).</summary>
        public string ImagePath { get; set; } = "";

        public bool IsAutotile => Format != AutotileFormat.None;

        public FreePiece(string name, PixelRect source, int column = 0, int row = 0)
        {
            Name = name ?? "";
            Source = source;
            Column = column;
            Row = row;
        }

        public int TilesWide(SliceSettings s) => IsAutotile ? AutotileLayout.BlockWidth : Math.Max(1, (Source.Width + s.TileWidth - 1) / s.TileWidth);
        public int TilesHigh(SliceSettings s) => IsAutotile ? AutotileLayout.BlockHeight : Math.Max(1, (Source.Height + s.TileHeight - 1) / s.TileHeight);

        /// <summary>The tile, inside its block, of one of the 47 pieces of an autotile.</summary>
        public (int column, int row) VariantCell(int variant) => (Column + variant % AutotileLayout.BlockWidth, Row + variant / AutotileLayout.BlockWidth);

        public FreePiece Clone() => new FreePiece(Name, Source, Column, Row) { Format = Format, ImagePath = ImagePath };
    }

    /// <summary>
    /// The hand-cut pieces of an image (free slicing). They are laid out, as whole tiles, in extra rows under the normal
    /// grid of the tileset: the palette, the maps and the renderer use them as ordinary tiles, and their numbers do not
    /// change when other pieces are added or removed.
    /// </summary>
    public sealed class FreePieceSet
    {
        private readonly List<FreePiece> _pieces = new List<FreePiece>();

        public IReadOnlyList<FreePiece> Pieces => _pieces;
        /// <summary>Rows of the normal grid when the first piece was cut (-1 = not fixed yet). The pieces start under them.</summary>
        public int BaseRows { get; set; } = -1;

        public bool IsEmpty => _pieces.Count == 0;

        /// <summary>The row where the pieces start, for an image with that many grid rows.</summary>
        public int StartRow(int gridRows) => Math.Max(BaseRows, gridRows);

        public int ExtraRows(SliceSettings s) => _pieces.Count == 0 ? 0 : _pieces.Max(p => p.Row + p.TilesHigh(s));

        /// <summary>Problems that stop a piece from being cut (empty = it can).</summary>
        public static IReadOnlyList<string> Check(PixelRect source, int imageWidth, int imageHeight, SliceSettings s, int columns)
        {
            var list = new List<string>();
            if (source.Width <= 0 || source.Height <= 0) list.Add("La pieza está vacía: arrastra un rectángulo en la imagen.");
            else if (source.X < 0 || source.Y < 0 || source.Right > imageWidth || source.Bottom > imageHeight)
                list.Add("La pieza se sale de la imagen.");
            else if (columns > 0 && (source.Width + s.TileWidth - 1) / s.TileWidth > columns)
                list.Add($"La pieza es más ancha que el tileset ({columns} tiles): córtala en dos.");
            if (columns <= 0) list.Add("Primero corta la imagen en tiles (asistente de corte).");
            return list;
        }

        /// <summary>Adds a piece in the first free place (left to right, top to bottom). Null with problems if it cannot.</summary>
        public FreePiece Add(string name, PixelRect source, SliceSettings s, int columns, int gridRows,
            AutotileFormat format = AutotileFormat.None, string imagePath = "")
        {
            if (columns <= 0) return null;
            var piece = new FreePiece(name, source) { Format = format, ImagePath = imagePath ?? "" };
            int w = piece.TilesWide(s), h = piece.TilesHigh(s);
            if (w > columns) return null;
            if (BaseRows < 0) BaseRows = gridRows;
            var spot = FindSpot(s, columns, w, h, null);
            piece.Column = spot.column;
            piece.Row = spot.row;
            _pieces.Add(piece);
            return piece;
        }

        public bool Remove(FreePiece piece) => _pieces.Remove(piece);

        /// <summary>Puts back a piece read from a file, in its saved place (Repair fixes it if that place is wrong).</summary>
        public void Restore(FreePiece piece) { if (piece != null) _pieces.Add(piece); }

        public FreePieceSet Clone()
        {
            var copy = new FreePieceSet { BaseRows = BaseRows };
            foreach (var p in _pieces) copy._pieces.Add(p.Clone());
            return copy;
        }

        /// <summary>Changes where a piece is cut from; it keeps its place if it still fits, otherwise it moves.</summary>
        public bool Reshape(FreePiece piece, PixelRect source, SliceSettings s, int columns)
        {
            if (!_pieces.Contains(piece) || source.Width <= 0 || source.Height <= 0) return false;
            if (!piece.IsAutotile && (source.Width + s.TileWidth - 1) / s.TileWidth > columns) return false;
            piece.Source = source;
            int w = piece.TilesWide(s), h = piece.TilesHigh(s);
            if (piece.Column + w <= columns && Free(s, piece.Column, piece.Row, w, h, piece)) return true;
            var spot = FindSpot(s, columns, w, h, piece);
            piece.Column = spot.column;
            piece.Row = spot.row;
            return true;
        }

        /// <summary>
        /// Puts back in a free place the pieces that overlap or no longer fit (the tile size or the image width changed).
        /// True if any moved (the maps that used them would show other tiles).
        /// </summary>
        public bool Repair(SliceSettings s, int columns)
        {
            bool moved = false;
            var placed = new List<FreePiece>();
            foreach (var p in _pieces.ToList())
            {
                int w = p.TilesWide(s), h = p.TilesHigh(s);
                bool ok = p.Column >= 0 && p.Row >= 0 && p.Column + w <= columns && placed.All(o => !Overlap(s, o, p.Column, p.Row, w, h));
                if (!ok)
                {
                    if (w > columns) { _pieces.Remove(p); moved = true; continue; }
                    var spot = FindSpotAmong(s, columns, w, h, placed);
                    p.Column = spot.column;
                    p.Row = spot.row;
                    moved = true;
                }
                placed.Add(p);
            }
            return moved;
        }

        /// <summary>The first tile number of a piece's block (its top-left tile).</summary>
        public int FirstTile(FreePiece piece, int columns, int gridRows) => (StartRow(gridRows) + piece.Row) * columns + piece.Column;

        /// <summary>The piece a tile number belongs to (null for normal tiles).</summary>
        public FreePiece PieceAt(int tile, SliceSettings s, int columns, int gridRows)
        {
            if (columns <= 0 || tile < 0) return null;
            int row = tile / columns - StartRow(gridRows), col = tile % columns;
            if (row < 0) return null;
            return _pieces.FirstOrDefault(p => col >= p.Column && col < p.Column + p.TilesWide(s) && row >= p.Row && row < p.Row + p.TilesHigh(s));
        }

        /// <summary>
        /// The image the tileset really uses: the original grid and, under it, the block of each piece (the piece standing on
        /// the bottom-left of its block, the rest transparent).
        /// </summary>
        public PixelImage Compose(PixelImage source, SliceSettings s, Func<string, PixelImage> loadImage = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int columns = s.ColumnsFor(source.Width), gridRows = s.RowsFor(source.Height);
            if (_pieces.Count == 0 || columns <= 0) return source;
            Repair(s, columns);
            int start = StartRow(gridRows), rows = start + ExtraRows(s);
            int pitchY = s.TileHeight + s.SpacingY;
            int height = Math.Max(source.Height, s.OffsetY + rows * pitchY);
            var image = new PixelImage(source.Width, height);
            int keep = Math.Min(source.Height, s.OffsetY + start * pitchY);
            for (int y = 0; y < keep; y++)
            for (int x = 0; x < source.Width; x++)
                image[x, y] = source[x, y];

            foreach (var p in _pieces)
            {
                if (p.IsAutotile)
                {
                    var from = string.IsNullOrEmpty(p.ImagePath) ? source : SafeLoad(loadImage, p.ImagePath);
                    if (from == null) continue;
                    var pieces = AutotileLayout.Build(from, p.Source, p.Format, s.TileWidth, s.TileHeight);
                    for (int v = 0; v < AutotileLayout.Variants; v++)
                    {
                        var (vc, vr) = p.VariantCell(v);
                        var dest = s.CellRect(vc, start + vr);
                        int px = (v % AutotileLayout.BlockWidth) * s.TileWidth, py = (v / AutotileLayout.BlockWidth) * s.TileHeight;
                        for (int y = 0; y < s.TileHeight; y++)
                        for (int x = 0; x < s.TileWidth; x++)
                            if (image.Contains(dest.X + x, dest.Y + y)) image[dest.X + x, dest.Y + y] = pieces[px + x, py + y];
                    }
                    continue;
                }
                int w = p.TilesWide(s), h = p.TilesHigh(s);
                int padY = h * s.TileHeight - p.Source.Height; // stands on the bottom of its block
                for (int cy = 0; cy < h * s.TileHeight; cy++)
                for (int cx = 0; cx < w * s.TileWidth; cx++)
                {
                    int sx = p.Source.X + cx, sy = p.Source.Y + cy - padY;
                    var dest = s.CellRect(p.Column + cx / s.TileWidth, start + p.Row + cy / s.TileHeight);
                    int dx = dest.X + cx % s.TileWidth, dy = dest.Y + cy % s.TileHeight;
                    if (!image.Contains(dx, dy)) continue;
                    bool inside = cx < p.Source.Width && sy >= p.Source.Y && sy < p.Source.Bottom && source.Contains(sx, sy);
                    image[dx, dy] = inside ? source[sx, sy] : Rgba32.Transparent;
                }
            }
            return image;
        }

        private static PixelImage SafeLoad(Func<string, PixelImage> load, string path)
        {
            if (load == null) return null;
            try { return load(path); }
            catch (Exception) { return null; } // a missing autotile image leaves its block empty (Problems says so)
        }

        /// <summary>
        /// Finds the loose objects of an image (groups of touching opaque pixels, merged when their boxes overlap or are
        /// closer than <paramref name="gap"/> pixels): a first cut of a ripped sheet to adjust by hand.
        /// </summary>
        public static List<PixelRect> Detect(PixelImage image, int minSize = 4, int gap = 1)
        {
            int w = image.Width, h = image.Height;
            var seen = new bool[w * h];
            var boxes = new List<PixelRect>();
            var stack = new Stack<int>();
            for (int y0 = 0; y0 < h; y0++)
            for (int x0 = 0; x0 < w; x0++)
            {
                int i0 = y0 * w + x0;
                if (seen[i0] || image[x0, y0].A == 0) continue;
                int minX = x0, maxX = x0, minY = y0, maxY = y0;
                seen[i0] = true;
                stack.Push(i0);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % w, y = i / w;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (seen[n] || image[nx, ny].A == 0) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
                boxes.Add(new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }

            // Merge boxes that touch (shadows, sparkles and other bits of the same object).
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int a = 0; a < boxes.Count && !merged; a++)
                for (int b = a + 1; b < boxes.Count && !merged; b++)
                {
                    var A = boxes[a]; var B = boxes[b];
                    if (A.X - gap > B.Right || B.X - gap > A.Right || A.Y - gap > B.Bottom || B.Y - gap > A.Bottom) continue;
                    int x = Math.Min(A.X, B.X), y = Math.Min(A.Y, B.Y);
                    boxes[a] = new PixelRect(x, y, Math.Max(A.Right, B.Right) - x, Math.Max(A.Bottom, B.Bottom) - y);
                    boxes.RemoveAt(b);
                    merged = true;
                }
            }
            return boxes.Where(r => r.Width >= minSize || r.Height >= minSize)
                .OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        }

        private (int column, int row) FindSpot(SliceSettings s, int columns, int w, int h, FreePiece except) =>
            FindSpotAmong(s, columns, w, h, _pieces.Where(p => p != except).ToList());

        private static (int column, int row) FindSpotAmong(SliceSettings s, int columns, int w, int h, List<FreePiece> others)
        {
            for (int row = 0; ; row++)
            for (int col = 0; col + w <= columns; col++)
                if (others.All(o => !Overlap(s, o, col, row, w, h))) return (col, row);
        }

        private bool Free(SliceSettings s, int col, int row, int w, int h, FreePiece except) =>
            _pieces.All(o => o == except || !Overlap(s, o, col, row, w, h));

        private static bool Overlap(SliceSettings s, FreePiece o, int col, int row, int w, int h) =>
            col < o.Column + o.TilesWide(s) && o.Column < col + w && row < o.Row + o.TilesHigh(s) && o.Row < row + h;
    }
}
