using System;
using System.Collections.Generic;
using System.IO;

namespace CTEditor.Project
{
    /// <summary>Un tamaño de tile que se ofrece al crear un proyecto.</summary>
    public sealed class TileSizePreset
    {
        public int Size { get; }
        public string Label { get; }
        public TileSizePreset(int size, string label) { Size = size; Label = label; }
    }

    /// <summary>
    /// «proyecto.json»: lo básico de un juego. La base es RPG Maker XP / Pokémon Essentials (tiles de 32 px, pantalla de
    /// 512 × 384), para que importar sus proyectos sea directo; el tamaño del tile se elige al crear el proyecto.
    /// </summary>
    public sealed class ProjectSettings
    {
        public const int CurrentFormat = 1;
        public const int DefaultTileSize = 32;
        public const int DefaultScreenWidth = 512;
        public const int DefaultScreenHeight = 384;

        public static IReadOnlyList<TileSizePreset> TileSizes { get; } = new[]
        {
            new TileSizePreset(32, "32 px — RPG Maker XP / Essentials (recomendado)"),
            new TileSizePreset(16, "16 px — estilo GBA / DS"),
            new TileSizePreset(48, "48 px — RPG Maker MV / MZ"),
            new TileSizePreset(24, "24 px"),
        };

        public string Name { get; set; } = "Mi juego";
        public int TileSize { get; set; } = DefaultTileSize;
        public int ScreenWidth { get; set; } = DefaultScreenWidth;
        public int ScreenHeight { get; set; } = DefaultScreenHeight;
        /// <summary>Mapa donde empieza la partida (vacío = aún no hay).</summary>
        public string StartMap { get; set; } = "";
        /// <summary>Casilla de inicio en ese mapa.</summary>
        public int StartX { get; set; }
        public int StartY { get; set; }
        /// <summary>Hoja de personaje del jugador (ruta relativa de su imagen; vacío = la primera que haya).</summary>
        public string PlayerCharacter { get; set; } = "";
        /// <summary>Variocolor en TODO el juego: 1 entre N (4096 desde la 6.ª gen.; 8192 antes). Cada especie de un encuentro puede tener la suya.</summary>
        public int ShinyOdds { get; set; } = DefaultShinyOdds;
        public const int DefaultShinyOdds = 4096;
        public int Format { get; set; } = CurrentFormat;

        /// <summary>Errores que impiden usar estos ajustes (vacío = bien).</summary>
        public IReadOnlyList<string> Problems()
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(Name)) list.Add("El proyecto necesita un nombre.");
            if (TileSize < 8 || TileSize > 256) list.Add("El tamaño del tile debe estar entre 8 y 256 px.");
            if (ScreenWidth < TileSize || ScreenHeight < TileSize) list.Add("La pantalla debe medir al menos un tile.");
            if (Format > CurrentFormat) list.Add($"El proyecto es de una versión más nueva de CTEditor (formato {Format}).");
            return list;
        }

        public JsonObject ToJson() => new JsonObject()
            .Set("formato", Format)
            .Set("nombre", Name)
            .Set("tamaño_tile", TileSize)
            .Set("pantalla_ancho", ScreenWidth)
            .Set("pantalla_alto", ScreenHeight)
            .Set("mapa_inicial", StartMap)
            .Set("inicio_x", StartX)
            .Set("inicio_y", StartY)
            .Set("personaje_jugador", PlayerCharacter)
            .Set("variocolor", ShinyOdds);

        public static ProjectSettings FromJson(JsonObject o) => new ProjectSettings
        {
            Format = o.GetInt("formato", CurrentFormat),
            Name = o.GetString("nombre", "Mi juego"),
            TileSize = o.GetInt("tamaño_tile", DefaultTileSize),
            ScreenWidth = o.GetInt("pantalla_ancho", DefaultScreenWidth),
            ScreenHeight = o.GetInt("pantalla_alto", DefaultScreenHeight),
            StartMap = o.GetString("mapa_inicial", ""),
            StartX = o.GetInt("inicio_x"),
            StartY = o.GetInt("inicio_y"),
            PlayerCharacter = o.GetString("personaje_jugador", ""),
            ShinyOdds = Math.Max(1, o.GetInt("variocolor", DefaultShinyOdds)),
        };
    }

    /// <summary>Crear, abrir y guardar la carpeta de un proyecto.</summary>
    public static class ProjectFile
    {
        public static string PathIn(string root) => Path.Combine(root, ProjectLayout.ProjectFile);
        public static bool IsProject(string root) => File.Exists(PathIn(root));

        /// <summary>Crea un proyecto nuevo en 'root' (vacía o inexistente) con sus carpetas.</summary>
        public static ProjectSettings Create(string root, string name, int tileSize = ProjectSettings.DefaultTileSize)
        {
            if (IsProject(root)) throw new InvalidOperationException("Ya hay un proyecto en esa carpeta.");
            var settings = new ProjectSettings { Name = name, TileSize = tileSize };
            var problems = settings.Problems();
            if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems));
            ProjectLayout.CreateFolders(root);
            Save(root, settings);
            return settings;
        }

        public static ProjectSettings Load(string root)
        {
            if (!IsProject(root)) throw new FileNotFoundException($"No hay «{ProjectLayout.ProjectFile}» en esa carpeta.", PathIn(root));
            return ProjectSettings.FromJson(Json.ParseObject(File.ReadAllText(PathIn(root))));
        }

        public static void Save(string root, ProjectSettings settings)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(PathIn(root), Json.Write(settings.ToJson()));
        }
    }
}
