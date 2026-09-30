using System;

namespace CTEditor.Art.Domain
{
    /// <summary>Un color RGBA de 8 bits por canal. A = 0 es totalmente transparente.</summary>
    public readonly struct Rgba32 : IEquatable<Rgba32>
    {
        public readonly byte R, G, B, A;

        public Rgba32(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

        public static readonly Rgba32 Transparent = new Rgba32(0, 0, 0, 0);
        public bool IsTransparent => A == 0;

        public bool Equals(Rgba32 o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object obj) => obj is Rgba32 o && Equals(o);
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
        public static bool operator ==(Rgba32 a, Rgba32 b) => a.Equals(b);
        public static bool operator !=(Rgba32 a, Rgba32 b) => !a.Equals(b);
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    }

    /// <summary>A rectangle in pixels (origin top-left, like PNG files).</summary>
    public readonly struct PixelRect : IEquatable<PixelRect>
    {
        public readonly int X, Y, Width, Height;
        public PixelRect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool Equals(PixelRect o) => X == o.X && Y == o.Y && Width == o.Width && Height == o.Height;
        public override bool Equals(object obj) => obj is PixelRect o && Equals(o);
        public override int GetHashCode() => (X * 397 ^ Y) * 397 ^ (Width * 31 + Height);
        public override string ToString() => $"({X},{Y}) {Width}×{Height}";
    }

    /// <summary>
    /// Una imagen de píxeles en memoria, sin Unity: la base del corte de tilesets, del editor de píxeles y de las
    /// comprobaciones (tiles vacíos, repetidos). Origen arriba a la izquierda, filas de arriba abajo (como en un PNG).
    /// </summary>
    public sealed class PixelImage
    {
        private readonly Rgba32[] _pixels;

        public int Width { get; }
        public int Height { get; }

        public PixelImage(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "La imagen debe medir al menos 1×1.");
            Width = width;
            Height = height;
            _pixels = new Rgba32[width * height];
        }

        public PixelImage(int width, int height, Rgba32[] pixels) : this(width, height)
        {
            if (pixels == null || pixels.Length != width * height)
                throw new ArgumentException($"Se esperaban {width * height} píxeles.", nameof(pixels));
            Array.Copy(pixels, _pixels, pixels.Length);
        }

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool Contains(PixelRect r) => r.Width > 0 && r.Height > 0 && r.X >= 0 && r.Y >= 0 && r.Right <= Width && r.Bottom <= Height;

        public Rgba32 this[int x, int y]
        {
            get => _pixels[y * Width + x];
            set => _pixels[y * Width + x] = value;
        }

        /// <summary>Copia de todos los píxeles (fila a fila).</summary>
        public Rgba32[] ToArray() => (Rgba32[])_pixels.Clone();

        public PixelImage Clone() => new PixelImage(Width, Height, _pixels);

        public PixelImage Crop(PixelRect r)
        {
            RequireInside(r);
            var result = new PixelImage(r.Width, r.Height);
            for (int y = 0; y < r.Height; y++)
                Array.Copy(_pixels, (r.Y + y) * Width + r.X, result._pixels, y * r.Width, r.Width);
            return result;
        }

        /// <summary>¿Todos los píxeles del rectángulo son transparentes?</summary>
        public bool IsTransparent(PixelRect r)
        {
            RequireInside(r);
            for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
                if (!_pixels[y * Width + x].IsTransparent) return false;
            return true;
        }

        /// <summary>
        /// Huella del contenido del rectángulo (FNV-1a de 64 bits). Los transparentes cuentan todos igual, sea cual sea su
        /// color, para que dos tiles que se ven iguales den la misma huella.
        /// </summary>
        public ulong Fingerprint(PixelRect r)
        {
            RequireInside(r);
            ulong h = 14695981039346656037UL;
            void Mix(byte b) { h ^= b; h *= 1099511628211UL; }
            Mix((byte)r.Width); Mix((byte)(r.Width >> 8)); Mix((byte)r.Height); Mix((byte)(r.Height >> 8));
            for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                var p = _pixels[y * Width + x];
                if (p.IsTransparent) { Mix(0); Mix(0); Mix(0); Mix(0); }
                else { Mix(p.R); Mix(p.G); Mix(p.B); Mix(p.A); }
            }
            return h;
        }

        /// <summary>¿Se ven igual los dos rectángulos (mismo tamaño; los transparentes cuentan igual)?</summary>
        public bool SameContent(PixelRect a, PixelRect b)
        {
            RequireInside(a);
            RequireInside(b);
            if (a.Width != b.Width || a.Height != b.Height) return false;
            for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                var p = _pixels[(a.Y + y) * Width + a.X + x];
                var q = _pixels[(b.Y + y) * Width + b.X + x];
                if (p.IsTransparent && q.IsTransparent) continue;
                if (p != q) return false;
            }
            return true;
        }

        private void RequireInside(PixelRect r)
        {
            if (!Contains(r)) throw new ArgumentOutOfRangeException(nameof(r), $"El rectángulo {r} se sale de la imagen ({Width}×{Height}).");
        }
    }
}
