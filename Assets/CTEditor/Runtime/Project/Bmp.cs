using System;
using System.IO;
using CTEditor.Art.Domain;

namespace CTEditor.Project
{
    /// <summary>
    /// Lector de BMP / DIB (los «.dib» son el mismo formato) sin Unity. Muchos tilesets antiguos de RPG Maker 2000/2003 y
    /// de ripeos vienen así. Lee cabeceras CORE, INFO, V4 y V5; 1, 4, 8, 16, 24 y 32 bits; sin comprimir, BITFIELDS y
    /// RLE4/RLE8; de abajo arriba o de arriba abajo. El proyecto guarda siempre PNG: al importar se convierten.
    /// </summary>
    public static class Bmp
    {
        public static bool IsBmp(byte[] head) => head != null && head.Length >= 2 && head[0] == 'B' && head[1] == 'M';

        public static bool TryReadSize(string path, out int width, out int height)
        {
            width = height = 0;
            var head = new byte[26];
            using (var f = File.OpenRead(path))
                if (f.Read(head, 0, 26) < 26 || !IsBmp(head)) return false;
            int headerSize = I32(head, 14);
            if (headerSize == 12) { width = U16(head, 18); height = U16(head, 20); }
            else { width = I32(head, 18); height = Math.Abs(I32(head, 22)); }
            return width > 0 && height > 0;
        }

        public static PixelImage Read(string path) => Read(File.ReadAllBytes(path));

        public static PixelImage Read(byte[] d)
        {
            if (!IsBmp(d) || d.Length < 26) throw new InvalidDataException("No es un BMP.");
            int dataOffset = I32(d, 10);
            int headerSize = I32(d, 14);
            int width, height, bpp, compression = 0, colors = 0;
            uint maskR = 0, maskG = 0, maskB = 0, maskA = 0;
            bool core = headerSize == 12;
            if (core)
            {
                width = U16(d, 18); height = (short)U16(d, 20); bpp = U16(d, 24);
            }
            else
            {
                if (headerSize < 40) throw new InvalidDataException("BMP con una cabecera desconocida.");
                width = I32(d, 18); height = I32(d, 22); bpp = U16(d, 28);
                compression = I32(d, 30); colors = I32(d, 46);
                if (compression == 3 || compression == 6)
                {
                    // Masks follow a 40-byte header, or live inside V4/V5 headers.
                    int m = 14 + 40;
                    maskR = U32(d, m); maskG = U32(d, m + 4); maskB = U32(d, m + 8);
                    if (compression == 6 || headerSize >= 56) maskA = U32(d, m + 12);
                }
            }
            if (width <= 0 || height == 0 || width > 32768 || Math.Abs(height) > 32768)
                throw new InvalidDataException("BMP con un tamaño no válido.");
            bool topDown = height < 0;
            height = Math.Abs(height);

            // Palette.
            Rgba32[] palette = null;
            if (bpp <= 8)
            {
                int count = colors > 0 ? colors : 1 << bpp;
                int entry = core ? 3 : 4;
                int p = 14 + headerSize + (compression == 3 && headerSize == 40 ? 12 : 0);
                palette = new Rgba32[256];
                for (int i = 0; i < count && i < 256 && p + i * entry + 2 < d.Length; i++)
                    palette[i] = new Rgba32(d[p + i * entry + 2], d[p + i * entry + 1], d[p + i * entry]);
            }

            var img = new PixelImage(width, height);
            void Put(int x, int row, Rgba32 c)
            {
                if (x < 0 || x >= width || row < 0 || row >= height) return;
                int y = topDown ? row : height - 1 - row;
                img[x, y] = c;
            }

            if (compression == 1 || compression == 2)
            {
                DecodeRle(d, dataOffset, compression == 2, (x, row, i) => Put(x, row, palette[i & 255]));
                return img;
            }
            if (compression != 0 && compression != 3 && compression != 6)
                throw new InvalidDataException("BMP comprimido con un formato que no se puede leer (JPEG/PNG dentro de BMP).");

            if (bpp == 16 && maskR == 0) { maskR = 0x7C00; maskG = 0x03E0; maskB = 0x001F; }
            if (bpp == 32 && maskR == 0) { maskR = 0x00FF0000; maskG = 0x0000FF00; maskB = 0x000000FF; }
            int stride = ((width * bpp + 31) / 32) * 4;
            bool anyAlpha = false;
            for (int row = 0; row < height; row++)
            {
                int o = dataOffset + row * stride;
                if (o + stride > d.Length) throw new InvalidDataException("BMP cortado.");
                for (int x = 0; x < width; x++)
                {
                    Rgba32 c;
                    switch (bpp)
                    {
                        case 1: c = palette[(d[o + x / 8] >> (7 - x % 8)) & 1]; break;
                        case 4: c = palette[(d[o + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 15]; break;
                        case 8: c = palette[d[o + x]]; break;
                        case 24: c = new Rgba32(d[o + x * 3 + 2], d[o + x * 3 + 1], d[o + x * 3]); break;
                        case 16:
                        {
                            uint v = (uint)U16(d, o + x * 2);
                            c = new Rgba32(Channel(v, maskR), Channel(v, maskG), Channel(v, maskB), maskA != 0 ? Channel(v, maskA) : (byte)255);
                            break;
                        }
                        case 32:
                        {
                            uint v = U32(d, o + x * 4);
                            byte a = maskA != 0 ? Channel(v, maskA) : (byte)(v >> 24);
                            if (a != 0) anyAlpha = true;
                            c = new Rgba32(Channel(v, maskR), Channel(v, maskG), Channel(v, maskB), a);
                            break;
                        }
                        default: throw new InvalidDataException($"BMP de {bpp} bits: no se puede leer.");
                    }
                    Put(x, row, c);
                }
            }
            // Many 32-bit BMPs leave the alpha byte at 0: then the image is opaque.
            if (bpp == 32 && !anyAlpha)
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var c = img[x, y];
                    img[x, y] = new Rgba32(c.R, c.G, c.B);
                }
            return img;
        }

        private static void DecodeRle(byte[] d, int p, bool four, Action<int, int, int> put)
        {
            int x = 0, row = 0;
            while (p + 1 < d.Length)
            {
                int n = d[p++], v = d[p++];
                if (n > 0)
                {
                    for (int i = 0; i < n; i++) put(x++, row, four ? (i % 2 == 0 ? v >> 4 : v & 15) : v);
                    continue;
                }
                if (v == 0) { x = 0; row++; }            // end of line
                else if (v == 1) return;                 // end of bitmap
                else if (v == 2) { if (p + 1 >= d.Length) return; x += d[p++]; row += d[p++]; } // delta
                else
                {
                    // Absolute run of v pixels, padded to 16 bits.
                    int bytes = four ? (v + 1) / 2 : v;
                    for (int i = 0; i < v && p + (four ? i / 2 : i) < d.Length; i++)
                        put(x++, row, four ? (i % 2 == 0 ? d[p + i / 2] >> 4 : d[p + i / 2] & 15) : d[p + i]);
                    p += bytes + (bytes & 1);
                }
            }
        }

        private static byte Channel(uint value, uint mask)
        {
            if (mask == 0) return 0;
            int shift = 0;
            while (((mask >> shift) & 1) == 0) shift++;
            uint max = mask >> shift;
            return (byte)(((value & mask) >> shift) * 255 / max);
        }

        private static int U16(byte[] d, int i) => d[i] | d[i + 1] << 8;
        private static int I32(byte[] d, int i) => d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24;
        private static uint U32(byte[] d, int i) => (uint)I32(d, i);
    }

    /// <summary>
    /// Imágenes que entiende el proyecto: PNG (el formato de trabajo) y BMP/DIB (se leen y se convierten a PNG al
    /// importar). Decide por el contenido, no por la extensión.
    /// </summary>
    public static class ImageFile
    {
        /// <summary>Extensions offered when importing.</summary>
        public static readonly string[] ImportExtensions = { ".png", ".bmp", ".dib" };

        public static bool CanImport(string path) =>
            Array.IndexOf(ImportExtensions, Path.GetExtension(path ?? "").ToLowerInvariant()) >= 0;

        public static bool IsPng(string path) => Path.GetExtension(path ?? "").Equals(".png", StringComparison.OrdinalIgnoreCase);

        public static PixelImage Read(string path)
        {
            var data = File.ReadAllBytes(path);
            return Bmp.IsBmp(data) ? Bmp.Read(data) : Png.Read(data);
        }

        public static bool TryReadSize(string path, out int width, out int height) =>
            Png.TryReadSize(path, out width, out height) || Bmp.TryReadSize(path, out width, out height);

        /// <summary>
        /// Copies an image into the project as PNG: a PNG is copied as is; a BMP/DIB is converted («casa.bmp» →
        /// «casa.png»). Returns the path written.
        /// </summary>
        public static string ImportAsPng(string source, string destinationFolder)
        {
            Directory.CreateDirectory(destinationFolder);
            var dest = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(source) + ".png");
            if (IsPng(source)) File.Copy(source, dest, true);
            else Png.Write(Read(source), dest);
            return dest;
        }
    }
}
