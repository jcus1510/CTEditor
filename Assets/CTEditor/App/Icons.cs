using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTEditor.App
{
    /// <summary>
    /// Los iconos de <see cref="IconArt"/> como texturas de Unity, rasterizados a los píxeles físicos que ocupan (tamaño
    /// en puntos × escala de la interfaz) y teñidos con el color del tema al mostrarlos. Se guardan por nombre y tamaño.
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<(string, int), Texture2D> Cache = new Dictionary<(string, int), Texture2D>();

        public static Texture2D Get(string name, float points)
        {
            int px = Mathf.Max(8, Mathf.RoundToInt(points * Ui.PixelsPerPoint));
            if (Cache.TryGetValue((name, px), out var tex) && tex != null) return tex;
            var coverage = IconArt.Rasterize(name, px);
            tex = new Texture2D(px, px, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
                name = "Icono " + name,
            };
            var pixels = new Color32[px * px];
            for (int y = 0; y < px; y++)
            for (int x = 0; x < px; x++)
                pixels[(px - 1 - y) * px + x] = new Color32(255, 255, 255, coverage[y * px + x]); // texture rows start at the bottom
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Cache[(name, px)] = tex;
            return tex;
        }

        /// <summary>An element showing an icon, tinted with a theme color.</summary>
        public static VisualElement Element(string name, float points, Color tint)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.width = points;
            e.style.height = points;
            e.style.flexShrink = 0;
            e.style.backgroundImage = Get(name, points);
            e.style.unityBackgroundImageTintColor = tint;
            return e;
        }
    }
}
