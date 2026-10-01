using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Corte libre (fase 6, B1): piezas cortadas a mano con precisión de píxel que se colocan, como bloques de tiles
    /// enteros, debajo de la rejilla del tileset sin cambiar los números de los demás tiles.
    /// </summary>
    public class FreeSliceTests
    {
        private static readonly Rgba32 Red = new Rgba32(255, 0, 0);
        private static readonly Rgba32 Green = new Rgba32(0, 255, 0);
        private static readonly SliceSettings Sixteen = SliceSettings.Square(16);

        /// <summary>64×32 (4×2 tiles of 16) with a 20×24 red object at (5, 3) that does not follow the grid.</summary>
        private static PixelImage Ripped()
        {
            var img = new PixelImage(64, 32);
            for (int y = 3; y < 27; y++)
            for (int x = 5; x < 25; x++)
                img[x, y] = Red;
            img[60, 30] = Green;
            return img;
        }

        [Test]
        public void Pieces_take_whole_tiles_in_the_first_free_place_and_keep_their_numbers()
        {
            var set = new FreePieceSet();
            var tree = set.Add("Árbol", new PixelRect(5, 3, 20, 24), Sixteen, columns: 4, gridRows: 2);
            Assert.AreEqual((2, 2), (tree.TilesWide(Sixteen), tree.TilesHigh(Sixteen)));
            Assert.AreEqual((0, 0), (tree.Column, tree.Row));
            Assert.AreEqual(2, set.BaseRows, "pieces start under the grid it had when the first one was cut");
            Assert.AreEqual(8, set.FirstTile(tree, 4, 2), "row 2, column 0 of a 4-column tileset");

            var rock = set.Add("Roca", new PixelRect(40, 0, 10, 10), Sixteen, 4, 2);
            Assert.AreEqual((2, 0), (rock.Column, rock.Row), "next to the tree");
            var wide = set.Add("Valla", new PixelRect(0, 0, 50, 8), Sixteen, 4, 2);
            Assert.AreEqual((0, 2), (wide.Column, wide.Row), "does not fit beside them: goes under");
            Assert.AreEqual(3, set.ExtraRows(Sixteen));

            set.Remove(rock);
            Assert.AreEqual((0, 0), (tree.Column, tree.Row), "removing a piece does not move the others");
            Assert.AreSame(tree, set.PieceAt(13, Sixteen, 4, 2), "tile 13 = row 3, column 1: inside the tree's block");
            Assert.IsNull(set.PieceAt(3, Sixteen, 4, 2), "grid tiles are not pieces");

            Assert.IsNull(set.Add("Enorme", new PixelRect(0, 0, 64 + 1, 4), Sixteen, 4, 2), "wider than the tileset");
            StringAssert.Contains("ancha", FreePieceSet.Check(new PixelRect(0, 0, 65, 4), 100, 100, Sixteen, 4)[0]);
            StringAssert.Contains("sale", FreePieceSet.Check(new PixelRect(60, 0, 10, 4), 64, 32, Sixteen, 4)[0]);
        }

        [Test]
        public void The_composed_image_keeps_the_grid_and_puts_each_piece_standing_on_its_block()
        {
            var src = Ripped();
            var set = new FreePieceSet();
            set.Add("Árbol", new PixelRect(5, 3, 20, 24), Sixteen, 4, 2);
            var img = set.Compose(src, Sixteen);

            Assert.AreEqual((64, 64), (img.Width, img.Height), "two more rows of tiles");
            Assert.AreEqual(Green, img[60, 30], "the grid is untouched");
            // Block = 32×32 at (0, 32). The piece is 20×24: 8 transparent rows on top, then the object from x = 0.
            Assert.AreEqual(Rgba32.Transparent, img[0, 32 + 7]);
            Assert.AreEqual(Red, img[0, 32 + 8]);
            Assert.AreEqual(Red, img[19, 63]);
            Assert.AreEqual(Rgba32.Transparent, img[20, 63], "right of the piece: transparent");

            var ts = new Tileset("t", "t", Sixteen, img.Width, img.Height) { Free = set, GridRows = 2 };
            Assert.AreEqual(16, ts.Count);
            Assert.AreEqual("Árbol", ts.FreePieceOf(9).Name);
            Assert.AreEqual(new PixelRect(5, 3, 20, 24), ts.SourceRectOf(9), "Retoque opens the piece in the original image");
            Assert.AreEqual(ts.RectOf(2), ts.SourceRectOf(2));

            Assert.AreSame(src, new FreePieceSet().Compose(src, Sixteen), "no pieces: the same image");
        }

        [Test]
        public void Pieces_that_no_longer_fit_are_put_back_in_a_free_place()
        {
            var set = new FreePieceSet { BaseRows = 2 };
            set.Restore(new FreePiece("a", new PixelRect(0, 0, 16, 16), 0, 0));
            set.Restore(new FreePiece("b", new PixelRect(0, 0, 16, 16), 0, 0)); // same place: overlaps
            set.Restore(new FreePiece("c", new PixelRect(0, 0, 32, 16), 3, 0)); // sticks out of 4 columns
            Assert.IsTrue(set.Repair(Sixteen, 4));
            var places = set.Pieces.Select(p => (p.Column, p.Row)).ToList();
            Assert.AreEqual(places.Count, places.Distinct().Count());
            Assert.IsTrue(set.Pieces.All(p => p.Column + p.TilesWide(Sixteen) <= 4));
            Assert.IsFalse(set.Repair(Sixteen, 4), "already right: nothing moves");
        }

        [Test]
        public void Detect_finds_loose_objects_and_merges_touching_bits()
        {
            var img = Ripped();
            img[25, 10] = Red; // touches the object: same box
            var found = FreePieceSet.Detect(img, minSize: 1);
            Assert.AreEqual(2, found.Count);
            Assert.AreEqual(new PixelRect(5, 3, 21, 24), found[0]);
            Assert.AreEqual(new PixelRect(60, 30, 1, 1), found[1]);
            Assert.AreEqual(1, FreePieceSet.Detect(img, minSize: 4).Count, "specks are skipped");
        }

        [Test]
        public void Pieces_are_saved_with_the_cut_and_the_tileset_loads_them()
        {
            var root = Path.Combine(Path.GetTempPath(), "cteditor_free_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectLayout.CreateFolders(root);
                var dir = Path.Combine(root, "graficos", "tilesets");
                var image = Path.Combine(dir, "ripeo.png");
                Png.Write(Ripped(), image);
                var file = new SliceFile(Sixteen);
                file.Free.Add("Árbol", new PixelRect(5, 3, 20, 24), Sixteen, 4, 2);
                file.SaveFor(image);

                var back = SliceFile.LoadFor(image);
                Assert.AreEqual(1, back.Free.Pieces.Count);
                Assert.AreEqual("Árbol", back.Free.Pieces[0].Name);
                Assert.AreEqual(new PixelRect(5, 3, 20, 24), back.Free.Pieces[0].Source);
                Assert.AreEqual(2, back.Free.BaseRows);

                var repo = new FolderTilesetRepository(root);
                var ts = repo.Load(repo.List().Single());
                Assert.AreEqual((4, 4), (ts.Columns, ts.Rows), "the grid plus the piece's two rows");
                Assert.AreEqual(TileCoverage.Partial, ts.Coverage[8], "the piece's tiles have their own coverage");
                Assert.AreEqual(TileCoverage.Empty, ts.Coverage[10]);

                ts.Attributes.Set(9, new TileProperties { Priority = 1 });
                repo.SaveAttributes(ts);
                Assert.AreEqual(1, SliceFile.LoadFor(image).Free.Pieces.Count, "saving tile properties keeps the pieces");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
