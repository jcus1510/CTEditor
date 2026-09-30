using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTEditor.Project;

namespace CTEditor.Workspace
{
    /// <summary>Una acción con atajo de teclado.</summary>
    public sealed class ShortcutAction
    {
        public string Id { get; }
        public string Label { get; }
        public string DefaultKeys { get; }
        public ShortcutAction(string id, string label, string defaultKeys) { Id = id; Label = label; DefaultKeys = defaultKeys; }
    }

    /// <summary>
    /// Atajos de teclado, cambiables. Formato «Ctrl+Mayús+Z», «F5», «Ctrl+F5» (se guardan normalizados: Ctrl, Alt, Mayús y
    /// la tecla). Avisa si dos acciones comparten atajo.
    /// </summary>
    public sealed class ShortcutMap
    {
        public static IReadOnlyList<ShortcutAction> Actions { get; } = new[]
        {
            new ShortcutAction("jugar", "Jugar desde el principio", "F5"),
            new ShortcutAction("probar_aqui", "Probar desde aquí", "Ctrl+F5"),
            new ShortcutAction("depurador", "Depurador (durante el juego)", "F9"),
            new ShortcutAction("pantalla_completa", "Pantalla completa / ventana", "F11"),
            new ShortcutAction("guardar", "Guardar", "Ctrl+S"),
            new ShortcutAction("deshacer", "Deshacer", "Ctrl+Z"),
            new ShortcutAction("rehacer", "Rehacer", "Ctrl+Y"),
            new ShortcutAction("buscar", "Buscar en todo el proyecto", "Ctrl+P"),
            new ShortcutAction("lapiz", "Herramienta: lápiz", "B"),
            new ShortcutAction("relleno", "Herramienta: relleno", "G"),
            new ShortcutAction("rectangulo", "Herramienta: rectángulo", "U"),
            new ShortcutAction("cuentagotas", "Herramienta: cuentagotas", "I"),
            new ShortcutAction("goma", "Herramienta: goma", "E"),
            new ShortcutAction("seleccion", "Herramienta: selección", "M"),
            new ShortcutAction("rejilla", "Mostrar u ocultar la rejilla", "Ctrl+G"),
            new ShortcutAction("capa_siguiente", "Capa siguiente", "PageDown"),
            new ShortcutAction("capa_anterior", "Capa anterior", "PageUp"),
        };

        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

        public ShortcutMap() { foreach (var a in Actions) _keys[a.Id] = a.DefaultKeys; }

        public string KeysFor(string action) => _keys.TryGetValue(action, out var k) ? k : "";

        /// <summary>Cambia el atajo ("" = sin atajo). Devuelve las acciones que ya lo usaban (se quedan sin atajo).</summary>
        public IReadOnlyList<string> Rebind(string action, string keys)
        {
            if (Actions.All(a => a.Id != action)) throw new ArgumentException($"No existe la acción «{action}».", nameof(action));
            var norm = Normalize(keys);
            var clashes = new List<string>();
            if (norm.Length > 0)
                foreach (var kv in _keys.ToList())
                    if (kv.Key != action && kv.Value == norm) { clashes.Add(kv.Key); _keys[kv.Key] = ""; }
            _keys[action] = norm;
            return clashes;
        }

        /// <summary>¿Qué acción tiene ese atajo? (null = ninguna).</summary>
        public string ActionFor(string keys)
        {
            var norm = Normalize(keys);
            return norm.Length == 0 ? null : _keys.FirstOrDefault(kv => kv.Value == norm).Key;
        }

        public void ResetToDefaults() { foreach (var a in Actions) _keys[a.Id] = a.DefaultKeys; }

        /// <summary>«mayús + ctrl + z» → «Ctrl+Mayús+Z».</summary>
        public static string Normalize(string keys)
        {
            if (string.IsNullOrWhiteSpace(keys)) return "";
            bool ctrl = false, alt = false, shift = false;
            string key = "";
            foreach (var raw in keys.Split('+'))
            {
                var p = raw.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": case "cmd": ctrl = true; break;
                    case "alt": alt = true; break;
                    case "mayús": case "mayus": case "shift": shift = true; break;
                    case "": break;
                    default: key = p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p.Substring(1); break;
                }
            }
            if (key.Length == 0) return "";
            return (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Mayús+" : "") + key;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            foreach (var a in Actions) o[a.Id] = KeysFor(a.Id);
            return o;
        }

        public static ShortcutMap FromJson(JsonObject o)
        {
            var map = new ShortcutMap();
            if (o == null) return map;
            foreach (var a in Actions)
                if (o[a.Id] is string k) map._keys[a.Id] = Normalize(k);
            return map;
        }
    }

    /// <summary>
    /// El ENTORNO DE TRABAJO de cada usuario (no del proyecto): tema, distribución de paneles, distribuciones guardadas con
    /// nombre, escala de la interfaz, tamaño de letra y atajos. Se guarda en la carpeta del usuario («entorno.json»).
    /// Por defecto: tema oscuro y distribución «Clásico (RPG Maker)».
    /// </summary>
    public sealed class WorkspaceSettings
    {
        public const string FileName = "entorno.json";
        public const float MinScale = 0.75f, MaxScale = 2f;

        public Theme Theme { get; set; } = Theme.Dark();
        public DockLayout Layout { get; set; } = DockLayout.Classic();
        /// <summary>Distribuciones que el usuario guardó con nombre.</summary>
        public List<DockLayout> SavedLayouts { get; } = new List<DockLayout>();
        public ShortcutMap Shortcuts { get; private set; } = new ShortcutMap();

        private float _uiScale = 1f;
        public float UiScale { get => _uiScale; set => _uiScale = Math.Max(MinScale, Math.Min(MaxScale, value)); }

        private int _fontSize = 13;
        public int FontSize { get => _fontSize; set => _fontSize = Math.Max(9, Math.Min(24, value)); }

        /// <summary>Últimos proyectos abiertos (el primero, el más reciente).</summary>
        public List<string> RecentProjects { get; } = new List<string>();

        /// <summary>Guarda la distribución actual con un nombre (sustituye a otra con el mismo nombre).</summary>
        public void SaveLayoutAs(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Pon un nombre a la distribución.", nameof(name));
            SavedLayouts.RemoveAll(l => l.Name == name);
            SavedLayouts.Add(Layout.Clone(name));
        }

        /// <summary>Pasa a una distribución guardada o de fábrica (por nombre). false si no existe.</summary>
        public bool UseLayout(string name)
        {
            var found = SavedLayouts.FirstOrDefault(l => l.Name == name)
                        ?? DockLayout.Presets.Select(p => p()).FirstOrDefault(l => l.Name == name);
            if (found == null) return false;
            Layout = found.Clone();
            return true;
        }

        public void NoteRecentProject(string path, int max = 10)
        {
            RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            RecentProjects.Insert(0, path);
            if (RecentProjects.Count > max) RecentProjects.RemoveRange(max, RecentProjects.Count - max);
        }

        public JsonObject ToJson() => new JsonObject()
            .Set("tema", Theme.ToJson())
            .Set("distribucion", Layout.ToJson())
            .Set("distribuciones_guardadas", SavedLayouts.Select(l => (object)l.ToJson()).ToList())
            .Set("escala", Math.Round(UiScale, 2))
            .Set("tamaño_letra", FontSize)
            .Set("atajos", Shortcuts.ToJson())
            .Set("recientes", RecentProjects.Cast<object>().ToList());

        public static WorkspaceSettings FromJson(JsonObject o)
        {
            var w = new WorkspaceSettings();
            if (o == null) return w;
            if (o.GetObject("tema") is JsonObject t) w.Theme = Theme.FromJson(t);
            if (o.GetObject("distribucion") is JsonObject l) w.Layout = DockLayout.FromJson(l);
            foreach (var s in o.GetArray("distribuciones_guardadas") ?? new List<object>())
                if (s is JsonObject so) w.SavedLayouts.Add(DockLayout.FromJson(so));
            w.UiScale = o.GetFloat("escala", 1f);
            w.FontSize = o.GetInt("tamaño_letra", 13);
            w.Shortcuts = ShortcutMap.FromJson(o.GetObject("atajos"));
            foreach (var r in o.GetArray("recientes") ?? new List<object>())
                if (r is string rs) w.RecentProjects.Add(rs);
            return w;
        }

        /// <summary>Lee el entorno; si no existe o está dañado, el de fábrica (nunca falla al arrancar).</summary>
        public static WorkspaceSettings Load(string path)
        {
            try
            {
                return File.Exists(path) ? FromJson(Json.ParseObject(File.ReadAllText(path))) : new WorkspaceSettings();
            }
            catch (FormatException) { return new WorkspaceSettings(); }
            catch (IOException) { return new WorkspaceSettings(); }
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, Json.Write(ToJson()));
        }
    }
}
