using System.Collections.Generic;

namespace CTEditor.Art.Domain
{
    /// <summary>Un tamaño de tile propuesto y por qué.</summary>
    public sealed class SizeSuggestion
    {
        public int Width { get; }
        public int Height { get; }
        public string Reason { get; }

        public SizeSuggestion(int width, int height, string reason) { Width = width; Height = height; Reason = reason; }

        public SliceSettings ToSettings() => new SliceSettings(Width, Height);
        public override string ToString() => $"{Width}×{Height} ({Reason})";
    }

    /// <summary>
    /// Propone tamaños de tile para una imagen, del más probable al menos: primero los formatos conocidos (tileset de RPG
    /// Maker XP), luego el tamaño del proyecto si encaja, luego los tamaños habituales que dividen la imagen exactamente.
    /// Solo propone: el autor elige en el asistente.
    /// </summary>
    public static class TileSizeSuggester
    {
        /// <summary>Tamaños habituales, en orden de preferencia.</summary>
        public static readonly int[] CommonSizes = { 16, 32, 48, 24, 64, 8 };

        public static IReadOnlyList<SizeSuggestion> Suggest(int imageWidth, int imageHeight, int projectTileSize = 0)
        {
            var list = new List<SizeSuggestion>();
            void Add(int w, int h, string why)
            {
                if (w <= 0 || h <= 0 || w > imageWidth || h > imageHeight) return;
                foreach (var s in list) if (s.Width == w && s.Height == h) return;
                list.Add(new SizeSuggestion(w, h, why));
            }

            if (imageWidth == 256 && imageHeight % 32 == 0)
                Add(32, 32, "tileset de RPG Maker XP: 8 columnas de 32 px");
            if (projectTileSize > 0 && Divides(projectTileSize, imageWidth, imageHeight))
                Add(projectTileSize, projectTileSize, "el tamaño de tile del proyecto");
            foreach (int s in CommonSizes)
                if (Divides(s, imageWidth, imageHeight))
                    Add(s, s, $"encaja exacto: {imageWidth / s}×{imageHeight / s} tiles");
            if (list.Count == 0 && projectTileSize > 0)
                Add(projectTileSize, projectTileSize, "el tamaño del proyecto (sobrarán píxeles)");
            return list;
        }

        private static bool Divides(int size, int w, int h) => size > 0 && w % size == 0 && h % size == 0;
    }
}
