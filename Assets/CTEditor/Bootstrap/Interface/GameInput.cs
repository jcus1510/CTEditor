using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
#if CTEDITOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// LOS BOTONES DEL JUEGO en ejecución: «¿se pulsó Confirmar?», «¿qué flecha toca mover?».
    ///
    /// Cada botón (Confirmar, Cancelar, Menú, flechas, L, R...) es un IDENTIFICADOR con sus enlaces:
    ///   • Los del AUTOR (editor de Controles; si no hay ficha, los clásicos): teclas y botones de mando.
    ///   • Los del JUGADOR (Opciones → Controles), guardados en PlayerPrefs y aplicados ENCIMA.
    /// Sirve con el Input System (teclado + mando) o con el Input Manager antiguo (teclado + botones y stick).
    ///
    /// Un objeto oculto lo actualiza una vez por fotograma antes que nadie; Consume(...) evita que una
    /// pulsación sirva dos veces.
    /// </summary>
    public static class GameInput
    {
        private const string PlayerPrefsKey = "CTEditor.Controles";

        private static InputState _state;
        private static InterfaceSettings _settings;
        private static InterfaceSettingsData _style;
        private static InputBindings _defaults;          // los del autor
        private static ControlOverrides _player;         // los cambios del jugador
        private static int _lastFrame = -1;
        private static bool _loaded;

        /// <summary>Ajustes (teclas activas, texto) de la ficha «ajustes» o los clásicos, con los cambios del jugador.</summary>
        public static InterfaceSettings Settings { get { Load(); return _settings; } }

        /// <summary>La ficha de ajustes (colores, tamaño de letra...). Puede ser null (se usan los clásicos).</summary>
        public static InterfaceSettingsData Style { get { Load(); return _style; } }

        /// <summary>Los controles del autor (sin los cambios del jugador).</summary>
        public static InputBindings Defaults { get { Load(); return _defaults; } }

        /// <summary>Vuelve a leer los ajustes (tras cambiarlos en el editor con el juego en marcha).</summary>
        public static void Reload() { _loaded = false; _parsed.Clear(); Load(); }

        private const string TextSpeedKey = "CTEditor.VelocidadTexto";

        internal static void ResetStatics() { _loaded = false; _lastFrame = -1; _parsed.Clear(); }

        /// <summary>Velocidad del texto elegida por el jugador (Opciones); se guarda.</summary>
        public static void SetPlayerTextSpeed(float speed)
        {
            Load();
            _settings.TextSpeed = speed;
            PlayerPrefs.SetFloat(TextSpeedKey, speed);
            PlayerPrefs.Save();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var all = Resources.LoadAll<InterfaceSettingsData>(ContentFolders.Interface);
            _style = all.FirstOrDefault(a => a.Id == "ajustes") ?? all.FirstOrDefault();
            _settings = _style != null ? InterfaceMapper.ToDomain(_style) : new InterfaceSettings();
            _defaults = _settings.Bindings;
            _player = ControlOverrides.Parse(PlayerPrefs.GetString(PlayerPrefsKey, ""));
            _settings.Bindings = _player.Apply(_defaults);
            if (PlayerPrefs.HasKey(TextSpeedKey)) _settings.TextSpeed = PlayerPrefs.GetFloat(TextSpeedKey, _settings.TextSpeed);
            _state = new InputState { RepeatDelay = _defaults.RepeatDelay, RepeatInterval = _defaults.RepeatInterval };
        }

        // ---------------- Cambios del jugador ----------------

        /// <summary>El jugador pone 'binding' (tecla o botón de mando) en 'button'. Devuelve un aviso si hubo choque.</summary>
        public static string Rebind(GameButton button, string binding)
        {
            Load();
            string note = _player.Rebind(button, binding, _defaults);
            ApplyPlayer();
            // La tecla recién asignada sigue pulsada: que no cuente como una pulsación nueva al soltar el menú.
            _state.Update(0f, IsHeldNow);
            _state.ConsumeAll();
            return note;
        }

        /// <summary>Vuelve a los controles del autor (solo un tipo de un botón, o todo si button es null).</summary>
        public static void ResetPlayer(GameButton? button = null)
        {
            Load();
            if (button == null) _player.Clear();
            else { _player.Clear(button.Value, false); _player.Clear(button.Value, true); }
            ApplyPlayer();
        }

        public static bool PlayerChanged(GameButton b) { Load(); return _player.Has(b, false) || _player.Has(b, true); }

        private static void ApplyPlayer()
        {
            _settings.Bindings = _player.Apply(_defaults);
            PlayerPrefs.SetString(PlayerPrefsKey, _player.Serialize());
            PlayerPrefs.Save();
        }

        // ---------------- Estado por fotograma ----------------

        private static InputState State
        {
            get
            {
                Load();
                if (Time.frameCount != _lastFrame)
                {
                    _lastFrame = Time.frameCount;
                    _state.Update(Time.unscaledDeltaTime, IsHeldNow);
                }
                return _state;
            }
        }

        public static bool Pressed(GameButton b) => State.Pressed(b);
        public static bool Repeated(GameButton b) => State.Repeated(b);
        public static bool Held(GameButton b) => State.Held(b);
        public static bool Released(GameButton b) => State.Released(b);
        public static Direction RepeatedDirection() => State.RepeatedDirection();
        public static void Consume(GameButton b) => State.Consume(b);
        public static void ConsumeAll() => State.ConsumeAll();

        /// <summary>Actualiza el estado de los botones (lo llama cada fotograma un componente oculto).</summary>
        public static void Tick() { var _ = State; }

        // Un objeto oculto que actualiza la entrada CADA fotograma, antes que nadie: así no se pierde una
        // pulsación rápida aunque en ese momento ningún componente esté preguntando.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDriver()
        {
            var go = new GameObject("Entrada del juego (CTEditor)");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameInputDriver>();
        }

        /// <summary>¿Avanza el texto? (Confirmar, o Cancelar si así lo quiere el autor).</summary>
        public static bool AdvancePressed()
            => Pressed(GameButton.Confirm) || (Settings.CancelAdvancesText && Pressed(GameButton.Cancel));

        private static bool IsHeldNow(GameButton b)
        {
            foreach (var binding in _settings.Bindings.KeysOf(b))
                if (BindingHeld(binding)) return true;
            return false;
        }

        // ---------------- Capturar la siguiente tecla o botón (para cambiar controles) ----------------

        private static string[] _keyboardCandidates;

        /// <summary>
        /// Espera a que el jugador pulse UNA tecla (pad = false) o un botón del mando (pad = true) y la devuelve
        /// por 'result' (null si pasa el tiempo). Antes espera a que suelte lo que tuviera pulsado.
        /// </summary>
        public static IEnumerator Capture(bool pad, float timeout, Action<string> result)
        {
            if (_keyboardCandidates == null)
                _keyboardCandidates = Enum.GetNames(typeof(KeyCode))
                    .Where(n => n != "None" && !n.StartsWith("Mouse") && !n.StartsWith("Joystick")).Distinct().ToArray();
            var candidates = pad ? PadControls.All.Select(p => p.id).ToArray() : _keyboardCandidates;

            float t = 0f;
            while (candidates.Any(BindingHeld) && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }

            var before = new HashSet<string>(candidates.Where(BindingHeld));
            while (t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
                foreach (var c in candidates)
                {
                    bool now = BindingHeld(c);
                    if (now && !before.Contains(c)) { GameInput.ConsumeAll(); result(c); yield break; }
                    if (!now) before.Remove(c);
                }
            }
            result(null);
        }

        // ---------------- Lectura real de teclas y mando ----------------

        private static readonly Dictionary<string, int> _parsed = new Dictionary<string, int>();

        /// <summary>¿Está pulsado ahora este enlace (una tecla o un control de mando)?</summary>
        public static bool BindingHeld(string binding)
            => PadControls.IsPad(binding) ? PadHeld(binding) : KeyHeld(binding);

        private static bool KeyHeld(string key)
        {
#if CTEDITOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (!_parsed.TryGetValue(key, out int k)) _parsed[key] = k = Enum.TryParse<Key>(ToInputSystemName(key), true, out var parsed) ? (int)parsed : -1;
                if (k > 0) return kb[(Key)k].isPressed;
#if !ENABLE_LEGACY_INPUT_MANAGER
                return false;
#endif
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!_parsed.TryGetValue("legacy:" + key, out int code))
                _parsed["legacy:" + key] = code = Enum.TryParse<KeyCode>(key, true, out var kc) ? (int)kc : -1;
            return code > 0 && Input.GetKey((KeyCode)code);
#else
            return false;
#endif
        }

        private const float StickThreshold = 0.6f;

        private static bool PadHeld(string id)
        {
            string c = id.Substring(PadControls.Prefix.Length);
#if CTEDITOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null)
            {
                switch (c)
                {
                    case "South": return pad.buttonSouth.isPressed;
                    case "East": return pad.buttonEast.isPressed;
                    case "West": return pad.buttonWest.isPressed;
                    case "North": return pad.buttonNorth.isPressed;
                    case "Start": return pad.startButton.isPressed;
                    case "Select": return pad.selectButton.isPressed;
                    case "LB": return pad.leftShoulder.isPressed;
                    case "RB": return pad.rightShoulder.isPressed;
                    case "LT": return pad.leftTrigger.isPressed;
                    case "RT": return pad.rightTrigger.isPressed;
                    case "DpadUp": return pad.dpad.up.isPressed;
                    case "DpadDown": return pad.dpad.down.isPressed;
                    case "DpadLeft": return pad.dpad.left.isPressed;
                    case "DpadRight": return pad.dpad.right.isPressed;
                }
                var l = pad.leftStick.ReadValue();
                var r = pad.rightStick.ReadValue();
                switch (c)
                {
                    case "LStickUp": return l.y > StickThreshold;
                    case "LStickDown": return l.y < -StickThreshold;
                    case "LStickLeft": return l.x < -StickThreshold;
                    case "LStickRight": return l.x > StickThreshold;
                    case "RStickUp": return r.y > StickThreshold;
                    case "RStickDown": return r.y < -StickThreshold;
                    case "RStickLeft": return r.x < -StickThreshold;
                    case "RStickRight": return r.x > StickThreshold;
                }
                return false;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            // Input Manager antiguo: botones del mando 0-7 (distribución de Xbox) y el stick izquierdo por los
            // ejes «Horizontal»/«Vertical» del proyecto. La cruceta y los gatillos dependen de cada mando: aquí no.
            switch (c)
            {
                case "South": return Input.GetKey(KeyCode.JoystickButton0);
                case "East": return Input.GetKey(KeyCode.JoystickButton1);
                case "West": return Input.GetKey(KeyCode.JoystickButton2);
                case "North": return Input.GetKey(KeyCode.JoystickButton3);
                case "LB": return Input.GetKey(KeyCode.JoystickButton4);
                case "RB": return Input.GetKey(KeyCode.JoystickButton5);
                case "Select": return Input.GetKey(KeyCode.JoystickButton6);
                case "Start": return Input.GetKey(KeyCode.JoystickButton7);
                case "LStickUp": return Axis("Vertical") > StickThreshold;
                case "LStickDown": return Axis("Vertical") < -StickThreshold;
                case "LStickLeft": return Axis("Horizontal") < -StickThreshold;
                case "LStickRight": return Axis("Horizontal") > StickThreshold;
            }
#endif
            return false;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        private static bool _axesMissing;
        private static float Axis(string name)
        {
            if (_axesMissing) return 0f;
            try { return Input.GetAxisRaw(name); }
            catch (ArgumentException) { _axesMissing = true; return 0f; } // el proyecto no tiene ese eje
        }
#endif

        /// <summary>Nombres de tecla de Unity (KeyCode) → nombres del Input System (Key).</summary>
        public static string ToInputSystemName(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            switch (key)
            {
                case "Return": return "Enter";
                case "KeypadEnter": return "NumpadEnter";
                case "LeftControl": return "LeftCtrl";
                case "RightControl": return "RightCtrl";
                case "LeftCommand": case "LeftWindows": return "LeftMeta";
                case "RightCommand": case "RightWindows": return "RightMeta";
                case "BackQuote": return "Backquote";
                case "KeypadPlus": return "NumpadPlus";
                case "KeypadMinus": return "NumpadMinus";
                case "KeypadMultiply": return "NumpadMultiply";
                case "KeypadDivide": return "NumpadDivide";
                case "KeypadPeriod": return "NumpadPeriod";
                case "KeypadEquals": return "NumpadEquals";
            }
            if (key.StartsWith("Alpha") && key.Length == 6) return "Digit" + key[5];
            if (key.StartsWith("Keypad") && key.Length == 7 && char.IsDigit(key[6])) return "Numpad" + key[6];
            return key;
        }
    }

    /// <summary>Actualiza GameInput cada fotograma, antes que el resto de componentes.</summary>
    [DefaultExecutionOrder(-1000)]
    internal sealed class GameInputDriver : MonoBehaviour
    {
        private void Update() => GameInput.Tick();
    }
}
