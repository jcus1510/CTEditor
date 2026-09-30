using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Editing;
using CTEditor.World.Domain;

namespace CTEditor.Editing
{
    /// <summary>Herramienta activa del editor de mapas.</summary>
    public enum MapTool
    {
        Pencil = 0,
        Rectangle = 1,
        Fill = 2,
        Eraser = 3,
        Picker = 4,
        /// <summary>Colocar el punto de inicio del jugador.</summary>
        PlayerStart = 5
    }

    /// <summary>
    /// CASO DE USO «editar mapas» (capa de aplicación): el árbol de mapas, el mapa abierto, su tileset, la capa, la
    /// herramienta y el sello elegidos, deshacer/rehacer y guardar. No sabe nada de Unity ni de archivos: usa los
    /// repositorios del dominio y avisa con eventos. La interfaz (paneles Mapas, Mapa, Tiles, Capas, Propiedades) solo
    /// llama a estos métodos y se redibuja con los eventos: así varios paneles comparten el mismo estado sin conocerse.
    /// </summary>
    public sealed class MapEditorSession
    {
        private readonly IMapRepository _maps;
        private readonly ITilesetRepository _tilesets;
        private readonly Dictionary<string, MapDefinition> _loaded = new Dictionary<string, MapDefinition>();
        private readonly Dictionary<string, Tileset> _tilesetCache = new Dictionary<string, Tileset>();
        private readonly HashSet<string> _dirtyMaps = new HashSet<string>();
        private readonly HashSet<string> _dirtyTilesets = new HashSet<string>();
        private bool _treeDirty;

        private TileStroke _stroke;
        private (int x, int y)? _dragStart;

        public MapTree Tree { get; private set; } = new MapTree();
        public MapDefinition Map { get; private set; }
        public Tileset Tileset => Map == null ? null : TilesetFor(Map.TilesetId);
        public int ActiveLayer { get; private set; }
        public MapTool Tool { get; private set; } = MapTool.Pencil;
        public TileStamp Stamp { get; private set; } = TileStamp.Single(0);
        public CommandHistory History { get; } = new CommandHistory();
        public TerrainCatalog Terrains { get; set; } = TerrainCatalog.Essentials();

        /// <summary>Rectángulo que se está arrastrando (rectángulo o cuentagotas), en casillas; null si no hay.</summary>
        public (int x0, int y0, int x1, int y1)? DragRect { get; private set; }

        /// <summary>Inicio del jugador (mapa y casilla). Lo guarda el proyecto.</summary>
        public (string map, int x, int y) PlayerStart { get; private set; }

        public event Action TreeChanged;
        /// <summary>Se abrió otro mapa (o ninguno).</summary>
        public event Action MapOpened;
        /// <summary>Cambiaron casillas: (x, y, ancho, alto) en tiles.</summary>
        public event Action<int, int, int, int> TilesChanged;
        /// <summary>Cambiaron las capas, el tamaño, el tileset o las propiedades del mapa: redibujar todo.</summary>
        public event Action StructureChanged;
        /// <summary>Cambió la herramienta, el sello, la capa activa o el rectángulo arrastrado.</summary>
        public event Action SelectionChanged;
        /// <summary>Cambiaron las propiedades de tiles o se volvió a cortar un tileset.</summary>
        public event Action<string> TilesetChanged;
        public event Action DirtyChanged;
        public event Action PlayerStartChanged;
        /// <summary>Mensaje para el usuario (texto, nivel: «texto», «aviso», «error», «exito»).</summary>
        public event Action<string, string> Message;

        public MapEditorSession(IMapRepository maps, ITilesetRepository tilesets)
        {
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _tilesets = tilesets ?? throw new ArgumentNullException(nameof(tilesets));
        }

        public bool IsDirty => _dirtyMaps.Count > 0 || _dirtyTilesets.Count > 0 || _treeDirty;
        public bool IsMapDirty(string id) => _dirtyMaps.Contains(id);
        public IReadOnlyList<string> AvailableTilesets() => _tilesets.List();

        // ── Tree ─────────────────────────────────────────────────────────────────────────────────

        public void LoadTree()
        {
            Tree = _maps.LoadTree();
            TreeChanged?.Invoke();
        }

        public MapEntry CreateMap(string name, string parentId = "", int width = MapDefinition.DefaultWidth, int height = MapDefinition.DefaultHeight,
            string tilesetId = null)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Mapa nuevo";
            var id = Tree.NewId(name);
            tilesetId ??= Map?.TilesetId ?? AvailableTilesets().FirstOrDefault() ?? "";
            var map = new MapDefinition(id, name.Trim(), width, height, tilesetId);
            _maps.Save(map);
            _loaded[id] = map;
            var entry = new MapEntry(id, map.Name, parentId ?? "");
            Tree.Add(entry);
            _treeDirty = true;
            SaveTreeNow();
            TreeChanged?.Invoke();
            if (string.IsNullOrEmpty(PlayerStart.map)) SetPlayerStart(id, width / 2, height / 2);
            OpenMap(id);
            return entry;
        }

        public void RenameMap(string id, string name)
        {
            var e = Tree.Find(id);
            if (e == null || string.IsNullOrWhiteSpace(name)) return;
            e.Name = name.Trim();
            var map = Get(id);
            if (map != null) { map.Name = e.Name; MarkDirty(id); }
            _treeDirty = true;
            TreeChanged?.Invoke();
            if (Map?.Id == id) StructureChanged?.Invoke();
        }

        public void DeleteMap(string id)
        {
            if (!Tree.Contains(id)) return;
            Tree.Remove(id);
            _maps.Delete(id);
            _loaded.Remove(id);
            _dirtyMaps.Remove(id);
            _treeDirty = true;
            SaveTreeNow();
            if (Map?.Id == id) { Map = null; MapOpened?.Invoke(); }
            TreeChanged?.Invoke();
            DirtyChanged?.Invoke();
        }

        public bool MoveMap(string id, string newParent)
        {
            if (!Tree.Move(id, newParent)) { Message?.Invoke("No se puede meter un mapa dentro de sí mismo.", "aviso"); return false; }
            _treeDirty = true;
            TreeChanged?.Invoke();
            DirtyChanged?.Invoke();
            return true;
        }

        public void SetExpanded(string id, bool expanded)
        {
            var e = Tree.Find(id);
            if (e == null || e.Expanded == expanded) return;
            e.Expanded = expanded;
            _treeDirty = true;
            TreeChanged?.Invoke();
        }

        // ── Open / save ──────────────────────────────────────────────────────────────────────────

        private MapDefinition Get(string id)
        {
            if (_loaded.TryGetValue(id, out var m)) return m;
            if (!_maps.Exists(id)) return null;
            m = _maps.Load(id);
            _loaded[id] = m;
            return m;
        }

        public bool OpenMap(string id)
        {
            MapDefinition map;
            try { map = Get(id); }
            catch (Exception e)
            {
                Message?.Invoke($"No se pudo abrir el mapa «{id}»: {e.Message}", "error");
                return false;
            }
            if (map == null) { Message?.Invoke($"No existe el mapa «{id}».", "error"); return false; }
            Map = map;
            ActiveLayer = Math.Max(0, Math.Min(ActiveLayer, map.Layers.Count - 1));
            CancelDrag();
            MapOpened?.Invoke();
            SelectionChanged?.Invoke();
            return true;
        }

        public void CloseMap()
        {
            Map = null;
            MapOpened?.Invoke();
        }

        /// <summary>Guarda lo que haya cambiado (mapas, árbol, propiedades de tiles).</summary>
        public void Save()
        {
            foreach (var id in _dirtyMaps.ToList())
                if (_loaded.TryGetValue(id, out var m)) _maps.Save(m);
            _dirtyMaps.Clear();
            foreach (var id in _dirtyTilesets.ToList())
                if (_tilesetCache.TryGetValue(id, out var t) && t != null) _tilesets.SaveAttributes(t);
            _dirtyTilesets.Clear();
            if (_treeDirty) SaveTreeNow();
            History.MarkSaved();
            DirtyChanged?.Invoke();
        }

        private void SaveTreeNow()
        {
            _maps.SaveTree(Tree);
            _treeDirty = false;
        }

        private void MarkDirty(string mapId)
        {
            if (mapId == null) return;
            _dirtyMaps.Add(mapId);
            DirtyChanged?.Invoke();
        }

        // ── Tilesets ─────────────────────────────────────────────────────────────────────────────

        public Tileset TilesetFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_tilesetCache.TryGetValue(id, out var t)) return t;
            try { t = _tilesets.Load(id); }
            catch (Exception) { t = null; }
            _tilesetCache[id] = t;
            return t;
        }

        /// <summary>Vuelve a leer los tilesets (se volvió a cortar una imagen o se cambió desde fuera).</summary>
        public void ReloadTilesets()
        {
            foreach (var id in _tilesetCache.Keys.Where(k => !_dirtyTilesets.Contains(k)).ToList()) _tilesetCache.Remove(id);
            TilesetChanged?.Invoke(Map?.TilesetId);
            StructureChanged?.Invoke();
        }

        /// <summary>Cambia propiedades de tiles de un tileset (deshacible; null = el del mapa abierto).</summary>
        public void ChangeTileProperties(string tilesetId, IEnumerable<int> tiles, Action<TileProperties> change, string label)
        {
            var ts = tilesetId == null ? Tileset : TilesetFor(tilesetId);
            if (ts == null) return;
            var changes = tiles.Where(ts.Contains).Distinct().Select(t =>
            {
                var p = ts.Attributes.Get(t);
                change(p);
                return (t, p);
            }).ToList();
            var cmd = new TileAttributeCommand(ts.Attributes, changes, label);
            if (cmd.IsEmpty) return;
            Run(new Tracked(cmd, null, ts.Id, this));
        }

        // ── Tools ────────────────────────────────────────────────────────────────────────────────

        public void SetTool(MapTool tool)
        {
            if (Tool == tool) return;
            Tool = tool;
            CancelDrag();
            SelectionChanged?.Invoke();
        }

        public void SetStamp(TileStamp stamp)
        {
            Stamp = stamp ?? TileStamp.Single(0);
            if (Tool == MapTool.Eraser || Tool == MapTool.Picker || Tool == MapTool.PlayerStart) Tool = MapTool.Pencil;
            SelectionChanged?.Invoke();
        }

        public void SetActiveLayer(int index)
        {
            if (Map == null) return;
            ActiveLayer = Math.Max(0, Math.Min(Map.Layers.Count - 1, index));
            SelectionChanged?.Invoke();
        }

        private TileStamp CurrentStamp => Tool == MapTool.Eraser ? TileStamp.Eraser : Stamp;

        public void PointerDown(int x, int y)
        {
            if (Map == null) return;
            if (Tool != MapTool.Picker && Tool != MapTool.PlayerStart && ActiveLayer < Map.Layers.Count && Map.Layers[ActiveLayer].Locked)
            {
                Message?.Invoke($"La capa «{Map.Layers[ActiveLayer].Name}» está bloqueada.", "aviso");
                return;
            }
            switch (Tool)
            {
                case MapTool.Pencil:
                case MapTool.Eraser:
                    _stroke = new TileStroke(Map);
                    StrokeAt(x, y);
                    break;
                case MapTool.Rectangle:
                case MapTool.Picker:
                    _dragStart = (x, y);
                    DragRect = (x, y, x, y);
                    SelectionChanged?.Invoke();
                    break;
                case MapTool.Fill:
                    Commit(MapTools.Fill(Map, ActiveLayer, x, y, Stamp), "rellenar");
                    break;
                case MapTool.PlayerStart:
                    if (Map.Contains(x, y)) SetPlayerStart(Map.Id, x, y);
                    break;
            }
        }

        public void PointerDrag(int x, int y)
        {
            if (Map == null) return;
            if (_stroke != null) StrokeAt(x, y);
            else if (_dragStart.HasValue)
            {
                DragRect = (_dragStart.Value.x, _dragStart.Value.y, x, y);
                SelectionChanged?.Invoke();
            }
        }

        public void PointerUp(int x, int y)
        {
            if (Map == null) return;
            if (_stroke != null)
            {
                var stroke = _stroke;
                _stroke = null;
                if (!stroke.IsEmpty) Record(stroke.ToCommand(Tool == MapTool.Eraser ? "borrar" : "pintar"));
            }
            else if (_dragStart.HasValue)
            {
                var (x0, y0) = _dragStart.Value;
                _dragStart = null;
                DragRect = null;
                if (Tool == MapTool.Rectangle) Commit(MapTools.Rectangle(Map, ActiveLayer, x0, y0, x, y, Stamp), "rectángulo");
                else if (Tool == MapTool.Picker) SetStamp(MapTools.Pick(Map, -1, x0, y0, x, y));
                SelectionChanged?.Invoke();
            }
        }

        public void CancelDrag()
        {
            if (_stroke != null)
            {
                var cmd = _stroke.ToCommand();
                _stroke = null;
                cmd.Undo();
                if (Map != null) StructureChanged?.Invoke();
            }
            _dragStart = null;
            DragRect = null;
        }

        private void StrokeAt(int x, int y)
        {
            var changes = MapTools.Pencil(Map, ActiveLayer, x, y, CurrentStamp);
            if (changes.Count == 0) return;
            _stroke.Add(changes);
            RaiseTiles(changes);
            MarkDirty(Map.Id);
        }

        private void Commit(List<TileChange> changes, string label)
        {
            if (changes.Count == 0) return;
            Run(new Tracked(new TilePaintCommand(Map, changes, label), Map.Id, null, this, changes));
        }

        private void Record(TilePaintCommand cmd) =>
            History.Record(new Tracked(cmd, Map.Id, null, this, cmd.Changes));

        private void RaiseTiles(IReadOnlyCollection<TileChange> changes)
        {
            if (changes.Count == 0) return;
            int minX = changes.Min(c => c.X), minY = changes.Min(c => c.Y);
            TilesChanged?.Invoke(minX, minY, changes.Max(c => c.X) - minX + 1, changes.Max(c => c.Y) - minY + 1);
        }

        public void SetPlayerStart(string mapId, int x, int y)
        {
            PlayerStart = (mapId ?? "", x, y);
            PlayerStartChanged?.Invoke();
        }

        /// <summary>Carga el inicio guardado en el proyecto (sin avisar de cambios).</summary>
        public void LoadPlayerStart(string mapId, int x, int y) => PlayerStart = (mapId ?? "", x, y);

        // ── Structure ────────────────────────────────────────────────────────────────────────────

        private void Structure(string label, Action<MapDefinition> change)
        {
            if (Map == null) return;
            CancelDrag();
            var cmd = MapStructureCommand.Run(Map, label, change);
            History.Record(new Tracked(cmd, Map.Id, null, this));
            MarkDirty(Map.Id);
            ActiveLayer = Math.Max(0, Math.Min(ActiveLayer, Map.Layers.Count - 1));
            StructureChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void AddLayer(string name = null)
        {
            Structure("añadir capa", m => m.AddLayer(name ?? $"Capa {m.Layers.Count + 1}", ActiveLayer + 1));
            SetActiveLayer(ActiveLayer + 1);
        }

        public void RemoveLayer(int index)
        {
            if (Map == null || index < 0 || index >= Map.Layers.Count) return;
            if (Map.Layers.Count == 1) { Message?.Invoke("Un mapa necesita al menos una capa.", "aviso"); return; }
            Structure("quitar capa", m => m.Layers.RemoveAt(index));
        }

        public void MoveLayer(int index, int delta)
        {
            if (Map == null) return;
            int to = index + delta;
            if (index < 0 || index >= Map.Layers.Count || to < 0 || to >= Map.Layers.Count) return;
            Structure("mover capa", m =>
            {
                var l = m.Layers[index];
                m.Layers.RemoveAt(index);
                m.Layers.Insert(to, l);
            });
            SetActiveLayer(to);
        }

        public void RenameLayer(int index, string name)
        {
            if (Map == null || index < 0 || index >= Map.Layers.Count || string.IsNullOrWhiteSpace(name)) return;
            Structure("renombrar capa", m => m.Layers[index].Name = name.Trim());
        }

        /// <summary>Visible / bloqueada / opacidad: comodidades del editor, sin deshacer (como en los programas de dibujo).</summary>
        public void SetLayerView(int index, bool? visible = null, bool? locked = null, float? opacity = null)
        {
            if (Map == null || index < 0 || index >= Map.Layers.Count) return;
            var l = Map.Layers[index];
            if (visible.HasValue) l.Visible = visible.Value;
            if (locked.HasValue) l.Locked = locked.Value;
            if (opacity.HasValue) l.Opacity = Math.Max(0f, Math.Min(1f, opacity.Value));
            MarkDirty(Map.Id);
            StructureChanged?.Invoke();
        }

        /// <summary>Cambia el tamaño. anchorX/anchorY: 0 = se añade/quita a la derecha/abajo, 0,5 = por igual, 1 = a la izquierda/arriba.</summary>
        public void ResizeMap(int width, int height, float anchorX = 0f, float anchorY = 0f)
        {
            if (Map == null) return;
            if (width < 1 || height < 1 || width > MapDefinition.MaxSize || height > MapDefinition.MaxSize)
            {
                Message?.Invoke($"El tamaño debe estar entre 1 y {MapDefinition.MaxSize}.", "aviso");
                return;
            }
            int ox = (int)Math.Round((width - Map.Width) * anchorX), oy = (int)Math.Round((height - Map.Height) * anchorY);
            Structure("cambiar tamaño", m => m.Resize(width, height, ox, oy));
        }

        public void SetMapTileset(string tilesetId)
        {
            if (Map == null || Map.TilesetId == tilesetId) return;
            Structure("cambiar tileset", m => m.TilesetId = tilesetId ?? "");
            TilesetChanged?.Invoke(tilesetId);
        }

        public void SetMapProperties(string music = null, bool? bicycle = null, bool? outdoor = null)
        {
            if (Map == null) return;
            Structure("propiedades del mapa", m =>
            {
                if (music != null) m.Music = music;
                if (bicycle.HasValue) m.Bicycle = bicycle.Value;
                if (outdoor.HasValue) m.Outdoor = outdoor.Value;
            });
        }

        // ── Undo ─────────────────────────────────────────────────────────────────────────────────

        public void Undo()
        {
            CancelDrag();
            if (!History.Undo()) Message?.Invoke("No hay nada que deshacer.", "texto");
        }

        public void Redo()
        {
            CancelDrag();
            if (!History.Redo()) Message?.Invoke("No hay nada que rehacer.", "texto");
        }

        private void Run(Tracked cmd) => History.Execute(cmd);

        /// <summary>
        /// Wraps a command so doing/undoing it (now or later, from any map) marks what it touched as unsaved and tells the
        /// panels what to redraw. Undoing on a map that is not open still works: it is kept loaded.
        /// </summary>
        private sealed class Tracked : IEditCommand
        {
            private readonly IEditCommand _inner;
            private readonly string _map, _tileset;
            private readonly MapEditorSession _s;
            private readonly IReadOnlyCollection<TileChange> _tiles;

            public Tracked(IEditCommand inner, string map, string tileset, MapEditorSession s, IReadOnlyCollection<TileChange> tiles = null)
            {
                _inner = inner; _map = map; _tileset = tileset; _s = s; _tiles = tiles;
            }

            public string Label => _inner.Label;
            public void Do() { _inner.Do(); After(); }
            public void Undo() { _inner.Undo(); After(); }

            private void After()
            {
                if (_map != null) _s.MarkDirty(_map);
                if (_tileset != null)
                {
                    _s._dirtyTilesets.Add(_tileset);
                    _s.DirtyChanged?.Invoke();
                    _s.TilesetChanged?.Invoke(_tileset);
                }
                if (_map == null || _s.Map?.Id != _map) return;
                if (_tiles != null) _s.RaiseTiles(_tiles);
                else
                {
                    _s.ActiveLayer = Math.Max(0, Math.Min(_s.ActiveLayer, _s.Map.Layers.Count - 1));
                    _s.StructureChanged?.Invoke();
                }
            }
        }
    }
}
