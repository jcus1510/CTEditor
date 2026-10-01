using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTEditor.Project;

namespace CTEditor.Workspace
{
    /// <summary>Una acción con atajo de teclado, en una categoría (General, Herramientas, Vista...).</summary>
    public sealed class ShortcutAction
    {
        public string Id { get; }
        public string Label { get; }
        public string DefaultKeys { get; }
        public string Category { get; }
        public ShortcutAction(string id, string label, string defaultKeys, string category = "General")
        {
            Id = id; Label = label; DefaultKeys = defaultKeys; Category = category ?? "General";
        }
    }

    /// <summary>
    /// Atajos de teclado, cambiables. Formato «Ctrl+Mayús+Z», «F5», «Ctrl+F5» (se guardan normalizados: Ctrl, Alt, Mayús y
    /// la tecla). Avisa si dos acciones comparten atajo.
    /// </summary>
    public sealed class ShortcutMap
    {
        /// <summary>
        /// Atajos de fábrica: los de siempre en programas de dibujo y de mapas (Photoshop / Aseprite: B, E, G, I, M;
        /// Tiled y Pokémon Studio: herramientas de una letra; GB Studio: Ctrl + número para cambiar de vista, números
        /// para los modos; Unity: F para encuadrar). Todos se pueden cambiar en Entorno → Atajos de teclado.
        /// </summary>
        private static readonly List<ShortcutAction> ActionList = new List<ShortcutAction>
        {
            // General
            new ShortcutAction("guardar", "Guardar", "Ctrl+S"),
            new ShortcutAction("deshacer", "Deshacer", "Ctrl+Z"),
            new ShortcutAction("rehacer", "Rehacer", "Ctrl+Y"),
            new ShortcutAction("buscar", "Buscar en todo el proyecto", "Ctrl+P"),
            new ShortcutAction("nuevo_mapa", "Nuevo mapa…", "Ctrl+N"),
            new ShortcutAction("atajos", "Ver y cambiar los atajos", "F1"),
            new ShortcutAction("entorno", "Personalizar el entorno…", "Ctrl+Comma"),
            new ShortcutAction("pantalla_completa", "Pantalla completa / ventana", "F11"),
            // Jugar
            new ShortcutAction("jugar", "Jugar desde el principio", "F5", "Jugar"),
            new ShortcutAction("probar_aqui", "Probar desde la casilla del ratón", "Ctrl+F5", "Jugar"),
            new ShortcutAction("depurador", "Depurador (durante el juego)", "F9", "Jugar"),
            // Herramientas del mapa
            new ShortcutAction("lapiz", "Lápiz", "B", "Herramientas"),
            new ShortcutAction("rectangulo", "Rectángulo", "U", "Herramientas"),
            new ShortcutAction("relleno", "Relleno", "G", "Herramientas"),
            new ShortcutAction("goma", "Goma", "E", "Herramientas"),
            new ShortcutAction("cuentagotas", "Cuentagotas", "I", "Herramientas"),
            new ShortcutAction("seleccion", "Selección", "M", "Herramientas"),
            new ShortcutAction("inicio", "Colocar el inicio del jugador", "P", "Herramientas"),
            new ShortcutAction("zona", "Pintar la zona de encuentros", "H", "Herramientas"),
            // Selección
            new ShortcutAction("seleccionar_todo", "Seleccionar todo el mapa", "Ctrl+A", "Selección"),
            new ShortcutAction("deseleccionar", "Quitar la selección", "Ctrl+D", "Selección"),
            // Vista
            new ShortcutAction("rejilla", "Mostrar u ocultar la rejilla", "Ctrl+G", "Vista"),
            new ShortcutAction("vecinos", "Mostrar u ocultar los tramos vecinos", "N", "Vista"),
            new ShortcutAction("acercar", "Acercar", "Z", "Vista"),
            new ShortcutAction("alejar", "Alejar", "Mayús+Z", "Vista"),
            new ShortcutAction("encuadrar", "Encuadrar: ver el tramo entero", "F", "Vista"),
            // Capas
            new ShortcutAction("capa_siguiente", "Capa siguiente", "PageDown", "Capas"),
            new ShortcutAction("capa_anterior", "Capa anterior", "PageUp", "Capas"),
            new ShortcutAction("capa_nueva", "Nueva capa", "Ctrl+Mayús+N", "Capas"),
            new ShortcutAction("capa_ver", "Mostrar u ocultar la capa elegida", "Ctrl+H", "Capas"),
            new ShortcutAction("capa_bloquear", "Bloquear o desbloquear la capa elegida", "Ctrl+Mayús+H", "Capas"),
            new ShortcutAction("capas_auto", "Capas automáticas sí / no", "Ctrl+L", "Capas"),
            // Tiles: modes by number (like the collision modes of GB Studio)
            new ShortcutAction("modo_pintar", "Tiles: pintar", "Alpha1", "Tiles"),
            new ShortcutAction("modo_paso", "Tiles: paso", "Alpha2", "Tiles"),
            new ShortcutAction("modo_prioridad", "Tiles: prioridad", "Alpha3", "Tiles"),
            new ShortcutAction("modo_terreno", "Tiles: terreno", "Alpha4", "Tiles"),
            new ShortcutAction("modo_arbusto", "Tiles: arbusto", "Alpha5", "Tiles"),
            new ShortcutAction("modo_mostrador", "Tiles: mostrador", "Alpha6", "Tiles"),
            new ShortcutAction("modo_pieza", "Tiles: pieza (capa automática)", "Alpha7", "Tiles"),
            // Ventanas: Ctrl + number (like the views of GB Studio)
            new ShortcutAction("ventana_mapa", "Ir a la ventana Mapa", "Ctrl+Alpha1", "Ventanas"),
            new ShortcutAction("ventana_paleta", "Ir a la ventana Tiles", "Ctrl+Alpha2", "Ventanas"),
            new ShortcutAction("ventana_capas", "Ir a la ventana Capas", "Ctrl+Alpha3", "Ventanas"),
            new ShortcutAction("ventana_mundo", "Ir a la ventana Mundo", "Ctrl+Alpha4", "Ventanas"),
            new ShortcutAction("ventana_encuentros", "Ir a la ventana Encuentros", "Ctrl+Alpha5", "Ventanas"),
            new ShortcutAction("ventana_recursos", "Ir a la ventana Recursos", "Ctrl+Alpha6", "Ventanas"),
            new ShortcutAction("ventana_retoque", "Ir a la ventana Retoque", "Ctrl+Alpha7", "Ventanas"),
            new ShortcutAction("ventana_inspector", "Ir a la ventana Propiedades", "Ctrl+Alpha8", "Ventanas"),
            new ShortcutAction("ventana_mapas", "Ir a la ventana Mapas", "Ctrl+Alpha9", "Ventanas"),
        };

        /// <summary>The order of the categories in lists (others go after, alphabetically).</summary>
        public static readonly string[] Categories = { "General", "Jugar", "Herramientas", "Selección", "Vista", "Capas", "Tiles", "Ventanas" };

        /// <summary>«Ctrl+Alpha1» → «Ctrl+1», «PageDown» → «Av Pág»: how a shortcut is shown to people.</summary>
        public static string Pretty(string keys)
        {
            if (string.IsNullOrEmpty(keys)) return "";
            var parts = keys.Split('+');
            var key = parts[parts.Length - 1];
            string pretty = key switch
            {
                "PageDown" => "Av Pág",
                "PageUp" => "Re Pág",
                "Delete" => "Supr",
                "Backspace" => "Retroceso",
                "Comma" => ",",
                "Period" => ".",
                "Space" => "Espacio",
                "Return" => "Intro",
                "Escape" => "Esc",
                "Equals" => "=",
                "Minus" => "-",
                "Plus" => "+",
                "LeftArrow" => "Izquierda",
                "RightArrow" => "Derecha",
                "UpArrow" => "Arriba",
                "DownArrow" => "Abajo",
                _ when key.StartsWith("Alpha") && key.Length == 6 => key.Substring(5),
                _ when key.StartsWith("Keypad") && key.Length == 7 => "Num " + key.Substring(6),
                _ => key,
            };
            parts[parts.Length - 1] = pretty;
            return string.Join("+", parts);
        }

        public static IReadOnlyList<ShortcutAction> Actions => ActionList;

        /// <summary>
        /// Un módulo nuevo añade sus acciones con atajo. Los entornos ya guardados la reciben con su atajo de fábrica (si ese
        /// atajo está libre).
        /// </summary>
        public static void RegisterAction(ShortcutAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            ActionList.RemoveAll(a => a.Id == action.Id);
            ActionList.Add(action);
        }

        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

        public ShortcutMap() { foreach (var a in Actions) _keys[a.Id] = a.DefaultKeys; }

        public string KeysFor(string action)
        {
            if (_keys.TryGetValue(action, out var k)) return k;
            // An action registered after this map was made: its default, unless someone else already uses it.
            var a = Actions.FirstOrDefault(x => x.Id == action);
            if (a == null) return "";
            var norm = Normalize(a.DefaultKeys);
            return _keys.Values.Contains(norm) ? "" : norm;
        }

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
            return norm.Length == 0 ? null : Actions.Select(a => a.Id).FirstOrDefault(id => KeysFor(id) == norm);
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
                    // A digit typed by hand is the number row («1» → «Alpha1», as Unity names that key).
                    default:
                        key = p.Length == 1 && char.IsDigit(p[0]) ? "Alpha" + p
                            : p == "," ? "Comma" : p == "." ? "Period"
                            : p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p.Substring(1);
                        break;
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

        public const float MinPanelScale = 0.6f, MaxPanelScale = 2.5f;
        private readonly Dictionary<string, float> _panelScales = new Dictionary<string, float>();

        /// <summary>Escala propia de una ventana (Alt + rueda sobre ella), por encima de la global. 1 = como las demás.</summary>
        public float PanelScale(string panelId) =>
            panelId != null && _panelScales.TryGetValue(panelId, out var s) ? s : 1f;

        public void SetPanelScale(string panelId, float scale)
        {
            if (string.IsNullOrEmpty(panelId)) return;
            scale = (float)Math.Round(Math.Max(MinPanelScale, Math.Min(MaxPanelScale, scale)), 2);
            if (Math.Abs(scale - 1f) < 0.001f) _panelScales.Remove(panelId);
            else _panelScales[panelId] = scale;
        }

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
            .Set("escala_ventanas", _panelScales.Aggregate(new JsonObject(), (j, kv) => j.Set(kv.Key, Math.Round(kv.Value, 2))))
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
            if (o.GetObject("escala_ventanas") is JsonObject ps)
                foreach (var key in ps.Keys) w.SetPanelScale(key, ps.GetFloat(key, 1f));
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
