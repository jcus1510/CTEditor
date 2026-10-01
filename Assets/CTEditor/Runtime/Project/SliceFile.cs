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

        private static readonly string[] AutotileKeys = { "no", "xp", "vx", "47" };

        private static readonly string[] PieceKeys = { "auto", "suelo", "detalle", "encima" };

        public SliceDefinition Slice { get; }
        public TileAttributes Attributes { get; }
        /// <summary>Id fijo de la imagen (lo usan los mapas). Vacío = aún no tiene (se le da uno al guardar).</summary>
        public string Id { get; set; } = "";

        public SliceFile(SliceSettings settings, SheetKind kind = SheetKind.Tileset, TileAttributes attributes = null)
        {
            Slice = new SliceDefinition(settings ?? SliceSettings.Square(ProjectSettings.DefaultTileSize), kind);
            Attributes = attributes ?? new TileAttributes();
        }

        public SliceSettings Settings { get => Slice.Settings; set => Slice.Settings = value; }
        public SheetKind Kind { get => Slice.Kind; set => Slice.Kind = value; }
        public string CharacterLayout { get => Slice.CharacterLayout; set => Slice.CharacterLayout = value ?? ""; }
        public FreePieceSet Free { get => Slice.Free; set => Slice.Free = value ?? new FreePieceSet(); }

        public TileProperties Get(int index) => Attributes.Get(index);
        public void Set(int index, TileProperties props) => Attributes.Set(index, props);
        public IEnumerable<int> CustomizedTiles => Attributes.CustomizedTiles;

        public JsonObject ToJson()
        {
            var s = Settings;
            var json = new JsonObject()
                .Set("id", Id ?? "")
                .Set("tipo", KindKeys[(int)Kind])
                .Set("ancho", s.TileWidth).Set("alto", s.TileHeight)
                .Set("desplazamiento_x", s.OffsetX).Set("desplazamiento_y", s.OffsetY)
                .Set("separacion_x", s.SpacingX).Set("separacion_y", s.SpacingY);
            if (Kind == SheetKind.Character) json["plantilla"] = CharacterLayout;
            if (!Free.IsEmpty)
            {
                var pieces = new List<object>();
                foreach (var f in Free.Pieces)
                {
                    var po = new JsonObject().Set("nombre", f.Name ?? "")
                        .Set("x", f.Source.X).Set("y", f.Source.Y).Set("ancho", f.Source.Width).Set("alto", f.Source.Height)
                        .Set("columna", f.Column).Set("fila", f.Row);
                    if (f.IsAutotile) po["autotile"] = AutotileKeys[(int)f.Format];
                    if (!string.IsNullOrEmpty(f.ImagePath)) po["imagen"] = f.ImagePath;
                    pieces.Add(po);
                }
                json["piezas_libres"] = new JsonObject().Set("filas_base", Free.BaseRows).Set("piezas", pieces);
            }
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
                if (p.Piece != TilePiece.Auto) o["pieza"] = PieceKeys[(int)p.Piece];
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
            var file = new SliceFile(settings, kind < 0 ? SheetKind.Tileset : (SheetKind)kind)
                { CharacterLayout = o.GetString("plantilla", ""), Id = o.GetString("id", "") };
            if (o.Has("piezas_libres") && o["piezas_libres"] is JsonObject free)
            {
                file.Free.BaseRows = free.GetInt("filas_base", -1);
                foreach (var po in free.GetArray("piezas") ?? new List<object>())
                {
                    if (!(po is JsonObject f)) continue;
                    var rect = new PixelRect(f.GetInt("x"), f.GetInt("y"), f.GetInt("ancho"), f.GetInt("alto"));
                    if (rect.Width <= 0 || rect.Height <= 0) continue;
                    int format = Math.Max(0, Array.IndexOf(AutotileKeys, f.GetString("autotile", "no")));
                    file.Free.Restore(new FreePiece(f.GetString("nombre", ""), rect, f.GetInt("columna"), f.GetInt("fila"))
                        { Format = (AutotileFormat)format, ImagePath = f.GetString("imagen", "") });
                }
            }
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
                    Piece = (TilePiece)Math.Max(0, Array.IndexOf(PieceKeys, to.GetString("pieza", "auto"))),
                });
            }
            return file;
        }

        public static SliceFile LoadFor(string imagePath)
        {
            var path = ProjectLayout.SlicePathFor(imagePath);
            return File.Exists(path) ? FromJson(Json.ParseObject(File.ReadAllText(path))) : null;
        }

        /// <summary>Guarda junto a la imagen. Si no tiene id, se le da uno a partir del nombre del archivo.</summary>
        public void SaveFor(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = IdFromName(Path.GetFileNameWithoutExtension(imagePath));
            File.WriteAllText(ProjectLayout.SlicePathFor(imagePath), Json.Write(ToJson()));
        }

        /// <summary>«Pueblo Raíz (2).png» → «pueblo_raiz_2».</summary>
        public static string IdFromName(string name) => new MapTree().NewId(name);
    }
}
