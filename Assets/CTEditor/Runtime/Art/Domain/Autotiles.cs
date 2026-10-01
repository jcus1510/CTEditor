using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Art.Domain
{
    /// <summary>How an autotile image is drawn.</summary>
    public enum AutotileFormat
    {
        /// <summary>Not an autotile (a normal hand-cut piece).</summary>
        None = 0,
        /// <summary>RPG Maker XP: 3 × 4 tiles (single tile, inner corners, then a 3 × 3 frame), built from quarter tiles.</summary>
        Xp = 1,
        /// <summary>RPG Maker VX / MV (A2): 2 × 3 tiles (single tile, inner corners, then a 2 × 2 frame).</summary>
        Vx = 2,
        /// <summary>The 47 pieces already drawn, 8 per row, in CTEditor's order (see AutotileLayout.MaskOf).</summary>
        Blob47 = 3,
    }

    /// <summary>
    /// AUTOTILES: water, paths, cliffs... whose edges draw themselves. Each cell looks at its 8 neighbours (same autotile
    /// or not); a corner only counts when both sides next to it are the same, which leaves 47 different pieces. They are
    /// built once from the image (scaled to the project's tile) and laid out in a block of 8 × 6 tiles.
    /// </summary>
    public static class AutotileLayout
    {
        public const int N = 1, NE = 2, E = 4, SE = 8, S = 16, SW = 32, W = 64, NW = 128;
        public const int Variants = 47;
        public const int BlockWidth = 8, BlockHeight = 6;

        private static readonly int[] MaskByVariant;
        private static readonly int[] VariantByMask = new int[256];

        static AutotileLayout()
        {
            MaskByVariant = Enumerable.Range(0, 256).Select(Reduce).Distinct().OrderBy(m => m).ToArray();
            for (int m = 0; m < 256; m++) VariantByMask[m] = Array.IndexOf(MaskByVariant, Reduce(m));
        }

        /// <summary>Drops the corners that do not count (a corner needs both sides next to it).</summary>
        public static int Reduce(int mask)
        {
            mask &= 255;
            if ((mask & (N | E)) != (N | E)) mask &= ~NE;
            if ((mask & (S | E)) != (S | E)) mask &= ~SE;
            if ((mask & (S | W)) != (S | W)) mask &= ~SW;
            if ((mask & (N | W)) != (N | W)) mask &= ~NW;
            return mask;
        }

        /// <summary>Which of the 47 pieces a cell needs, from its neighbours (0 = alone, 46 = surrounded).</summary>
        public static int VariantOf(int mask) => VariantByMask[mask & 255];

        public static int MaskOf(int variant) => MaskByVariant[variant];

        /// <summary>The format and the size of one source tile, from the size of the image (or region).</summary>
        public static AutotileFormat Detect(int width, int height, out int sourceTile)
        {
            sourceTile = 0;
            if (width <= 0 || height <= 0) return AutotileFormat.None;
            // XP: 3 × 4 (animated ones repeat the 3 columns: 12 × 4 = 4 frames).
            if (height % 4 == 0 && width % (3 * height / 4) == 0 && (height / 4) % 2 == 0) { sourceTile = height / 4; return AutotileFormat.Xp; }
            if (height % 3 == 0 && width * 3 == height * 2 && (width / 2) % 2 == 0) { sourceTile = width / 2; return AutotileFormat.Vx; }
            if (width % 8 == 0 && width * 6 == height * 8) { sourceTile = width / 8; return AutotileFormat.Blob47; }
            return AutotileFormat.None;
        }

        /// <summary>Why a region cannot be an autotile of that format (null = it can).</summary>
        public static string Problem(PixelRect region, AutotileFormat format)
        {
            int tile = SourceTile(region, format);
            if (format == AutotileFormat.None) return null;
            if (tile <= 1) return "La imagen es demasiado pequeña para un autotile.";
            switch (format)
            {
                case AutotileFormat.Xp when region.Height != tile * 4 || region.Width < tile * 3 || tile % 2 != 0:
                    return "Un autotile de RPG Maker XP mide 3 × 4 tiles (96 × 128 con tiles de 32).";
                case AutotileFormat.Vx when region.Height != tile * 3 || region.Width < tile * 2 || tile % 2 != 0:
                    return "Un autotile de VX / MV mide 2 × 3 tiles (64 × 96 con tiles de 32).";
                case AutotileFormat.Blob47 when region.Height < tile * 6:
                    return "Un autotile de 47 piezas mide 8 × 6 tiles.";
            }
            return null;
        }

        public static int SourceTile(PixelRect region, AutotileFormat format) => format switch
        {
            AutotileFormat.Xp => region.Height / 4,
            AutotileFormat.Vx => region.Height / 3,
            AutotileFormat.Blob47 => region.Width / 8,
            _ => 0,
        };

        /// <summary>
        /// The 47 pieces, each tileWidth × tileHeight (scaled from the source with nearest pixels), laid out 8 per row in
        /// an image of 8 × 6 tiles.
        /// </summary>
        public static PixelImage Build(PixelImage source, PixelRect region, AutotileFormat format, int tileWidth, int tileHeight)
        {
            var output = new PixelImage(BlockWidth * tileWidth, BlockHeight * tileHeight);
            int st = SourceTile(region, format);
            if (format == AutotileFormat.None || st <= 0) return output;
            for (int v = 0; v < Variants; v++)
            {
                int ox = (v % BlockWidth) * tileWidth, oy = (v / BlockWidth) * tileHeight;
                if (format == AutotileFormat.Blob47)
                {
                    Blit(source, region.X + (v % BlockWidth) * st, region.Y + (v / BlockWidth) * st, st, st, output, ox, oy, tileWidth, tileHeight);
                    continue;
                }
                int mask = MaskOf(v), m = st / 2;
                int b = format == AutotileFormat.Xp ? 6 : 4;          // the frame, in quarter tiles
                int innerX = format == AutotileFormat.Xp ? 2 * st : st; // the inner corners tile (top row)
                for (int qy = 0; qy < 2; qy++)
                for (int qx = 0; qx < 2; qx++)
                {
                    bool vert = (mask & (qy == 0 ? N : S)) != 0;
                    bool hor = (mask & (qx == 0 ? W : E)) != 0;
                    bool diag = (mask & (qy == 0 ? (qx == 0 ? NW : NE) : (qx == 0 ? SW : SE))) != 0;
                    int sx, sy; // source quarter, in quarter tiles from the frame's corner (or the inner tile)
                    bool inner = false;
                    if (vert && hor && diag) { sx = b / 2 - 1 + qx; sy = b / 2 - 1 + qy; }
                    else if (vert && hor) { sx = qx; sy = qy; inner = true; }
                    else if (vert) { sx = qx == 0 ? 0 : b - 1; sy = b / 2 - 1 + qy; }
                    else if (hor) { sx = b / 2 - 1 + qx; sy = qy == 0 ? 0 : b - 1; }
                    else { sx = qx == 0 ? 0 : b - 1; sy = qy == 0 ? 0 : b - 1; }
                    int px = region.X + (inner ? innerX : 0) + sx * m, py = region.Y + (inner ? 0 : st) + sy * m;
                    int dw0 = tileWidth / 2, dh0 = tileHeight / 2;
                    int dx = ox + (qx == 0 ? 0 : dw0), dy = oy + (qy == 0 ? 0 : dh0);
                    int dw = qx == 0 ? dw0 : tileWidth - dw0, dh = qy == 0 ? dh0 : tileHeight - dh0;
                    Blit(source, px, py, m, m, output, dx, dy, dw, dh);
                }
            }
            return output;
        }

        /// <summary>Copies a w × h area into a dw × dh one (nearest pixel: crisp when scaling by whole numbers).</summary>
        private static void Blit(PixelImage src, int x, int y, int w, int h, PixelImage dst, int dx, int dy, int dw, int dh)
        {
            for (int j = 0; j < dh; j++)
            for (int i = 0; i < dw; i++)
            {
                int sx = x + i * w / dw, sy = y + j * h / dh;
                if (src.Contains(sx, sy) && dst.Contains(dx + i, dy + j)) dst[dx + i, dy + j] = src[sx, sy];
            }
        }
    }
}
