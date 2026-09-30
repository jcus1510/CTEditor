using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
using CTEditor.Art.Domain;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Un tileset ya subido a la tarjeta gráfica: sus texturas (en tiras si la imagen es muy alta) y un Tile de Unity por
    /// cada número de tile, creado la primera vez que se usa. 1 unidad del mundo = el ancho de un tile.
    /// </summary>
    public sealed class TilesetAtlas : IDisposable
    {
        private readonly List<(Texture2D texture, int y, int height)> _strips = new List<(Texture2D, int, int)>();
        private readonly Dictionary<int, Tile> _tiles = new Dictionary<int, Tile>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        public Tileset Tileset { get; }
        public PixelImage Image { get; }
        public string FullPath { get; }
        /// <summary>Alto de una casilla en unidades (tiles no cuadrados).</summary>
        public float CellHeight => (float)Tileset.TileHeight / Tileset.TileWidth;

        public TilesetAtlas(Tileset tileset, string fullPath)
        {
            Tileset = tileset;
            FullPath = fullPath;
            Image = Png.Read(fullPath);
            // Strips whose height is a whole number of tile rows, so no tile is split between two textures.
            int pitch = tileset.TileHeight + tileset.Slice.SpacingY;
            int max = Math.Max(pitch, Math.Min(SystemInfo.maxTextureSize, 8192) / pitch * pitch);
            for (int y = 0; y < Image.Height; y += max)
            {
                int h = Math.Min(max, Image.Height - y);
                var part = y == 0 && h == Image.Height ? Image : Image.Crop(new PixelRect(0, y, Image.Width, h));
                var tex = Textures.FromImage(part);
                _owned.Add(tex);
                _strips.Add((tex, y, h));
            }
        }

        public static TilesetAtlas TryLoad(Tileset tileset, string projectRoot)
        {
            if (tileset == null) return null;
            var path = Path.Combine(projectRoot, tileset.Id);
            try { return File.Exists(path) ? new TilesetAtlas(tileset, path) : null; }
            catch (Exception e)
            {
                Debug.LogWarning($"[CTEditor] No se pudo cargar el tileset {tileset.Id}: {e.Message}");
                return null;
            }
        }

        /// <summary>The Unity tile for a tile number (null for empty or out of range).</summary>
        public Tile TileFor(int index)
        {
            if (index < 0 || !Tileset.Contains(index)) return null;
            if (_tiles.TryGetValue(index, out var tile)) return tile;
            var sprite = SpriteFor(Tileset.RectOf(index), new Vector2(0.5f, 0.5f));
            if (sprite == null) return null;
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.hideFlags = HideFlags.DontSave;
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            _owned.Add(tile);
            _tiles[index] = tile;
            return tile;
        }

        /// <summary>A sprite for any rectangle of the image (image coordinates, top-left origin).</summary>
        public Sprite SpriteFor(PixelRect r, Vector2 pivot)
        {
            foreach (var (texture, y, height) in _strips)
            {
                if (r.Y < y || r.Bottom > y + height) continue;
                // Texture rows start at the bottom.
                var rect = new Rect(r.X, height - (r.Y - y) - r.Height, r.Width, r.Height);
                var sprite = Sprite.Create(texture, rect, pivot, Tileset.TileWidth, 0, SpriteMeshType.FullRect);
                sprite.hideFlags = HideFlags.DontSave;
                _owned.Add(sprite);
                return sprite;
            }
            return null;
        }

        public IEnumerable<(Texture2D texture, int y, int height)> Strips => _strips;

        public void Dispose()
        {
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            _tiles.Clear();
            _strips.Clear();
        }
    }
}
