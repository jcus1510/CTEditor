using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Adventure.Domain.Interface
{
    /// <summary>
    /// Los BOTONES DEL JUEGO (como en una consola): el juego nunca pregunta «¿se pulsó la Z?», sino
    /// «¿se pulsó Confirmar?». Qué teclas son cada botón lo decide el autor en el editor de Controles.
    /// </summary>
    public enum GameButton
    {
        Up, Down, Left, Right,
        Confirm,   // A: aceptar, hablar, avanzar texto
        Cancel,    // B: atrás, cerrar, correr
        Menu,      // Start: abrir el menú de pausa
        Select,    // Select: atajo (objeto registrado, ordenar...)
        L,         // gatillo izquierdo: cambiar de bolsillo / página
        R          // gatillo derecho
    }

    public enum Direction { None, Up, Down, Left, Right }

    public static class GameButtons
    {
        public static readonly GameButton[] All = (GameButton[])Enum.GetValues(typeof(GameButton));
        public static readonly GameButton[] Directions = { GameButton.Up, GameButton.Down, GameButton.Left, GameButton.Right };

        /// <summary>Nombre en español para editores y mensajes.</summary>
        public static string NameOf(GameButton b)
        {
            switch (b)
            {
                case GameButton.Up: return "Arriba";
                case GameButton.Down: return "Abajo";
                case GameButton.Left: return "Izquierda";
                case GameButton.Right: return "Derecha";
                case GameButton.Confirm: return "Confirmar (A)";
                case GameButton.Cancel: return "Cancelar (B)";
                case GameButton.Menu: return "Menú (Start)";
                case GameButton.Select: return "Especial (Select)";
                case GameButton.L: return "L (página anterior)";
                default: return "R (página siguiente)";
            }
        }

        public static Direction ToDirection(GameButton b)
        {
            switch (b)
            {
                case GameButton.Up: return Direction.Up;
                case GameButton.Down: return Direction.Down;
                case GameButton.Left: return Direction.Left;
                case GameButton.Right: return Direction.Right;
                default: return Direction.None;
            }
        }
    }

    /// <summary>
    /// CONTROLES DE MANDO, por nombre ("Pad/South"...). Un enlace puede ser una tecla ("Z", "UpArrow": los
    /// nombres de KeyCode de Unity) o un control de mando (empieza por "Pad/"). La capa de Unity los lee con
    /// el Input System (o con el Input Manager antiguo, que entiende los botones y el stick izquierdo).
    /// </summary>
    public static class PadControls
    {
        public const string Prefix = "Pad/";

        public static readonly (string id, string name)[] All =
        {
            ("Pad/South", "A / Cruz (botón de abajo)"), ("Pad/East", "B / Círculo (botón de la derecha)"),
            ("Pad/West", "X / Cuadrado (botón de la izquierda)"), ("Pad/North", "Y / Triángulo (botón de arriba)"),
            ("Pad/Start", "Start / Options"), ("Pad/Select", "Select / Share"),
            ("Pad/LB", "Gatillo L1 / LB"), ("Pad/RB", "Gatillo R1 / RB"), ("Pad/LT", "Gatillo L2 / LT"), ("Pad/RT", "Gatillo R2 / RT"),
            ("Pad/DpadUp", "Cruceta ↑"), ("Pad/DpadDown", "Cruceta ↓"), ("Pad/DpadLeft", "Cruceta ←"), ("Pad/DpadRight", "Cruceta →"),
            ("Pad/LStickUp", "Stick izquierdo ↑"), ("Pad/LStickDown", "Stick izquierdo ↓"), ("Pad/LStickLeft", "Stick izquierdo ←"), ("Pad/LStickRight", "Stick izquierdo →"),
            ("Pad/RStickUp", "Stick derecho ↑"), ("Pad/RStickDown", "Stick derecho ↓"), ("Pad/RStickLeft", "Stick derecho ←"), ("Pad/RStickRight", "Stick derecho →"),
        };

        public static bool IsPad(string binding) => binding != null && binding.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        public static bool IsKnown(string binding) => All.Any(p => string.Equals(p.id, binding, StringComparison.OrdinalIgnoreCase));

        /// <summary>Nombre corto (sin la explicación entre paréntesis), para listas.</summary>
        public static string Short(string binding)
        {
            string f = Friendly(binding);
            int i = f.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? f.Substring(0, i) : f;
        }

        /// <summary>Nombre amigable de cualquier enlace (tecla o mando), para menús y editores.</summary>
        public static string Friendly(string binding)
        {
            if (string.IsNullOrEmpty(binding)) return "—";
            if (IsPad(binding))
            {
                var p = All.FirstOrDefault(x => string.Equals(x.id, binding, StringComparison.OrdinalIgnoreCase));
                return p.name ?? binding.Substring(Prefix.Length);
            }
            switch (binding)
            {
                case "UpArrow": return "↑";
                case "DownArrow": return "↓";
                case "LeftArrow": return "←";
                case "RightArrow": return "→";
                case "Return": return "Intro";
                case "Escape": return "Esc";
                case "Space": return "Espacio";
                case "Backspace": return "Retroceso";
                case "LeftShift": return "Mayús izq.";
                case "RightShift": return "Mayús der.";
                case "LeftControl": return "Ctrl izq.";
                case "RightControl": return "Ctrl der.";
                case "Tab": return "Tab";
            }
            if (binding.StartsWith("Alpha") && binding.Length == 6) return binding.Substring(5);
            if (binding.StartsWith("Keypad")) return "Num " + binding.Substring(6);
            return binding;
        }
    }

    /// <summary>
    /// QUÉ TECLAS son cada botón, y cómo se repite una flecha mantenida. Las teclas se guardan por su
    /// NOMBRE (el de KeyCode de Unity: "UpArrow", "Z", "Return", "Escape"...); la capa de Unity las traduce
    /// al sistema de entrada que use el proyecto (el antiguo o el Input System).
    /// Un botón puede tener varias teclas (Flechas y WASD a la vez).
    /// </summary>
    public sealed class InputBindings
    {
        private readonly Dictionary<GameButton, List<string>> _keys = new Dictionary<GameButton, List<string>>();

        /// <summary>Segundos que hay que mantener una flecha antes de que empiece a repetirse.</summary>
        public float RepeatDelay { get; set; } = 0.40f;
        /// <summary>Segundos entre repeticiones mientras se mantiene.</summary>
        public float RepeatInterval { get; set; } = 0.09f;

        public InputBindings()
        {
            foreach (var b in GameButtons.All) _keys[b] = new List<string>();
        }

        /// <summary>Todos los enlaces del botón (teclas y mando).</summary>
        public IReadOnlyList<string> KeysOf(GameButton b) => _keys[b];
        /// <summary>Solo las teclas del teclado.</summary>
        public List<string> Keyboard(GameButton b) => _keys[b].Where(k => !PadControls.IsPad(k)).ToList();
        /// <summary>Solo los controles del mando.</summary>
        public List<string> Pad(GameButton b) => _keys[b].Where(PadControls.IsPad).ToList();

        /// <summary>Cambia los enlaces de UN tipo (teclado o mando) de un botón, sin tocar los del otro.</summary>
        public void Replace(GameButton b, bool pad, IEnumerable<string> bindings)
        {
            _keys[b].RemoveAll(k => PadControls.IsPad(k) == pad);
            foreach (var k in bindings ?? Enumerable.Empty<string>())
            {
                string key = (k ?? "").Trim();
                if (key.Length > 0 && PadControls.IsPad(key) == pad && !_keys[b].Contains(key)) _keys[b].Add(key);
            }
        }

        public void Remove(GameButton b, string binding) => _keys[b].Remove(binding);

        public InputBindings Bind(GameButton b, params string[] keys)
        {
            foreach (var k in keys)
            {
                string key = (k ?? "").Trim();
                if (key.Length > 0 && !_keys[b].Contains(key)) _keys[b].Add(key);
            }
            return this;
        }

        public void Clear(GameButton b) => _keys[b].Clear();

        /// <summary>Botones que usan esa tecla.</summary>
        public List<GameButton> ButtonsOf(string key)
            => GameButtons.All.Where(b => _keys[b].Contains(key)).ToList();

        /// <summary>
        /// Problemas de la configuración, en español: un botón sin tecla, o una tecla en dos botones que
        /// se usan a la vez (Confirmar y Cancelar con la misma tecla, o dos direcciones).
        /// Menú y Cancelar SÍ pueden compartir tecla (como Esc/X en muchos juegos: en el mapa abre el
        /// menú y dentro de un menú vuelve atrás).
        /// </summary>
        public List<string> Problems()
        {
            var list = new List<string>();
            foreach (var b in GameButtons.All)
                if (Keyboard(b).Count == 0 && b != GameButton.Select && b != GameButton.L && b != GameButton.R)
                    list.Add($"«{GameButtons.NameOf(b)}» no tiene ninguna tecla.");
            var allKeys = GameButtons.All.SelectMany(b => _keys[b]).Distinct();
            foreach (var k in allKeys)
            {
                var users = ButtonsOf(k);
                if (users.Count < 2) continue;
                bool okPair = users.Count == 2 && users.Contains(GameButton.Cancel) && users.Contains(GameButton.Menu);
                if (!okPair) list.Add($"{(PadControls.IsPad(k) ? "El botón de mando" : "La tecla")} «{PadControls.Friendly(k)}» está en {string.Join(" y ", users.Select(GameButtons.NameOf))}.");
            }
            return list;
        }

        public InputBindings Copy()
        {
            var c = new InputBindings { RepeatDelay = RepeatDelay, RepeatInterval = RepeatInterval };
            foreach (var b in GameButtons.All) c.Bind(b, _keys[b].ToArray());
            return c;
        }
    }

    /// <summary>
    /// LOS CONTROLES QUE ELIGE EL JUGADOR (pantalla Opciones → Controles), guardados aparte de los del autor:
    /// para cada botón del juego y cada tipo (teclado / mando), su lista de enlaces. Lo que el jugador no
    /// cambió sigue siendo lo del autor. Se guarda como texto sencillo (PlayerPrefs en Unity):
    ///   "Confirm.teclado=K,Space;Cancel.mando=Pad/East"
    /// </summary>
    public sealed class ControlOverrides
    {
        private readonly Dictionary<(GameButton b, bool pad), List<string>> _map = new Dictionary<(GameButton, bool), List<string>>();

        public bool IsEmpty => _map.Count == 0;
        public bool Has(GameButton b, bool pad) => _map.ContainsKey((b, pad));
        public IReadOnlyList<string> Get(GameButton b, bool pad) => _map.TryGetValue((b, pad), out var l) ? l : null;

        public void Set(GameButton b, bool pad, IEnumerable<string> bindings) => _map[(b, pad)] = (bindings ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
        public void Clear() => _map.Clear();
        public void Clear(GameButton b, bool pad) => _map.Remove((b, pad));

        /// <summary>Los controles que valen: los del autor con los cambios del jugador encima.</summary>
        public InputBindings Apply(InputBindings defaults)
        {
            var result = defaults.Copy();
            foreach (var kv in _map) result.Replace(kv.Key.b, kv.Key.pad, kv.Value);
            // Red de seguridad: un botón imprescindible nunca se queda sin teclas (el jugador podría atascarse).
            foreach (var b in Essential)
                foreach (bool pad in new[] { false, true })
                {
                    var now = pad ? result.Pad(b) : result.Keyboard(b);
                    var def = pad ? defaults.Pad(b) : defaults.Keyboard(b);
                    if (now.Count == 0 && def.Count > 0) result.Replace(b, pad, def);
                }
            return result;
        }

        /// <summary>
        /// El jugador pone 'binding' en 'button'. Si ese enlace ya lo usaba OTRO botón (que se pueda pulsar a
        /// la vez), se le quita allí para que no haya choques (salvo Cancelar/Menú, que pueden compartir).
        /// Devuelve la frase para el jugador ("Esa tecla era de Cancelar: se la quitamos").
        /// </summary>
        /// <summary>Botones sin los que no se puede jugar: nunca se quedan sin teclas.</summary>
        public static readonly GameButton[] Essential =
            { GameButton.Up, GameButton.Down, GameButton.Left, GameButton.Right, GameButton.Confirm, GameButton.Cancel, GameButton.Menu };

        public string Rebind(GameButton button, string binding, InputBindings defaults)
        {
            bool pad = PadControls.IsPad(binding);
            var current = Apply(defaults);
            var previous = (pad ? current.Pad(button) : current.Keyboard(button)).Where(k => k != binding).ToList();
            string note = "";
            foreach (var other in GameButtons.All)
            {
                if (other == button || !current.KeysOf(other).Contains(binding)) continue;
                bool okPair = (button == GameButton.Cancel && other == GameButton.Menu) || (button == GameButton.Menu && other == GameButton.Cancel);
                if (okPair) continue;
                var rest = current.KeysOf(other).Where(k => PadControls.IsPad(k) == pad && k != binding).ToList();
                if (rest.Count == 0 && previous.Count > 0)
                {
                    // Se quedaría sin nada: se INTERCAMBIAN (el otro se queda con las de este que nadie más use).
                    var other2 = other;
                    var give = previous.Where(k => !GameButtons.All.Any(x => x != button && x != other2 && current.KeysOf(x).Contains(k))).ToList();
                    if (give.Count == 0) give = previous;
                    Set(other, pad, give);
                    note += $" {GameButtons.NameOf(other)} pasa a «{string.Join(", ", give.Select(PadControls.Short))}» (intercambio).";
                }
                else
                {
                    Set(other, pad, rest);
                    note += $" «{PadControls.Friendly(binding)}» era de {GameButtons.NameOf(other)}: se le quitó allí.";
                }
            }
            Set(button, pad, new[] { binding });
            return note.Trim();
        }

        public string Serialize()
            => string.Join(";", _map.OrderBy(k => k.Key.b).ThenBy(k => k.Key.pad)
                .Select(kv => $"{kv.Key.b}.{(kv.Key.pad ? "mando" : "teclado")}={string.Join(",", kv.Value)}"));

        public static ControlOverrides Parse(string text)
        {
            var o = new ControlOverrides();
            if (string.IsNullOrWhiteSpace(text)) return o;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('='), dot = part.IndexOf('.');
                if (eq < 0 || dot < 0 || dot > eq) continue;
                if (!Enum.TryParse<GameButton>(part.Substring(0, dot).Trim(), out var b)) continue;
                bool pad = part.Substring(dot + 1, eq - dot - 1).Trim() == "mando";
                o.Set(b, pad, part.Substring(eq + 1).Split(',').Where(k => PadControls.IsPad(k) == pad));
            }
            return o;
        }
    }

    /// <summary>
    /// ESTADO DE LOS BOTONES fotograma a fotograma (puro: se prueba sin Unity). Cada fotograma recibe
    /// «qué botones están apretados» y calcula:
    ///   • Pressed: se acaba de apretar (un solo fotograma).
    ///   • Repeated: Pressed, o sigue apretado y toca repetir (para bajar por una lista manteniendo la flecha).
    ///   • Consume: marca un botón como «ya usado» este fotograma, para que al abrir un menú con Confirmar
    ///     ese mismo Confirmar no elija también la primera opción.
    /// </summary>
    public sealed class InputState
    {
        private readonly Dictionary<GameButton, bool> _held = new Dictionary<GameButton, bool>();
        private readonly Dictionary<GameButton, bool> _pressed = new Dictionary<GameButton, bool>();
        private readonly Dictionary<GameButton, bool> _released = new Dictionary<GameButton, bool>();
        private readonly Dictionary<GameButton, bool> _repeat = new Dictionary<GameButton, bool>();
        private readonly Dictionary<GameButton, float> _heldTime = new Dictionary<GameButton, float>();
        private readonly Dictionary<GameButton, float> _nextRepeat = new Dictionary<GameButton, float>();
        private readonly HashSet<GameButton> _consumed = new HashSet<GameButton>();

        public float RepeatDelay { get; set; } = 0.40f;
        public float RepeatInterval { get; set; } = 0.09f;

        public InputState()
        {
            foreach (var b in GameButtons.All) { _held[b] = _pressed[b] = _released[b] = _repeat[b] = false; _heldTime[b] = 0f; _nextRepeat[b] = 0f; }
        }

        /// <summary>Avanza un fotograma: 'isHeld' dice si cada botón está apretado AHORA.</summary>
        public void Update(float deltaTime, Func<GameButton, bool> isHeld)
        {
            _consumed.Clear();
            foreach (var b in GameButtons.All)
            {
                bool now = isHeld != null && isHeld(b);
                bool before = _held[b];
                _pressed[b] = now && !before;
                _released[b] = !now && before;
                _repeat[b] = false;
                if (_pressed[b]) { _heldTime[b] = 0f; _nextRepeat[b] = RepeatDelay; _repeat[b] = true; }
                else if (now)
                {
                    _heldTime[b] += Math.Max(0f, deltaTime);
                    if (_heldTime[b] >= _nextRepeat[b])
                    {
                        _repeat[b] = true;
                        // Si el fotograma fue muy largo no se disparan varias repeticiones de golpe.
                        _nextRepeat[b] = _heldTime[b] + Math.Max(0.01f, RepeatInterval);
                    }
                }
                _held[b] = now;
            }
        }

        public bool Held(GameButton b) => _held[b];
        public bool Pressed(GameButton b) => _pressed[b] && !_consumed.Contains(b);
        public bool Released(GameButton b) => _released[b];
        public bool Repeated(GameButton b) => _repeat[b] && !_consumed.Contains(b);

        public void Consume(GameButton b) => _consumed.Add(b);
        public void ConsumeAll() { foreach (var b in GameButtons.All) _consumed.Add(b); }

        /// <summary>La dirección que toca mover este fotograma (con repetición), o None.</summary>
        public Direction RepeatedDirection()
        {
            foreach (var b in GameButtons.Directions)
                if (Repeated(b)) return GameButtons.ToDirection(b);
            return Direction.None;
        }
    }
}
