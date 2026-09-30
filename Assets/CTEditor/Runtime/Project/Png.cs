using System;
using System.IO;
using System.IO.Compression;
using CTEditor.Art.Domain;

namespace CTEditor.Project
{
    /// <summary>
    /// Lector y escritor de PNG sin Unity: así la carpeta del proyecto se lee, se corta y se prueba fuera del motor.
    /// Lee todos los tipos de color (gris, RGB, paleta, gris+alfa, RGBA), profundidades 1-16 bits, transparencia (tRNS) y
    /// entrelazado Adam7. Escribe RGBA de 8 bits.
    /// </summary>
    public static class Png
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Solo el tamaño (lee la cabecera; no descomprime nada).</summary>
        public static bool TryReadSize(Stream s, out int width, out int height)
        {
            width = height = 0;
            var head = new byte[24];
            if (ReadFully(s, head, 24) < 24) return false;
            for (int i = 0; i < 8; i++) if (head[i] != Signature[i]) return false;
            if (head[12] != 'I' || head[13] != 'H' || head[14] != 'D' || head[15] != 'R') return false;
            width = BigEndian(head, 16);
            height = BigEndian(head, 20);
            return width > 0 && height > 0;
        }

        public static bool TryReadSize(string path, out int width, out int height)
        {
            using (var f = File.OpenRead(path)) return TryReadSize(f, out width, out height);
        }

        public static PixelImage Read(string path)
        {
            using (var f = File.OpenRead(path)) return Read(f);
        }

        public static PixelImage Read(byte[] data)
        {
            using (var m = new MemoryStream(data)) return Read(m);
        }

        public static PixelImage Read(Stream s)
        {
            var sig = new byte[8];
            if (ReadFully(s, sig, 8) < 8) throw new InvalidDataException("No es un PNG (archivo demasiado corto).");
            for (int i = 0; i < 8; i++) if (sig[i] != Signature[i]) throw new InvalidDataException("No es un PNG.");

            int width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
            byte[] palette = null, trns = null;
            var idat = new MemoryStream();
            var lenBuf = new byte[8];
            while (true)
            {
                if (ReadFully(s, lenBuf, 8) < 8) throw new InvalidDataException("PNG cortado: falta el final (IEND).");
                int len = BigEndian(lenBuf, 0);
                string type = new string(new[] { (char)lenBuf[4], (char)lenBuf[5], (char)lenBuf[6], (char)lenBuf[7] });
                if (len < 0) throw new InvalidDataException("PNG dañado.");
                var data = new byte[len];
                if (ReadFully(s, data, len) < len) throw new InvalidDataException("PNG cortado.");
                ReadFully(s, new byte[4], 4); // CRC (not checked)
                if (type == "IHDR")
                {
                    width = BigEndian(data, 0);
                    height = BigEndian(data, 4);
                    depth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                }
                else if (type == "PLTE") palette = data;
                else if (type == "tRNS") trns = data;
                else if (type == "IDAT") idat.Write(data, 0, data.Length);
                else if (type == "IEND") break;
            }
            if (width <= 0 || height <= 0) throw new InvalidDataException("PNG sin cabecera válida.");
            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0) throw new InvalidDataException($"Tipo de color PNG no válido ({colorType}).");
            if (colorType == 3 && palette == null) throw new InvalidDataException("PNG con paleta pero sin paleta (PLTE).");

            byte[] raw = Inflate(idat.ToArray());
            var image = new PixelImage(width, height);
            int bitsPerPixel = channels * depth;
            int bpp = Math.Max(1, bitsPerPixel / 8);
            int offset = 0;
            if (interlace == 0)
                offset = DecodePass(raw, offset, image, width, height, 0, 0, 1, 1, bitsPerPixel, bpp, depth, colorType, palette, trns);
            else
            {
                int[] sx = { 0, 4, 0, 2, 0, 1, 0 }, sy = { 0, 0, 4, 0, 2, 0, 1 }, dx = { 8, 8, 4, 4, 2, 2, 1 }, dy = { 8, 8, 8, 4, 4, 2, 2 };
                for (int p = 0; p < 7; p++)
                {
                    int pw = (width - sx[p] + dx[p] - 1) / dx[p], ph = (height - sy[p] + dy[p] - 1) / dy[p];
                    if (pw <= 0 || ph <= 0) continue;
                    offset = DecodePass(raw, offset, image, pw, ph, sx[p], sy[p], dx[p], dy[p], bitsPerPixel, bpp, depth, colorType, palette, trns);
                }
            }
            return image;
        }

        private static int DecodePass(byte[] raw, int offset, PixelImage image, int w, int h, int x0, int y0, int dx, int dy,
            int bitsPerPixel, int bpp, int depth, int colorType, byte[] palette, byte[] trns)
        {
            int stride = (w * bitsPerPixel + 7) / 8;
            var prev = new byte[stride];
            var cur = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                if (offset + 1 + stride > raw.Length) throw new InvalidDataException("PNG cortado: faltan datos de imagen.");
                int filter = raw[offset++];
                Array.Copy(raw, offset, cur, 0, stride);
                offset += stride;
                Unfilter(filter, cur, prev, bpp);
                for (int x = 0; x < w; x++)
                    image[x0 + x * dx, y0 + y * dy] = Pixel(cur, x, depth, colorType, palette, trns);
                var t = prev; prev = cur; cur = t;
            }
            return offset;
        }

        private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
        {
            for (int i = 0; i < cur.Length; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                switch (filter)
                {
                    case 0: break;
                    case 1: cur[i] = (byte)(cur[i] + a); break;
                    case 2: cur[i] = (byte)(cur[i] + b); break;
                    case 3: cur[i] = (byte)(cur[i] + ((a + b) >> 1)); break;
                    case 4: cur[i] = (byte)(cur[i] + Paeth(a, b, c)); break;
                    default: throw new InvalidDataException($"Filtro PNG no válido ({filter}).");
                }
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        /// <summary>Sample 'n' of the row (0-based), scaled to 8 bits except for palette indices.</summary>
        private static int Sample(byte[] row, int n, int depth, bool scale = true)
        {
            switch (depth)
            {
                case 8: return row[n];
                case 16: return row[n * 2]; // high byte
                default:
                    int perByte = 8 / depth, shift = 8 - depth * (n % perByte + 1);
                    int v = (row[n / perByte] >> shift) & ((1 << depth) - 1);
                    return scale ? v * 255 / ((1 << depth) - 1) : v;
            }
        }

        private static int Sample16(byte[] row, int n) => (row[n * 2] << 8) | row[n * 2 + 1];

        private static Rgba32 Pixel(byte[] row, int x, int depth, int colorType, byte[] palette, byte[] trns)
        {
            switch (colorType)
            {
                case 0:
                {
                    int g = Sample(row, x, depth);
                    byte a = 255;
                    if (trns != null && trns.Length >= 2)
                    {
                        int key = (trns[0] << 8) | trns[1];
                        int rawV = depth == 16 ? Sample16(row, x) : Sample(row, x, depth, false);
                        if (rawV == key) a = 0;
                    }
                    return new Rgba32((byte)g, (byte)g, (byte)g, a);
                }
                case 2:
                {
                    int r = Sample(row, x * 3, depth), g = Sample(row, x * 3 + 1, depth), b = Sample(row, x * 3 + 2, depth);
                    byte a = 255;
                    if (trns != null && trns.Length >= 6)
                    {
                        bool match = depth == 16
                            ? Sample16(row, x * 3) == ((trns[0] << 8) | trns[1]) && Sample16(row, x * 3 + 1) == ((trns[2] << 8) | trns[3])
                              && Sample16(row, x * 3 + 2) == ((trns[4] << 8) | trns[5])
                            : r == trns[1] && g == trns[3] && b == trns[5];
                        if (match) a = 0;
                    }
                    return new Rgba32((byte)r, (byte)g, (byte)b, a);
                }
                case 3:
                {
                    int i = Sample(row, x, depth, false);
                    if (i * 3 + 2 >= palette.Length) return Rgba32.Transparent;
                    byte a = trns != null && i < trns.Length ? trns[i] : (byte)255;
                    return new Rgba32(palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2], a);
                }
                case 4:
                {
                    int g = Sample(row, x * 2, depth), a = Sample(row, x * 2 + 1, depth);
                    return new Rgba32((byte)g, (byte)g, (byte)g, (byte)a);
                }
                default:
                    return new Rgba32((byte)Sample(row, x * 4, depth), (byte)Sample(row, x * 4 + 1, depth), (byte)Sample(row, x * 4 + 2, depth),
                        (byte)Sample(row, x * 4 + 3, depth));
            }
        }

        // ── Writing ───────────────────────────────────────────────────────────────────────────────

        public static byte[] Write(PixelImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            var raw = new byte[(image.Width * 4 + 1) * image.Height];
            int o = 0;
            for (int y = 0; y < image.Height; y++)
            {
                raw[o++] = 0;
                for (int x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    raw[o++] = p.R; raw[o++] = p.G; raw[o++] = p.B; raw[o++] = p.A;
                }
            }
            using (var m = new MemoryStream())
            {
                m.Write(Signature, 0, 8);
                var ihdr = new byte[13];
                PutBigEndian(ihdr, 0, image.Width);
                PutBigEndian(ihdr, 4, image.Height);
                ihdr[8] = 8; ihdr[9] = 6;
                Chunk(m, "IHDR", ihdr);
                Chunk(m, "IDAT", Deflate(raw));
                Chunk(m, "IEND", new byte[0]);
                return m.ToArray();
            }
        }

        public static void Write(PixelImage image, string path) => File.WriteAllBytes(path, Write(image));

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            PutBigEndian(head, 0, data.Length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head, 0, 8);
            s.Write(data, 0, data.Length);
            uint crc = Crc32(head, 4, 4, 0xFFFFFFFFu);
            crc = Crc32(data, 0, data.Length, crc) ^ 0xFFFFFFFFu;
            var c = new byte[4];
            PutBigEndian(c, 0, (int)crc);
            s.Write(c, 0, 4);
        }

        // ── zlib (header + deflate + Adler-32) ───────────────────────────────────────────────────

        private static byte[] Inflate(byte[] zlib)
        {
            if (zlib.Length < 2) throw new InvalidDataException("PNG sin datos de imagen.");
            using (var input = new MemoryStream(zlib, 2, zlib.Length - 2))
            using (var d = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                d.CopyTo(output);
                return output.ToArray();
            }
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var d = new DeflateStream(output, CompressionLevel.Optimal, true)) d.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (byte v in data) { a = (a + v) % 65521; b = (b + a) % 65521; }
                var adler = new byte[4];
                PutBigEndian(adler, 0, (int)((b << 16) | a));
                output.Write(adler, 0, 4);
                return output.ToArray();
            }
        }

        private static uint[] _crcTable;

        private static uint Crc32(byte[] data, int start, int count, uint crc)
        {
            if (_crcTable == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                _crcTable = t;
            }
            for (int i = start; i < start + count; i++) crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static int BigEndian(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        private static void PutBigEndian(byte[] b, int i, int v)
        {
            b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v;
        }

        private static int ReadFully(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) break;
                read += n;
            }
            return read;
        }
    }
}
