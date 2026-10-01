using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTEditor.Art.Domain;
using CTEditor.World.Domain;

namespace CTEditor.Project
{
    /// <summary>
    /// Mapas en la carpeta del proyecto: «mapas/mapas.json» (el árbol) y «mapas/&lt;id&gt;.mapa.json» (cada tramo con sus
    /// capas, tilesets, posición en el mundo, objetos y zonas de encuentros). Implementa el repositorio del dominio: el
    /// resto del programa no sabe que son archivos JSON. Lee también los archivos antiguos (un solo «tileset»).
    /// </summary>
    public sealed class JsonMapRepository : IMapRepository
    {
        public const string TreeFile = "mapas.json";
        public const string MapSuffix = ".mapa.json";

        private static readonly string[] KindKeys = { "exterior", "interior" };
        private static readonly string[] CategoryKeys = { "pueblo", "ciudad", "ruta", "bosque", "cueva", "agua", "montana", "edificio", "especial" };
        private static readonly string[] RoleKeys = { "", "suelo", "detalles", "encima" };
        private static readonly string[] TimeKeys = { "manana", "dia", "tarde", "noche" };

        private readonly string _folder;

        public JsonMapRepository(string projectRoot)
        {
            _folder = Path.Combine(projectRoot, ProjectLayout.MapsFolder);
        }

        private string PathOf(string id) => Path.Combine(_folder, id + MapSuffix);

        // ── Tree ─────────────────────────────────────────────────────────────────────────────────

        public MapTree LoadTree()
        {
            var tree = new MapTree();
            var path = Path.Combine(_folder, TreeFile);
            if (File.Exists(path))
            {
                foreach (var e in Json.ParseObject(File.ReadAllText(path)).GetArray("mapas") ?? new List<object>())
                    if (e is JsonObject o && o.GetString("id") is string id && !tree.Contains(id))
                        tree.Add(new MapEntry(id, o.GetString("nombre", id), o.GetString("padre", ""), o.GetInt("orden"))
                        {
                            Expanded = o.GetBool("abierto", true),
                            HiddenInWorld = o.GetBool("oculto_mundo"),
                            LockedInWorld = o.GetBool("bloqueado_mundo"),
                        });
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
                .Set("id", e.Id).Set("nombre", e.Name).Set("padre", e.ParentId).Set("orden", e.Order).Set("abierto", e.Expanded)
                .Set("oculto_mundo", e.HiddenInWorld).Set("bloqueado_mundo", e.LockedInWorld)).ToList();
            WriteSafely(Path.Combine(_folder, TreeFile), Json.Write(new JsonObject().Set("mapas", list)));
        }

        // ── Maps ─────────────────────────────────────────────────────────────────────────────────

        public bool Exists(string mapId) => File.Exists(PathOf(mapId));

        public MapDefinition Load(string mapId)
        {
            var o = Json.ParseObject(File.ReadAllText(PathOf(mapId)));
            int w = o.GetInt("ancho", MapDefinition.DefaultWidth), h = o.GetInt("alto", MapDefinition.DefaultHeight);
            var map = new MapDefinition(mapId, o.GetString("nombre", mapId), w, h, "", withDefaultLayers: false)
            {
                Music = o.GetString("musica", ""),
                Weather = o.GetString("clima", ""),
                Bicycle = o.GetBool("bici", true),
                Outdoor = o.GetBool("exterior", true),
                Kind = (MapKind)Math.Max(0, Array.IndexOf(KindKeys, o.GetString("tipo", "exterior"))),
                Category = (SectionCategory)Math.Max(0, Array.IndexOf(CategoryKeys, o.GetString("categoria", "ruta"))),
                InWorld = o.GetBool("en_mundo"),
                WorldX = o.GetInt("mundo_x"),
                WorldY = o.GetInt("mundo_y"),
                ShowOnRegionMap = o.GetBool("en_mapa_region", true),
            };
            if (o.GetArray("tilesets") is List<object> ts) map.TilesetIds.AddRange(ts.OfType<string>());
            else if (!string.IsNullOrEmpty(o.GetString("tileset"))) map.TilesetIds.Add(o.GetString("tileset")); // format 1

            foreach (var l in o.GetArray("capas") ?? new List<object>())
            {
                if (!(l is JsonObject lo)) continue;
                var layer = map.AddLayer(lo.GetString("nombre", "Capa"));
                layer.Visible = lo.GetBool("visible", true);
                layer.Locked = lo.GetBool("bloqueada");
                layer.Opacity = lo.GetFloat("opacidad", 1f);
                int role = Array.IndexOf(RoleKeys, lo.GetString("papel", ""));
                layer.Role = role > 0 ? (LayerRole)role : LayerRole.Custom;
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
            if (map.Layers.Count == 0) map.AddLayer("Suelo", role: LayerRole.Ground);
            // Format 1 had no roles: the three default layers get theirs (automatic layers work on old maps too).
            if (!o.Has("formato") || o.GetInt("formato") < 2)
            {
                string[] names = { "Suelo", "Detalles", "Encima" };
                for (int i = 0; i < map.Layers.Count && i < 3; i++)
                    if (map.Layers[i].Name == names[i]) map.Layers[i].Role = (LayerRole)(i + 1);
            }

            foreach (var ob in o.GetArray("objetos") ?? new List<object>())
            {
                if (!(ob is JsonObject oo) || !(oo.GetString("id") is string oid)) continue;
                var mo = new MapObject(oid, oo.GetString("tipo", "objeto"), oo.GetInt("x"), oo.GetInt("y"), oo.GetString("nombre"))
                    { Width = Math.Max(1, oo.GetInt("ancho", 1)), Height = Math.Max(1, oo.GetInt("alto", 1)) };
                if (oo.GetObject("props") is JsonObject props)
                    foreach (var kv in props) if (kv.Value is string v) mo.Properties[kv.Key] = v;
                map.Objects.Add(mo);
            }

            foreach (var e in o.GetArray("encuentros") ?? new List<object>())
            {
                if (!(e is JsonObject eo) || !(eo.GetString("id") is string aid)) continue;
                var area = new EncounterArea(aid, eo.GetString("nombre", aid), eo.GetBool("todo_el_mapa")) { Color = eo.GetString("color", "#E0B34A") };
                var rects = new List<(int, int, int, int)>();
                foreach (var r in eo.GetArray("zonas") ?? new List<object>())
                    if (r is List<object> ra && ra.Count == 4)
                        rects.Add(((int)(double)ra[0], (int)(double)ra[1], (int)(double)ra[2], (int)(double)ra[3]));
                area.SetRectangles(rects);
                foreach (var t in eo.GetArray("tablas") ?? new List<object>())
                {
                    if (!(t is JsonObject to) || !(to.GetString("metodo") is string method)) continue;
                    var table = area.GetOrAddTable(method);
                    table.Rate = to.GetInt("probabilidad", -1);
                    table.DoublePercent = to.GetInt("dobles", 0);
                    foreach (var sl in to.GetArray("especies") ?? new List<object>())
                    {
                        if (!(sl is JsonObject so)) continue;
                        var times = TimeOfDay.Any;
                        foreach (var k in so.GetArray("horas") ?? new List<object>())
                        {
                            int i = Array.IndexOf(TimeKeys, k as string);
                            if (i >= 0) times |= (TimeOfDay)(1 << i);
                        }
                        table.Slots.Add(new EncounterSlot(so.GetString("especie", ""), so.GetInt("min", 2), so.GetInt("max", 4), so.GetInt("peso", 10), times)
                            {
                                RequiredFlag = so.GetString("interruptor", ""), Form = so.GetInt("forma", 0),
                                HeldItem = so.GetString("objeto", ""), ShinyOdds = so.GetInt("variocolor", 0),
                            });
                    }
                }
                map.Encounters.Add(area);
            }
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
                var lo = new JsonObject().Set("nombre", l.Name);
                if (l.Role != LayerRole.Custom) lo["papel"] = RoleKeys[(int)l.Role];
                layers.Add(lo.Set("visible", l.Visible).Set("bloqueada", l.Locked).Set("opacidad", Math.Round(l.Opacity, 2)).Set("filas", rows));
            }
            var objects = map.Objects.Select(ob =>
            {
                var props = new JsonObject();
                foreach (var kv in ob.Properties) props[kv.Key] = kv.Value;
                return (object)new JsonObject().Set("id", ob.Id).Set("tipo", ob.Kind).Set("nombre", ob.Name)
                    .Set("x", ob.X).Set("y", ob.Y).Set("ancho", ob.Width).Set("alto", ob.Height).Set("props", props);
            }).ToList();
            var encounters = map.Encounters.Select(a => (object)new JsonObject()
                .Set("id", a.Id).Set("nombre", a.Name).Set("todo_el_mapa", a.WholeMap).Set("color", a.Color)
                .Set("zonas", a.ToRectangles().Select(r => (object)new List<object> { r.x, r.y, r.w, r.h }).ToList())
                .Set("tablas", a.Tables.Select(t => (object)new JsonObject()
                    .Set("metodo", t.MethodId).Set("probabilidad", t.Rate).Set("dobles", t.DoublePercent)
                    .Set("especies", t.Slots.Select(sl =>
                    {
                        var so = new JsonObject().Set("especie", sl.SpeciesId).Set("min", sl.MinLevel).Set("max", sl.MaxLevel).Set("peso", sl.Weight);
                        if (sl.Times != TimeOfDay.Any)
                            so["horas"] = Enumerable.Range(0, 4).Where(i => (sl.Times & (TimeOfDay)(1 << i)) != 0).Select(i => (object)TimeKeys[i]).ToList();
                        if (!string.IsNullOrEmpty(sl.RequiredFlag)) so["interruptor"] = sl.RequiredFlag;
                        if (sl.Form != 0) so["forma"] = sl.Form;
                        if (!string.IsNullOrEmpty(sl.HeldItem)) so["objeto"] = sl.HeldItem;
                        if (sl.ShinyOdds > 0) so["variocolor"] = sl.ShinyOdds;
                        return (object)so;
                    }).ToList())).ToList())).ToList();
            var o = new JsonObject()
                .Set("formato", 2).Set("nombre", map.Name).Set("ancho", map.Width).Set("alto", map.Height)
                .Set("tipo", KindKeys[(int)map.Kind]).Set("categoria", CategoryKeys[(int)map.Category])
                .Set("en_mundo", map.InWorld).Set("mundo_x", map.WorldX).Set("mundo_y", map.WorldY).Set("en_mapa_region", map.ShowOnRegionMap)
                .Set("tilesets", map.TilesetIds.Cast<object>().ToList())
                .Set("musica", map.Music).Set("clima", map.Weather).Set("bici", map.Bicycle).Set("exterior", map.Outdoor)
                .Set("capas", layers).Set("objetos", objects).Set("encuentros", encounters);
            WriteSafely(PathOf(map.Id), Json.Write(o));
        }

        public void Delete(string mapId)
        {
            if (File.Exists(PathOf(mapId))) File.Delete(PathOf(mapId));
        }

        public static string CategoryKey(SectionCategory c) => CategoryKeys[(int)c];

        /// <summary>Write to a temporary file first: a crash while saving never leaves a half-written file.</summary>
        internal static void WriteSafely(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }

    /// <summary>
    /// Tilesets = imágenes de «graficos/tilesets» cortadas como Tileset. Cada una tiene un ID FIJO guardado en su
    /// «.corte.json» (se crea solo); los mapas guardan ese id, así renombrar o mover la imagen no rompe nada. Acepta
    /// también la ruta de la imagen (proyectos antiguos). Las propiedades de los tiles se guardan en el mismo archivo.
    /// </summary>
    public sealed class FolderTilesetRepository : ITilesetRepository
    {
        private readonly string _root;
        private Dictionary<string, string> _pathById;

        public FolderTilesetRepository(string projectRoot) { _root = projectRoot; }

        /// <summary>Vuelve a leer la carpeta (se cortó o se movió una imagen).</summary>
        public void Refresh() => _pathById = null;

        /// <summary>id → ruta relativa, dando id (único) a los cortes que no lo tengan.</summary>
        private Dictionary<string, string> Index()
        {
            if (_pathById != null) return _pathById;
            var index = new Dictionary<string, string>();
            foreach (var a in AssetCatalog.Scan(_root).Where(a => a.Kind == AssetKind.Tileset && a.IsSliced && a.Problem == null))
            {
                var full = Path.Combine(_root, a.RelativePath);
                SliceFile slice;
                try { slice = SliceFile.LoadFor(full); }
                catch (Exception) { continue; }
                if (slice == null || slice.Kind != SheetKind.Tileset) continue;
                var id = string.IsNullOrWhiteSpace(slice.Id) ? SliceFile.IdFromName(a.Name) : slice.Id;
                if (index.ContainsKey(id))
                {
                    var baseId = id;
                    for (int i = 2; index.ContainsKey(id); i++) id = baseId + "_" + i;
                }
                if (slice.Id != id)
                {
                    slice.Id = id;
                    try { slice.SaveFor(full); } catch (IOException) { /* read-only: the id still works this session */ }
                }
                index[id] = a.RelativePath;
            }
            return _pathById = index;
        }

        public IReadOnlyList<string> List() => Index().Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The image path of a tileset id (null if unknown).</summary>
        public string PathOf(string id) => id != null && Index().TryGetValue(id, out var p) ? p : null;

        /// <summary>The id of the tileset whose image is at that relative path (for old maps that saved the path).</summary>
        public string IdOfPath(string relativePath)
        {
            var norm = ProjectLayout.Normalize(relativePath);
            return Index().FirstOrDefault(kv => string.Equals(kv.Value, norm, StringComparison.OrdinalIgnoreCase)).Key;
        }

        public Tileset Load(string idOrPath)
        {
            if (string.IsNullOrEmpty(idOrPath)) return null;
            var rel = PathOf(idOrPath);
            var id = idOrPath;
            if (rel == null)
            {
                id = IdOfPath(idOrPath);
                rel = id == null ? null : PathOf(id);
            }
            if (rel == null) return null;
            var image = Path.Combine(_root, rel);
            var slice = SliceFile.LoadFor(image);
            if (slice == null) return null;
            var pixels = Png.Read(image);
            var ts = new Tileset(id, Path.GetFileNameWithoutExtension(rel), slice.Settings, pixels.Width, pixels.Height, slice.Attributes, rel);
            ts.ComputeCoverage(pixels);
            return ts;
        }

        public void SaveAttributes(Tileset tileset)
        {
            var image = Path.Combine(_root, tileset.ImagePath);
            var slice = SliceFile.LoadFor(image) ?? new SliceFile(tileset.Slice);
            var file = new SliceFile(slice.Settings, slice.Kind, tileset.Attributes) { CharacterLayout = slice.CharacterLayout, Id = tileset.Id };
            file.SaveFor(image);
        }

        public string FullPath(string id) => PathOf(id) is string rel ? Path.Combine(_root, rel) : null;
    }

    /// <summary>Imágenes PNG en disco (lector y escritor propios, sin Unity).</summary>
    public sealed class PngImageRepository : IImageRepository
    {
        public PixelImage Load(string path) => Png.Read(path);

        /// <summary>Writes to a temporary file first so a crash never leaves a broken image.</summary>
        public void Save(string path, PixelImage image)
        {
            var tmp = path + ".tmp";
            Png.Write(image, tmp);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }

    /// <summary>
    /// «datos/metodos_encuentro.json»: los métodos de encuentro del proyecto. Los clásicos siempre están (si el archivo los
    /// cambió, con sus cambios); el autor añade los suyos («volando»...).
    /// </summary>
    public sealed class JsonEncounterMethodRepository : IEncounterMethodRepository
    {
        public const string FileName = "metodos_encuentro.json";
        private static readonly string[] TriggerKeys = { "pisar_terreno", "pisar", "usar_objeto", "interactuar", "evento" };
        private readonly string _path;

        public JsonEncounterMethodRepository(string projectRoot) { _path = Path.Combine(projectRoot, ProjectLayout.DataFolder, FileName); }

        public EncounterMethodCatalog Load()
        {
            var catalog = EncounterMethodCatalog.Classic();
            if (!File.Exists(_path)) return catalog;
            foreach (var m in Json.ParseObject(File.ReadAllText(_path)).GetArray("metodos") ?? new List<object>())
            {
                if (!(m is JsonObject mo) || !(mo.GetString("id") is string id)) continue;
                var builtIn = catalog.Find(id)?.BuiltIn ?? false;
                var method = new EncounterMethod(id, mo.GetString("nombre", id),
                    (EncounterTrigger)Math.Max(0, Array.IndexOf(TriggerKeys, mo.GetString("cuando", "evento"))), mo.GetInt("probabilidad", 10),
                    (mo.GetArray("terrenos") ?? new List<object>()).OfType<double>().Select(d => (int)d).ToArray())
                    { ItemId = mo.GetString("objeto", ""), BuiltIn = builtIn };
                catalog.Add(method);
            }
            return catalog;
        }

        public void Save(EncounterMethodCatalog catalog)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var list = catalog.All.Select(m => (object)new JsonObject()
                .Set("id", m.Id).Set("nombre", m.Label).Set("cuando", TriggerKeys[(int)m.Trigger]).Set("probabilidad", m.DefaultRate)
                .Set("terrenos", m.TerrainTags.Cast<object>().ToList()).Set("objeto", m.ItemId)).ToList();
            JsonMapRepository.WriteSafely(_path, Json.Write(new JsonObject().Set("metodos", list)));
        }
    }

    /// <summary>Las especies del proyecto, leídas de «datos/especies.csv» (columnas id y nombre; las de los packs sirven).</summary>
    public sealed class CsvSpeciesDirectory : ISpeciesDirectory
    {
        private readonly string _path;
        private DateTime _stamp;
        private List<(string id, string name)> _cache;

        public CsvSpeciesDirectory(string projectRoot) { _path = Path.Combine(projectRoot, ProjectLayout.DataFolder, "especies.csv"); }

        public IReadOnlyList<(string id, string name)> All()
        {
            if (!File.Exists(_path)) return new (string, string)[0];
            var stamp = File.GetLastWriteTimeUtc(_path);
            if (_cache != null && stamp == _stamp) return _cache;
            var rows = Csv.Read(File.ReadAllText(_path));
            var list = new List<(string, string)>();
            if (rows.Count > 0)
            {
                int id = rows[0].FindIndex(h => h.Trim().Equals("id", StringComparison.OrdinalIgnoreCase));
                int name = rows[0].FindIndex(h => h.Trim().Equals("nombre", StringComparison.OrdinalIgnoreCase));
                if (id >= 0)
                    foreach (var r in rows.Skip(1))
                        if (r.Count > id && r[id].Trim().Length > 0)
                            list.Add((r[id].Trim(), name >= 0 && r.Count > name && r[name].Trim().Length > 0 ? r[name].Trim() : r[id].Trim()));
            }
            _stamp = stamp;
            return _cache = list;
        }
    }

    /// <summary>CSV como los de CTEditor: «;», comillas dobles para textos con «;» o saltos, BOM opcional.</summary>
    public static class Csv
    {
        public static List<List<string>> Read(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool quoted = false;
            int i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; break;
                    case ';': row.Add(cell.ToString()); cell.Clear(); break;
                    case '\r': break;
                    case '\n': row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); break;
                    default: cell.Append(c); break;
                }
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }

    /// <summary>Copia las hojas de un pack (Gen1…Gen7) a «datos/» del proyecto: especies, movimientos, objetos...</summary>
    public static class PackInstaller
    {
        /// <summary>Hojas copiadas (sin extensión). 'overwrite' = sustituir las que ya existan.</summary>
        public static IReadOnlyList<string> Install(string packFolder, string projectRoot, bool overwrite)
        {
            var dest = Path.Combine(projectRoot, ProjectLayout.DataFolder);
            Directory.CreateDirectory(dest);
            var done = new List<string>();
            foreach (var file in Directory.GetFiles(packFolder, "*.csv"))
            {
                var target = Path.Combine(dest, Path.GetFileName(file));
                if (File.Exists(target) && !overwrite) continue;
                File.Copy(file, target, true);
                done.Add(Path.GetFileNameWithoutExtension(file));
            }
            return done;
        }

        /// <summary>Los packs de una carpeta («Gen1», «Gen2»...), ordenados.</summary>
        public static IReadOnlyList<string> Find(string packsRoot) =>
            Directory.Exists(packsRoot)
                ? Directory.GetDirectories(packsRoot).Where(d => File.Exists(Path.Combine(d, "especies.csv")))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList()
                : (IReadOnlyList<string>)new string[0];
    }

    /// <summary>El mapa de la región generado: la imagen (retocable) y dónde queda cada tramo.</summary>
    public static class RegionMapFiles
    {
        public const string ImagePath = "graficos/interfaz/mapa_region.png";
        public const string DataPath = "datos/mapa_region.json";

        public static void Save(string projectRoot, PixelImage image, IEnumerable<RegionMapArea> areas)
        {
            var img = Path.Combine(projectRoot, ImagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(img));
            new PngImageRepository().Save(img, image);
            var list = areas.Select(a => (object)new JsonObject().Set("mapa", a.MapId).Set("nombre", a.Name)
                .Set("tipo", JsonMapRepository.CategoryKey(a.Category))
                .Set("x", a.Rect.X).Set("y", a.Rect.Y).Set("ancho", a.Rect.Width).Set("alto", a.Rect.Height)).ToList();
            var data = Path.Combine(projectRoot, DataPath);
            Directory.CreateDirectory(Path.GetDirectoryName(data));
            JsonMapRepository.WriteSafely(data, Json.Write(new JsonObject().Set("imagen", ImagePath).Set("zonas", list)));
        }
    }
}
