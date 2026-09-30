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
                if (!ProjectLayout.ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                var rel = ProjectLayout.Normalize(Path.GetRelativePath(projectRoot, file));
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
