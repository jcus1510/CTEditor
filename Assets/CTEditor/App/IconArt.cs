using System;
using System.Collections.Generic;

namespace CTEditor.App
{
    /// <summary>
    /// Iconos de la interfaz dibujados con formas (no son imágenes): se rasterizan al tamaño exacto en píxeles de la
    /// pantalla, así se ven nítidos con cualquier escala de interfaz. Cada icono se diseña en una cuadrícula de 24 × 24.
    /// Para añadir uno: una entrada en <see cref="Library"/> con sus formas (Line, Quad, Poly, Box, Frame, Circle, Ring;
    /// Cut(...) recorta lo ya dibujado). Sin dependencias de Unity: se puede probar y previsualizar fuera.
    /// </summary>
    public static class IconArt
    {
        public const float Grid = 24f;

        // ── Shapes ───────────────────────────────────────────────────────────────────────────────

        public abstract class Shape
        {
            public bool Cut;
            public abstract bool Contains(float x, float y);
        }

        private sealed class Capsule : Shape
        {
            public float X0, Y0, X1, Y1, R;
            public override bool Contains(float x, float y)
            {
                float dx = X1 - X0, dy = Y1 - Y0;
                float len2 = dx * dx + dy * dy;
                float t = len2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((x - X0) * dx + (y - Y0) * dy) / len2));
                float px = X0 + t * dx - x, py = Y0 + t * dy - y;
                return px * px + py * py <= R * R;
            }
        }

        private sealed class Polygon : Shape
        {
            public float[] P;
            public override bool Contains(float x, float y)
            {
                bool inside = false;
                int n = P.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float xi = P[i * 2], yi = P[i * 2 + 1], xj = P[j * 2], yj = P[j * 2 + 1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                }
                return inside;
            }
        }

        private sealed class Disc : Shape
        {
            public float X, Y, R, Inner;
            public override bool Contains(float x, float y)
            {
                float d2 = (x - X) * (x - X) + (y - Y) * (y - Y);
                return d2 <= R * R && d2 >= Inner * Inner;
            }
        }

        /// <summary>A line with round ends.</summary>
        public static Shape Line(float x0, float y0, float x1, float y1, float width) => new Capsule { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, R = width / 2 };

        /// <summary>A line with square ends (a rotated rectangle) from one point to another.</summary>
        public static Shape Quad(float x0, float y0, float x1, float y1, float width)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / len * width / 2, ny = dx / len * width / 2;
            return Poly(x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny);
        }

        public static Shape Poly(params float[] points) => new Polygon { P = points };
        public static Shape Box(float x, float y, float w, float h) => Poly(x, y, x + w, y, x + w, y + h, x, y + h);
        public static Shape Circle(float x, float y, float r) => new Disc { X = x, Y = y, R = r };
        public static Shape Ring(float x, float y, float r, float width) => new Disc { X = x, Y = y, R = r, Inner = r - width };

        /// <summary>The outline of a rectangle (four boxes).</summary>
        public static IEnumerable<Shape> Frame(float x, float y, float w, float h, float t)
        {
            yield return Box(x, y, w, t);
            yield return Box(x, y + h - t, w, t);
            yield return Box(x, y, t, h);
            yield return Box(x + w - t, y, t, h);
        }

        /// <summary>A dashed rectangle outline.</summary>
        public static IEnumerable<Shape> Dashed(float x, float y, float w, float h, float t, float dash, float gap)
        {
            for (float p = 0; p < w; p += dash + gap)
            {
                float l = Math.Min(dash, w - p);
                yield return Box(x + p, y, l, t);
                yield return Box(x + w - p - l, y + h - t, l, t);
            }
            for (float p = 0; p < h; p += dash + gap)
            {
                float l = Math.Min(dash, h - p);
                yield return Box(x, y + h - p - l, t, l);
                yield return Box(x + w - t, y + p, t, l);
            }
        }

        public static Shape Cut(Shape s) { s.Cut = true; return s; }

        // ── Library ──────────────────────────────────────────────────────────────────────────────

        private static IEnumerable<Shape> Pencil()
        {
            yield return Quad(8.5f, 15.5f, 17.5f, 6.5f, 5.2f);
            yield return Poly(6.66f, 13.66f, 10.34f, 17.34f, 4f, 20f);
            yield return Cut(Quad(14.6f, 9.4f, 15.6f, 8.4f, 6f));
            yield return Quad(16.3f, 7.7f, 19.3f, 4.7f, 5.2f);
        }

        private static IEnumerable<Shape> Fill()
        {
            // A tilted bucket with paint spilling and a drop.
            yield return Poly(10f, 3.5f, 18.5f, 12f, 11.5f, 19f, 3f, 10.5f);
            yield return Cut(Poly(10f, 6.3f, 15.7f, 12f, 11.5f, 16.2f, 5.8f, 10.5f));
            yield return Poly(5.8f, 10.5f, 15.7f, 12f, 11.5f, 16.2f);
            yield return Line(10f, 3.5f, 7f, 0.8f, 1.8f);
            yield return Circle(19.5f, 18f, 2.3f);
            yield return Poly(19.5f, 13.2f, 17.4f, 17.2f, 21.6f, 17.2f);
        }

        private static IEnumerable<Shape> Eraser()
        {
            yield return Quad(5.5f, 15.5f, 15.5f, 5.5f, 9f);
            yield return Cut(Quad(11.3f, 9.7f, 14.7f, 6.3f, 6f));
            yield return Box(9f, 19.5f, 12f, 1.8f);
        }

        private static IEnumerable<Shape> Picker()
        {
            yield return Line(4.5f, 19.5f, 12f, 12f, 2.4f);
            yield return Quad(9.5f, 14.5f, 15f, 9f, 5f);
            yield return Circle(17.5f, 6.5f, 3.6f);
            yield return Quad(11.5f, 9f, 15f, 12.5f, 2f);
            yield return Circle(4f, 20f, 1.4f);
        }

        private static IEnumerable<Shape> Replace()
        {
            // A full colour square turns into another: filled square, arrow, outlined square.
            yield return Box(2.5f, 2.5f, 9f, 9f);
            foreach (var s in Frame(12.5f, 12.5f, 9f, 9f, 2f)) yield return s;
            yield return Line(14f, 5f, 19f, 5f, 2f);
            yield return Line(19f, 5f, 19f, 9.5f, 2f);
            yield return Poly(16.2f, 8.5f, 21.8f, 8.5f, 19f, 11.8f);
            yield return Line(5f, 14f, 5f, 19f, 2f);
            yield return Line(5f, 19f, 9.5f, 19f, 2f);
            yield return Poly(8.5f, 16.2f, 8.5f, 21.8f, 11.8f, 19f);
        }

        private static IEnumerable<Shape> Eye(bool open)
        {
            // Almond outline: two arcs approximated by polygons.
            var outer = new List<float>();
            var inner = new List<float>();
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f, x = 2f + 20f * t, h = (float)Math.Sin(t * Math.PI);
                outer.Add(x); outer.Add(12f - 7.5f * h);
                inner.Add(x); inner.Add(12f - 5.6f * h);
            }
            for (int i = 16; i >= 0; i--)
            {
                float t = i / 16f, x = 2f + 20f * t, h = (float)Math.Sin(t * Math.PI);
                outer.Add(x); outer.Add(12f + 7.5f * h);
                inner.Add(x); inner.Add(12f + 5.6f * h);
            }
            yield return Poly(outer.ToArray());
            yield return Cut(Poly(inner.ToArray()));
            yield return Circle(12f, 12f, 3.6f);
            if (!open) { yield return Cut(Line(4f, 21f, 20f, 3f, 4.4f)); yield return Line(4.5f, 20.5f, 19.5f, 3.5f, 2f); }
        }

        private static IEnumerable<Shape> Lock(bool closed)
        {
            yield return Box(5f, 10.5f, 14f, 10.5f);
            yield return Cut(Circle(12f, 15f, 1.6f));
            yield return Cut(Box(11.3f, 15f, 1.4f, 3.2f));
            if (closed)
            {
                yield return Ring(12f, 9f, 5f, 2.1f);
                yield return Cut(Box(5f, 9f, 14f, 1.5f));
                yield return Box(7f, 8.5f, 2.1f, 2.5f);
                yield return Box(14.9f, 8.5f, 2.1f, 2.5f);
            }
            else
            {
                yield return Ring(16f, 7f, 4.5f, 2.1f);
                yield return Cut(Box(10f, 7f, 12f, 4f));
                yield return Box(18.4f, 6.5f, 2.1f, 4.5f);
            }
        }

        private static IEnumerable<Shape> Trash()
        {
            yield return Box(4f, 5f, 16f, 2.2f);
            yield return Box(9f, 2.5f, 6f, 2.5f);
            yield return Poly(5.5f, 8.5f, 18.5f, 8.5f, 17.2f, 21.5f, 6.8f, 21.5f);
            yield return Cut(Box(9f, 10.5f, 1.6f, 9f));
            yield return Cut(Box(13.4f, 10.5f, 1.6f, 9f));
        }

        private static IEnumerable<Shape> Grip()
        {
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 2; c++)
                yield return Circle(9f + c * 6f, 6f + r * 6f, 1.6f);
        }

        private static IEnumerable<Shape> Passage()
        {
            // «No pasar»: a ring with a bar.
            yield return Ring(12f, 12f, 9f, 2.4f);
            yield return Quad(6.3f, 6.3f, 17.7f, 17.7f, 2.4f);
        }

        private static IEnumerable<Shape> Priority()
        {
            // Two stacked layers, the top one solid.
            yield return Poly(12f, 3f, 21f, 8f, 12f, 13f, 3f, 8f);
            yield return Poly(3f, 12f, 5.2f, 10.8f, 12f, 14.6f, 18.8f, 10.8f, 21f, 12f, 12f, 17f);
            yield return Poly(3f, 16f, 5.2f, 14.8f, 12f, 18.6f, 18.8f, 14.8f, 21f, 16f, 12f, 21f);
        }

        private static IEnumerable<Shape> Terrain()
        {
            // A label (tag) with its hole: terrain tags.
            yield return Poly(3f, 4f, 13f, 4f, 21f, 12f, 13f, 20f, 3f, 20f);
            yield return Cut(Circle(7.5f, 12f, 1.8f));
        }

        private static IEnumerable<Shape> Bush()
        {
            yield return Circle(8f, 13f, 4.6f);
            yield return Circle(16f, 13f, 4.6f);
            yield return Circle(12f, 9f, 5f);
            yield return Box(3.5f, 13f, 17f, 5f);
            yield return Box(2f, 19.5f, 20f, 2f);
        }

        private static IEnumerable<Shape> Counter()
        {
            // A counter seen from the front with a person behind.
            yield return Circle(12f, 5.5f, 2.6f);
            yield return Poly(7.5f, 12f, 9f, 9f, 15f, 9f, 16.5f, 12f);
            yield return Box(2.5f, 12f, 19f, 3f);
            yield return Box(4.5f, 15f, 2.2f, 6.5f);
            yield return Box(17.3f, 15f, 2.2f, 6.5f);
        }

        private static IEnumerable<Shape> Piece()
        {
            // Three flat layers: ground, detail, above.
            yield return Box(3f, 17f, 18f, 3f);
            yield return Box(6f, 11f, 12f, 3f);
            yield return Box(9f, 5f, 6f, 3f);
        }

        private static IEnumerable<Shape> Retouch()
        {
            // Pixels (a 3 × 3 checker) and a small pencil.
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if ((r + c) % 2 == 0) yield return Box(2.5f + c * 4.5f, 9.5f + r * 4.5f, 4.5f, 4.5f);
            yield return Quad(13.5f, 10.5f, 19.5f, 4.5f, 3.4f);
            yield return Poly(12.3f, 9.3f, 14.7f, 11.7f, 11f, 13f);
        }

        private static IEnumerable<Shape> FoldedMap()
        {
            // Three folded panels.
            yield return Poly(3f, 6f, 9f, 3.5f, 9f, 18f, 3f, 20.5f);
            yield return Poly(10f, 3.5f, 14f, 6f, 14f, 20.5f, 10f, 18f);
            yield return Poly(15f, 6f, 21f, 3.5f, 21f, 18f, 15f, 20.5f);
        }

        private static IEnumerable<Shape> TreeList()
        {
            yield return Box(3f, 3.5f, 7f, 4.5f);
            yield return Box(5.6f, 8f, 1.8f, 10.8f);
            yield return Box(5.6f, 11.6f, 5f, 1.8f);
            yield return Box(5.6f, 17f, 5f, 1.8f);
            yield return Box(11f, 10f, 10f, 4.5f);
            yield return Box(11f, 15.5f, 10f, 4.5f);
        }

        private static IEnumerable<Shape> Sliders()
        {
            for (int i = 0; i < 3; i++)
            {
                float y = 5f + i * 7f;
                yield return Box(3f, y - 0.9f, 18f, 1.8f);
                yield return Circle(i == 1 ? 15f : 8f, y, 2.8f);
            }
        }

        private static IEnumerable<Shape> FolderIcon()
        {
            yield return Poly(2.5f, 5f, 9.5f, 5f, 11.5f, 7.5f, 21.5f, 7.5f, 21.5f, 19.5f, 2.5f, 19.5f);
            yield return Cut(Box(4.5f, 10f, 15f, 1.6f));
        }

        private static IEnumerable<Shape> Warning()
        {
            yield return Poly(12f, 2.5f, 22f, 20.5f, 2f, 20.5f);
            yield return Cut(Box(11f, 8.5f, 2f, 6.5f));
            yield return Cut(Circle(12f, 17.5f, 1.3f));
        }

        private static IEnumerable<Shape> Database()
        {
            yield return Box(4f, 5f, 16f, 14f);
            yield return Circle(12f, 5f, 0f);
            foreach (var y in new[] { 5f, 19f })
            {
                // Ellipses approximated by polygons (top and bottom of the cylinder).
                var pts = new List<float>();
                for (int i = 0; i < 16; i++) { double a = i / 16.0 * Math.PI * 2; pts.Add(12f + 8f * (float)Math.Cos(a)); pts.Add(y + 2.6f * (float)Math.Sin(a)); }
                yield return Poly(pts.ToArray());
            }
            var top = new List<float>();
            for (int i = 0; i < 16; i++) { double a = i / 16.0 * Math.PI * 2; top.Add(12f + 6.2f * (float)Math.Cos(a)); top.Add(5f + 1.4f * (float)Math.Sin(a)); }
            yield return Cut(Poly(top.ToArray()));
            yield return Cut(Box(4f, 11.2f, 16f, 1.4f));
        }

        private static IEnumerable<Shape> Globe()
        {
            yield return Ring(12f, 12f, 9f, 2f);
            yield return Box(3.5f, 11.1f, 17f, 1.8f);
            var meridian = new List<float>();
            var inner = new List<float>();
            for (int i = 0; i < 24; i++)
            {
                double a = i / 24.0 * Math.PI * 2;
                meridian.Add(12f + 4.2f * (float)Math.Cos(a)); meridian.Add(12f + 8.5f * (float)Math.Sin(a));
                inner.Add(12f + 2.4f * (float)Math.Cos(a)); inner.Add(12f + 6.8f * (float)Math.Sin(a));
            }
            yield return Poly(meridian.ToArray());
            yield return Cut(Poly(inner.ToArray()));
            yield return Box(11.1f, 3.5f, 1.8f, 17f);
        }

        private static IEnumerable<Shape> Floppy()
        {
            // A floppy disk: body with a cut corner, the label below and the shutter on top.
            yield return Poly(3.5f, 3.5f, 17f, 3.5f, 20.5f, 7f, 20.5f, 20.5f, 3.5f, 20.5f);
            yield return Cut(Box(7f, 3.5f, 8.5f, 5.5f));
            yield return Box(12.2f, 4.5f, 2f, 3.5f);
            yield return Cut(Box(6.5f, 12f, 11f, 8.5f));
            yield return Box(8.5f, 14.2f, 7f, 1.4f);
            yield return Box(8.5f, 17f, 7f, 1.4f);
        }

        private static IEnumerable<Shape> Sun()
        {
            yield return Circle(12f, 12f, 4.4f);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                yield return Line(12f + c * 7f, 12f + s * 7f, 12f + c * 9.6f, 12f + s * 9.6f, 2f);
            }
        }

        private static IEnumerable<Shape> Horizon(bool rising)
        {
            // A half sun on the horizon; an arrow up (morning) or down (evening).
            yield return Box(2f, 17f, 20f, 2f);
            yield return Circle(12f, 17f, rising ? 5.5f : 4.5f);
            yield return Cut(Box(0f, 17f, 24f, 7f));
            yield return Box(2f, 17f, 20f, 2f);
            if (rising) { yield return Line(9f, 7f, 12f, 4f, 2f); yield return Line(12f, 4f, 15f, 7f, 2f); }
            else { yield return Line(9f, 4f, 12f, 7f, 2f); yield return Line(12f, 7f, 15f, 4f, 2f); }
            yield return Line(3.5f, 13f, 5.5f, 14.5f, 1.8f);
            yield return Line(20.5f, 13f, 18.5f, 14.5f, 1.8f);
        }

        private static IEnumerable<Shape> Switch()
        {
            // A toggle switch: a rounded track with its knob on the right (on).
            yield return Line(6.5f, 12f, 17.5f, 12f, 11f);
            yield return Cut(Line(6.5f, 12f, 17.5f, 12f, 7.4f));
            yield return Circle(17.2f, 12f, 3.4f);
        }

        private static IEnumerable<Shape> Paste()
        {
            foreach (var s in Frame(4f, 4.5f, 13f, 16.5f, 2f)) yield return s;
            yield return Box(7.5f, 2.5f, 6f, 4f);
            yield return Box(11f, 10f, 10f, 12f);
            yield return Cut(Box(13f, 12f, 6f, 8f));
        }

        private static IEnumerable<Shape> Zone()
        {
            // A tuft of tall grass.
            yield return Poly(3f, 21f, 6.5f, 8f, 8.5f, 21f);
            yield return Poly(7.5f, 21f, 12f, 3f, 14.5f, 21f);
            yield return Poly(13f, 21f, 18f, 7f, 19.5f, 21f);
            yield return Poly(17f, 21f, 21.5f, 12f, 22f, 21f);
            yield return Box(2f, 20.2f, 20.5f, 1.8f);
        }

        private static IEnumerable<Shape> Start()
        {
            yield return Box(5f, 3f, 2.2f, 19f);
            yield return Poly(7.2f, 3.5f, 19.5f, 8f, 7.2f, 12.5f);
            yield return Box(3f, 20.2f, 7f, 1.8f);
        }

        private static IEnumerable<Shape> Fit()
        {
            float a = 3.5f, b = 20.5f, l = 6f, t = 2f;
            yield return Box(a, a, l, t); yield return Box(a, a, t, l);
            yield return Box(b - l, a, l, t); yield return Box(b - t, a, t, l);
            yield return Box(a, b - t, l, t); yield return Box(a, b - l, t, l);
            yield return Box(b - l, b - t, l, t); yield return Box(b - t, b - l, t, l);
            yield return Box(8.5f, 8.5f, 7f, 7f);
        }

        private static IEnumerable<Shape> GridIcon()
        {
            foreach (var s in Frame(3.5f, 3.5f, 17f, 17f, 1.8f)) yield return s;
            yield return Box(9.2f, 3.5f, 1.6f, 17f);
            yield return Box(14.2f, 3.5f, 1.6f, 17f);
            yield return Box(3.5f, 9.2f, 17f, 1.6f);
            yield return Box(3.5f, 14.2f, 17f, 1.6f);
        }

        private static IEnumerable<Shape> Neighbors()
        {
            foreach (var s in Frame(8f, 8f, 8f, 8f, 2f)) yield return s;
            yield return Box(8f, 2.5f, 8f, 3.5f);
            yield return Box(8f, 18f, 8f, 3.5f);
            yield return Box(2.5f, 8f, 3.5f, 8f);
            yield return Box(18f, 8f, 3.5f, 8f);
        }

        private static readonly Dictionary<string, Func<IEnumerable<Shape>>> Library = new Dictionary<string, Func<IEnumerable<Shape>>>
        {
            ["lapiz"] = Pencil,
            ["rectangulo"] = () => Frame(4f, 5f, 16f, 14f, 2.2f),
            ["relleno"] = Fill,
            ["goma"] = Eraser,
            ["cuentagotas"] = Picker,
            ["linea"] = () => new[] { Line(5f, 19f, 19f, 5f, 2.4f) },
            ["rect_relleno"] = () => new[] { Box(4f, 5f, 16f, 14f) },
            ["reemplazar"] = Replace,
            ["interruptor"] = Switch,
            ["dia"] = Sun,
            ["manana"] = () => Horizon(rising: true),
            ["tarde"] = () => Horizon(rising: false),
            ["noche"] = () => new[] { Circle(12f, 12f, 8.5f), Cut(Circle(16.5f, 8.5f, 7.5f)) },
            ["puntos"] = () => new[] { Circle(5.5f, 12f, 2.1f), Circle(12f, 12f, 2.1f), Circle(18.5f, 12f, 2.1f) },
            ["copiar"] = () => new[] { Box(8f, 8f, 13f, 13f), Cut(Box(10f, 10f, 9f, 9f)), Box(3f, 3f, 13f, 2f), Box(3f, 3f, 2f, 13f) },
            ["estrella"] = () => new[] { Poly(12f, 2.5f, 14.6f, 9f, 21.5f, 9.3f, 16.1f, 13.6f, 17.9f, 20.5f, 12f, 16.6f, 6.1f, 20.5f, 7.9f, 13.6f, 2.5f, 9.3f, 9.4f, 9f) },
            ["guardar"] = Floppy,
            ["mapa"] = FoldedMap,
            ["arbol"] = TreeList,
            ["propiedades"] = Sliders,
            ["carpeta"] = FolderIcon,
            ["eventos"] = () => new[] { Poly(13.5f, 2.5f, 5f, 13.5f, 11f, 13.5f, 9.5f, 21.5f, 19f, 9.5f, 13f, 9.5f) },
            ["aviso"] = Warning,
            ["base"] = Database,
            ["mundo"] = Globe,
            ["capas_todas"] = () => new[] { Box(3f, 3f, 8f, 8f), Box(13f, 3f, 8f, 8f), Box(3f, 13f, 8f, 8f), Box(13f, 13f, 8f, 8f) },
            ["ver"] = () => Eye(true),
            ["oculto"] = () => Eye(false),
            ["bloqueado"] = () => Lock(true),
            ["desbloqueado"] = () => Lock(false),
            ["papelera"] = Trash,
            ["asa"] = Grip,
            ["arriba"] = () => new[] { Line(5f, 15f, 12f, 8f, 2.4f), Line(12f, 8f, 19f, 15f, 2.4f) },
            ["abajo"] = () => new[] { Line(5f, 9f, 12f, 16f, 2.4f), Line(12f, 16f, 19f, 9f, 2.4f) },
            ["paso"] = Passage,
            ["prioridad"] = Priority,
            ["terreno"] = Terrain,
            ["arbusto"] = Bush,
            ["mostrador"] = Counter,
            ["pieza"] = Piece,
            ["retocar"] = Retouch,
            ["seleccion"] = () => Dashed(3.5f, 4.5f, 17f, 15f, 2f, 3.4f, 2.2f),
            ["pegar"] = Paste,
            ["zona"] = Zone,
            ["inicio"] = Start,
            ["jugar"] = () => new[] { Poly(7f, 4.5f, 19.5f, 12f, 7f, 19.5f) },
            ["ajustar"] = Fit,
            ["rejilla"] = GridIcon,
            ["vecinos"] = Neighbors,
            ["cerrar"] = () => new[] { Line(6.5f, 6.5f, 17.5f, 17.5f, 2.2f), Line(17.5f, 6.5f, 6.5f, 17.5f, 2.2f) },
            ["menos"] = () => new[] { Box(5f, 11f, 14f, 2.2f) },
            ["mas"] = () => new[] { Box(5f, 11f, 14f, 2.2f), Box(10.9f, 5f, 2.2f, 14f) },
            ["anterior"] = () => new[] { Line(15f, 5f, 8f, 12f, 2.4f), Line(8f, 12f, 15f, 19f, 2.4f) },
            ["siguiente"] = () => new[] { Line(9f, 5f, 16f, 12f, 2.4f), Line(16f, 12f, 9f, 19f, 2.4f) },
        };

        public static bool Has(string name) => name != null && Library.ContainsKey(name);
        public static IEnumerable<string> Names => Library.Keys;

        /// <summary>Lets modules add icons (same 24 × 24 grid).</summary>
        public static void Register(string name, Func<IEnumerable<Shape>> shapes) => Library[name] = shapes;

        /// <summary>
        /// Coverage (0-255) of each pixel of a size × size icon, rows from the top. 4 × 4 samples per pixel give smooth
        /// edges at any size.
        /// </summary>
        public static byte[] Rasterize(string name, int size)
        {
            var result = new byte[size * size];
            if (!Library.TryGetValue(name, out var make) || size <= 0) return result;
            var shapes = new List<Shape>(make());
            const int S = 4;
            float scale = Grid / size;
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                int hits = 0;
                for (int sy = 0; sy < S; sy++)
                for (int sx = 0; sx < S; sx++)
                {
                    float x = (px + (sx + 0.5f) / S) * scale, y = (py + (sy + 0.5f) / S) * scale;
                    bool on = false;
                    foreach (var s in shapes)
                        if (s.Contains(x, y)) on = !s.Cut;
                    if (on) hits++;
                }
                result[py * size + px] = (byte)(hits * 255 / (S * S));
            }
            return result;
        }
    }
}
