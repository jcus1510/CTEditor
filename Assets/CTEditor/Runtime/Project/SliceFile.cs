using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTEditor.Art.Domain;

namespace CTEditor.Project
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

    /// <summary>Una etiqueta de terreno (qué pasa al pisar el tile).</summary>
    public sealed class TerrainTagInfo
    {
        public int Id { get; }
        public string Key { get; }
        public string Label { get; }
        public TerrainTagInfo(int id, string key, string label) { Id = id; Key = key; Label = label; }
    }

    /// <summary>Lo que es un tile para el juego. Los valores por defecto = suelo normal por el que se pasa.</summary>
    public sealed class TileProperties
    {
        public PassageBlock Blocked { get; set; }
        /// <summary>0 = a ras de suelo; 1-5 = por encima del jugador (como la prioridad de RPG Maker XP).</summary>
        public int Priority { get; set; }
        /// <summary>Etiqueta de terreno (números de Essentials, ver SliceFile.EssentialsTerrainTags).</summary>
        public int TerrainTag { get; set; }
        /// <summary>El jugador se ve medio hundido (hierba alta, arbustos).</summary>
        public bool Bush { get; set; }
        /// <summary>Mostrador: se habla con quien está al otro lado.</summary>
        public bool Counter { get; set; }

        public bool IsDefault => Blocked == PassageBlock.None && Priority == 0 && TerrainTag == 0 && !Bush && !Counter;

        public TileProperties Clone() => (TileProperties)MemberwiseClone();
    }

    /// <summary>
    /// «imagen.corte.json»: cómo se corta una imagen y qué es cada tile. Solo se guardan los tiles que no son de por defecto
    /// (el archivo queda pequeño y legible). Si se cambia la imagen, se vuelve a cortar con los mismos ajustes.
    /// </summary>
    public sealed class SliceFile
    {
        /// <summary>Etiquetas de terreno de Pokémon Essentials (mismos números, para importar).</summary>
        public static IReadOnlyList<TerrainTagInfo> EssentialsTerrainTags { get; } = new[]
        {
            new TerrainTagInfo(0, "ninguna", "Ninguna"),
            new TerrainTagInfo(1, "saliente", "Saliente (se salta hacia abajo)"),
            new TerrainTagInfo(2, "hierba", "Hierba (encuentros)"),
            new TerrainTagInfo(3, "arena", "Arena"),
            new TerrainTagInfo(4, "roca", "Roca"),
            new TerrainTagInfo(5, "agua_profunda", "Agua profunda (buceo)"),
            new TerrainTagInfo(6, "agua_quieta", "Agua quieta"),
            new TerrainTagInfo(7, "agua", "Agua (surf)"),
            new TerrainTagInfo(8, "cascada", "Cascada"),
            new TerrainTagInfo(9, "cima_cascada", "Cima de cascada"),
            new TerrainTagInfo(10, "hierba_alta", "Hierba alta"),
            new TerrainTagInfo(11, "hierba_submarina", "Hierba submarina"),
            new TerrainTagInfo(12, "hielo", "Hielo (resbala)"),
            new TerrainTagInfo(13, "neutral", "Neutral"),
            new TerrainTagInfo(14, "hierba_ceniza", "Hierba con ceniza"),
            new TerrainTagInfo(15, "puente", "Puente"),
            new TerrainTagInfo(16, "charco", "Charco"),
            new TerrainTagInfo(17, "sin_efecto", "Sin efecto"),
        };

        public const string KindTileset = "tileset";
        public const string KindCharacter = "personaje";
        public const string KindSprites = "sprites";

        public SliceSettings Settings { get; set; }
        /// <summary>«tileset», «personaje» o «sprites».</summary>
        public string Kind { get; set; } = KindTileset;
        /// <summary>Solo personajes: la plantilla de hoja (nombre de CharacterSheetLayout).</summary>
        public string CharacterLayout { get; set; } = "";

        private readonly Dictionary<int, TileProperties> _tiles = new Dictionary<int, TileProperties>();

        public SliceFile(SliceSettings settings) { Settings = settings ?? SliceSettings.Square(ProjectSettings.DefaultTileSize); }

        /// <summary>Propiedades del tile 'index' (copia editable: guardar con Set).</summary>
        public TileProperties Get(int index) => _tiles.TryGetValue(index, out var p) ? p.Clone() : new TileProperties();

        public void Set(int index, TileProperties props)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (props == null || props.IsDefault) _tiles.Remove(index);
            else _tiles[index] = props.Clone();
        }

        /// <summary>Tiles con propiedades distintas de las de por defecto.</summary>
        public IEnumerable<int> CustomizedTiles => _tiles.Keys.OrderBy(i => i);

        public JsonObject ToJson()
        {
            var s = Settings;
            var tiles = new List<object>();
            foreach (var i in CustomizedTiles)
            {
                var p = _tiles[i];
                var o = new JsonObject().Set("tile", i);
                if (p.Blocked != PassageBlock.None) o["bloqueo"] = (int)p.Blocked;
                if (p.Priority != 0) o["prioridad"] = p.Priority;
                if (p.TerrainTag != 0) o["terreno"] = p.TerrainTag;
                if (p.Bush) o["arbusto"] = true;
                if (p.Counter) o["mostrador"] = true;
                tiles.Add(o);
            }
            var json = new JsonObject()
                .Set("tipo", Kind)
                .Set("ancho", s.TileWidth).Set("alto", s.TileHeight)
                .Set("desplazamiento_x", s.OffsetX).Set("desplazamiento_y", s.OffsetY)
                .Set("separacion_x", s.SpacingX).Set("separacion_y", s.SpacingY);
            if (Kind == KindCharacter) json["plantilla"] = CharacterLayout;
            return json.Set("tiles", tiles);
        }

        public static SliceFile FromJson(JsonObject o)
        {
            int w = o.GetInt("ancho", ProjectSettings.DefaultTileSize);
            var settings = new SliceSettings(w, o.GetInt("alto", w), o.GetInt("desplazamiento_x"), o.GetInt("desplazamiento_y"),
                o.GetInt("separacion_x"), o.GetInt("separacion_y"));
            var file = new SliceFile(settings) { Kind = o.GetString("tipo", KindTileset), CharacterLayout = o.GetString("plantilla", "") };
            foreach (var t in o.GetArray("tiles") ?? new List<object>())
            {
                if (!(t is JsonObject to) || !to.Has("tile")) continue;
                file.Set(to.GetInt("tile"), new TileProperties
                {
                    Blocked = (PassageBlock)(to.GetInt("bloqueo") & (int)PassageBlock.All),
                    Priority = Math.Max(0, Math.Min(5, to.GetInt("prioridad"))),
                    TerrainTag = Math.Max(0, to.GetInt("terreno")),
                    Bush = to.GetBool("arbusto"),
                    Counter = to.GetBool("mostrador"),
                });
            }
            return file;
        }

        public static SliceFile LoadFor(string imagePath)
        {
            var path = ProjectLayout.SlicePathFor(imagePath);
            return File.Exists(path) ? FromJson(Json.ParseObject(File.ReadAllText(path))) : null;
        }

        public void SaveFor(string imagePath) => File.WriteAllText(ProjectLayout.SlicePathFor(imagePath), Json.Write(ToJson()));
    }
}
