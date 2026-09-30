using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Editing;

namespace CTEditor.Art.Domain
{
    /// <summary>Un píxel cambiado (para deshacer).</summary>
    public readonly struct PixelChange
    {
        public readonly int X, Y;
        public readonly Rgba32 Before, After;
        public PixelChange(int x, int y, Rgba32 before, Rgba32 after) { X = x; Y = y; Before = before; After = after; }
    }

    /// <summary>
    /// Herramientas del editor de píxeles «Retoque». Solo CALCULAN los cambios (lista de píxeles antes/después); aplicarlos
    /// y deshacerlos es cosa de PixelPaintCommand. Así cada herramienta se prueba sin interfaz.
    /// </summary>
    public static class PixelTools
    {
        /// <summary>Lápiz (o goma, con Transparent) de 'size' píxeles de lado centrado en (x, y).</summary>
        public static List<PixelChange> Pencil(PixelImage img, int x, int y, Rgba32 color, int size = 1)
        {
            var list = new List<PixelChange>();
            int r0 = -(size - 1) / 2, r1 = size / 2;
            for (int dy = r0; dy <= r1; dy++)
            for (int dx = r0; dx <= r1; dx++)
                Add(list, img, x + dx, y + dy, color);
            return list;
        }

        /// <summary>Línea de (x0, y0) a (x1, y1) (Bresenham: sin huecos, sin píxeles de más).</summary>
        public static List<PixelChange> Line(PixelImage img, int x0, int y0, int x1, int y1, Rgba32 color, int size = 1)
        {
            var list = new List<PixelChange>();
            var seen = new HashSet<(int, int)>();
            foreach (var (x, y) in LinePoints(x0, y0, x1, y1))
                foreach (var c in Pencil(img, x, y, color, size))
                    if (seen.Add((c.X, c.Y))) list.Add(c);
            return list;
        }

        public static IEnumerable<(int x, int y)> LinePoints(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                yield return (x0, y0);
                if (x0 == x1 && y0 == y1) yield break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Rectángulo (solo el borde o relleno).</summary>
        public static List<PixelChange> Rectangle(PixelImage img, int x0, int y0, int x1, int y1, Rgba32 color, bool filled)
        {
            var list = new List<PixelChange>();
            int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1), minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                if (filled || x == minX || x == maxX || y == minY || y == maxY)
                    Add(list, img, x, y, color);
            return list;
        }

        /// <summary>
        /// Cubo: rellena la zona contigua del mismo color que (x, y). 'limit' = no salir de ese rectángulo (el tile que
        /// se está retocando). Los transparentes cuentan todos como el mismo color.
        /// </summary>
        public static List<PixelChange> Fill(PixelImage img, int x, int y, Rgba32 color, PixelRect? limit = null)
        {
            var list = new List<PixelChange>();
            if (!img.Contains(x, y)) return list;
            var area = limit ?? new PixelRect(0, 0, img.Width, img.Height);
            bool Inside(int px, int py) => px >= area.X && py >= area.Y && px < area.Right && py < area.Bottom && img.Contains(px, py);
            if (!Inside(x, y)) return list;
            var target = img[x, y];
            if (Same(target, color)) return list;
            var seen = new HashSet<(int, int)> { (x, y) };
            var queue = new Queue<(int, int)>();
            queue.Enqueue((x, y));
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                Add(list, img, cx, cy, color);
                foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                    if (Inside(nx, ny) && !seen.Contains((nx, ny)) && Same(img[nx, ny], target))
                    {
                        seen.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
            }
            return list;
        }

        /// <summary>Cambia un color por otro en toda la imagen (o dentro de 'limit').</summary>
        public static List<PixelChange> ReplaceColor(PixelImage img, Rgba32 from, Rgba32 to, PixelRect? limit = null)
        {
            var list = new List<PixelChange>();
            var area = limit ?? new PixelRect(0, 0, img.Width, img.Height);
            for (int y = Math.Max(0, area.Y); y < Math.Min(img.Height, area.Bottom); y++)
            for (int x = Math.Max(0, area.X); x < Math.Min(img.Width, area.Right); x++)
                if (Same(img[x, y], from)) Add(list, img, x, y, to);
            return list;
        }

        /// <summary>Mueve (o copia) un rectángulo de píxeles: lo que queda detrás, transparente si se mueve.</summary>
        public static List<PixelChange> MoveRegion(PixelImage img, PixelRect region, int dx, int dy, bool copy = false)
        {
            var list = new List<PixelChange>();
            if (!img.Contains(region) || (dx == 0 && dy == 0)) return list;
            var pixels = img.Crop(region);
            var result = img.Clone();
            if (!copy)
                for (int y = region.Y; y < region.Bottom; y++)
                for (int x = region.X; x < region.Right; x++)
                    result[x, y] = Rgba32.Transparent;
            for (int y = 0; y < region.Height; y++)
            for (int x = 0; x < region.Width; x++)
            {
                int tx = region.X + x + dx, ty = region.Y + y + dy;
                if (result.Contains(tx, ty)) result[tx, ty] = pixels[x, y];
            }
            for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y] != result[x, y]) list.Add(new PixelChange(x, y, img[x, y], result[x, y]));
            return list;
        }

        /// <summary>Los colores de la imagen, del más usado al menos (sin los transparentes). Máximo 'max'.</summary>
        public static List<Rgba32> Palette(PixelImage img, int max = 256, PixelRect? limit = null)
        {
            var counts = new Dictionary<Rgba32, int>();
            var area = limit ?? new PixelRect(0, 0, img.Width, img.Height);
            for (int y = Math.Max(0, area.Y); y < Math.Min(img.Height, area.Bottom); y++)
            for (int x = Math.Max(0, area.X); x < Math.Min(img.Width, area.Right); x++)
            {
                var c = img[x, y];
                if (c.IsTransparent) continue;
                counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
            }
            return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.GetHashCode()).Take(max).Select(kv => kv.Key).ToList();
        }

        private static void Add(List<PixelChange> list, PixelImage img, int x, int y, Rgba32 color)
        {
            if (!img.Contains(x, y)) return;
            var before = img[x, y];
            if (before != color) list.Add(new PixelChange(x, y, before, color));
        }

        private static bool Same(Rgba32 a, Rgba32 b) => (a.IsTransparent && b.IsTransparent) || a == b;

        public static void Apply(PixelImage img, IEnumerable<PixelChange> changes, bool undo = false)
        {
            foreach (var c in undo ? changes.Reverse() : changes)
                if (img.Contains(c.X, c.Y)) img[c.X, c.Y] = undo ? c.Before : c.After;
        }

        /// <summary>Rectángulo que abarca los cambios (para actualizar solo esa parte de la textura).</summary>
        public static PixelRect Bounds(IReadOnlyCollection<PixelChange> changes)
        {
            if (changes.Count == 0) return new PixelRect(0, 0, 0, 0);
            int minX = changes.Min(c => c.X), minY = changes.Min(c => c.Y);
            return new PixelRect(minX, minY, changes.Max(c => c.X) - minX + 1, changes.Max(c => c.Y) - minY + 1);
        }
    }

    /// <summary>Cambiar píxeles (deshacible).</summary>
    public sealed class PixelPaintCommand : IEditCommand
    {
        private readonly PixelImage _image;
        public IReadOnlyList<PixelChange> Changes { get; }
        public string Label { get; }

        public PixelPaintCommand(PixelImage image, IReadOnlyList<PixelChange> changes, string label)
        {
            _image = image; Changes = changes; Label = label;
        }

        public void Do() => PixelTools.Apply(_image, Changes);
        public void Undo() => PixelTools.Apply(_image, Changes, undo: true);
    }

    /// <summary>Un trazo de lápiz/goma mientras se arrastra: se ve al momento y al soltar es un solo paso de deshacer.</summary>
    public sealed class PixelStroke
    {
        private readonly PixelImage _image;
        private readonly Dictionary<(int, int), (Rgba32 before, Rgba32 after)> _cells = new Dictionary<(int, int), (Rgba32, Rgba32)>();
        private readonly List<(int, int)> _order = new List<(int, int)>();

        public PixelStroke(PixelImage image) { _image = image; }

        public void Add(IEnumerable<PixelChange> changes)
        {
            foreach (var c in changes)
            {
                var key = (c.X, c.Y);
                if (_cells.TryGetValue(key, out var v)) _cells[key] = (v.before, c.After);
                else { _cells[key] = (c.Before, c.After); _order.Add(key); }
                _image[c.X, c.Y] = c.After;
            }
        }

        public PixelPaintCommand ToCommand(string label)
        {
            var list = _order.Select(k => new PixelChange(k.Item1, k.Item2, _cells[k].before, _cells[k].after))
                .Where(c => c.Before != c.After).ToList();
            return new PixelPaintCommand(_image, list, label);
        }
    }
}

namespace CTEditor.Art.Domain
{
    /// <summary>Dónde se leen y guardan las imágenes (el dominio no sabe que son PNG en una carpeta).</summary>
    public interface IImageRepository
    {
        PixelImage Load(string path);
        void Save(string path, PixelImage image);
    }
}
