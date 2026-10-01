using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CTEditor.Art.Domain;
using CTEditor.Project;

namespace CTEditor.App
{
    /// <summary>A piece of a big image (images taller than the GPU limit are shown in horizontal strips).</summary>
    public sealed class TextureStrip
    {
        public Texture2D Texture;
        public int Y;
        public int Height;
    }

    /// <summary>
    /// Imágenes del proyecto → texturas de Unity (píxeles nítidos, sin filtrar). Las muy altas (tilesets de Essentials de
    /// miles de píxeles) se parten en tiras; las miniaturas se reducen en memoria antes de subirlas.
    /// </summary>
    public static class Textures
    {
        private sealed class Thumb
        {
            public DateTime Stamp;
            public Texture2D Texture;
        }

        private static readonly Dictionary<string, Thumb> Thumbs = new Dictionary<string, Thumb>();

        public static Texture2D FromImage(PixelImage img)
        {
            var tex = new Texture2D(img.Width, img.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[img.Width * img.Height];
            // Unity textures start at the bottom row; images start at the top.
            for (int y = 0; y < img.Height; y++)
            {
                int dst = (img.Height - 1 - y) * img.Width;
                for (int x = 0; x < img.Width; x++) px[dst + x] = Ui.ToColor32(img[x, y]);
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>The image cut into strips no taller than the GPU allows.</summary>
        public static List<TextureStrip> Strips(PixelImage img)
        {
            int max = Math.Max(1024, Math.Min(SystemInfo.maxTextureSize, 8192));
            var list = new List<TextureStrip>();
            if (img.Width > max) throw new InvalidOperationException($"La imagen mide {img.Width} px de ancho; el máximo es {max}.");
            for (int y = 0; y < img.Height; y += max)
            {
                int h = Math.Min(max, img.Height - y);
                var part = y == 0 && h == img.Height ? img : img.Crop(new PixelRect(0, y, img.Width, h));
                list.Add(new TextureStrip { Texture = FromImage(part), Y = y, Height = h });
            }
            return list;
        }

        /// <summary>Nearest-neighbour reduction so the longest side is at most 'maxSide' (pixel art stays crisp).</summary>
        public static PixelImage Downscale(PixelImage img, int maxSide)
        {
            int longest = Math.Max(img.Width, img.Height);
            if (longest <= maxSide) return img;
            float f = (float)maxSide / longest;
            int w = Math.Max(1, (int)(img.Width * f)), h = Math.Max(1, (int)(img.Height * f));
            var small = new PixelImage(w, h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                small[x, y] = img[Math.Min(img.Width - 1, (int)(x / f)), Math.Min(img.Height - 1, (int)(y / f))];
            return small;
        }

        /// <summary>Thumbnail of a PNG file (cached until the file changes). Null if it cannot be read.</summary>
        public static Texture2D Thumbnail(string path, int maxSide = 96)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (Thumbs.TryGetValue(path, out var t) && t.Stamp == stamp && t.Texture != null) return t.Texture;
                var tex = FromImage(Downscale(ImageFile.Read(path), maxSide));
                if (t?.Texture != null) UnityEngine.Object.Destroy(t.Texture);
                Thumbs[path] = new Thumb { Stamp = stamp, Texture = tex };
                return tex;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Destroy(IEnumerable<TextureStrip> strips)
        {
            if (strips == null) return;
            foreach (var s in strips) if (s.Texture != null) UnityEngine.Object.Destroy(s.Texture);
        }
    }
}
