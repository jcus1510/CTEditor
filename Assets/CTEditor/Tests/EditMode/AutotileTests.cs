using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Autotiles (fase 6, B4): las 47 piezas, los formatos (RPG Maker XP, VX/MV, 47 piezas) detectados por las medidas y
    /// escalados al tile del proyecto, y los bordes que se recalculan solos al pintar y al borrar.
    /// </summary>
    public class AutotileTests
    {
        [Test]
        public void There_are_47_pieces_and_every_neighbourhood_has_one()
        {
            var all = Enumerable.Range(0, 256).Select(AutotileLayout.VariantOf).ToList();
            Assert.AreEqual(47, all.Distinct().Count());
            Assert.IsTrue(all.All(v => v >= 0 && v < AutotileLayout.Variants));
            Assert.AreEqual(0, AutotileLayout.VariantOf(0), "alone");
            Assert.AreEqual(46, AutotileLayout.VariantOf(255), "surrounded");
            // A corner without both sides next to it does not count.
            Assert.AreEqual(AutotileLayout.VariantOf(0), AutotileLayout.VariantOf(AutotileLayout.NE));
            Assert.AreEqual(AutotileLayout.VariantOf(AutotileLayout.N), AutotileLayout.VariantOf(AutotileLayout.N | AutotileLayout.NE));
            Assert.AreNotEqual(AutotileLayout.VariantOf(AutotileLayout.N | AutotileLayout.E),
                AutotileLayout.VariantOf(AutotileLayout.N | AutotileLayout.E | AutotileLayout.NE));
            for (int v = 0; v < 47; v++) Assert.AreEqual(v, AutotileLayout.VariantOf(AutotileLayout.MaskOf(v)));
        }

        [Test]
        public void Formats_are_detected_by_their_size()
        {
            Assert.AreEqual(AutotileFormat.Xp, AutotileLayout.Detect(96, 128, out int t));
            Assert.AreEqual(32, t);
            Assert.AreEqual(AutotileFormat.Xp, AutotileLayout.Detect(384, 128, out _), "animated: 4 frames side by side");
            Assert.AreEqual(AutotileFormat.Vx, AutotileLayout.Detect(64, 96, out t));
            Assert.AreEqual(32, t);
            Assert.AreEqual(AutotileFormat.Blob47, AutotileLayout.Detect(128, 96, out t));
            Assert.AreEqual(16, t);
            Assert.AreEqual(AutotileFormat.None, AutotileLayout.Detect(100, 100, out _));
        }

        /// <summary>An XP autotile of 32-px tiles where every 16-px quarter has its own colour (R = column, G = row).</summary>
        private static PixelImage XpQuarters()
        {
            var img = new PixelImage(96, 128);
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 96; x++)
                img[x, y] = new Rgba32((byte)(x / 16 * 10 + 1), (byte)(y / 16 * 10 + 1), 0);
            return img;
        }

        private static (int qx, int qy) QuarterAt(PixelImage built, int variant, int tile, int px, int py)
        {
            var c = built[(variant % 8) * tile + px, (variant / 8) * tile + py];
            return ((c.R - 1) / 10, (c.G - 1) / 10);
        }

        [Test]
        public void Xp_pieces_are_built_from_quarters_and_scaled_to_the_project_tile()
        {
            var built = AutotileLayout.Build(XpQuarters(), new PixelRect(0, 0, 96, 128), AutotileFormat.Xp, 16, 16);
            Assert.AreEqual((128, 96), (built.Width, built.Height), "8 × 6 tiles of 16");

            // Alone: the four outer corners of the 3 × 3 frame (quarters (0,2), (5,2), (0,7), (5,7) of the image).
            Assert.AreEqual((0, 2), QuarterAt(built, 0, 16, 0, 0));
            Assert.AreEqual((5, 2), QuarterAt(built, 0, 16, 15, 0));
            Assert.AreEqual((0, 7), QuarterAt(built, 0, 16, 0, 15));
            Assert.AreEqual((5, 7), QuarterAt(built, 0, 16, 15, 15));

            // Surrounded: the middle of the frame.
            Assert.AreEqual((2, 4), QuarterAt(built, 46, 16, 0, 0));
            Assert.AreEqual((3, 5), QuarterAt(built, 46, 16, 15, 15));

            // Surrounded but the top-left corner missing: that quarter is the inner corner (top row, third tile).
            int notch = AutotileLayout.VariantOf(255 & ~AutotileLayout.NW);
            Assert.AreEqual((4, 0), QuarterAt(built, notch, 16, 0, 0));
            Assert.AreEqual((3, 4), QuarterAt(built, notch, 16, 15, 0));

            // Only a neighbour above: the sides of the frame at the top, its bottom edge below.
            int up = AutotileLayout.VariantOf(AutotileLayout.N);
            Assert.AreEqual((0, 4), QuarterAt(built, up, 16, 0, 0), "left side");
            Assert.AreEqual((0, 7), QuarterAt(built, up, 16, 0, 15), "bottom-left corner");
        }

        private static Tileset WaterTileset(out FreePiece water)
        {
            var s = SliceSettings.Square(16);
            var set = new FreePieceSet();
            water = set.Add("Agua", new PixelRect(0, 0, 96, 128), s, columns: 8, gridRows: 2, AutotileFormat.Xp, "graficos/autotiles/agua.png");
            return new Tileset("t", "t", s, 128, 32 + 6 * 16) { Free = set, GridRows = 2 };
        }

        [Test]
        public void Painting_and_erasing_keep_the_edges_joined()
        {
            var ts = WaterTileset(out var water);
            Assert.AreSame(water, ts.AutotileOf(ts.AutotileTile(water, 10)));
            Assert.IsNull(ts.AutotileOf(3), "grid tiles are not autotiles");

            var map = new MapDefinition("m", "m", 5, 5);
            MapTilesets sets = ts;
            int any = ts.AutotileTile(water, 0);
            void Paint(int x, int y, int tile)
            {
                var changes = AutotileResolver.Resolve(map, sets, MapTools.Pencil(map, 0, x, y, TileStamp.Single(tile)));
                MapTools.Apply(map, changes);
            }
            int VariantAt(int x, int y)
            {
                int c = map.Layers[0].Get(x, y);
                return Enumerable.Range(0, 47).First(v => ts.AutotileTile(water, v) == MapTile.Index(c));
            }

            for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                Paint(x, y, any);
            Assert.AreEqual(46, VariantAt(2, 2), "the middle is surrounded");
            Assert.AreEqual(AutotileLayout.VariantOf(AutotileLayout.E | AutotileLayout.SE | AutotileLayout.S), VariantAt(1, 1));
            Assert.AreEqual(AutotileLayout.VariantOf(255 & ~(AutotileLayout.NW | AutotileLayout.N | AutotileLayout.NE)), VariantAt(2, 1));

            // Erasing the middle: the 8 around it get an inner shore.
            var erase = AutotileResolver.Resolve(map, sets, MapTools.Pencil(map, 0, 2, 2, TileStamp.Eraser));
            Assert.AreEqual(9, erase.Count, "the erased cell and its 8 neighbours");
            MapTools.Apply(map, erase);
            Assert.AreEqual(AutotileLayout.VariantOf(AutotileLayout.E | AutotileLayout.S), VariantAt(1, 1), "the corner lost its diagonal");

            // Undo puts everything back.
            MapTools.Apply(map, erase, undo: true);
            Assert.AreEqual(46, VariantAt(2, 2));

            // The map edge counts as water.
            Paint(0, 0, any);
            Assert.AreNotEqual(0, VariantAt(0, 0));
        }

        [Test]
        public void Autotiles_take_a_block_of_8_by_6_and_are_saved_with_the_cut()
        {
            var ts = WaterTileset(out var water);
            Assert.AreEqual((8, 6), (water.TilesWide(ts.Slice), water.TilesHigh(ts.Slice)));
            Assert.AreEqual(16, ts.AutotileTile(water, 0), "first tile under the 2 grid rows");
            Assert.AreEqual(16 + 8 + 2, ts.AutotileTile(water, 10));

            var file = new CTEditor.Project.SliceFile(ts.Slice) { Free = ts.Free };
            var back = CTEditor.Project.SliceFile.FromJson(CTEditor.Project.Json.ParseObject(CTEditor.Project.Json.Write(file.ToJson())));
            var p = back.Free.Pieces.Single();
            Assert.AreEqual((AutotileFormat.Xp, "graficos/autotiles/agua.png"), (p.Format, p.ImagePath));

            var composed = ts.Free.Compose(new PixelImage(128, 32), ts.Slice, path => XpQuarters());
            Assert.AreEqual(32 + 96, composed.Height);
            Assert.AreNotEqual(Rgba32.Transparent, composed[0, 32], "the 47 pieces are drawn under the grid");
            Assert.AreEqual(Rgba32.Transparent, composed[7 * 16, 32 + 5 * 16], "the 48th cell stays empty");
        }
    }
}
