using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Art.Domain;
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
        PlayerStart = 5,
        /// <summary>Seleccionar una zona (copiar, cortar, borrar).</summary>
        Select = 6,
        /// <summary>Pegar lo copiado donde se haga clic.</summary>
        Paste = 7,
        /// <summary>Pintar casillas de la zona de encuentros activa (clic derecho = quitar).</summary>
        EncounterPaint = 8,
        /// <summary>Puertas: clic = marcar una puerta y luego su salida (en otro mapa o en este); arrastrar una = moverla.</summary>
        Door = 9
    }

    /// <summary>
    /// CASO DE USO «editar el mundo» (capa de aplicación): el árbol de mapas (tramos), el mapa abierto y sus tilesets, la
    /// capa, la herramienta y el sello elegidos, las capas automáticas, la selección y el portapapeles, las zonas de
    /// encuentros, el mundo continuo (mover tramos), el mapa de la región, deshacer/rehacer y guardar. No sabe nada de
    /// Unity ni de archivos: usa los repositorios del dominio y avisa con eventos; los paneles solo llaman y redibujan.
    /// </summary>
    public sealed class MapEditorSession
    {
        private readonly IMapRepository _maps;
        private readonly ITilesetRepository _tilesets;
        private readonly IEncounterMethodRepository _methodRepo;
        private readonly Dictionary<string, MapDefinition> _loaded = new Dictionary<string, MapDefinition>();
        private readonly Dictionary<string, Tileset> _tilesetCache = new Dictionary<string, Tileset>();
        private readonly HashSet<string> _dirtyMaps = new HashSet<string>();
        private readonly HashSet<string> _dirtyTilesets = new HashSet<string>();
        private bool _treeDirty;

        private TileStroke _stroke;
        private (int x, int y)? _dragStart;
        private List<(int x, int y)> _areaAdded, _areaRemoved;
        private bool _areaErasing;

        public MapTree Tree { get; private set; } = new MapTree();
        public MapDefinition Map { get; private set; }
        public int ActiveLayer { get; private set; }
        public MapTool Tool { get; private set; } = MapTool.Pencil;
        public TileStamp Stamp { get; private set; } = TileStamp.Single(0);
        public CommandHistory History { get; } = new CommandHistory();
        public TerrainCatalog Terrains { get; set; } = TerrainCatalog.Essentials();
        public EncounterMethodCatalog Methods { get; private set; } = EncounterMethodCatalog.Classic();
        public ISpeciesDirectory Species { get; }

        /// <summary>Capas automáticas: cada tile va a la capa de su tipo de pieza (suelo, detalle, encima).</summary>
        public bool AutoLayers { get; private set; } = true;
        /// <summary>Ver las capas que no son la activa atenuadas (solo en el editor).</summary>
        public bool DimOtherLayers { get; private set; }
        /// <summary>Hueco del mapa cuyo tileset muestra la paleta.</summary>
        public int PaletteSlot { get; private set; }

        /// <summary>Rectángulo que se está arrastrando (rectángulo, cuentagotas, selección), en casillas; null si no hay.</summary>
        public (int x0, int y0, int x1, int y1)? DragRect { get; private set; }
        /// <summary>Zona seleccionada con la herramienta Selección (null = nada).</summary>
        public (int x0, int y0, int x1, int y1)? Selection { get; private set; }
        /// <summary>Lo copiado (Ctrl+C / Ctrl+X).</summary>
        public MapClipboard Clipboard { get; private set; }
        /// <summary>Zona de encuentros que se pinta y se edita (id; null = ninguna).</summary>
        public string ActiveAreaId { get; private set; }

        /// <summary>Casilla bajo el ratón en el mapa abierto (para «Probar aquí» y la barra de estado).</summary>
        public (int x, int y)? Cursor { get; set; }

        /// <summary>Inicio del jugador (mapa y casilla). Lo guarda el proyecto; en el mapa es un objeto «inicio».</summary>
        public (string map, int x, int y) PlayerStart { get; private set; }

        public event Action TreeChanged;
        /// <summary>Se abrió otro mapa (o ninguno).</summary>
        public event Action MapOpened;
        /// <summary>Cambiaron casillas: (x, y, ancho, alto) en tiles.</summary>
        public event Action<int, int, int, int> TilesChanged;
        /// <summary>Cambiaron las capas, el tamaño, los tilesets o las propiedades del mapa: redibujar todo.</summary>
        public event Action StructureChanged;
        /// <summary>Visible / bloqueada / opacidad de una capa (solo se redibuja esa capa; no cambia la estructura).</summary>
        public event Action<int> LayerViewChanged;
        /// <summary>Cambió la herramienta, el sello, la capa activa, la selección o el rectángulo arrastrado.</summary>
        public event Action SelectionChanged;
        /// <summary>Cambiaron las propiedades de tiles o se volvió a cortar un tileset.</summary>
        public event Action<string> TilesetChanged;
        /// <summary>Cambiaron las zonas de encuentros o sus tablas (del mapa abierto).</summary>
        public event Action EncountersChanged;
        /// <summary>Cambiaron los métodos de encuentro del proyecto.</summary>
        public event Action MethodsChanged;
        /// <summary>Se movió, colocó, ocultó o bloqueó un tramo del mundo.</summary>
        public event Action WorldChanged;
        public event Action DirtyChanged;
        public event Action PlayerStartChanged;
        /// <summary>Changed the objects of a map (doors...), the chosen object or the half-made door.</summary>
        public event Action ObjectsChanged;
        /// <summary>Mensaje para el usuario (texto, nivel: «texto», «aviso», «error», «exito»).</summary>
        public event Action<string, string> Message;

        public MapEditorSession(IMapRepository maps, ITilesetRepository tilesets, IEncounterMethodRepository methods = null,
            ISpeciesDirectory species = null)
        {
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _tilesets = tilesets ?? throw new ArgumentNullException(nameof(tilesets));
            _methodRepo = methods;
            Species = species;
            if (methods != null)
            {
                try { Methods = methods.Load(); }
                catch (Exception e) { Message?.Invoke("No se pudieron leer los métodos de encuentro: " + e.Message, "aviso"); }
            }
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

        /// <summary>
        /// Crea un tramo. Exterior: se coloca en el mundo pegado a 'nextTo' por 'side' (o en un hueco libre si no se da).
        /// Interior: va aparte (se entrará por puertas).
        /// </summary>
        public MapEntry CreateMap(string name, string parentId = "", int width = MapDefinition.DefaultWidth, int height = MapDefinition.DefaultHeight,
            string tilesetId = null, MapKind kind = MapKind.Exterior, SectionCategory category = SectionCategory.Route,
            string nextTo = null, FacingDirection side = FacingDirection.Right)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Mapa nuevo";
            var id = Tree.NewId(name);
            tilesetId ??= Map?.TilesetId ?? AvailableTilesets().FirstOrDefault() ?? "";
            var map = new MapDefinition(id, name.Trim(), width, height, tilesetId) { Kind = kind, Category = category };
            if (kind == MapKind.Exterior)
            {
                var world = World();
                var anchor = nextTo == null ? null : Get(nextTo);
                var (x, y) = anchor != null && anchor.InWorld ? world.PlaceNextTo(anchor, side, width, height) : world.FreeSpot(width, height);
                (map.WorldX, map.WorldY, map.InWorld) = (x, y, true);
            }
            else
            {
                map.Outdoor = false;
                map.Bicycle = false;
                map.ShowOnRegionMap = false;
            }
            _maps.Save(map);
            _loaded[id] = map;
            var entry = new MapEntry(id, map.Name, parentId ?? "");
            Tree.Add(entry);
            _treeDirty = true;
            SaveTreeNow();
            TreeChanged?.Invoke();
            WorldChanged?.Invoke();
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
            WorldChanged?.Invoke();
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
            WorldChanged?.Invoke();
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

        /// <summary>Un mapa por id (cargado si hace falta; null si no existe). Lo usan el modo juego y el mundo.</summary>
        public MapDefinition Find(string id)
        {
            try { return Get(id); }
            catch (Exception) { return null; }
        }

        private MapDefinition Get(string id)
        {
            if (id == null) return null;
            if (_loaded.TryGetValue(id, out var m)) return m;
            if (!_maps.Exists(id)) return null;
            m = _maps.Load(id);
            _loaded[id] = m;
            MigrateTilesetIds(m);
            return m;
        }

        /// <summary>Old maps saved the image path of their tileset: switch it to the tileset's fixed id.</summary>
        private void MigrateTilesetIds(MapDefinition m)
        {
            for (int i = 0; i < m.TilesetIds.Count; i++)
            {
                var ts = TilesetFor(m.TilesetIds[i]);
                if (ts != null && ts.Id != m.TilesetIds[i])
                {
                    m.TilesetIds[i] = ts.Id;
                    _dirtyMaps.Add(m.Id);
                }
            }
        }

        /// <summary>Todos los tramos del árbol (cargados).</summary>
        public IEnumerable<MapDefinition> AllMaps() => Tree.Entries.Select(e => Find(e.Id)).Where(m => m != null);

        public bool OpenMap(string id)
        {
            SelectedObject = null;
            _objectDrag = null;
            ObjectDragTo = null;
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
            PaletteSlot = Math.Max(0, Math.Min(PaletteSlot, map.TilesetIds.Count - 1));
            Selection = null;
            ActiveAreaId = map.Encounters.FirstOrDefault()?.Id;
            if (Tool == MapTool.Paste || Tool == MapTool.EncounterPaint && ActiveAreaId == null) Tool = MapTool.Pencil;
            CancelDrag();
            MapOpened?.Invoke();
            SelectionChanged?.Invoke();
            EncountersChanged?.Invoke();
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
            if (t != null && t.Id != id) _tilesetCache[t.Id] = t;
            return t;
        }

        /// <summary>Los tilesets de un mapa por hueco.</summary>
        public MapTilesets TilesetsOf(MapDefinition map) =>
            map == null ? MapTilesets.None : new MapTilesets(map.TilesetIds.Select(TilesetFor).ToList());

        /// <summary>Los del mapa abierto.</summary>
        public MapTilesets Tilesets => TilesetsOf(Map);

        /// <summary>El tileset que muestra la paleta (el del hueco elegido del mapa abierto).</summary>
        public Tileset Tileset => Map == null || PaletteSlot >= Map.TilesetIds.Count ? null : TilesetFor(Map.TilesetIds[PaletteSlot]);

        /// <summary>Vuelve a leer los tilesets (se volvió a cortar una imagen o se cambió desde fuera).</summary>
        public void ReloadTilesets()
        {
            _tilesets.Refresh();
            foreach (var id in _tilesetCache.Keys.Where(k => !_dirtyTilesets.Contains(k)).ToList()) _tilesetCache.Remove(id);
            TilesetChanged?.Invoke(null);
            StructureChanged?.Invoke();
        }

        /// <summary>Muestra en la paleta un tileset del mapa (hueco) o añade uno nuevo al mapa (deshacible).</summary>
        public void UsePaletteTileset(string tilesetId)
        {
            if (Map == null || string.IsNullOrEmpty(tilesetId)) return;
            int slot = Map.SlotOf(tilesetId, add: false);
            if (slot < 0) Structure("añadir tileset", m => m.SlotOf(tilesetId));
            PaletteSlot = Math.Max(0, Map.SlotOf(tilesetId, add: false));
            TilesetChanged?.Invoke(tilesetId);
            SelectionChanged?.Invoke();
        }

        /// <summary>Quita un tileset del mapa si ningún tile lo usa.</summary>
        public void RemoveTilesetFromMap(int slot)
        {
            if (Map == null || slot < 0 || slot >= Map.TilesetIds.Count) return;
            bool used = Map.Layers.Any(l => l.ToArray().Any(c => MapTile.Slot(c) == slot));
            if (used) { Message?.Invoke("Ese tileset se usa en el mapa: borra antes sus tiles.", "aviso"); return; }
            if (Map.TilesetIds.Count == 1) { Message?.Invoke("Un mapa necesita al menos un tileset.", "aviso"); return; }
            // Any tileset can go (if no tile uses it): the tiles of the later ones move down one number.
            Structure("quitar tileset", m =>
            {
                m.TilesetIds.RemoveAt(slot);
                foreach (var layer in m.Layers)
                    for (int y = 0; y < m.Height; y++)
                    for (int x = 0; x < m.Width; x++)
                    {
                        int c = layer.Get(x, y);
                        if (c >= 0 && MapTile.Slot(c) > slot) layer.Set(x, y, MapTile.Encode(MapTile.Slot(c) - 1, MapTile.Index(c)));
                    }
            });
            PaletteSlot = Math.Min(PaletteSlot, Math.Max(0, Map.TilesetIds.Count - 1));
            TilesetChanged?.Invoke(null);
        }

        /// <summary>Cambia propiedades de tiles de un tileset (deshacible; null = el de la paleta).</summary>
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
            if (tool == MapTool.Paste && Clipboard == null) { Message?.Invoke("No hay nada copiado.", "aviso"); return; }
            if (tool == MapTool.EncounterPaint && ActiveArea == null) { Message?.Invoke("Crea o elige una zona de encuentros primero.", "aviso"); return; }
            Tool = tool;
            if (tool != MapTool.Door) { PendingDoor = null; SelectedObject = null; }
            if (tool != MapTool.Select) Selection = null;
            CancelDrag();
            SelectionChanged?.Invoke();
        }

        public void SetStamp(TileStamp stamp)
        {
            Stamp = stamp ?? TileStamp.Single(0);
            if (Tool != MapTool.Pencil && Tool != MapTool.Rectangle && Tool != MapTool.Fill) Tool = MapTool.Pencil;
            SelectionChanged?.Invoke();
        }

        // ── Quick editing (B2): turn/flip the stamp, stored stamps, straight lines ─────────────────

        public void FlipStamp(bool horizontal)
        {
            Stamp = horizontal ? Stamp.FlippedHorizontally() : Stamp.FlippedVertically();
            SelectionChanged?.Invoke();
        }

        public void RotateStamp(bool clockwise = true)
        {
            Stamp = Stamp.Rotated(clockwise);
            SelectionChanged?.Invoke();
        }

        private readonly TileStamp[] _stored = new TileStamp[10];

        /// <summary>Keeps the current stamp in slot 1-9 (Ctrl+Alt+number) to bring it back later (Alt+number).</summary>
        public void StoreStamp(int slot)
        {
            if (slot < 1 || slot > 9) return;
            _stored[slot] = Stamp;
            Message?.Invoke($"Sello guardado en el {slot} (Alt+{slot} lo recupera).", "texto");
        }

        public bool RecallStamp(int slot)
        {
            if (slot < 1 || slot > 9 || _stored[slot] == null) { Message?.Invoke($"No hay sello guardado en el {slot} (Ctrl+Alt+{slot} guarda el actual).", "aviso"); return false; }
            SetStamp(_stored[slot]);
            return true;
        }

        public TileStamp StoredStamp(int slot) => slot >= 1 && slot <= 9 ? _stored[slot] : null;

        /// <summary>Where the pencil last painted (for Shift + click = straight line).</summary>
        public (int x, int y)? LastPaint { get; private set; }

        /// <summary>
        /// Shift + click with the pencil: the stamp along a straight line from the last painted cell to this one, as a
        /// single undo step (like Tiled and GB Studio).
        /// </summary>
        public bool PaintLine(int x, int y)
        {
            if (Map == null || !LastPaint.HasValue || (Tool != MapTool.Pencil && Tool != MapTool.Eraser)) return false;
            var (x0, y0) = LastPaint.Value;
            var stroke = new TileStroke(Map);
            foreach (var (lx, ly) in TileStamp.Line(x0, y0, x, y))
            {
                var changes = PencilAt(lx, ly, CurrentStamp);
                stroke.Add(changes);
                RaiseTiles(changes);
            }
            LastPaint = (x, y);
            if (!stroke.IsEmpty) { MarkDirty(Map.Id); Record(stroke.ToCommand("línea")); }
            return true;
        }

        public void SetActiveLayer(int index)
        {
            if (Map == null) return;
            ActiveLayer = Math.Max(0, Math.Min(Map.Layers.Count - 1, index));
            SelectionChanged?.Invoke();
        }

        public void SetAutoLayers(bool on)
        {
            AutoLayers = on;
            SelectionChanged?.Invoke();
        }

        public void SetDimOtherLayers(bool dim)
        {
            DimOtherLayers = dim;
            SelectionChanged?.Invoke();
        }

        private TileStamp CurrentStamp => Tool == MapTool.Eraser ? TileStamp.Eraser : Stamp;

        private List<TileChange> PencilAt(int x, int y, TileStamp stamp) =>
            AutoLayers ? MapTools.PencilAuto(Map, Tilesets, x, y, stamp, ActiveLayer) : MapTools.Pencil(Map, ActiveLayer, x, y, stamp);

        /// <summary>'secondary' = clic derecho (en las zonas de encuentros, quitar casillas).</summary>
        public void PointerDown(int x, int y, bool secondary = false)
        {
            if (Map == null) return;
            bool paints = Tool == MapTool.Pencil || Tool == MapTool.Rectangle || Tool == MapTool.Fill || Tool == MapTool.Eraser;
            if (paints && !AutoLayers && ActiveLayer < Map.Layers.Count && Map.Layers[ActiveLayer].Locked)
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
                case MapTool.Select:
                    _dragStart = (x, y);
                    DragRect = (x, y, x, y);
                    if (Tool == MapTool.Select) Selection = null;
                    SelectionChanged?.Invoke();
                    break;
                case MapTool.Fill:
                    Commit(AutoLayers ? MapTools.FillAuto(Map, Tilesets, x, y, Stamp, ActiveLayer) : MapTools.Fill(Map, ActiveLayer, x, y, Stamp), "rellenar");
                    break;
                case MapTool.PlayerStart:
                    if (Map.Contains(x, y)) SetPlayerStart(Map.Id, x, y);
                    break;
                case MapTool.Door:
                    DoorPointerDown(x, y);
                    break;
                case MapTool.Paste:
                    if (Clipboard == null) return;
                    int before = Map.TilesetIds.Count;
                    var changes = Clipboard.PasteChanges(Map, x, y);
                    if (Map.TilesetIds.Count != before) { MarkDirty(Map.Id); TilesetChanged?.Invoke(null); StructureChanged?.Invoke(); }
                    Commit(changes, "pegar");
                    break;
                case MapTool.EncounterPaint:
                    if (ActiveArea == null) return;
                    _areaAdded = new List<(int, int)>();
                    _areaRemoved = new List<(int, int)>();
                    _areaErasing = secondary;
                    AreaAt(x, y);
                    break;
            }
        }

        public void PointerDrag(int x, int y)
        {
            if (Map == null) return;
            if (_stroke != null) StrokeAt(x, y);
            else if (_areaAdded != null) AreaAt(x, y);
            else if (_objectDrag != null)
            {
                ObjectDragTo = Map.Contains(x, y) ? (x, y) : ObjectDragTo;
                ObjectsChanged?.Invoke();
            }
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
            else if (_objectDrag != null)
            {
                var moved = _objectDrag;
                _objectDrag = null;
                var to = ObjectDragTo;
                ObjectDragTo = null;
                if (to.HasValue && (to.Value.x != moved.X || to.Value.y != moved.Y)) MoveObject(moved, to.Value.x, to.Value.y);
                else ObjectsChanged?.Invoke();
            }
            else if (_areaAdded != null)
            {
                var cmd = new EncounterPaintCommand(ActiveArea, _areaAdded, _areaRemoved);
                _areaAdded = _areaRemoved = null;
                if (!cmd.IsEmpty) History.Record(new Tracked(cmd, Map.Id, null, this, encounters: true));
            }
            else if (_dragStart.HasValue)
            {
                var (x0, y0) = _dragStart.Value;
                _dragStart = null;
                DragRect = null;
                if (Tool == MapTool.Rectangle)
                    Commit(AutoLayers ? MapTools.RectangleAuto(Map, Tilesets, x0, y0, x, y, Stamp, ActiveLayer)
                        : MapTools.Rectangle(Map, ActiveLayer, x0, y0, x, y, Stamp), "rectángulo");
                else if (Tool == MapTool.Picker) SetStamp(MapTools.Pick(Map, AutoLayers ? -1 : ActiveLayer, x0, y0, x, y));
                else if (Tool == MapTool.Select) Selection = Clamp(x0, y0, x, y);
                SelectionChanged?.Invoke();
            }
        }

        private (int, int, int, int)? Clamp(int x0, int y0, int x1, int y1)
        {
            int minX = Math.Max(0, Math.Min(x0, x1)), maxX = Math.Min(Map.Width - 1, Math.Max(x0, x1));
            int minY = Math.Max(0, Math.Min(y0, y1)), maxY = Math.Min(Map.Height - 1, Math.Max(y0, y1));
            return minX > maxX || minY > maxY ? ((int, int, int, int)?)null : (minX, minY, maxX, maxY);
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
            if (_areaAdded != null)
            {
                new EncounterPaintCommand(ActiveArea, _areaAdded, _areaRemoved).Undo();
                _areaAdded = _areaRemoved = null;
                EncountersChanged?.Invoke();
            }
            _dragStart = null;
            DragRect = null;
        }

        private void StrokeAt(int x, int y)
        {
            LastPaint = (x, y);
            var changes = PencilAt(x, y, CurrentStamp);
            if (changes.Count == 0) return;
            _stroke.Add(changes);
            RaiseTiles(changes);
            MarkDirty(Map.Id);
        }

        private void AreaAt(int x, int y)
        {
            if (!Map.Contains(x, y)) return;
            var area = ActiveArea;
            if (_areaErasing) { if (area.Erase(x, y)) _areaRemoved.Add((x, y)); }
            else if (area.Paint(x, y)) _areaAdded.Add((x, y));
            MarkDirty(Map.Id);
            EncountersChanged?.Invoke();
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

        // ── Selection and clipboard ──────────────────────────────────────────────────────────────

        public bool Copy()
        {
            if (Map == null || !Selection.HasValue) { Message?.Invoke("Selecciona antes una zona (herramienta Selección).", "aviso"); return false; }
            var (x0, y0, x1, y1) = Selection.Value;
            Clipboard = MapClipboard.Copy(Map, x0, y0, x1, y1);
            Message?.Invoke($"Copiado: {Clipboard.Width} × {Clipboard.Height}. Ctrl+V para pegar.", "texto");
            return true;
        }

        public void Cut()
        {
            if (!Copy()) return;
            DeleteSelection("cortar");
        }

        /// <summary>Selecciona el mapa entero (pasa a la herramienta Selección).</summary>
        public void SelectAll()
        {
            if (Map == null) return;
            if (Tool != MapTool.Select) SetTool(MapTool.Select);
            Selection = (0, 0, Map.Width - 1, Map.Height - 1);
            SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            if (Selection == null) return;
            Selection = null;
            SelectionChanged?.Invoke();
        }

        public void DeleteSelection(string label = "borrar zona")
        {
            if (Map == null || !Selection.HasValue) return;
            var (x0, y0, x1, y1) = Selection.Value;
            Commit(MapTools.Clear(Map, x0, y0, x1, y1), label);
        }

        /// <summary>Pasa a la herramienta Pegar (el clic coloca lo copiado).</summary>
        public void BeginPaste() => SetTool(MapTool.Paste);

        // ── Doors (B3): created in pairs, linked by id ──────────────────────────────────────────────

        /// <summary>The first end of a door being made (the next click, in this map or another, makes its exit).</summary>
        public (string map, int x, int y)? PendingDoor { get; private set; }
        /// <summary>The chosen object (a door) in the open map, for its properties.</summary>
        public MapObject SelectedObject { get; private set; }
        /// <summary>While dragging an object: the cell under the pointer.</summary>
        public (int x, int y)? ObjectDragTo { get; private set; }
        private MapObject _objectDrag;

        private void DoorPointerDown(int x, int y)
        {
            var door = Doors.At(Map, x, y);
            if (door != null)
            {
                SelectedObject = door;
                _objectDrag = door;
                ObjectDragTo = (x, y);
                ObjectsChanged?.Invoke();
                return;
            }
            if (!Map.Contains(x, y)) return;
            if (PendingDoor.HasValue)
            {
                var (pm, px, py) = PendingDoor.Value;
                var first = Find(pm);
                PendingDoor = null;
                if (first == null || CreateDoorPair(first, px, py, Map, x, y) == null)
                    Message?.Invoke("No se pudo crear la puerta (¿ya hay una en esa casilla?).", "aviso");
                return;
            }
            PendingDoor = (Map.Id, x, y);
            SelectedObject = null;
            Message?.Invoke("Puerta marcada. Ahora haz clic donde sale: en otro mapa (ábrelo en Mapas) o en este. Esc cancela.", "texto");
            ObjectsChanged?.Invoke();
        }

        public void CancelPendingDoor()
        {
            if (!PendingDoor.HasValue) return;
            PendingDoor = null;
            ObjectsChanged?.Invoke();
        }

        public void SelectObject(MapObject o)
        {
            SelectedObject = o;
            ObjectsChanged?.Invoke();
        }

        /// <summary>Both doors at once, linked, as one undo step.</summary>
        public (MapObject a, MapObject b)? CreateDoorPair(MapDefinition mapA, int ax, int ay, MapDefinition mapB, int bx, int by)
        {
            (MapObject a, MapObject b)? pair = null;
            var cmd = MapObjectsCommand.Run("puerta", new[] { mapA, mapB }, () => pair = Doors.CreatePair(mapA, ax, ay, mapB, bx, by));
            if (pair == null) return null;
            RecordObjects(cmd);
            SelectedObject = Map == mapB ? pair.Value.b : Map == mapA ? pair.Value.a : null;
            ObjectsChanged?.Invoke();
            Message?.Invoke($"Puertas enlazadas: «{mapA.Name}» ({ax}, {ay}) ↔ «{mapB.Name}» ({bx}, {by}).", "exito");
            return pair;
        }

        /// <summary>
        /// A door here and a NEW interior behind it (a house, a shop), created together: the interior opens with its exit
        /// mat at the bottom middle, linked to this door.
        /// </summary>
        public MapDefinition CreateDoorWithInterior(int x, int y, string name, int width, int height, string tilesetId = null,
            SectionCategory category = SectionCategory.Building)
        {
            if (Map == null || !Map.Contains(x, y) || Doors.At(Map, x, y) != null) return null;
            var outside = Map;
            var entry = CreateMap(name, Tree.Find(outside.Id)?.ParentId ?? "", width, height, tilesetId, MapKind.Interior, category);
            var inside = Find(entry.Id);
            if (inside == null) return null;
            CreateDoorPair(outside, x, y, inside, width / 2, height - 1);
            return inside;
        }

        public void MoveObject(MapObject o, int x, int y)
        {
            var map = AllLoaded().FirstOrDefault(m => m.Objects.Contains(o));
            if (map == null || !map.Contains(x, y)) return;
            if (Doors.IsDoor(o) && Doors.At(map, x, y) is MapObject other && other != o)
            {
                Message?.Invoke("Ya hay una puerta en esa casilla.", "aviso");
                ObjectsChanged?.Invoke();
                return;
            }
            string id = o.Id;
            RecordObjects(MapObjectsCommand.Run("mover puerta", new[] { map }, () => { o.X = x; o.Y = y; }));
            SelectedObject = map.Objects.FirstOrDefault(m => m.Id == id);
            ObjectsChanged?.Invoke();
        }

        /// <summary>Removes a door; its other end stays but no longer leads anywhere (Problems says so), or goes too.</summary>
        public void RemoveDoor(MapObject door, bool alsoTheOther = false)
        {
            var map = AllLoaded().FirstOrDefault(m => m.Objects.Contains(door));
            if (map == null) return;
            var partner = Doors.Partner(door, Find);
            var maps = new List<MapDefinition> { map };
            if (partner.HasValue) maps.Add(partner.Value.map);
            RecordObjects(MapObjectsCommand.Run(alsoTheOther ? "quitar puertas" : "quitar puerta", maps, () =>
            {
                map.Objects.Remove(door);
                if (!partner.HasValue) return;
                if (alsoTheOther) partner.Value.map.Objects.Remove(partner.Value.door);
                else Doors.Unlink(partner.Value.door);
            }));
            if (SelectedObject == door) SelectedObject = null;
            ObjectsChanged?.Invoke();
        }

        /// <summary>Changes a door (name, exit direction...) as one undo step.</summary>
        public void ChangeObject(MapObject o, string label, Action<MapObject> change)
        {
            var map = AllLoaded().FirstOrDefault(m => m.Objects.Contains(o));
            if (map == null) return;
            string id = o.Id;
            RecordObjects(MapObjectsCommand.Run(label, new[] { map }, () => change(o)));
            SelectedObject = map.Objects.FirstOrDefault(m => m.Id == id);
            ObjectsChanged?.Invoke();
        }

        /// <summary>Links a door that lost its other end to another door (both point to each other).</summary>
        public void RelinkDoor(MapObject door, MapDefinition otherMap, MapObject other)
        {
            var map = AllLoaded().FirstOrDefault(m => m.Objects.Contains(door));
            if (map == null || otherMap == null || other == null || door == other) return;
            RecordObjects(MapObjectsCommand.Run("enlazar puertas", new[] { map, otherMap }, () => Doors.Link(map, door, otherMap, other)));
            ObjectsChanged?.Invoke();
        }

        private IEnumerable<MapDefinition> AllLoaded() => _loaded.Values.Where(m => m != null);

        private void RecordObjects(MapObjectsCommand cmd)
        {
            foreach (var m in cmd.Maps) MarkDirty(m.Id);
            History.Record(new TrackedObjects(cmd, this));
        }

        private sealed class TrackedObjects : IEditCommand
        {
            private readonly MapObjectsCommand _inner;
            private readonly MapEditorSession _s;
            public TrackedObjects(MapObjectsCommand inner, MapEditorSession s) { _inner = inner; _s = s; }
            public string Label => _inner.Label;
            public void Do() { _inner.Do(); After(); }
            public void Undo() { _inner.Undo(); After(); }

            private void After()
            {
                foreach (var m in _inner.Maps) _s.MarkDirty(m.Id);
                // The objects were replaced by copies: keep the chosen one by id.
                var sel = _s.SelectedObject;
                _s.SelectedObject = sel == null || _s.Map == null ? null : _s.Map.Objects.FirstOrDefault(o => o.Id == sel.Id && o.Kind == sel.Kind);
                _s.ObjectsChanged?.Invoke();
            }
        }

        // ── Player start ─────────────────────────────────────────────────────────────────────────

        public void SetPlayerStart(string mapId, int x, int y)
        {
            // Only one start in the whole world: move the object.
            var old = PlayerStart.map;
            foreach (var m in new[] { Find(old), Find(mapId) }.Where(m => m != null).Distinct())
                if (m.Objects.RemoveAll(o => o.Kind == MapObject.PlayerStartKind) > 0) MarkDirty(m.Id);
            var target = Find(mapId);
            if (target != null)
            {
                target.Objects.Add(new MapObject("inicio", MapObject.PlayerStartKind, x, y, "Inicio del jugador"));
                MarkDirty(target.Id);
            }
            PlayerStart = (mapId ?? "", x, y);
            PlayerStartChanged?.Invoke();
        }

        /// <summary>Carga el inicio guardado en el proyecto (sin avisar de cambios).</summary>
        public void LoadPlayerStart(string mapId, int x, int y) => PlayerStart = (mapId ?? "", x, y);

        // ── Structure ────────────────────────────────────────────────────────────────────────────

        private void Structure(string label, Action<MapDefinition> change, bool encounters = false)
        {
            if (Map == null) return;
            CancelDrag();
            var cmd = MapStructureCommand.Run(Map, label, change);
            History.Record(new Tracked(cmd, Map.Id, null, this, encounters: encounters));
            MarkDirty(Map.Id);
            ActiveLayer = Math.Max(0, Math.Min(ActiveLayer, Map.Layers.Count - 1));
            if (encounters) EncountersChanged?.Invoke();
            else
            {
                StructureChanged?.Invoke();
                SelectionChanged?.Invoke();
            }
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

        public void SetLayerRole(int index, LayerRole role)
        {
            if (Map == null || index < 0 || index >= Map.Layers.Count) return;
            Structure("papel de la capa", m =>
            {
                if (role != LayerRole.Custom) foreach (var l in m.Layers.Where(l => l.Role == role)) l.Role = LayerRole.Custom;
                m.Layers[index].Role = role;
            });
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
            LayerViewChanged?.Invoke(index);
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
            WorldChanged?.Invoke();
        }

        /// <summary>Cambia el tileset principal (hueco 0).</summary>
        public void SetMapTileset(string tilesetId)
        {
            if (Map == null || Map.TilesetId == tilesetId) return;
            Structure("cambiar tileset", m => m.TilesetId = tilesetId ?? "");
            TilesetChanged?.Invoke(tilesetId);
        }

        public void SetMapProperties(string music = null, bool? bicycle = null, bool? outdoor = null, string weather = null,
            MapKind? kind = null, SectionCategory? category = null, bool? showOnRegionMap = null)
        {
            if (Map == null) return;
            Structure("propiedades del mapa", m =>
            {
                if (music != null) m.Music = music;
                if (bicycle.HasValue) m.Bicycle = bicycle.Value;
                if (outdoor.HasValue) m.Outdoor = outdoor.Value;
                if (weather != null) m.Weather = weather;
                if (category.HasValue) m.Category = category.Value;
                if (showOnRegionMap.HasValue) m.ShowOnRegionMap = showOnRegionMap.Value;
                if (kind.HasValue && m.Kind != kind.Value)
                {
                    m.Kind = kind.Value;
                    if (kind.Value == MapKind.Interior) m.InWorld = false;
                }
            });
            if (kind.HasValue || category.HasValue || showOnRegionMap.HasValue) WorldChanged?.Invoke();
        }

        // ── World ────────────────────────────────────────────────────────────────────────────────

        /// <summary>El mundo continuo con los tramos exteriores colocados.</summary>
        public WorldLayout World() => new WorldLayout(AllMaps());

        /// <summary>Mueve (o coloca) un tramo exterior en el mundo (deshacible).</summary>
        public void MoveSection(string mapId, int worldX, int worldY)
        {
            var m = Find(mapId);
            if (m == null) return;
            if (m.Kind != MapKind.Exterior) { Message?.Invoke("Solo los exteriores van en el mundo; los interiores se enlazan con puertas.", "aviso"); return; }
            if (Tree.Find(mapId)?.LockedInWorld == true) { Message?.Invoke($"«{m.Name}» está bloqueado en el mundo.", "aviso"); return; }
            if (m.InWorld && m.WorldX == worldX && m.WorldY == worldY) return;
            Run(new Tracked(new MoveSectionCommand(m, worldX, worldY), m.Id, null, this, world: true));
            var clash = World().Overlaps().FirstOrDefault(p => p.a == m || p.b == m);
            if (clash.a != null) Message?.Invoke($"«{clash.a.Name}» y «{clash.b.Name}» se pisan: sepáralos para que se pueda pasar de uno a otro.", "aviso");
        }

        /// <summary>Saca un tramo del mundo (queda suelto hasta que se vuelva a colocar).</summary>
        public void RemoveFromWorld(string mapId)
        {
            var m = Find(mapId);
            if (m == null || !m.InWorld) return;
            var before = new MapSnapshot(m);
            m.InWorld = false;
            var after = new MapSnapshot(m);
            History.Record(new Tracked(new SnapshotCommand(m, "quitar del mundo", before, after), m.Id, null, this, world: true));
            MarkDirty(m.Id);
            WorldChanged?.Invoke();
        }

        /// <summary>Ocultar / bloquear un tramo en la vista del mundo, para centrarse en otros (como las capas).</summary>
        public void SetSectionView(string mapId, bool? hidden = null, bool? locked = null)
        {
            var e = Tree.Find(mapId);
            if (e == null) return;
            if (hidden.HasValue) e.HiddenInWorld = hidden.Value;
            if (locked.HasValue) e.LockedInWorld = locked.Value;
            _treeDirty = true;
            DirtyChanged?.Invoke();
            WorldChanged?.Invoke();
        }

        /// <summary>Solo este tramo: oculta todos los demás (o los vuelve a mostrar si ya estaba solo).</summary>
        public void Solo(string mapId)
        {
            bool alreadySolo = Tree.Entries.All(e => e.Id == mapId ? !e.HiddenInWorld : e.HiddenInWorld);
            foreach (var e in Tree.Entries) e.HiddenInWorld = !alreadySolo && e.Id != mapId;
            _treeDirty = true;
            DirtyChanged?.Invoke();
            WorldChanged?.Invoke();
        }

        /// <summary>El mapa de la región a partir del mundo (imagen + dónde queda cada tramo).</summary>
        public (PixelImage image, List<RegionMapArea> areas) BuildRegionMap(RegionMapStyle style, int tilesPerPixel)
        {
            var world = World();
            Rgba32? ColorAt(MapDefinition m, int x, int y)
            {
                int cell = m.TopTile(x, y, out _);
                var ts = TilesetsOf(m).For(cell);
                int i = MapTile.Index(cell);
                if (ts?.AverageColors == null || i < 0 || i >= ts.AverageColors.Length) return null;
                var c = ts.AverageColors[i];
                return c.IsTransparent ? (Rgba32?)null : c;
            }
            return RegionMapBuilder.Build(world, style, tilesPerPixel, ColorAt);
        }

        // ── Encounters ───────────────────────────────────────────────────────────────────────────

        public EncounterArea ActiveArea => Map?.Encounters.FirstOrDefault(a => a.Id == ActiveAreaId);

        /// <summary>Resaltar en el mapa todas las zonas de encuentros (true) o solo la elegida (false).</summary>
        public bool HighlightAllAreas { get; private set; }

        public void SetHighlightAllAreas(bool all)
        {
            HighlightAllAreas = all;
            EncountersChanged?.Invoke();
        }

        /// <summary>Deja de pintar la zona (vuelve al lápiz).</summary>
        public void StopAreaPainting()
        {
            if (Tool == MapTool.EncounterPaint) SetTool(MapTool.Pencil);
        }

        public void SetActiveArea(string id)
        {
            ActiveAreaId = Map?.Encounters.Any(a => a.Id == id) == true ? id : null;
            if (ActiveAreaId == null && Tool == MapTool.EncounterPaint) Tool = MapTool.Pencil;
            SelectionChanged?.Invoke();
            EncountersChanged?.Invoke();
        }

        /// <summary>Nueva zona: de todo el mapa (la tabla general del tramo) o para pintar.</summary>
        public EncounterArea AddArea(string name, bool wholeMap)
        {
            if (Map == null) return null;
            string id = null;
            string[] palette = { "#E0B34A", "#5FB865", "#4E8CF7", "#E0605A", "#B070E0", "#40C0C0", "#E08040" };
            Structure("nueva zona de encuentros", m =>
            {
                id = "zona_" + (m.Encounters.Count + 1);
                while (m.Encounters.Any(a => a.Id == id)) id += "_";
                m.Encounters.Add(new EncounterArea(id, string.IsNullOrWhiteSpace(name) ? (wholeMap ? "Todo el tramo" : "Zona") : name.Trim(), wholeMap)
                    { Color = palette[m.Encounters.Count % palette.Length] });
            }, encounters: true);
            SetActiveArea(id);
            if (!wholeMap) SetTool(MapTool.EncounterPaint);
            return ActiveArea;
        }

        public void RemoveArea(string id) => EditArea(id, "quitar zona", (m, a) => m.Encounters.Remove(a));
        public void RenameArea(string id, string name) => EditArea(id, "renombrar zona", (m, a) => { if (!string.IsNullOrWhiteSpace(name)) a.Name = name.Trim(); });
        public void SetAreaWholeMap(string id, bool whole) => EditArea(id, "zona de todo el mapa", (m, a) => a.WholeMap = whole);
        public void SetAreaColor(string id, string color) => EditArea(id, "color de zona", (m, a) => a.Color = color);

        public void AddTable(string areaId, string methodId) =>
            EditArea(areaId, "añadir método", (m, a) => a.GetOrAddTable(methodId));

        public void RemoveTable(string areaId, string methodId) =>
            EditArea(areaId, "quitar método", (m, a) => a.Tables.RemoveAll(t => t.MethodId == methodId));

        /// <summary>Rellena una zona con una plantilla (solo las especies que estén en los datos del proyecto, si hay).</summary>
        public void ApplyTemplate(string areaId, EncounterTemplate template)
        {
            if (template == null) return;
            var known = new HashSet<string>((Species?.All() ?? new (string id, string name)[0]).Select(x => x.id), StringComparer.OrdinalIgnoreCase);
            EditArea(areaId, "plantilla de zona", (m, a) => template.ApplyTo(a, Methods, known.Count == 0 ? (Func<string, bool>)null : known.Contains));
        }

        public void SetTableDoubles(string areaId, string methodId, int percent) =>
            EditArea(areaId, "combates dobles", (m, a) => a.GetOrAddTable(methodId).DoublePercent = Math.Max(0, Math.Min(100, percent)));

        /// <summary>Copia una especie justo debajo (para variantes: otra hora, otra forma...).</summary>
        public void DuplicateSlot(string areaId, string methodId, int index) =>
            EditArea(areaId, "duplicar especie", (m, a) =>
            {
                var t = a.TableFor(methodId);
                if (t != null && index >= 0 && index < t.Slots.Count) t.Slots.Insert(index + 1, t.Slots[index].Clone());
            });

        public void MoveSlot(string areaId, string methodId, int index, int delta) =>
            EditArea(areaId, "ordenar especies", (m, a) =>
            {
                var t = a.TableFor(methodId);
                int to = index + delta;
                if (t == null || index < 0 || index >= t.Slots.Count || to < 0 || to >= t.Slots.Count) return;
                var s = t.Slots[index];
                t.Slots.RemoveAt(index);
                t.Slots.Insert(to, s);
            });

        public void SetTableRate(string areaId, string methodId, int rate) =>
            EditArea(areaId, "probabilidad", (m, a) => a.GetOrAddTable(methodId).Rate = Math.Max(-1, Math.Min(100, rate)));

        public void AddSlot(string areaId, string methodId, EncounterSlot slot) =>
            EditArea(areaId, "añadir especie", (m, a) => a.GetOrAddTable(methodId).Slots.Add(slot));

        public void UpdateSlot(string areaId, string methodId, int index, Action<EncounterSlot> change) =>
            EditArea(areaId, "cambiar especie", (m, a) =>
            {
                var t = a.TableFor(methodId);
                if (t != null && index >= 0 && index < t.Slots.Count) change(t.Slots[index]);
            });

        public void RemoveSlot(string areaId, string methodId, int index) =>
            EditArea(areaId, "quitar especie", (m, a) =>
            {
                var t = a.TableFor(methodId);
                if (t != null && index >= 0 && index < t.Slots.Count) t.Slots.RemoveAt(index);
            });

        /// <summary>Pone a la tabla los pesos clásicos de los juegos (hierba 20/20/10...; agua 60/30/5/4/1; cañas 70/30).</summary>
        public void ApplyClassicWeights(string areaId, string methodId) =>
            EditArea(areaId, "pesos clásicos", (m, a) =>
            {
                var t = a.TableFor(methodId);
                if (t == null) return;
                var w = methodId.StartsWith("cana") || methodId == "supercana" ? EncounterTable.ClassicRodWeights
                    : methodId == "surf" || methodId == "buceo" ? EncounterTable.ClassicWaterWeights
                    : EncounterTable.ClassicGrassWeights;
                for (int i = 0; i < t.Slots.Count; i++) t.Slots[i].Weight = i < w.Length ? w[i] : 1;
            });

        private void EditArea(string areaId, string label, Action<MapDefinition, EncounterArea> change)
        {
            if (Map == null) return;
            var area = Map.Encounters.FirstOrDefault(a => a.Id == areaId);
            if (area == null) return;
            Structure(label, m => change(m, m.Encounters.First(a => a.Id == areaId)), encounters: true);
            if (Map.Encounters.All(a => a.Id != ActiveAreaId)) SetActiveArea(Map.Encounters.FirstOrDefault()?.Id);
        }

        // ── Encounter methods (project) ──────────────────────────────────────────────────────────

        public void SaveMethod(EncounterMethod method)
        {
            Methods.Add(method);
            PersistMethods();
        }

        public bool RemoveMethod(string id)
        {
            if (!Methods.Remove(id)) { Message?.Invoke("Los métodos de fábrica no se borran (sí se pueden cambiar).", "aviso"); return false; }
            PersistMethods();
            return true;
        }

        private void PersistMethods()
        {
            try { _methodRepo?.Save(Methods); }
            catch (Exception e) { Message?.Invoke("No se pudieron guardar los métodos: " + e.Message, "error"); }
            MethodsChanged?.Invoke();
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

        /// <summary>Snapshot-based command (before/after) for changes made outside Structure().</summary>
        private sealed class SnapshotCommand : IEditCommand
        {
            private readonly MapDefinition _map;
            private readonly MapSnapshot _before, _after;
            public string Label { get; }
            public SnapshotCommand(MapDefinition map, string label, MapSnapshot before, MapSnapshot after) { _map = map; Label = label; _before = before; _after = after; }
            public void Do() => _after.RestoreInto(_map);
            public void Undo() => _before.RestoreInto(_map);
        }

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
            private readonly bool _encounters, _world;

            public Tracked(IEditCommand inner, string map, string tileset, MapEditorSession s, IReadOnlyCollection<TileChange> tiles = null,
                bool encounters = false, bool world = false)
            {
                _inner = inner; _map = map; _tileset = tileset; _s = s; _tiles = tiles; _encounters = encounters; _world = world;
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
                if (_world) _s.WorldChanged?.Invoke();
                if (_map == null || _s.Map?.Id != _map) return;
                if (_tiles != null) _s.RaiseTiles(_tiles);
                else if (_encounters) _s.EncountersChanged?.Invoke();
                else if (!_world)
                {
                    _s.ActiveLayer = Math.Max(0, Math.Min(_s.ActiveLayer, _s.Map.Layers.Count - 1));
                    _s.StructureChanged?.Invoke();
                }
            }
        }
    }
}
