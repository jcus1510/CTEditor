using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Dibuja un mapa con el Tilemap de Unity en una RenderTexture (la muestra un panel de la interfaz). Lo usan el editor
    /// de mapas y el juego: lo que se ve al editar es lo que se juega.
    ///   - Cada capa son dos tilemaps: los tiles de prioridad 0 debajo del jugador y los de prioridad 1-5 encima.
    ///   - Casilla (x, y) del mapa (y hacia abajo) = celda (x, −y−1) de Unity (y hacia arriba).
    ///   - Cada renderizador vive en su propia capa de Unity (layer) con su cámara, así el editor y el juego no se mezclan.
    /// </summary>
    public sealed class MapRenderer : IDisposable
    {
        public const int EditorLayer = 31, PlayLayer = 30;
        public const int PlayerSortingOrder = 500;

        private readonly GameObject _root;
        private readonly Grid _grid;
        private readonly Camera _camera;
        private readonly int _unityLayer;
        private readonly List<(Tilemap below, Tilemap above)> _layers = new List<(Tilemap, Tilemap)>();
        private RenderTexture _target;

        public MapDefinition Map { get; private set; }
        public TilesetAtlas Atlas { get; private set; }
        public RenderTexture Target => _target;
        public Camera Camera => _camera;
        /// <summary>Píxeles de pantalla por píxel del tileset (1 = tamaño real).</summary>
        public float Zoom { get; set; } = 1f;
        /// <summary>Centro de la cámara en unidades del mundo.</summary>
        public Vector2 Center { get; set; }
        /// <summary>Capa activa del editor y si las demás se ven atenuadas.</summary>
        public int ActiveLayer { get; set; } = -1;
        public bool DimOthers { get; set; }

        public MapRenderer(string name, int unityLayer, Color background)
        {
            _unityLayer = unityLayer;
            _root = new GameObject(name) { hideFlags = HideFlags.DontSave };
            _root.layer = unityLayer;
            // Far away from other renderers too (belt and braces with the culling mask).
            _root.transform.position = new Vector3(unityLayer * 10000f, 0, 0);
            _grid = new GameObject("Rejilla").AddComponent<Grid>();
            _grid.gameObject.layer = unityLayer;
            _grid.transform.SetParent(_root.transform, false);

            var camGo = new GameObject("Cámara") { layer = unityLayer };
            camGo.transform.SetParent(_root.transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = background;
            _camera.cullingMask = 1 << unityLayer;
            _camera.nearClipPlane = -100f;
            _camera.farClipPlane = 100f;
            // Enabled: with a target texture it renders into it every frame (manual Camera.Render is not reliable in URP).
            _camera.enabled = true;
        }

        /// <summary>Unity position of the renderer's origin (tiles are relative to it).</summary>
        public Vector3 Origin => _root.transform.position;

        public void SetMap(MapDefinition map, TilesetAtlas atlas)
        {
            Map = map;
            Atlas = atlas;
            Rebuild();
        }

        /// <summary>Recreates every tilemap from the map (after opening, undoing a structure change...).</summary>
        public void Rebuild()
        {
            foreach (var (b, a) in _layers)
            {
                UnityEngine.Object.Destroy(b.gameObject);
                UnityEngine.Object.Destroy(a.gameObject);
            }
            _layers.Clear();
            if (Map == null) return;
            _grid.cellSize = new Vector3(1f, Atlas?.CellHeight ?? 1f, 0f);
            for (int i = 0; i < Map.Layers.Count; i++)
                _layers.Add((NewTilemap($"Capa {i}", i * 2), NewTilemap($"Capa {i} (encima)", 1000 + i * 2)));
            for (int i = 0; i < Map.Layers.Count; i++) RefreshLayerView(i);
            RefreshTiles(0, 0, Map.Width, Map.Height);
        }

        private Tilemap NewTilemap(string name, int order)
        {
            var go = new GameObject(name) { layer = _unityLayer };
            go.transform.SetParent(_grid.transform, false);
            var tm = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            r.sortingOrder = order;
            r.mode = TilemapRenderer.Mode.Chunk;
            return tm;
        }

        /// <summary>Visibility, opacity and dimming of one layer.</summary>
        public void RefreshLayerView(int i)
        {
            if (Map == null || i < 0 || i >= _layers.Count) return;
            var l = Map.Layers[i];
            float alpha = l.Opacity * (DimOthers && ActiveLayer >= 0 && i != ActiveLayer ? 0.3f : 1f);
            foreach (var tm in new[] { _layers[i].below, _layers[i].above })
            {
                tm.color = new Color(1, 1, 1, alpha);
                tm.GetComponent<TilemapRenderer>().enabled = l.Visible;
            }
        }

        public void RefreshAllLayerViews()
        {
            for (int i = 0; i < _layers.Count; i++) RefreshLayerView(i);
        }

        /// <summary>Redraws a block of cells (after painting).</summary>
        public void RefreshTiles(int x, int y, int w, int h)
        {
            if (Map == null) return;
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(Map.Width, x + w), y1 = Math.Min(Map.Height, y + h);
            if (x1 <= x0 || y1 <= y0) return;
            int count = (x1 - x0) * (y1 - y0);
            var positions = new Vector3Int[count];
            int n = 0;
            for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                positions[n++] = Cell(xx, yy);
            for (int i = 0; i < _layers.Count && i < Map.Layers.Count; i++)
            {
                var layer = Map.Layers[i];
                var below = new TileBase[count];
                var above = new TileBase[count];
                n = 0;
                for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++)
                {
                    int t = layer.Get(xx, yy);
                    var tile = Atlas?.TileFor(t);
                    bool high = tile != null && Atlas.Tileset.Properties(t).Priority > 0;
                    below[n] = high ? null : tile;
                    above[n] = high ? tile : null;
                    n++;
                }
                _layers[i].below.SetTiles(positions, below);
                _layers[i].above.SetTiles(positions, above);
            }
        }

        public static Vector3Int Cell(int x, int y) => new Vector3Int(x, -y - 1, 0);

        /// <summary>World position (Unity units, relative to the origin) of the top-left corner of a cell.</summary>
        public Vector2 CellTopLeft(float x, float y) => new Vector2(x, -y * (Atlas?.CellHeight ?? 1f));

        // ── Camera and texture ───────────────────────────────────────────────────────────────────

        /// <summary>Makes the target texture exactly width × height physical pixels.</summary>
        public void Resize(int width, int height)
        {
            width = Math.Max(16, width);
            height = Math.Max(16, height);
            if (_target != null && _target.width == width && _target.height == height) return;
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
            _target = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
                { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave, name = "Mapa" };
            _target.Create();
            _camera.targetTexture = _target;
        }

        /// <summary>Tile width in screen pixels at the current zoom.</summary>
        public float PixelsPerUnit => (Atlas?.Tileset.TileWidth ?? 32) * Zoom;

        /// <summary>Applies zoom and centre to the camera (it renders into the texture every frame).</summary>
        public void UpdateCamera()
        {
            if (_target == null) return;
            _camera.orthographicSize = _target.height / (2f * PixelsPerUnit);
            // Snap to whole screen pixels so pixel art never shimmers.
            float ppu = PixelsPerUnit;
            var c = new Vector2(Mathf.Round(Center.x * ppu) / ppu, Mathf.Round(Center.y * ppu) / ppu);
            _camera.transform.position = Origin + new Vector3(c.x, c.y, -10f);
        }

        /// <summary>Texture pixel (top-left origin) → map cell (fractional).</summary>
        public Vector2 PixelToCell(Vector2 pixel)
        {
            float ppu = PixelsPerUnit;
            float wx = Center.x + (pixel.x - _target.width / 2f) / ppu;
            float wy = Center.y + (_target.height / 2f - pixel.y) / ppu;
            return new Vector2(wx, -wy / (Atlas?.CellHeight ?? 1f));
        }

        /// <summary>Map cell (fractional, top-left corner) → texture pixel (top-left origin).</summary>
        public Vector2 CellToPixel(float x, float y)
        {
            float ppu = PixelsPerUnit;
            float wx = x, wy = -y * (Atlas?.CellHeight ?? 1f);
            return new Vector2(_target.width / 2f + (wx - Center.x) * ppu, _target.height / 2f - (wy - Center.y) * ppu);
        }

        /// <summary>Centre of the map in world units.</summary>
        public Vector2 MapCenter => Map == null ? Vector2.zero : new Vector2(Map.Width / 2f, -Map.Height * (Atlas?.CellHeight ?? 1f) / 2f);

        public GameObject CreateChild(string name)
        {
            var go = new GameObject(name) { layer = _unityLayer, hideFlags = HideFlags.DontSave };
            go.transform.SetParent(_root.transform, false);
            return go;
        }

        public void Dispose()
        {
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
            _target = null;
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }
}
