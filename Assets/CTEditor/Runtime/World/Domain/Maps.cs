using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.World.Domain
{
    /// <summary>Para qué es una capa: las capas automáticas mandan cada tile a la capa de su tipo de pieza.</summary>
    public enum LayerRole
    {
        /// <summary>Capa normal: solo se pinta en ella a mano.</summary>
        Custom = 0,
        Ground = 1,
        Detail = 2,
        Above = 3
    }

    /// <summary>Una capa del mapa: una rejilla de casillas (ver MapTile; -1 = vacía).</summary>
    public sealed class MapLayer
    {
        public const int Empty = -1;

        public string Name { get; set; }
        public LayerRole Role { get; set; }
        public bool Visible { get; set; } = true;
        /// <summary>Bloqueada: las herramientas no la cambian.</summary>
        public bool Locked { get; set; }
        /// <summary>0-1, solo para verla en el editor.</summary>
        public float Opacity { get; set; } = 1f;

        public int Width { get; private set; }
        public int Height { get; private set; }
        private int[] _tiles;

        public MapLayer(string name, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "El mapa debe medir al menos 1 × 1.");
            Name = name;
            Width = width;
            Height = height;
            _tiles = Enumerable.Repeat(Empty, width * height).ToArray();
        }

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public int Get(int x, int y) => Contains(x, y) ? _tiles[y * Width + x] : Empty;

        public void Set(int x, int y, int tile)
        {
            if (Contains(x, y)) _tiles[y * Width + x] = tile < 0 ? Empty : tile;
        }

        /// <summary>Todas las casillas, fila a fila (para guardar).</summary>
        public int[] ToArray() => (int[])_tiles.Clone();

        public void Load(int[] tiles)
        {
            if (tiles == null || tiles.Length != Width * Height)
                throw new ArgumentException($"La capa «{Name}» debería tener {Width * Height} casillas.", nameof(tiles));
            for (int i = 0; i < tiles.Length; i++) _tiles[i] = tiles[i] < 0 ? Empty : tiles[i];
        }

        /// <summary>Cambia el tamaño. offsetX/offsetY: dónde queda lo que había (positivo = se añade espacio a la izquierda / arriba).</summary>
        public void Resize(int width, int height, int offsetX, int offsetY)
        {
            var old = _tiles;
            int ow = Width, oh = Height;
            _tiles = Enumerable.Repeat(Empty, width * height).ToArray();
            Width = width;
            Height = height;
            for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int nx = x + offsetX, ny = y + offsetY;
                if (Contains(nx, ny)) _tiles[ny * width + nx] = old[y * ow + x];
            }
        }

        public MapLayer Clone()
        {
            var c = new MapLayer(Name, Width, Height) { Visible = Visible, Locked = Locked, Opacity = Opacity, Role = Role };
            c._tiles = (int[])_tiles.Clone();
            return c;
        }
    }

    /// <summary>Exterior = un tramo del mundo continuo (pueblo, ruta...); interior = casa, cueva, edificio (se entra por puertas).</summary>
    public enum MapKind
    {
        Exterior = 0,
        Interior = 1
    }

    /// <summary>Tipo de tramo: para el mapa de la región, los colores del mundo y los puntos de vuelo.</summary>
    public enum SectionCategory
    {
        Town = 0,
        City = 1,
        Route = 2,
        Forest = 3,
        Cave = 4,
        Water = 5,
        Mountain = 6,
        Building = 7,
        Special = 8
    }

    /// <summary>
    /// Algo colocado en el mapa que no es un tile: el inicio del jugador, puertas, carteles, NPC... Cada tipo (Kind) es
    /// un texto, así los módulos nuevos (eventos, NPC) añaden los suyos sin tocar el modelo. Propiedades = texto libre.
    /// </summary>
    public sealed class MapObject
    {
        public const string PlayerStartKind = "inicio";

        public string Id { get; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public MapObject(string id, string kind, int x, int y, string name = null)
        {
            Id = id; Kind = kind; X = x; Y = y; Name = name ?? kind;
        }

        public bool Covers(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

        public MapObject Clone()
        {
            var o = new MapObject(Id, Kind, X, Y, Name) { Width = Width, Height = Height };
            foreach (var kv in Properties) o.Properties[kv.Key] = kv.Value;
            return o;
        }
    }

    /// <summary>
    /// Un mapa = un TRAMO (pueblo, ruta, cueva...) con su nombre, sus límites, su música y sus encuentros. Los exteriores
    /// tienen además una posición en el mundo: juntos forman el mundo continuo y se recorren sin cargas; los interiores
    /// van aparte. Se pinta con uno o varios tilesets; por defecto tres capas (suelo, detalles, encima) como RPG Maker XP.
    /// </summary>
    public sealed class MapDefinition
    {
        public const int DefaultWidth = 20, DefaultHeight = 15; // RPG Maker XP
        public const int MaxSize = 500;

        public string Id { get; }
        public string Name { get; set; }
        /// <summary>Tilesets del mapa por hueco (ver MapTile). El primero es el principal.</summary>
        public List<string> TilesetIds { get; } = new List<string>();
        public int Width { get; private set; }
        public int Height { get; private set; }
        public List<MapLayer> Layers { get; } = new List<MapLayer>();
        public List<MapObject> Objects { get; } = new List<MapObject>();
        /// <summary>Zonas de encuentros salvajes del tramo (ver EncounterArea).</summary>
        public List<EncounterArea> Encounters { get; } = new List<EncounterArea>();

        public MapKind Kind { get; set; } = MapKind.Exterior;
        public SectionCategory Category { get; set; } = SectionCategory.Route;
        /// <summary>Colocado en el mundo continuo (solo exteriores). Si no, es un tramo suelto hasta que se coloque.</summary>
        public bool InWorld { get; set; }
        /// <summary>Esquina de arriba a la izquierda en el mundo, en tiles.</summary>
        public int WorldX { get; set; }
        public int WorldY { get; set; }
        /// <summary>Sale en el mapa de la región.</summary>
        public bool ShowOnRegionMap { get; set; } = true;

        /// <summary>Música del tramo (ruta relativa a «audio/musica», vacío = ninguna).</summary>
        public string Music { get; set; } = "";
        /// <summary>Clima del tramo (id de clima; vacío = despejado).</summary>
        public string Weather { get; set; } = "";
        /// <summary>Se puede usar la bici.</summary>
        public bool Bicycle { get; set; } = true;
        /// <summary>Es exterior (se puede volar, afecta la hora del día).</summary>
        public bool Outdoor { get; set; } = true;

        public MapDefinition(string id, string name, int width, int height, string tilesetId = "", bool withDefaultLayers = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El mapa necesita un id.", nameof(id));
            if (width <= 0 || height <= 0 || width > MaxSize || height > MaxSize)
                throw new ArgumentOutOfRangeException(nameof(width), $"El mapa debe medir entre 1 y {MaxSize} tiles por lado.");
            Id = id;
            Name = name ?? id;
            Width = width;
            Height = height;
            if (!string.IsNullOrEmpty(tilesetId)) TilesetIds.Add(tilesetId);
            if (withDefaultLayers)
                foreach (var (n, role) in new[] { ("Suelo", LayerRole.Ground), ("Detalles", LayerRole.Detail), ("Encima", LayerRole.Above) })
                    Layers.Add(new MapLayer(n, width, height) { Role = role });
        }

        /// <summary>El tileset principal (hueco 0). Asignarlo cambia el hueco 0 (los tiles siguen apuntando a ese hueco).</summary>
        public string TilesetId
        {
            get => TilesetIds.Count > 0 ? TilesetIds[0] : "";
            set
            {
                if (TilesetIds.Count == 0) { if (!string.IsNullOrEmpty(value)) TilesetIds.Add(value); }
                else TilesetIds[0] = value ?? "";
            }
        }

        /// <summary>Hueco de un tileset en este mapa (lo añade si no está). -1 si no caben más.</summary>
        public int SlotOf(string tilesetId, bool add = true)
        {
            int i = TilesetIds.IndexOf(tilesetId);
            if (i >= 0 || !add) return i;
            if (TilesetIds.Count >= MapTile.MaxSlots) return -1;
            TilesetIds.Add(tilesetId);
            return TilesetIds.Count - 1;
        }

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>Rectángulo del tramo en el mundo (x, y, ancho, alto) en tiles.</summary>
        public (int x, int y, int w, int h) WorldRect => (WorldX, WorldY, Width, Height);

        public MapLayer AddLayer(string name, int index = -1, LayerRole role = LayerRole.Custom)
        {
            var l = new MapLayer(name, Width, Height) { Role = role };
            if (index < 0 || index > Layers.Count) Layers.Add(l);
            else Layers.Insert(index, l);
            return l;
        }

        /// <summary>La primera capa con ese papel (-1 si no hay).</summary>
        public int LayerFor(LayerRole role) => Layers.FindIndex(l => l.Role == role);

        /// <summary>Cambia el tamaño de todas las capas (ancla: dónde se añade o quita espacio). Objetos y zonas se mueven igual.</summary>
        public void Resize(int width, int height, int offsetX = 0, int offsetY = 0)
        {
            if (width <= 0 || height <= 0 || width > MaxSize || height > MaxSize)
                throw new ArgumentOutOfRangeException(nameof(width), $"El mapa debe medir entre 1 y {MaxSize} tiles por lado.");
            foreach (var l in Layers) l.Resize(width, height, offsetX, offsetY);
            foreach (var o in Objects) { o.X += offsetX; o.Y += offsetY; }
            foreach (var a in Encounters) a.Shift(offsetX, offsetY, width, height);
            // Growing to the left or top keeps the rest of the world in place.
            WorldX -= offsetX;
            WorldY -= offsetY;
            Width = width;
            Height = height;
        }

        /// <summary>El tile visible más alto en (x, y) (-1 = nada). Para el cuentagotas.</summary>
        public int TopTile(int x, int y, out int layerIndex)
        {
            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                if (!Layers[i].Visible) continue;
                int t = Layers[i].Get(x, y);
                if (t >= 0) { layerIndex = i; return t; }
            }
            layerIndex = -1;
            return MapLayer.Empty;
        }

        public MapObject FindObject(string kind) => Objects.FirstOrDefault(o => o.Kind == kind);

        public string NewObjectId(string kind)
        {
            for (int i = 1; ; i++)
            {
                var id = kind + "_" + i;
                if (Objects.All(o => o.Id != id)) return id;
            }
        }
    }

    /// <summary>Un mapa en el árbol de mapas (como el de RPG Maker: carpetas = mapas padre).</summary>
    public sealed class MapEntry
    {
        public string Id { get; }
        public string Name { get; set; }
        /// <summary>Mapa padre (vacío = en la raíz).</summary>
        public string ParentId { get; set; } = "";
        public int Order { get; set; }
        /// <summary>Desplegado en el árbol del editor.</summary>
        public bool Expanded { get; set; } = true;
        /// <summary>Vista del mundo: oculto (para centrarse en otros tramos) y bloqueado (no se puede mover ni pintar).</summary>
        public bool HiddenInWorld { get; set; }
        public bool LockedInWorld { get; set; }

        public MapEntry(string id, string name, string parentId = "", int order = 0)
        {
            Id = id; Name = name; ParentId = parentId ?? ""; Order = order;
        }
    }

    /// <summary>El árbol de mapas del proyecto: orden y jerarquía (el contenido de cada mapa va aparte).</summary>
    public sealed class MapTree
    {
        private readonly List<MapEntry> _entries = new List<MapEntry>();

        public IReadOnlyList<MapEntry> Entries => _entries;
        public MapEntry Find(string id) => _entries.FirstOrDefault(e => e.Id == id);
        public bool Contains(string id) => Find(id) != null;

        public IEnumerable<MapEntry> ChildrenOf(string parentId) =>
            _entries.Where(e => e.ParentId == (parentId ?? "")).OrderBy(e => e.Order).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Recorrido en orden de árbol, con la profundidad (para dibujarlo con sangría).</summary>
        public IEnumerable<(MapEntry entry, int depth)> Walk(bool onlyExpanded = false)
        {
            IEnumerable<(MapEntry, int)> Visit(string parent, int depth)
            {
                foreach (var e in ChildrenOf(parent))
                {
                    yield return (e, depth);
                    if (onlyExpanded && !e.Expanded) continue;
                    foreach (var x in Visit(e.Id, depth + 1)) yield return x;
                }
            }
            return Visit("", 0);
        }

        public void Add(MapEntry entry)
        {
            if (Contains(entry.Id)) throw new InvalidOperationException($"Ya hay un mapa con el id «{entry.Id}».");
            if (entry.ParentId.Length > 0 && !Contains(entry.ParentId)) entry.ParentId = "";
            if (entry.Order == 0) entry.Order = ChildrenOf(entry.ParentId).Select(e => e.Order).DefaultIfEmpty(0).Max() + 1;
            _entries.Add(entry);
        }

        /// <summary>Quita un mapa; sus hijos suben al padre.</summary>
        public void Remove(string id)
        {
            var e = Find(id);
            if (e == null) return;
            foreach (var child in _entries.Where(c => c.ParentId == id)) child.ParentId = e.ParentId;
            _entries.Remove(e);
        }

        /// <summary>Mueve un mapa dentro de otro (vacío = raíz). No deja meter un mapa dentro de sí mismo o de un hijo suyo.</summary>
        public bool Move(string id, string newParent)
        {
            var e = Find(id);
            newParent = newParent ?? "";
            if (e == null || id == newParent) return false;
            for (var p = Find(newParent); p != null; p = Find(p.ParentId))
                if (p.Id == id) return false;
            e.ParentId = newParent;
            e.Order = ChildrenOf(newParent).Where(c => c.Id != id).Select(c => c.Order).DefaultIfEmpty(0).Max() + 1;
            return true;
        }

        /// <summary>Un id libre a partir de un nombre («Pueblo Paleta» → «pueblo_paleta», «pueblo_paleta_2»...).</summary>
        public string NewId(string name)
        {
            var baseId = new string((name ?? "mapa").Trim().ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? RemoveAccent(c) : '_').ToArray()).Trim('_');
            while (baseId.Contains("__")) baseId = baseId.Replace("__", "_");
            if (baseId.Length == 0) baseId = "mapa";
            var id = baseId;
            for (int i = 2; Contains(id); i++) id = baseId + "_" + i;
            return id;
        }

        private static char RemoveAccent(char c)
        {
            const string from = "áàäâéèëêíìïîóòöôúùüûñç", to = "aaaaeeeeiiiioooouuuunc";
            int i = from.IndexOf(c);
            return i >= 0 ? to[i] : c;
        }
    }

    /// <summary>
    /// Dónde se guardan los mapas. El dominio solo conoce esta interfaz; la carpeta del proyecto (JSON) la implementa en
    /// CTEditor.Project. Otra implementación (memoria para tests, base de datos...) no cambia nada más.
    /// </summary>
    public interface IMapRepository
    {
        MapTree LoadTree();
        void SaveTree(MapTree tree);
        bool Exists(string mapId);
        MapDefinition Load(string mapId);
        void Save(MapDefinition map);
        void Delete(string mapId);
    }

    /// <summary>Dónde están los tilesets (imágenes cortadas como «Tileset»).</summary>
    public interface ITilesetRepository
    {
        /// <summary>Ids fijos de los tilesets disponibles.</summary>
        IReadOnlyList<string> List();
        /// <summary>El tileset por su id (acepta también la ruta de la imagen, de proyectos antiguos). Null si no existe.</summary>
        Tileset Load(string id);
        /// <summary>Guarda las propiedades de sus tiles.</summary>
        void SaveAttributes(Tileset tileset);
        /// <summary>Vuelve a leer (se cortó, movió o renombró una imagen).</summary>
        void Refresh();
    }
}
