using System;
using System.Collections.Generic;
using System.Globalization;
using CTEditor.Project;

namespace CTEditor.Workspace
{
    /// <summary>Un color de la interfaz que el usuario puede cambiar.</summary>
    public sealed class ThemeToken
    {
        public string Id { get; }
        public string Label { get; }
        public ThemeToken(string id, string label) { Id = id; Label = label; }
    }

    /// <summary>
    /// Colores de la aplicación. Oscuro por defecto; el usuario puede elegir otro tema o cambiar cualquier color. Colores en
    /// «#RRGGBB» o «#RRGGBBAA».
    /// </summary>
    public sealed class Theme
    {
        public static IReadOnlyList<ThemeToken> Tokens { get; } = new[]
        {
            new ThemeToken("fondo", "Fondo de la ventana"),
            new ThemeToken("panel", "Fondo de los paneles"),
            new ThemeToken("panel_alt", "Filas alternas y cabeceras"),
            new ThemeToken("borde", "Bordes y separadores"),
            new ThemeToken("texto", "Texto"),
            new ThemeToken("texto_suave", "Texto secundario"),
            new ThemeToken("acento", "Acento (botones, selección activa)"),
            new ThemeToken("seleccion", "Selección"),
            new ThemeToken("rejilla", "Rejilla del mapa"),
            new ThemeToken("aviso", "Avisos"),
            new ThemeToken("error", "Errores"),
            new ThemeToken("exito", "Correcto"),
        };

        private readonly Dictionary<string, string> _colors = new Dictionary<string, string>();

        public string Name { get; set; }

        public Theme(string name, IDictionary<string, string> colors = null)
        {
            Name = name;
            if (colors != null) foreach (var kv in colors) Set(kv.Key, kv.Value);
        }

        /// <summary>Color de un token («#RRGGBB[AA]»); si falta, el del tema oscuro.</summary>
        public string Get(string token) =>
            _colors.TryGetValue(token, out var c) ? c : ReferenceEquals(this, DarkPreset) ? "#FF00FF" : DarkPreset.Get(token);

        public void Set(string token, string color)
        {
            if (!IsColor(color)) throw new ArgumentException($"«{color}» no es un color (usa #RRGGBB o #RRGGBBAA).", nameof(color));
            _colors[token] = color.ToUpperInvariant();
        }

        public Theme Clone(string name = null) => new Theme(name ?? Name, _colors);

        public static bool IsColor(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '#' || (s.Length != 7 && s.Length != 9)) return false;
            return uint.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        public JsonObject ToJson()
        {
            var colors = new JsonObject();
            foreach (var t in Tokens) colors[t.Id] = Get(t.Id);
            return new JsonObject().Set("nombre", Name).Set("colores", colors);
        }

        public static Theme FromJson(JsonObject o)
        {
            var theme = new Theme(o?.GetString("nombre", "Personalizado") ?? "Personalizado");
            var colors = o?.GetObject("colores");
            if (colors != null)
                foreach (var kv in colors)
                    if (kv.Value is string c && IsColor(c)) theme.Set(kv.Key, c);
            return theme;
        }

        // ── Presets ──────────────────────────────────────────────────────────────────────────────

        private static Theme _dark;

        public static Theme DarkPreset => _dark ??= new Theme("Oscuro", new Dictionary<string, string>
        {
            { "fondo", "#1E1F22" }, { "panel", "#2B2D30" }, { "panel_alt", "#313338" }, { "borde", "#43454A" },
            { "texto", "#DFE1E5" }, { "texto_suave", "#9DA0A8" }, { "acento", "#4E8CF7" }, { "seleccion", "#2E436E" },
            { "rejilla", "#FFFFFF33" }, { "aviso", "#E0B34A" }, { "error", "#E0605A" }, { "exito", "#5FB865" },
        });

        public static Theme Dark() => DarkPreset.Clone();

        public static Theme Light() => new Theme("Claro", new Dictionary<string, string>
        {
            { "fondo", "#E9EAEC" }, { "panel", "#F7F8FA" }, { "panel_alt", "#EDEEF1" }, { "borde", "#C9CCD1" },
            { "texto", "#1F2328" }, { "texto_suave", "#5F6570" }, { "acento", "#2F6FDB" }, { "seleccion", "#C9DBFB" },
            { "rejilla", "#00000033" }, { "aviso", "#9A6B00" }, { "error", "#C0362F" }, { "exito", "#2E7D32" },
        });

        public static Theme HighContrast() => new Theme("Alto contraste", new Dictionary<string, string>
        {
            { "fondo", "#000000" }, { "panel", "#0A0A0A" }, { "panel_alt", "#1A1A1A" }, { "borde", "#FFFFFF" },
            { "texto", "#FFFFFF" }, { "texto_suave", "#D0D0D0" }, { "acento", "#FFD400" }, { "seleccion", "#005FCC" },
            { "rejilla", "#FFFFFF66" }, { "aviso", "#FFD400" }, { "error", "#FF5555" }, { "exito", "#55FF55" },
        });

        public static IReadOnlyList<Func<Theme>> Presets { get; } = new Func<Theme>[] { Dark, Light, HighContrast };
    }
}
