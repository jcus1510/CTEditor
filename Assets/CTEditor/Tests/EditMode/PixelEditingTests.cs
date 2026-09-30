using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.Project;

namespace CTEditor.Tests.EditMode
{
    /// <summary>Editor de píxeles «Retoque» (fase 4): herramientas, paleta, deshacer y la sesión que guarda el PNG.</summary>
    public class PixelEditingTests
    {
        private static readonly Rgba32 Red = new Rgba32(255, 0, 0);
        private static readonly Rgba32 Blue = new Rgba32(0, 0, 255);

        [Test]
        public void Lines_have_no_gaps_and_no_extra_pixels()
        {
            var pts = PixelTools.LinePoints(0, 0, 5, 2).ToList();
            Assert.AreEqual(6, pts.Count, "one pixel per column on a shallow line");
            Assert.AreEqual((0, 0), pts.First());
            Assert.AreEqual((5, 2), pts.Last());
            for (int i = 1; i < pts.Count; i++)
                Assert.LessOrEqual(Math.Max(Math.Abs(pts[i].x - pts[i - 1].x), Math.Abs(pts[i].y - pts[i - 1].y)), 1);
            var img = new PixelImage(8, 8);
            Assert.AreEqual(8, PixelTools.Line(img, 0, 0, 2, 0, Red, size: 3).Count, "a 3-px brush along x 0..2: x -1..3 × y -1..1, clipped to x 0..3 × y 0..1");
        }

        [Test]
        public void Fill_stays_in_its_area_and_inside_the_tile_limit()
        {
            var img = new PixelImage(8, 4);
            for (int y = 0; y < 4; y++) img[3, y] = Blue; // a wall splitting the image
            var left = PixelTools.Fill(img, 0, 0, Red);
            Assert.AreEqual(12, left.Count);
            var limited = PixelTools.Fill(img, 5, 0, Red, new PixelRect(4, 0, 2, 2));
            Assert.AreEqual(4, limited.Count, "the tile limit stops the fill");
            Assert.IsEmpty(PixelTools.Fill(img, 3, 0, Blue), "same color: nothing");
        }

        [Test]
        public void Replace_color_move_region_and_palette()
        {
            var img = new PixelImage(4, 4);
            img[0, 0] = Red; img[1, 0] = Red; img[2, 0] = Blue;
            var rep = PixelTools.ReplaceColor(img, Red, Blue);
            Assert.AreEqual(2, rep.Count);
            PixelTools.Apply(img, rep);
            Assert.AreEqual(Blue, img[0, 0]);
            CollectionAssert.AreEqual(new[] { Blue }, PixelTools.Palette(img));

            var moved = PixelTools.MoveRegion(img, new PixelRect(0, 0, 3, 1), 0, 2);
            PixelTools.Apply(img, moved);
            Assert.IsTrue(img[0, 0].IsTransparent);
            Assert.AreEqual(Blue, img[2, 2]);
            PixelTools.Apply(img, moved, undo: true);
            Assert.AreEqual(Blue, img[0, 0]);
        }

        [Test]
        public void The_session_paints_previews_undoes_and_saves_the_png()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cteditor_px_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "tile.png");
            try
            {
                Png.Write(new PixelImage(16, 16), path);
                var s = new PixelEditorSession(new PngImageRepository());
                Assert.IsTrue(s.Open(path, 8, 8));
                s.SetPrimary(Red);

                // A fast drag from (0,0) to (5,0) paints the whole line, as one undo step.
                s.PointerDown(0, 0);
                s.PointerDrag(5, 0);
                s.PointerUp(5, 0);
                Assert.AreEqual(Red, s.Image[3, 0]);
                Assert.IsTrue(s.IsDirty);

                // Rectangle preview follows the mouse and only the last one stays.
                s.SetTool(PixelTool.Rectangle);
                s.PointerDown(2, 2);
                s.PointerDrag(9, 9);
                s.PointerDrag(4, 4);
                s.PointerUp(4, 4);
                Assert.AreEqual(Red, s.Image[4, 2]);
                Assert.IsTrue(s.Image[9, 9].IsTransparent, "the earlier preview is gone");
                Assert.IsTrue(s.Image[3, 3].IsTransparent, "outline only");

                s.Undo();
                Assert.IsTrue(s.Image[4, 2].IsTransparent);
                s.Undo();
                Assert.IsTrue(s.Image[3, 0].IsTransparent);
                s.Redo();

                // Tile mode: fill stays inside the 8×8 tile.
                s.SetTool(PixelTool.Fill);
                s.SetTileLimit(s.TileAt(10, 10));
                s.PointerDown(10, 10);
                Assert.AreEqual(Red, s.Image[15, 15]);
                Assert.IsTrue(s.Image[7, 15].IsTransparent);

                s.SetTool(PixelTool.Picker);
                s.PointerDown(20, 20); // outside: nothing
                s.SetSecondary(Blue);
                s.PointerDown(0, 0, secondary: true);
                Assert.AreEqual(Red, s.Secondary);

                Assert.IsTrue(s.Save());
                Assert.IsFalse(s.IsDirty);
                var back = Png.Read(path);
                Assert.AreEqual(Red, back[1, 0]);
                Assert.AreEqual(Red, back[12, 12]);
                Assert.IsFalse(File.Exists(path + ".tmp"));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
