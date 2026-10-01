using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Project;

namespace CTEditor.Tests.EditMode
{
    /// <summary>BMP / DIB reading (old tilesets and rips) and its conversion to PNG on import.</summary>
    public sealed class ImageFormatTests
    {
        private static byte[] Bmp(int width, int height, int bpp, byte[] pixelData, byte[] palette = null, int compression = 0, int colors = 0)
        {
            var b = new List<byte>();
            void U16(int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
            void I32(int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }
            int offset = 14 + 40 + (palette?.Length ?? 0);
            b.Add((byte)'B'); b.Add((byte)'M'); I32(offset + pixelData.Length); I32(0); I32(offset);
            I32(40); I32(width); I32(height); U16(1); U16(bpp); I32(compression); I32(pixelData.Length); I32(2835); I32(2835); I32(colors); I32(0);
            if (palette != null) b.AddRange(palette);
            b.AddRange(pixelData);
            return b.ToArray();
        }

        [Test]
        public void Reads_24_bit_bottom_up_rows_with_padding()
        {
            // 2×2: bottom row first, each row padded to 4 bytes (6 → 8). Pixels are B, G, R.
            var data = new byte[]
            {
                0, 0, 255, 0, 255, 0, 0, 0,     // bottom: red, green
                255, 0, 0, 255, 255, 255, 0, 0, // top: blue, white
            };
            var img = Project.Bmp.Read(Bmp(2, 2, 24, data));
            Assert.AreEqual(new Rgba32(0, 0, 255), img[0, 0]);
            Assert.AreEqual(new Rgba32(255, 255, 255), img[1, 0]);
            Assert.AreEqual(new Rgba32(255, 0, 0), img[0, 1]);
            Assert.AreEqual(new Rgba32(0, 255, 0), img[1, 1]);
        }

        [Test]
        public void Reads_8_bit_palette_and_rle8()
        {
            var palette = new byte[] { 0, 0, 0, 0, 0, 0, 255, 0 }; // 0 black, 1 red (B, G, R, 0)
            var plain = Project.Bmp.Read(Bmp(3, 1, 8, new byte[] { 1, 0, 1, 0 }, palette, colors: 2));
            Assert.AreEqual(new Rgba32(255, 0, 0), plain[0, 0]);
            Assert.AreEqual(new Rgba32(0, 0, 0), plain[1, 0]);

            // RLE8: 4 × colour 1, end of line, end of bitmap.
            var rle = Project.Bmp.Read(Bmp(4, 1, 8, new byte[] { 4, 1, 0, 0, 0, 1 }, palette, compression: 1, colors: 2));
            for (int x = 0; x < 4; x++) Assert.AreEqual(new Rgba32(255, 0, 0), rle[x, 0]);
        }

        [Test]
        public void A_32_bit_bmp_with_empty_alpha_is_opaque()
        {
            var img = Project.Bmp.Read(Bmp(1, 1, 32, new byte[] { 10, 20, 30, 0 }));
            Assert.AreEqual(new Rgba32(30, 20, 10, 255), img[0, 0]);
        }

        [Test]
        public void The_kind_is_guessed_from_the_name_then_from_the_size()
        {
            Assert.AreEqual(AssetKind.Tileset, AssetKindGuesser.Guess("Game Boy Advance - Tileset.png", 300, 450).kind);
            Assert.AreEqual(AssetKind.Battle, AssetKindGuesser.Guess("025_front.png", 80, 80).kind);
            Assert.AreEqual(AssetKind.Tileset, AssetKindGuesser.Guess("pueblo.png", 256, 4096).kind, "8 columns of 32 px");
            Assert.AreEqual(AssetKind.Character, AssetKindGuesser.Guess("rojo.png", 128, 192).kind, "RPG Maker XP sheet 4 × 4");
            Assert.AreEqual(AssetKind.Icon, AssetKindGuesser.Guess("pocion.png", 24, 24).kind);
            StringAssert.Contains("tileset", AssetKindGuesser.Guess("Game Boy Advance - Tileset.png", 300, 450).reason);
        }

        [Test]
        public void Changing_the_kind_moves_the_image_and_its_slice()
        {
            var root = Path.Combine(Path.GetTempPath(), "ct_kind_" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectLayout.CreateFolders(root);
                var img = Path.Combine(root, "graficos", "tilesets", "rojo.png");
                File.WriteAllBytes(img, Png.Write(new PixelImage(4, 4)));
                File.WriteAllText(ProjectLayout.SlicePathFor(img), "{}");
                var rel = AssetMover.MoveToKind(root, "graficos/tilesets/rojo.png", AssetKind.Character);
                Assert.AreEqual("graficos/personajes/rojo.png", rel);
                Assert.IsTrue(File.Exists(Path.Combine(root, "graficos", "personajes", "rojo.png")));
                Assert.IsTrue(File.Exists(Path.Combine(root, "graficos", "personajes", "rojo" + ProjectLayout.SliceSuffix)));
                Assert.IsFalse(File.Exists(img));
                Assert.AreEqual(rel, AssetMover.MoveToKind(root, rel, AssetKind.Character), "same kind: nothing moves");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void Importing_a_bmp_writes_a_png_next_to_the_others()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ct_bmp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = Path.Combine(dir, "viejo.dib");
                File.WriteAllBytes(src, Bmp(1, 1, 24, new byte[] { 0, 0, 255, 0 }));
                Assert.IsTrue(ImageFile.CanImport(src));
                Assert.IsTrue(ImageFile.TryReadSize(src, out int w, out int h));
                Assert.AreEqual((1, 1), (w, h));
                var png = ImageFile.ImportAsPng(src, Path.Combine(dir, "graficos"));
                Assert.AreEqual("viejo.png", Path.GetFileName(png));
                Assert.AreEqual(new Rgba32(255, 0, 0), Png.Read(png)[0, 0]);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
