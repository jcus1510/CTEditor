using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            if (tileset.Free != null && !tileset.Free.IsEmpty) Image = tileset.Free.Compose(Image, tileset.Slice); // hand-cut pieces under the grid
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
            var path = Path.Combine(projectRoot, tileset.ImagePath);
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
            tile.flags = TileFlags.None; // cells may carry their own transform (flipped / turned tiles)
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

        /// <summary>The texture and normalized UV rectangle of a tile (for UI previews). Null if out of range.</summary>
        public (Texture2D texture, Rect uv)? UvFor(int index)
        {
            if (index < 0 || !Tileset.Contains(index)) return null;
            var r = Tileset.RectOf(index);
            foreach (var (texture, y, height) in _strips)
            {
                if (r.Y < y || r.Bottom > y + height) continue;
                float w = texture.width, h = texture.height;
                return (texture, new Rect(r.X / w, (height - (r.Y - y) - r.Height) / h, r.Width / w, r.Height / h));
            }
            return null;
        }

        public void Dispose()
        {
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            _tiles.Clear();
            _strips.Clear();
        }
    }
}

namespace CTEditor.App
{
    /// <summary>
    /// Los tilesets ya subidos a la tarjeta gráfica, por id, compartidos por un panel (varios tramos y huecos usan los
    /// mismos). Se vacía cuando cambia una imagen o un corte.
    /// </summary>
    public sealed class AtlasCache : IDisposable
    {
        private readonly Dictionary<string, TilesetAtlas> _atlases = new Dictionary<string, TilesetAtlas>();
        private readonly Func<string, Tileset> _resolve;
        private readonly string _root;

        public AtlasCache(string projectRoot, Func<string, Tileset> resolve)
        {
            _root = projectRoot;
            _resolve = resolve;
        }

        public TilesetAtlas Get(string tilesetId)
        {
            if (string.IsNullOrEmpty(tilesetId)) return null;
            if (_atlases.TryGetValue(tilesetId, out var a)) return a;
            a = TilesetAtlas.TryLoad(_resolve(tilesetId), _root);
            _atlases[tilesetId] = a;
            return a;
        }

        /// <summary>Atlases of a map by slot (null where a tileset is missing).</summary>
        public TilesetAtlas[] For(World.Domain.MapDefinition map) => map == null ? new TilesetAtlas[0] : map.TilesetIds.Select(Get).ToArray();

        public void Clear()
        {
            foreach (var a in _atlases.Values) a?.Dispose();
            _atlases.Clear();
        }

        public void Dispose() => Clear();
    }
}
