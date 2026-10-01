using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Project;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Base de la aplicación (fase 0-1): imagen de píxeles, corte de tilesets y hojas de personaje, sugerencia de tamaño,
    /// PNG sin Unity y lectura de la carpeta «graficos/» del proyecto.
    /// </summary>
    public class TileSlicingTests
    {
        private static readonly Rgba32 Red = new Rgba32(255, 0, 0);
        private static readonly Rgba32 Blue = new Rgba32(0, 0, 255);

        /// <summary>Imagen de cols×rows tiles de 'size' px; 'paint' decide el color de cada tile (Transparent = vacío).</summary>
        private static PixelImage Grid(int cols, int rows, int size, Func<int, int, Rgba32> paint, int spacing = 0, int offset = 0)
        {
            var img = new PixelImage(offset + cols * size + (cols - 1) * spacing, offset + rows * size + (rows - 1) * spacing);
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var color = paint(c, r);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    img[offset + c * (size + spacing) + x, offset + r * (size + spacing) + y] = color;
            }
            return img;
        }

        [Test]
        public void Slices_a_grid_and_marks_empty_and_repeated_tiles()
        {
            // Row 0: red, blue, red (repeated). Row 1: empty, blue (repeated), a unique one with a dot.
            var img = Grid(3, 2, 16, (c, r) => r == 1 && c == 0 ? Rgba32.Transparent : (c == 1 ? Blue : Red));
            img[2 * 16 + 5, 16 + 5] = Blue; // tile (2,1) now differs from the plain red ones

            var sheet = TileSlicer.Slice(img, SliceSettings.Square(16));

            Assert.IsTrue(sheet.Ok);
            Assert.AreEqual(3, sheet.Columns);
            Assert.AreEqual(2, sheet.Rows);
            Assert.AreEqual(6, sheet.Cells.Count);
            Assert.IsTrue(sheet.At(0, 1).IsEmpty);
            Assert.AreEqual(0, sheet.At(2, 0).DuplicateOf, "the second red is a repeat of the first");
            Assert.AreEqual(1, sheet.At(1, 1).DuplicateOf, "the second blue is a repeat of the first");
            Assert.IsFalse(sheet.At(2, 1).IsDuplicate, "one pixel of difference makes it unique");
            Assert.AreEqual(1, sheet.EmptyCount);
            Assert.AreEqual(2, sheet.DuplicateCount);
            Assert.AreEqual(3, sheet.UniqueCount);
            Assert.AreEqual(new PixelRect(32, 16, 16, 16), sheet.At(2, 1).Rect);
        }

        [Test]
        public void Offset_and_spacing_are_respected()
        {
            var img = Grid(4, 3, 32, (c, r) => (c + r) % 2 == 0 ? Red : Blue, spacing: 2, offset: 1);
            var sheet = TileSlicer.Slice(img, new SliceSettings(32, 32, 1, 1, 2, 2));

            Assert.AreEqual(4, sheet.Columns);
            Assert.AreEqual(3, sheet.Rows);
            Assert.AreEqual(0, sheet.LeftoverX);
            Assert.AreEqual(0, sheet.Warnings.Count);
            Assert.AreEqual(new PixelRect(1 + 3 * 34, 1 + 2 * 34, 32, 32), sheet.At(3, 2).Rect);
            // Checkerboard: everything after the first red and first blue is a repeat.
            Assert.AreEqual(2, sheet.UniqueCount);
        }

        [Test]
        public void Leftover_pixels_warn_and_impossible_settings_explain_why()
        {
            var img = new PixelImage(50, 32);
            img[0, 0] = Red;
            var sheet = TileSlicer.Slice(img, SliceSettings.Square(16));
            Assert.IsTrue(sheet.Ok);
            Assert.AreEqual(3, sheet.Columns);
            Assert.AreEqual(2, sheet.LeftoverX);
            Assert.AreEqual(1, sheet.Warnings.Count);
            StringAssert.Contains("Sobran 2 píxeles", sheet.Warnings[0]);

            var tooBig = TileSlicer.Slice(img, SliceSettings.Square(64));
            Assert.IsFalse(tooBig.Ok);
            Assert.AreEqual(0, tooBig.Cells.Count);
            StringAssert.Contains("cabe", tooBig.Problems[0]);
            Assert.IsFalse(TileSlicer.Slice(img, new SliceSettings(0, 16)).Ok);
            Assert.IsFalse(TileSlicer.Slice(img, new SliceSettings(16, 16, -1)).Ok);
        }

        [Test]
        public void Transparent_pixels_count_the_same_whatever_their_color()
        {
            var img = new PixelImage(32, 16);
            img[3, 3] = Red;
            img[16 + 3, 3] = Red;
            img[16 + 8, 8] = new Rgba32(10, 20, 30, 0); // invisible garbage
            var sheet = TileSlicer.Slice(img, SliceSettings.Square(16));
            Assert.AreEqual(0, sheet.At(1, 0).DuplicateOf);
        }

        [Test]
        public void Suggests_known_formats_then_project_size_then_exact_fits()
        {
            var xp = TileSizeSuggester.Suggest(256, 1024);
            Assert.AreEqual(32, xp[0].Width);
            StringAssert.Contains("RPG Maker XP", xp[0].Reason);

            var s = TileSizeSuggester.Suggest(96, 48, projectTileSize: 48);
            Assert.AreEqual(48, s[0].Width);
            Assert.IsTrue(s.Any(x => x.Width == 16));
            Assert.IsTrue(s.Any(x => x.Width == 24));
            Assert.IsFalse(s.Any(x => x.Width == 32), "32 does not divide 96×48 exactly");

            var odd = TileSizeSuggester.Suggest(50, 50, projectTileSize: 16);
            Assert.AreEqual(1, odd.Count);
            StringAssert.Contains("sobrarán", odd[0].Reason);
        }

        [Test]
        public void Usual_sizes_are_offered_even_when_they_do_not_divide_the_image()
        {
            var u = TileSizeSuggester.Usual(300, 450, projectTileSize: 32);
            CollectionAssert.AreEqual(new[] { 16, 32, 48 }, u.Select(x => x.Width).ToArray());
            StringAssert.Contains("sobran 12 px a la derecha y 2 px abajo", u[0].Reason);
            StringAssert.Contains("del proyecto", u[1].Reason);

            var exact = TileSizeSuggester.Usual(64, 64, projectTileSize: 24);
            Assert.IsTrue(exact.Any(x => x.Width == 24), "the project size is added");
            StringAssert.Contains("encaja exacto: 4×4", exact[0].Reason);
            Assert.IsFalse(exact.Any(x => x.Width == 48 && x.Reason.Contains("exacto")));
        }

        [Test]
        public void Character_sheets_are_detected_with_their_directions_and_walk_cycle()
        {
            // RPG Maker XP: 4×4 frames of 32×48.
            Assert.AreSame(CharacterSheetLayout.RpgMakerXp, CharacterSheetLayout.Detect(128, 192));
            // RPG Maker VX/MV: 3×4 frames of 32×32 (XP would give 24-px frames: 32 is the more usual width).
            Assert.AreSame(CharacterSheetLayout.RpgMakerVx, CharacterSheetLayout.Detect(96, 128));
            Assert.IsNull(CharacterSheetLayout.Detect(100, 130));

            var xp = CharacterSheetLayout.RpgMakerXp;
            Assert.AreEqual(new PixelRect(0, 48, 32, 48), xp.FrameRect(128, 192, FacingDirection.Left));
            Assert.AreEqual(new PixelRect(64, 144, 32, 48), xp.FrameRect(128, 192, FacingDirection.Up, step: 2));

            var vx = CharacterSheetLayout.RpgMakerVx;
            Assert.AreEqual(new PixelRect(32, 0, 32, 32), vx.FrameRect(96, 128, FacingDirection.Down), "idle = middle column");
            Assert.AreEqual(new PixelRect(0, 64, 32, 32), vx.FrameRect(96, 128, FacingDirection.Right, step: 1));
            Assert.AreEqual(new PixelRect(32, 64, 32, 32), vx.FrameRect(96, 128, FacingDirection.Right, step: 4), "the cycle loops");
            Assert.AreEqual(12, TileSlicer.Slice(new PixelImage(96, 128), vx.SliceFor(96, 128)).Cells.Count);
        }

        [Test]
        public void Png_round_trip_keeps_every_pixel()
        {
            var img = new PixelImage(7, 5);
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 7; x++)
                img[x, y] = new Rgba32((byte)(x * 30), (byte)(y * 50), (byte)(x * y * 7), (byte)(x == 3 ? 0 : 200 + y));

            var bytes = Png.Write(img);
            var back = Png.Read(bytes);

            Assert.AreEqual(7, back.Width);
            Assert.AreEqual(5, back.Height);
            CollectionAssert.AreEqual(img.ToArray(), back.ToArray());
            using (var m = new MemoryStream(bytes))
            {
                Assert.IsTrue(Png.TryReadSize(m, out int w, out int h));
                Assert.AreEqual((7, 5), (w, h));
            }
            Assert.Throws<InvalidDataException>(() => Png.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        }

        // PNGs made by other encoders (Pillow; the interlaced one by hand with zlib), to test what our writer never writes.
        private const string Palette4BitWithTransparency =
            "iVBORw0KGgoAAAANSUhEUgAAAAUAAAADBAMAAACpGNjLAAAAMFBMVEUAAAD/AAAA/wAAAP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACly1EKAAAAAXRSTlMAQObYZgAAABRJREFUeJxjYFQWYFBiNmAQVmQAAARwAL573kBxAAAAAElFTkSuQmCC";
        private const string GrayAlphaAdam7 =
            "iVBORw0KGgoAAAANSUhEUgAAAAkAAAAJCAQAAAE9n/4NAAAAYUlEQVR4nG2OUQ0AMQhDKwQhCJkQhEzIhEzInPQed3/LJQsp5VEm6VgypXhClo+UXhQtt2ogXXTN4hRTZHhoeiM0jFA7ntp/IzmcNAWwmHCk+QTEInC/6Q7yodiG+j53Lz5DpFk76Gr/HQAAAABJRU5ErkJggg==";
        private const string RgbOptimized =
            "iVBORw0KGgoAAAANSUhEUgAAAAYAAAAECAIAAAAiZtkUAAAAGklEQVR42mNkYGDQYBBBRiwMNiIMDCiIOCEAjuwDVj6MkmcAAAAASUVORK5CYII=";

        [Test]
        public void Reads_pngs_made_by_other_programs()
        {
            // 4-bit palette, index 0 transparent: 0,1,2,3,1 / 2,2,0,3,3 / 1,3,2,1,0 with 1=red 2=green 3=blue.
            var pal = Png.Read(Convert.FromBase64String(Palette4BitWithTransparency));
            Assert.AreEqual((5, 3), (pal.Width, pal.Height));
            Assert.IsTrue(pal[0, 0].IsTransparent);
            Assert.AreEqual(Red, pal[1, 0]);
            Assert.AreEqual(new Rgba32(0, 255, 0), pal[0, 1]);
            Assert.AreEqual(Blue, pal[4, 1]);
            Assert.IsTrue(pal[4, 2].IsTransparent);

            // Gray + alpha, Adam7 interlaced: gray = x*28, alpha = 0 when (x+y) % 3 == 0.
            var la = Png.Read(Convert.FromBase64String(GrayAlphaAdam7));
            Assert.AreEqual((9, 9), (la.Width, la.Height));
            for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
            {
                byte g = (byte)(x * 28 % 256);
                Assert.AreEqual(new Rgba32(g, g, g, (byte)((x + y) % 3 == 0 ? 0 : 255)), la[x, y], $"pixel {x},{y}");
            }

            // RGB with the encoder's own filters: (x*40, y*60, (x+y)*20).
            var rgb = Png.Read(Convert.FromBase64String(RgbOptimized));
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 6; x++)
                Assert.AreEqual(new Rgba32((byte)(x * 40), (byte)(y * 60), (byte)((x + y) * 20)), rgb[x, y], $"pixel {x},{y}");
        }

        [Test]
        public void Scans_the_graphics_folder_by_kind_with_size_and_slice_state()
        {
            var root = Path.Combine(Path.GetTempPath(), "cteditor_scan_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectLayout.CreateFolders(root);
                var tiles = Path.Combine(root, "graficos", "tilesets", "pueblo.png");
                Png.Write(Grid(8, 2, 32, (c, r) => Red), tiles);
                File.WriteAllText(ProjectLayout.SlicePathFor(tiles), "{}");
                Directory.CreateDirectory(Path.Combine(root, "graficos", "personajes", "npc"));
                Png.Write(new PixelImage(128, 192), Path.Combine(root, "graficos", "personajes", "npc", "chica.png"));
                File.WriteAllText(Path.Combine(root, "graficos", "roto.png"), "no soy un png");
                File.WriteAllText(Path.Combine(root, "graficos", "tilesets", "notas.txt"), "ignored");

                var list = AssetCatalog.Scan(root);

                Assert.AreEqual(3, list.Count);
                var town = list.Single(e => e.Name == "pueblo");
                Assert.AreEqual(AssetKind.Tileset, town.Kind);
                Assert.AreEqual("graficos/tilesets/pueblo.png", town.RelativePath);
                Assert.AreEqual((256, 64), (town.Width, town.Height));
                Assert.IsTrue(town.IsSliced);
                var girl = list.Single(e => e.Name == "chica");
                Assert.AreEqual(AssetKind.Character, girl.Kind);
                Assert.IsFalse(girl.IsSliced);
                var broken = list.Single(e => e.Name == "roto");
                Assert.AreEqual(AssetKind.Other, broken.Kind);
                Assert.IsNotNull(broken.Problem);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            Assert.AreEqual(0, AssetCatalog.Scan(root).Count, "a missing folder is just empty");
        }
    }
}
