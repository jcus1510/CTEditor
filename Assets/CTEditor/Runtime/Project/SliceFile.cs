using System;
using System.Collections.Generic;
using System.IO;
using CTEditor.Art.Domain;
using CTEditor.World.Domain;

namespace CTEditor.Project
{
    /// <summary>
    /// «imagen.corte.json»: el ARCHIVO que guarda cómo se corta una imagen (SliceDefinition, del dominio de arte) y qué es
    /// cada tile (TileAttributes, del dominio del mundo). Solo traduce entre JSON y el dominio: no decide nada.
    /// </summary>
    public sealed class SliceFile
    {
        private static readonly string[] KindKeys = { "tileset", "personaje", "sprites" };

        public SliceDefinition Slice { get; }
        public TileAttributes Attributes { get; }

        public SliceFile(SliceSettings settings, SheetKind kind = SheetKind.Tileset, TileAttributes attributes = null)
        {
            Slice = new SliceDefinition(settings ?? SliceSettings.Square(ProjectSettings.DefaultTileSize), kind);
            Attributes = attributes ?? new TileAttributes();
        }

        public SliceSettings Settings { get => Slice.Settings; set => Slice.Settings = value; }
        public SheetKind Kind { get => Slice.Kind; set => Slice.Kind = value; }
        public string CharacterLayout { get => Slice.CharacterLayout; set => Slice.CharacterLayout = value ?? ""; }

        public TileProperties Get(int index) => Attributes.Get(index);
        public void Set(int index, TileProperties props) => Attributes.Set(index, props);
        public IEnumerable<int> CustomizedTiles => Attributes.CustomizedTiles;

        public JsonObject ToJson()
        {
            var s = Settings;
            var json = new JsonObject()
                .Set("tipo", KindKeys[(int)Kind])
                .Set("ancho", s.TileWidth).Set("alto", s.TileHeight)
                .Set("desplazamiento_x", s.OffsetX).Set("desplazamiento_y", s.OffsetY)
                .Set("separacion_x", s.SpacingX).Set("separacion_y", s.SpacingY);
            if (Kind == SheetKind.Character) json["plantilla"] = CharacterLayout;
            json["tiles"] = AttributesToJson(Attributes);
            return json;
        }

        public static List<object> AttributesToJson(TileAttributes attributes)
        {
            var tiles = new List<object>();
            foreach (var i in attributes.CustomizedTiles)
            {
                var p = attributes.Peek(i);
                var o = new JsonObject().Set("tile", i);
                if (p.Blocked != PassageBlock.None) o["bloqueo"] = (int)p.Blocked;
                if (p.Priority != 0) o["prioridad"] = p.Priority;
                if (p.TerrainTag != 0) o["terreno"] = p.TerrainTag;
                if (p.Bush) o["arbusto"] = true;
                if (p.Counter) o["mostrador"] = true;
                tiles.Add(o);
            }
            return tiles;
        }

        public static SliceFile FromJson(JsonObject o)
        {
            int w = o.GetInt("ancho", ProjectSettings.DefaultTileSize);
            var settings = new SliceSettings(w, o.GetInt("alto", w), o.GetInt("desplazamiento_x"), o.GetInt("desplazamiento_y"),
                o.GetInt("separacion_x"), o.GetInt("separacion_y"));
            int kind = Array.IndexOf(KindKeys, o.GetString("tipo", "tileset"));
            var file = new SliceFile(settings, kind < 0 ? SheetKind.Tileset : (SheetKind)kind) { CharacterLayout = o.GetString("plantilla", "") };
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
