using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTEditor.Art.Domain;
using CTEditor.World.Domain;

namespace CTEditor.Project
{
    /// <summary>
    /// Mapas en la carpeta del proyecto: «mapas/mapas.json» (el árbol) y «mapas/&lt;id&gt;.mapa.json» (cada mapa). Implementa
    /// el repositorio del dominio: el resto del programa no sabe que son archivos JSON.
    /// </summary>
    public sealed class JsonMapRepository : IMapRepository
    {
        public const string TreeFile = "mapas.json";
        public const string MapSuffix = ".mapa.json";

        private readonly string _folder;

        public JsonMapRepository(string projectRoot)
        {
            _folder = Path.Combine(projectRoot, ProjectLayout.MapsFolder);
        }

        private string PathOf(string id) => Path.Combine(_folder, id + MapSuffix);

        public MapTree LoadTree()
        {
            var tree = new MapTree();
            var path = Path.Combine(_folder, TreeFile);
            if (File.Exists(path))
            {
                foreach (var e in Json.ParseObject(File.ReadAllText(path)).GetArray("mapas") ?? new List<object>())
                    if (e is JsonObject o && o.GetString("id") is string id && !tree.Contains(id))
                        tree.Add(new MapEntry(id, o.GetString("nombre", id), o.GetString("padre", ""), o.GetInt("orden"))
                            { Expanded = o.GetBool("abierto", true) });
            }
            // Maps whose file exists but are missing from the tree (copied by hand) are added at the root.
            if (Directory.Exists(_folder))
                foreach (var file in Directory.GetFiles(_folder, "*" + MapSuffix))
                {
                    var id = Path.GetFileName(file).Substring(0, Path.GetFileName(file).Length - MapSuffix.Length);
                    if (!tree.Contains(id)) tree.Add(new MapEntry(id, id));
                }
            return tree;
        }

        public void SaveTree(MapTree tree)
        {
            Directory.CreateDirectory(_folder);
            var list = tree.Entries.Select(e => (object)new JsonObject()
                .Set("id", e.Id).Set("nombre", e.Name).Set("padre", e.ParentId).Set("orden", e.Order).Set("abierto", e.Expanded)).ToList();
            File.WriteAllText(Path.Combine(_folder, TreeFile), Json.Write(new JsonObject().Set("mapas", list)));
        }

        public bool Exists(string mapId) => File.Exists(PathOf(mapId));

        public MapDefinition Load(string mapId)
        {
            var o = Json.ParseObject(File.ReadAllText(PathOf(mapId)));
            int w = o.GetInt("ancho", MapDefinition.DefaultWidth), h = o.GetInt("alto", MapDefinition.DefaultHeight);
            var map = new MapDefinition(mapId, o.GetString("nombre", mapId), w, h, o.GetString("tileset", ""), withDefaultLayers: false)
            {
                Music = o.GetString("musica", ""),
                Bicycle = o.GetBool("bici", true),
                Outdoor = o.GetBool("exterior", true),
            };
            foreach (var l in o.GetArray("capas") ?? new List<object>())
            {
                if (!(l is JsonObject lo)) continue;
                var layer = map.AddLayer(lo.GetString("nombre", "Capa"));
                layer.Visible = lo.GetBool("visible", true);
                layer.Locked = lo.GetBool("bloqueada");
                layer.Opacity = lo.GetFloat("opacidad", 1f);
                var rows = lo.GetArray("filas");
                if (rows == null) continue;
                var tiles = Enumerable.Repeat(MapLayer.Empty, w * h).ToArray();
                for (int y = 0; y < Math.Min(h, rows.Count); y++)
                {
                    if (!(rows[y] is string row)) continue;
                    var cells = row.Split(' ');
                    for (int x = 0; x < Math.Min(w, cells.Length); x++)
                        if (int.TryParse(cells[x], out int t)) tiles[y * w + x] = t;
                }
                layer.Load(tiles);
            }
            if (map.Layers.Count == 0) map.AddLayer("Suelo");
            return map;
        }

        /// <summary>Each row is one text line of numbers («-1 -1 4 4 ...»): compact and readable in git diffs.</summary>
        public void Save(MapDefinition map)
        {
            Directory.CreateDirectory(_folder);
            var layers = new List<object>();
            foreach (var l in map.Layers)
            {
                var tiles = l.ToArray();
                var rows = new List<object>();
                for (int y = 0; y < map.Height; y++)
                    rows.Add(string.Join(" ", Enumerable.Range(0, map.Width).Select(x => tiles[y * map.Width + x].ToString())));
                layers.Add(new JsonObject().Set("nombre", l.Name).Set("visible", l.Visible).Set("bloqueada", l.Locked)
                    .Set("opacidad", Math.Round(l.Opacity, 2)).Set("filas", rows));
            }
            var o = new JsonObject()
                .Set("formato", 1).Set("nombre", map.Name).Set("ancho", map.Width).Set("alto", map.Height)
                .Set("tileset", map.TilesetId).Set("musica", map.Music).Set("bici", map.Bicycle).Set("exterior", map.Outdoor)
                .Set("capas", layers);
            // Write to a temporary file first: a crash while saving never leaves a half-written map.
            var path = PathOf(map.Id);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Write(o));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public void Delete(string mapId)
        {
            if (File.Exists(PathOf(mapId))) File.Delete(PathOf(mapId));
        }
    }

    /// <summary>
    /// Tilesets = imágenes de «graficos/tilesets» cortadas como Tileset. El id es la ruta relativa de la imagen. Las
    /// propiedades de los tiles se guardan en su «.corte.json».
    /// </summary>
    public sealed class FolderTilesetRepository : ITilesetRepository
    {
        private readonly string _root;

        public FolderTilesetRepository(string projectRoot) { _root = projectRoot; }

        public IReadOnlyList<string> List() =>
            AssetCatalog.Scan(_root)
                .Where(a => a.Kind == AssetKind.Tileset && a.IsSliced && a.Problem == null)
                .Select(a => a.RelativePath)
                .Where(id => (SliceFile.LoadFor(Path.Combine(_root, id))?.Kind ?? SheetKind.Sprites) == SheetKind.Tileset)
                .ToList();

        public Tileset Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var image = Path.Combine(_root, id);
            if (!File.Exists(image)) return null;
            var slice = SliceFile.LoadFor(image);
            if (slice == null || !Png.TryReadSize(image, out int w, out int h)) return null;
            return new Tileset(id, Path.GetFileNameWithoutExtension(id), slice.Settings, w, h, slice.Attributes);
        }

        public void SaveAttributes(Tileset tileset)
        {
            var image = Path.Combine(_root, tileset.Id);
            var slice = SliceFile.LoadFor(image) ?? new SliceFile(tileset.Slice);
            var file = new SliceFile(slice.Settings, slice.Kind, tileset.Attributes) { CharacterLayout = slice.CharacterLayout };
            file.SaveFor(image);
        }

        public string FullPath(string id) => Path.Combine(_root, id);
    }
}

namespace CTEditor.Project
{
    /// <summary>Imágenes PNG en disco (lector y escritor propios, sin Unity).</summary>
    public sealed class PngImageRepository : CTEditor.Art.Domain.IImageRepository
    {
        public CTEditor.Art.Domain.PixelImage Load(string path) => Png.Read(path);

        /// <summary>Writes to a temporary file first so a crash never leaves a broken image.</summary>
        public void Save(string path, CTEditor.Art.Domain.PixelImage image)
        {
            var tmp = path + ".tmp";
            Png.Write(image, tmp);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            System.IO.File.Move(tmp, path);
        }
    }
}
