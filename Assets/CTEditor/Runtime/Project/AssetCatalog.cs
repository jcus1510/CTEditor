using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CTEditor.Project
{
    /// <summary>Para qué sirve una imagen, según la carpeta de «graficos/» en la que está.</summary>
    public enum AssetKind
    {
        Other = 0,
        Tileset = 1,
        Character = 2,
        Battle = 3,
        Icon = 4,
        Interface = 5,
        Portrait = 6
    }

    /// <summary>
    /// Nombres de las carpetas y archivos de un proyecto (en español, porque el autor los ve). Ver
    /// docs/PROPUESTA_APLICACION.md, «Carpeta de proyecto».
    /// </summary>
    public static class ProjectLayout
    {
        public const string ProjectFile = "proyecto.json";
        public const string DataFolder = "datos";
        public const string MapsFolder = "mapas";
        public const string EventsFolder = "eventos";
        public const string GraphicsFolder = "graficos";
        public const string AudioFolder = "audio";
        /// <summary>Junto a cada imagen: cómo se corta y qué es cada tile («casa.png» → «casa.corte.json»).</summary>
        public const string SliceSuffix = ".corte.json";

        /// <summary>Subcarpeta de «graficos/» de cada tipo.</summary>
        public static readonly IReadOnlyDictionary<AssetKind, string> KindFolders = new Dictionary<AssetKind, string>
        {
            { AssetKind.Tileset, "tilesets" },
            { AssetKind.Character, "personajes" },
            { AssetKind.Battle, "combate" },
            { AssetKind.Icon, "iconos" },
            { AssetKind.Interface, "interfaz" },
            { AssetKind.Portrait, "retratos" },
        };

        public static readonly string[] ImageExtensions = { ".png" };
        /// <summary>Imágenes que se ven en la carpeta pero hay que convertir a PNG para usarlas (BMP, DIB).</summary>
        public static readonly string[] ConvertibleExtensions = { ".bmp", ".dib" };

        /// <summary>Tipo de una ruta relativa a la raíz del proyecto («graficos/tilesets/pueblo.png» → Tileset).</summary>
        public static AssetKind KindOf(string relativePath)
        {
            var parts = Normalize(relativePath).Split('/');
            if (parts.Length < 3 || !parts[0].Equals(GraphicsFolder, StringComparison.OrdinalIgnoreCase)) return AssetKind.Other;
            foreach (var kv in KindFolders)
                if (parts[1].Equals(kv.Value, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            return AssetKind.Other;
        }

        public static string SlicePathFor(string imagePath) =>
            Path.Combine(Path.GetDirectoryName(imagePath) ?? "", Path.GetFileNameWithoutExtension(imagePath) + SliceSuffix);

        /// <summary>Crea las carpetas de un proyecto nuevo (las que falten).</summary>
        public static void CreateFolders(string root)
        {
            foreach (var f in new[] { DataFolder, MapsFolder, EventsFolder, AudioFolder })
                Directory.CreateDirectory(Path.Combine(root, f));
            foreach (var f in KindFolders.Values)
                Directory.CreateDirectory(Path.Combine(root, GraphicsFolder, f));
        }

        public static string Normalize(string path) => (path ?? "").Replace('\\', '/').TrimStart('/');
    }

    /// <summary>Una imagen encontrada en la carpeta del proyecto.</summary>
    public sealed class AssetEntry
    {
        /// <summary>Ruta relativa a la raíz, con «/» («graficos/tilesets/pueblo.png»).</summary>
        public string RelativePath { get; }
        public AssetKind Kind { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>¿Tiene ya su archivo de corte al lado?</summary>
        public bool IsSliced { get; }
        /// <summary>Si no se pudo leer (no es un PNG válido): por qué. Null = bien.</summary>
        public string Problem { get; }

        public AssetEntry(string relativePath, AssetKind kind, int width, int height, bool isSliced, string problem = null)
        {
            RelativePath = relativePath;
            Kind = kind;
            Width = width;
            Height = height;
            IsSliced = isSliced;
            Problem = problem;
        }

        public const string NeedsPngProblem = "Está en BMP: conviértela a PNG para cortarla y usarla.";
        /// <summary>¿Es un BMP/DIB que se puede convertir a PNG con un clic?</summary>
        public bool NeedsPng => Problem == NeedsPngProblem;

        public string Name => Path.GetFileNameWithoutExtension(RelativePath);
        public override string ToString() => $"{RelativePath} ({Width}×{Height})";
    }

    /// <summary>
    /// Lee la carpeta «graficos/» de un proyecto: cada imagen con su tipo (por la subcarpeta), su tamaño (solo la cabecera
    /// del PNG: rápido aunque haya miles) y si ya está cortada. Es lo que muestra el panel de recursos de la aplicación.
    /// </summary>
    public static class AssetCatalog
    {
        public static IReadOnlyList<AssetEntry> Scan(string projectRoot)
        {
            var graphics = Path.Combine(projectRoot, ProjectLayout.GraphicsFolder);
            if (!Directory.Exists(graphics)) return Array.Empty<AssetEntry>();
            var list = new List<AssetEntry>();
            foreach (var file in Directory.EnumerateFiles(graphics, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var rel = ProjectLayout.Normalize(Path.GetRelativePath(projectRoot, file));
                if (ProjectLayout.ConvertibleExtensions.Contains(ext))
                {
                    Bmp.TryReadSize(file, out int bw, out int bh);
                    list.Add(new AssetEntry(rel, ProjectLayout.KindOf(rel), bw, bh, false, AssetEntry.NeedsPngProblem));
                    continue;
                }
                if (!ProjectLayout.ImageExtensions.Contains(ext)) continue;
                bool sliced = File.Exists(ProjectLayout.SlicePathFor(file));
                string problem = null;
                int w = 0, h = 0;
                try
                {
                    if (!Png.TryReadSize(file, out w, out h)) problem = "No es un PNG válido.";
                }
                catch (IOException e) { problem = "No se pudo leer: " + e.Message; }
                catch (UnauthorizedAccessException e) { problem = "Sin permiso para leerlo: " + e.Message; }
                list.Add(new AssetEntry(rel, ProjectLayout.KindOf(rel), w, h, sliced, problem));
            }
            return list.OrderBy(e => e.Kind).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}

namespace CTEditor.Project
{
    /// <summary>
    /// Adivina qué es una imagen al importarla (para proponer la carpeta): primero por el nombre del archivo
    /// («Tileset», «battler», «icon»...) y si no, por las medidas (8 columnas de 32 px = tileset de RPG Maker XP, hojas de
    /// personaje 4 × 4 o 3 × 4, cuadrados de combate...). Solo propone: el autor elige.
    /// </summary>
    public static class AssetKindGuesser
    {
        private static readonly (string[] words, AssetKind kind)[] ByName =
        {
            (new[] { "autotile", "tileset", "tiles", "tile" }, AssetKind.Tileset),
            (new[] { "battler", "battle", "combate", "front", "back", "frente", "espalda" }, AssetKind.Battle),
            (new[] { "icon", "icono" }, AssetKind.Icon),
            (new[] { "portrait", "retrato", "face", "faceset", "busto" }, AssetKind.Portrait),
            (new[] { "windowskin", "window", "ventana", "interfaz", "interface", "hud", "title", "titulo", "picture" }, AssetKind.Interface),
            (new[] { "character", "personaje", "charset", "trainer", "entrenador", "npc", "player", "jugador", "walk", "overworld" }, AssetKind.Character),
        };

        public static (AssetKind kind, string reason) Guess(string fileName, int width, int height)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(fileName ?? "").ToLowerInvariant();
            foreach (var (words, kind) in ByName)
                foreach (var w in words)
                    if (name.Contains(w)) return (kind, $"el nombre contiene «{w}»");
            if (width <= 0 || height <= 0) return (AssetKind.Other, "no se pudo leer el tamaño");
            if (width == 256 && height % 32 == 0) return (AssetKind.Tileset, "8 columnas de 32 px: tileset de RPG Maker XP");
            if (width == height && width <= 48) return (AssetKind.Icon, "cuadrado pequeño");
            if (Art.Domain.CharacterSheetLayout.Detect(width, height) != null) return (AssetKind.Character, "medidas de hoja de personaje");
            if (width == height && width <= 192) return (AssetKind.Battle, "cuadrado: sprite de combate");
            if (width % 16 == 0 && height % 16 == 0 && width >= 64 && height >= 64) return (AssetKind.Tileset, "medidas de rejilla de tiles");
            return (AssetKind.Tileset, "imagen grande: probablemente un tileset");
        }
    }

    /// <summary>Cambia el tipo de una imagen ya importada: la mueve a la carpeta de ese tipo junto con su corte.</summary>
    public static class AssetMover
    {
        /// <returns>La nueva ruta relativa.</returns>
        public static string MoveToKind(string projectRoot, string relativePath, AssetKind kind)
        {
            var from = System.IO.Path.Combine(projectRoot, relativePath);
            if (!System.IO.File.Exists(from)) throw new System.IO.FileNotFoundException("No existe la imagen.", relativePath);
            if (!ProjectLayout.KindFolders.TryGetValue(kind, out var folder)) throw new ArgumentException("Tipo sin carpeta: " + kind);
            var destDir = System.IO.Path.Combine(projectRoot, ProjectLayout.GraphicsFolder, folder);
            System.IO.Directory.CreateDirectory(destDir);
            var to = System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(from));
            if (System.IO.Path.GetFullPath(to) == System.IO.Path.GetFullPath(from)) return relativePath;
            if (System.IO.File.Exists(to)) throw new System.IO.IOException($"Ya hay una imagen llamada «{System.IO.Path.GetFileName(to)}» en {folder}.");
            var slice = ProjectLayout.SlicePathFor(from);
            System.IO.File.Move(from, to);
            if (System.IO.File.Exists(slice)) System.IO.File.Move(slice, ProjectLayout.SlicePathFor(to));
            return ProjectLayout.Normalize(System.IO.Path.GetRelativePath(projectRoot, to));
        }
    }
}
