using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Art.Domain;

namespace CTEditor.World.Domain
{
    /// <summary>
    /// Por dónde NO se puede pasar un tile. Mismos bits que RPG Maker XP (abajo 1, izquierda 2, derecha 4, arriba 8), así
    /// los tilesets de Essentials se importan tal cual. 15 = bloqueado por los cuatro lados.
    /// </summary>
    [Flags]
    public enum PassageBlock
    {
        None = 0,
        Down = 1,
        Left = 2,
        Right = 4,
        Up = 8,
        All = Down | Left | Right | Up
    }

    public static class PassageBlocks
    {
        public static PassageBlock Of(FacingDirection d) => d switch
        {
            FacingDirection.Down => PassageBlock.Down,
            FacingDirection.Left => PassageBlock.Left,
            FacingDirection.Right => PassageBlock.Right,
            _ => PassageBlock.Up,
        };
    }

    /// <summary>
    /// Qué tipo de pieza es un tile, para las CAPAS AUTOMÁTICAS: el editor pone cada tile en la capa que le toca.
    /// Auto = lo deduce: con prioridad → Encima; sin píxeles transparentes → Suelo; con transparencia → Detalle.
    /// </summary>
    public enum TilePiece
    {
        Auto = 0,
        Ground = 1,
        Detail = 2,
        Above = 3
    }

    /// <summary>Cuánto tapa un tile (se calcula de la imagen): vacío, en parte (tiene transparencia) o entero.</summary>
    public enum TileCoverage
    {
        Empty = 0,
        Partial = 1,
        Full = 2
    }

    /// <summary>Lo que es un tile para el juego. Los valores por defecto = suelo normal por el que se pasa.</summary>
    public sealed class TileProperties
    {
        public PassageBlock Blocked { get; set; }
        /// <summary>0 = a ras de suelo; 1-5 = por encima del jugador (como la prioridad de RPG Maker XP).</summary>
        public int Priority { get; set; }
        /// <summary>Etiqueta de terreno (id de TerrainCatalog; por defecto los números de Essentials).</summary>
        public int TerrainTag { get; set; }
        /// <summary>El jugador se ve medio hundido (hierba alta, arbustos).</summary>
        public bool Bush { get; set; }
        /// <summary>Mostrador: se habla con quien está al otro lado.</summary>
        public bool Counter { get; set; }
        /// <summary>Tipo de pieza para las capas automáticas (Auto = se deduce).</summary>
        public TilePiece Piece { get; set; }

        public bool IsDefault => Blocked == PassageBlock.None && Priority == 0 && TerrainTag == 0 && !Bush && !Counter && Piece == TilePiece.Auto;

        public TileProperties Clone() => (TileProperties)MemberwiseClone();

        public static readonly TileProperties Default = new TileProperties();
    }

    /// <summary>Las propiedades de los tiles de un tileset (solo se guardan las que no son de por defecto).</summary>
    public sealed class TileAttributes
    {
        private readonly Dictionary<int, TileProperties> _tiles = new Dictionary<int, TileProperties>();

        /// <summary>Copia editable de las propiedades del tile (guardar con Set).</summary>
        public TileProperties Get(int index) => _tiles.TryGetValue(index, out var p) ? p.Clone() : new TileProperties();

        /// <summary>Lectura sin copiar (para el motor: rápido). No modificar el resultado.</summary>
        public TileProperties Peek(int index) => _tiles.TryGetValue(index, out var p) ? p : TileProperties.Default;

        public void Set(int index, TileProperties props)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (props == null || props.IsDefault) _tiles.Remove(index);
            else _tiles[index] = props.Clone();
        }

        public IEnumerable<int> CustomizedTiles => _tiles.Keys.OrderBy(i => i);
    }

    /// <summary>Una etiqueta de terreno: qué pasa al pisar el tile.</summary>
    public sealed class TerrainType
    {
        public int Id { get; }
        public string Key { get; }
        public string Label { get; }
        /// <summary>Al pisarlo pueden salir monstruos salvajes.</summary>
        public bool Encounters { get; }
        /// <summary>Hace falta Surf (u otra habilidad de agua) para entrar.</summary>
        public bool NeedsSurf { get; }

        public TerrainType(int id, string key, string label, bool encounters = false, bool needsSurf = false)
        {
            Id = id; Key = key; Label = label; Encounters = encounters; NeedsSurf = needsSurf;
        }
    }

    /// <summary>
    /// Las etiquetas de terreno que existen. Por defecto las de Pokémon Essentials (mismos números, para importar); un
    /// juego puede añadir las suyas con Register sin tocar el motor.
    /// </summary>
    public sealed class TerrainCatalog
    {
        private readonly Dictionary<int, TerrainType> _types = new Dictionary<int, TerrainType>();

        public IEnumerable<TerrainType> All => _types.Values.OrderBy(t => t.Id);
        public TerrainType Find(int id) => _types.TryGetValue(id, out var t) ? t : null;
        public string LabelOf(int id) => Find(id)?.Label ?? $"Terreno {id}";

        public void Register(TerrainType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            _types[type.Id] = type;
        }

        public static TerrainCatalog Essentials()
        {
            var c = new TerrainCatalog();
            foreach (var t in new[]
                     {
                         new TerrainType(0, "ninguna", "Ninguna"),
                         new TerrainType(1, "saliente", "Saliente (se salta hacia abajo)"),
                         new TerrainType(2, "hierba", "Hierba (encuentros)", encounters: true),
                         new TerrainType(3, "arena", "Arena"),
                         new TerrainType(4, "roca", "Roca"),
                         new TerrainType(5, "agua_profunda", "Agua profunda (buceo)", needsSurf: true),
                         new TerrainType(6, "agua_quieta", "Agua quieta", needsSurf: true),
                         new TerrainType(7, "agua", "Agua (surf)", needsSurf: true),
                         new TerrainType(8, "cascada", "Cascada", needsSurf: true),
                         new TerrainType(9, "cima_cascada", "Cima de cascada", needsSurf: true),
                         new TerrainType(10, "hierba_alta", "Hierba alta", encounters: true),
                         new TerrainType(11, "hierba_submarina", "Hierba submarina", encounters: true),
                         new TerrainType(12, "hielo", "Hielo (resbala)"),
                         new TerrainType(13, "neutral", "Neutral"),
                         new TerrainType(14, "hierba_ceniza", "Hierba con ceniza", encounters: true),
                         new TerrainType(15, "puente", "Puente"),
                         new TerrainType(16, "charco", "Charco"),
                         new TerrainType(17, "sin_efecto", "Sin efecto"),
                     })
                c.Register(t);
            return c;
        }
    }

    /// <summary>
    /// Un tileset listo para pintar: la imagen cortada (tamaño del tile, columnas, filas) y las propiedades de cada tile.
    /// El número de un tile es su posición en la rejilla (fila a fila), como en el asistente de corte.
    /// Tiene un ID FIJO (guardado en su «.corte.json»): renombrar o mover la imagen no rompe los mapas que lo usan.
    /// </summary>
    public sealed class Tileset
    {
        /// <summary>Id fijo del tileset («pueblo»). Los mapas guardan este id, no la ruta.</summary>
        public string Id { get; }
        public string Name { get; }
        /// <summary>Ruta de la imagen relativa al proyecto («graficos/tilesets/pueblo.png»).</summary>
        public string ImagePath { get; }
        public SliceSettings Slice { get; }
        public int Columns { get; }
        public int Rows { get; }
        public TileAttributes Attributes { get; }
        /// <summary>Cuánto tapa cada tile (null = no se sabe: se trata como entero).</summary>
        public TileCoverage[] Coverage { get; set; }
        /// <summary>Color medio de cada tile (para el mapa de la región reducido; null = no calculado).</summary>
        public Rgba32[] AverageColors { get; set; }

        public Tileset(string id, string name, SliceSettings slice, int imageWidth, int imageHeight, TileAttributes attributes = null,
            string imagePath = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            ImagePath = imagePath ?? id;
            Slice = slice ?? throw new ArgumentNullException(nameof(slice));
            Columns = slice.ColumnsFor(imageWidth);
            Rows = slice.RowsFor(imageHeight);
            Attributes = attributes ?? new TileAttributes();
        }

        /// <summary>El tipo de pieza del tile: el elegido o, en Auto, el que se deduce (ver TilePiece).</summary>
        public TilePiece PieceOf(int tile)
        {
            var p = Properties(tile);
            if (p.Piece != TilePiece.Auto) return p.Piece;
            if (p.Priority > 0) return TilePiece.Above;
            var cov = Coverage != null && tile >= 0 && tile < Coverage.Length ? Coverage[tile] : TileCoverage.Full;
            return cov == TileCoverage.Full ? TilePiece.Ground : TilePiece.Detail;
        }

        /// <summary>Calcula Coverage a partir de la imagen (lo hace quien la carga).</summary>
        public void ComputeCoverage(PixelImage image)
        {
            var cov = new TileCoverage[Count];
            var avg = new Rgba32[Count];
            for (int i = 0; i < Count; i++)
            {
                var r = RectOf(i);
                if (!image.Contains(r)) { cov[i] = TileCoverage.Empty; continue; }
                bool any = false, all = true;
                long sr = 0, sg = 0, sb = 0, n = 0;
                for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                {
                    var p = image[x, y];
                    if (p.A == 0) { all = false; continue; }
                    any = true;
                    sr += p.R; sg += p.G; sb += p.B; n++;
                }
                cov[i] = !any ? TileCoverage.Empty : all ? TileCoverage.Full : TileCoverage.Partial;
                avg[i] = n == 0 ? Rgba32.Transparent : new Rgba32((byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
            }
            Coverage = cov;
            AverageColors = avg;
        }

        /// <summary>Hand-cut pieces (free slicing): their blocks are the rows under the grid of the image.</summary>
        public FreePieceSet Free { get; set; } = new FreePieceSet();
        /// <summary>Rows of the grid of the original image (the pieces start under them). -1 = all rows are the grid.</summary>
        public int GridRows { get; set; } = -1;

        /// <summary>The hand-cut piece a tile belongs to (null for tiles of the grid).</summary>
        public FreePiece FreePieceOf(int tile) =>
            Free == null || Free.IsEmpty ? null : Free.PieceAt(tile, Slice, Columns, GridRows < 0 ? Rows : GridRows);

        /// <summary>Where a tile comes from in the ORIGINAL image (for Retoque): its cell, or the part of its piece.</summary>
        public PixelRect SourceRectOf(int tile)
        {
            var piece = FreePieceOf(tile);
            return piece == null ? RectOf(tile) : piece.Source;
        }

        public int TileWidth => Slice.TileWidth;
        public int TileHeight => Slice.TileHeight;
        public int Count => Columns * Rows;
        public bool Contains(int tile) => tile >= 0 && tile < Count;
        public PixelRect RectOf(int tile) => Slice.CellRect(tile % Columns, tile / Columns);
        public TileProperties Properties(int tile) => Attributes.Peek(tile);
    }
}

namespace CTEditor.World.Domain
{
    /// <summary>
    /// Una casilla de un mapa guarda UN número con dos datos: qué tileset del mapa (hueco 0, 1, 2...) y qué tile de ese
    /// tileset. Así un mapa puede usar varios tilesets (césped, ciudad, playa) a la vez. Los mapas de un solo tileset
    /// quedan igual que antes (hueco 0 = el número del tile tal cual). -1 = vacía.
    /// </summary>
    /// <summary>
    /// Una casilla de una capa en un entero: el tile (bits 0-19), el tileset del mapa (hueco, bits 20-27) y cómo se
    /// dibuja: volteado en horizontal (bit 28), en vertical (29) o en diagonal (30; con los otros dos da los giros),
    /// como en Tiled. -1 = vacía. Los mapas antiguos no tienen esos bits: se leen igual.
    /// </summary>
    public static class MapTile
    {
        public const int Empty = -1;
        public const int SlotShift = 20;
        public const int IndexMask = (1 << SlotShift) - 1;
        public const int SlotMask = 0xFF;
        public const int MaxSlots = SlotMask + 1;
        public const int FlipH = 1 << 28, FlipV = 1 << 29, FlipD = 1 << 30;
        public const int FlagMask = FlipH | FlipV | FlipD;

        public static int Encode(int slot, int index, int flags = 0) =>
            index < 0 || slot < 0 ? Empty : ((slot & SlotMask) << SlotShift) | (index & IndexMask) | (flags & FlagMask);
        public static int Slot(int cell) => cell < 0 ? -1 : (cell >> SlotShift) & SlotMask;
        public static int Index(int cell) => cell < 0 ? -1 : cell & IndexMask;
        public static int Flags(int cell) => cell < 0 ? 0 : cell & FlagMask;
        public static int WithFlags(int cell, int flags) => cell < 0 ? cell : (cell & ~FlagMask) | (flags & FlagMask);

        // The drawing of a tile as a 2 × 2 matrix (x' = a·x + b·y, y' = c·x + d·y; y downwards): diagonal first, then
        // horizontal, then vertical (Tiled's order).
        public static (int a, int b, int c, int d) Matrix(int flags)
        {
            int a = 1, b = 0, c = 0, d = 1;
            if ((flags & FlipD) != 0) (a, b, c, d) = (0, 1, 1, 0);
            if ((flags & FlipH) != 0) (a, b) = (-a, -b);
            if ((flags & FlipV) != 0) (c, d) = (-c, -d);
            return (a, b, c, d);
        }

        public static int FlagsOf((int a, int b, int c, int d) m)
        {
            foreach (int f in new[] { 0, FlipH, FlipV, FlipH | FlipV, FlipD, FlipD | FlipH, FlipD | FlipV, FlipD | FlipH | FlipV })
                if (Matrix(f) == m) return f;
            return 0;
        }

        /// <summary>The flags after turning the tile a quarter clockwise (or anticlockwise).</summary>
        public static int Rotated(int flags, bool clockwise)
        {
            var (a, b, c, d) = Matrix(flags);
            // Clockwise with y down: (x, y) → (−y, x).
            return clockwise ? FlagsOf((-c, -d, a, b)) : FlagsOf((c, d, -a, -b));
        }
    }

    /// <summary>Los tilesets de un mapa, por hueco. Traduce una casilla a su tileset y a las propiedades de su tile.</summary>
    public sealed class MapTilesets
    {
        public static readonly MapTilesets None = new MapTilesets(new Tileset[0]);

        public IReadOnlyList<Tileset> Slots { get; }

        public MapTilesets(IReadOnlyList<Tileset> slots) { Slots = slots ?? new Tileset[0]; }

        public static implicit operator MapTilesets(Tileset single) => single == null ? None : new MapTilesets(new[] { single });

        public Tileset For(int cell)
        {
            int slot = MapTile.Slot(cell);
            return slot >= 0 && slot < Slots.Count ? Slots[slot] : null;
        }

        public TileProperties Properties(int cell) => For(cell)?.Properties(MapTile.Index(cell)) ?? TileProperties.Default;

        public TilePiece PieceOf(int cell)
        {
            var ts = For(cell);
            return ts == null ? TilePiece.Ground : ts.PieceOf(MapTile.Index(cell));
        }

        public bool IsEmpty => Slots.All(s => s == null);
    }
}
