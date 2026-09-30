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

        public bool IsDefault => Blocked == PassageBlock.None && Priority == 0 && TerrainTag == 0 && !Bush && !Counter;

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
    /// </summary>
    public sealed class Tileset
    {
        /// <summary>Ruta de la imagen relativa al proyecto («graficos/tilesets/pueblo.png»): es también su id.</summary>
        public string Id { get; }
        public string Name { get; }
        public SliceSettings Slice { get; }
        public int Columns { get; }
        public int Rows { get; }
        public TileAttributes Attributes { get; }

        public Tileset(string id, string name, SliceSettings slice, int imageWidth, int imageHeight, TileAttributes attributes = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Slice = slice ?? throw new ArgumentNullException(nameof(slice));
            Columns = slice.ColumnsFor(imageWidth);
            Rows = slice.RowsFor(imageHeight);
            Attributes = attributes ?? new TileAttributes();
        }

        public int TileWidth => Slice.TileWidth;
        public int TileHeight => Slice.TileHeight;
        public int Count => Columns * Rows;
        public bool Contains(int tile) => tile >= 0 && tile < Count;
        public PixelRect RectOf(int tile) => Slice.CellRect(tile % Columns, tile / Columns);
        public TileProperties Properties(int tile) => Attributes.Peek(tile);
    }
}
