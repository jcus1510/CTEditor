using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>Un tramo para dibujar: su mapa, sus tilesets (por hueco), dónde va (en casillas) y su opacidad.</summary>
    public sealed class SectionView
    {
        public MapDefinition Map;
        public TilesetAtlas[] Atlases;
        public MapTilesets Sets;
        /// <summary>Posición de su esquina, en casillas, respecto al origen del renderizador.</summary>
        public Vector2Int Offset;
        public float Alpha = 1f;
    }

    /// <summary>
    /// Dibuja uno o varios tramos con el Tilemap de Unity en una RenderTexture (la muestra un panel). Lo usan el editor de
    /// mapas (el tramo abierto y sus vecinos atenuados), la vista del mundo (todos los tramos) y el juego: lo que se ve al
    /// editar es lo que se juega.
    ///   - Cada capa son dos tilemaps: los tiles de prioridad 0 debajo del jugador y los de prioridad 1-5 encima.
    ///   - Casilla (x, y) (y hacia abajo) = celda (x, −y−1) de Unity (y hacia arriba). 1 unidad = 1 casilla.
    ///   - Cada renderizador vive en su propia capa de Unity con su cámara, así el editor y el juego no se mezclan.
    /// </summary>
    public sealed class MapRenderer : IDisposable
    {
        public const int EditorLayer = 31, PlayLayer = 30, WorldLayer = 29;
        public const int PlayerSortingOrder = 500;

        private sealed class Section
        {
            public SectionView View;
            public Grid Grid;
            public readonly List<(Tilemap below, Tilemap above)> Layers = new List<(Tilemap, Tilemap)>();
        }

        private readonly GameObject _root;
        private readonly Camera _camera;
        private readonly int _unityLayer;
        private readonly List<Section> _sections = new List<Section>();
        private RenderTexture _target;

        /// <summary>El tramo principal (el primero): el que se edita.</summary>
        public MapDefinition Map => _sections.Count > 0 ? _sections[0].View.Map : null;
        public RenderTexture Target => _target;
        public Camera Camera => _camera;
        /// <summary>Tamaño del tile en píxeles a zoom 1 (el del proyecto).</summary>
        public int TileSize { get; set; } = 32;
        /// <summary>Píxeles de pantalla por píxel de tile (1 = tamaño real).</summary>
        public float Zoom { get; set; } = 1f;
        /// <summary>Centro de la cámara en casillas del mundo del renderizador (y hacia arriba, negativa hacia abajo).</summary>
        public Vector2 Center { get; set; }
        /// <summary>Capa activa del editor y si las demás se ven atenuadas (solo en el tramo principal).</summary>
        public int ActiveLayer { get; set; } = -1;
        public bool DimOthers { get; set; }

        public MapRenderer(string name, int unityLayer, Color background)
        {
            _unityLayer = unityLayer;
            _root = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = unityLayer };
            // At the origin: renderers are kept apart only by their Unity layer and culling mask. (They used to be placed
            // at x = layer × 100 000; at 3 100 000 units a float only has a precision of about 0.25, so tiles snapped
            // to quarter cells horizontally and drifted off the grid when zooming.)
            _root.transform.position = Vector3.zero;

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

        public Vector3 Origin => _root.transform.position;

        public void SetMap(MapDefinition map, TilesetAtlas[] atlases, MapTilesets sets) =>
            SetSections(map == null ? new SectionView[0] : new[] { new SectionView { Map = map, Atlases = atlases, Sets = sets } });

        /// <summary>Recreates every tilemap (after opening, undoing a structure change, moving sections...).</summary>
        public void SetSections(IEnumerable<SectionView> views)
        {
            foreach (var s in _sections) UnityEngine.Object.Destroy(s.Grid.gameObject);
            _sections.Clear();
            int index = 0;
            foreach (var v in views)
            {
                if (v?.Map == null) continue;
                var grid = new GameObject("Tramo " + v.Map.Id) { layer = _unityLayer }.AddComponent<Grid>();
                grid.transform.SetParent(_root.transform, false);
                grid.transform.localPosition = new Vector3(v.Offset.x, -v.Offset.y, 0);
                grid.cellSize = Vector3.one;
                var s = new Section { View = v, Grid = grid };
                for (int i = 0; i < v.Map.Layers.Count; i++)
                    s.Layers.Add((NewTilemap(grid, $"Capa {i}", index * 40 + i * 2 - 2000), NewTilemap(grid, $"Capa {i} (encima)", 1000 + index * 40 + i * 2)));
                _sections.Add(s);
                for (int i = 0; i < v.Map.Layers.Count; i++) RefreshLayerView(s, i);
                Fill(s, 0, 0, v.Map.Width, v.Map.Height);
                index++;
            }
        }

        private Tilemap NewTilemap(Grid grid, string name, int order)
        {
            var go = new GameObject(name) { layer = _unityLayer };
            go.transform.SetParent(grid.transform, false);
            var tm = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            r.sortingOrder = order;
            r.mode = TilemapRenderer.Mode.Chunk;
            return tm;
        }

        /// <summary>Visibility, opacity and dimming of one layer of the main section.</summary>
        public void RefreshLayerView(int i)
        {
            if (_sections.Count > 0) RefreshLayerView(_sections[0], i);
        }

        public void RefreshAllLayerViews()
        {
            foreach (var s in _sections)
                for (int i = 0; i < s.Layers.Count; i++) RefreshLayerView(s, i);
        }

        private void RefreshLayerView(Section s, int i)
        {
            if (i < 0 || i >= s.Layers.Count || i >= s.View.Map.Layers.Count) return;
            var l = s.View.Map.Layers[i];
            bool main = s == _sections[0];
            float alpha = l.Opacity * s.View.Alpha * (main && DimOthers && ActiveLayer >= 0 && i != ActiveLayer ? 0.3f : 1f);
            foreach (var tm in new[] { s.Layers[i].below, s.Layers[i].above })
            {
                tm.color = new Color(1, 1, 1, alpha);
                tm.GetComponent<TilemapRenderer>().enabled = l.Visible;
            }
        }

        /// <summary>Redraws a block of cells of the main section (after painting).</summary>
        public void RefreshTiles(int x, int y, int w, int h)
        {
            if (_sections.Count > 0) Fill(_sections[0], x, y, w, h);
        }

        /// <summary>Redraws a block of cells of any section by map id.</summary>
        public void RefreshTiles(string mapId, int x, int y, int w, int h)
        {
            var s = _sections.FirstOrDefault(v => v.View.Map.Id == mapId);
            if (s != null) Fill(s, x, y, w, h);
        }

        private static void Fill(Section s, int x, int y, int w, int h)
        {
            var map = s.View.Map;
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(map.Width, x + w), y1 = Math.Min(map.Height, y + h);
            if (x1 <= x0 || y1 <= y0) return;
            int count = (x1 - x0) * (y1 - y0);
            var positions = new Vector3Int[count];
            int n = 0;
            for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                positions[n++] = Cell(xx, yy);
            for (int i = 0; i < s.Layers.Count && i < map.Layers.Count; i++)
            {
                var layer = map.Layers[i];
                var below = new TileBase[count];
                var above = new TileBase[count];
                n = 0;
                for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++)
                {
                    int cell = layer.Get(xx, yy);
                    int slot = MapTile.Slot(cell);
                    var atlas = slot >= 0 && s.View.Atlases != null && slot < s.View.Atlases.Length ? s.View.Atlases[slot] : null;
                    var tile = atlas?.TileFor(MapTile.Index(cell));
                    bool high = tile != null && (s.View.Sets?.Properties(cell).Priority ?? 0) > 0;
                    below[n] = high ? null : tile;
                    above[n] = high ? tile : null;
                    n++;
                }
                s.Layers[i].below.SetTiles(positions, below);
                s.Layers[i].above.SetTiles(positions, above);
            }
        }

        public static Vector3Int Cell(int x, int y) => new Vector3Int(x, -y - 1, 0);

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
            // Unity may keep the screen's aspect for a camera with a target texture: set it, or cells stretch sideways.
            _camera.aspect = width / (float)height;
        }

        /// <summary>One cell in screen pixels at the current zoom.</summary>
        public float PixelsPerUnit => TileSize * Zoom;

        /// <summary>Applies zoom and centre to the camera (it renders into the texture every frame).</summary>
        public void UpdateCamera()
        {
            if (_target == null) return;
            _camera.aspect = _target.width / (float)_target.height;
            _camera.orthographicSize = _target.height / (2f * PixelsPerUnit);
            // Snap so cell edges fall on whole texture pixels (also with odd sizes): pixel art never shimmers and the
            // overlay grid, which uses the same Center, matches the tiles exactly.
            float ppu = PixelsPerUnit;
            float halfW = _target.width / 2f, halfH = _target.height / 2f;
            Center = new Vector2((Mathf.Round(Center.x * ppu - halfW) + halfW) / ppu, (Mathf.Round(Center.y * ppu - halfH) + halfH) / ppu);
            _camera.transform.position = Origin + new Vector3(Center.x, Center.y, -10f);
            // An explicit projection: exactly width × height texture pixels, ppu pixels per cell, whatever the render
            // pipeline does with the camera's aspect (the cells came out about 3 % narrower than the grid when zooming).
            float halfWorldW = halfW / ppu, halfWorldH = halfH / ppu;
            _camera.projectionMatrix = Matrix4x4.Ortho(-halfWorldW, halfWorldW, -halfWorldH, halfWorldH, _camera.nearClipPlane, _camera.farClipPlane);
        }

        /// <summary>Texture pixel (top-left origin) → cell (fractional, y down).</summary>
        public Vector2 PixelToCell(Vector2 pixel)
        {
            float ppu = PixelsPerUnit;
            float wx = Center.x + (pixel.x - _target.width / 2f) / ppu;
            float wy = Center.y + (_target.height / 2f - pixel.y) / ppu;
            return new Vector2(wx, -wy);
        }

        /// <summary>Cell (fractional, top-left corner, y down) → texture pixel (top-left origin).</summary>
        public Vector2 CellToPixel(float x, float y)
        {
            float ppu = PixelsPerUnit;
            return new Vector2(_target.width / 2f + (x - Center.x) * ppu, _target.height / 2f - (-y - Center.y) * ppu);
        }

        /// <summary>Centre (world units) of a rectangle of cells.</summary>
        public static Vector2 CenterOf(float x, float y, float w, float h) => new Vector2(x + w / 2f, -(y + h / 2f));

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
