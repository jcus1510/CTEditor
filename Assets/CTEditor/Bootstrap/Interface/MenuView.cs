using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// UN MENÚ EN PANTALLA, dibujado a partir de su definición (editor de Menús): caja con borde en la
    /// esquina elegida, opciones en lista o rejilla, cursor ▶, flechas con repetición, Confirmar y
    /// Cancelar. Si hay muchas opciones, se desplaza. También funciona con el ratón (clic = elegir).
    ///
    ///   var menu = MenuView.Open(definición);
    ///   yield return menu.Wait();
    ///   if (menu.Result == null) → canceló; si no, menu.Result es la opción elegida.
    ///   menu.Close();
    /// </summary>
    public sealed class MenuView : MonoBehaviour, IMenuPresenter
    {
        // Dónde se quedó el cursor de cada menú (si el menú lo recuerda).
        private static readonly Dictionary<string, int> Remembered = new Dictionary<string, int>();
        /// <summary>Cierra todos los menús abiertos (al interrumpir una pantalla a medias).</summary>
        public static void CloseAll() => MenuStack.CloseAll();

        public const int MaxRowsDefault = 8;

        private MenuDefinition _def;
        private List<MenuOption> _options;
        private Func<MenuOption, bool> _enabled;
        private MenuCursor _cursor;
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private IReadOnlyDictionary<string, string> _vars;
        private TextMeshProUGUI _help, _upArrow, _downArrow;
        private RectTransform _helpFrame;
        private int _topRow, _maxRows;
        private float _openedAt;

        public MenuOption Result { get; private set; }
        public int ResultIndex { get; private set; } = -1;
        public bool Done { get; private set; }
        public int Index => _cursor?.Index ?? -1;

        /// <summary>Se llama cada vez que el cursor cambia de opción (para enseñar detalles al lado).</summary>
        public event Action<int> Moved;

        /// <summary>Abre un menú. 'hasFlag' decide qué opciones se ven; 'enabled', cuáles se pueden elegir.</summary>
        public static MenuView Open(MenuDefinition def, Func<string, bool> hasFlag = null, Func<MenuOption, bool> enabled = null,
            IReadOnlyDictionary<string, string> vars = null, int maxRows = MaxRowsDefault, float width = 0f, int startIndex = -1)
        {
            var root = UiRoot.Instance.Rect;
            var go = new GameObject("Menú " + def.Id, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var view = go.AddComponent<MenuView>();
            view.Build(def, hasFlag, enabled, vars, maxRows, width, startIndex);
            MenuStack.Push(view);
            UiRoot.Register(view);
            return view;
        }

        /// <summary>Menú rápido de una lista de textos (equipo, objetos...). El id de cada opción es su índice.</summary>
        public static MenuView OpenList(string id, string title, IList<string> labels, MenuAnchor anchor = MenuAnchor.Center,
            Func<int, bool> enabled = null, IList<string> help = null, float width = 640f, int maxRows = 6, bool remember = false)
        {
            var def = new MenuDefinition(id, title) { Title = title, Anchor = anchor, RememberCursor = remember };
            for (int i = 0; i < labels.Count; i++)
                def.Add(new MenuOption(i.ToString(), labels[i], MenuActionKind.ScreenAction, i.ToString(), help: help != null && i < help.Count ? help[i] : ""));
            return Open(def, null, enabled == null ? null : (Func<MenuOption, bool>)(o => enabled(int.Parse(o.Id))), null, maxRows, width);
        }

        public IEnumerator Wait() { while (!Done) yield return null; }

        public void Close()
        {
            if (this == null) return;
            MenuStack.Remove(this);
            UiRoot.Unregister(this);
            Done = true;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Done = true;
            MenuStack.Remove(this);
            UiRoot.Unregister(this);
            if (_helpFrame != null) Destroy(_helpFrame.gameObject);
        }

        // ---------------- Construcción ----------------

        private void Build(MenuDefinition def, Func<string, bool> hasFlag, Func<MenuOption, bool> enabled,
            IReadOnlyDictionary<string, string> vars, int maxRows, float width, int startIndex)
        {
            _def = def;
            _vars = vars;
            _enabled = enabled ?? (_ => true);
            _options = def.VisibleOptions(hasFlag ?? (_ => false));
            int cols = Math.Max(1, def.Columns);
            int rows = Math.Max(1, (_options.Count + cols - 1) / cols);
            _maxRows = Math.Max(1, Math.Min(rows, maxRows));

            float font = UiKit.FontSize;
            float rowH = font * 1.45f;
            float colW = width > 0 ? (width - 24f) / cols
                : Math.Max(font * 5f, _options.Select(o => TextPager.VisibleLength(Label(o))).DefaultIfEmpty(4).Max() * font * 0.62f + font * 1.6f);
            float titleH = string.IsNullOrWhiteSpace(def.Title) ? 0f : rowH;
            bool anyHelp = _options.Any(o => !string.IsNullOrWhiteSpace(o.Help));
            float w = cols * colW + 24f, h = _maxRows * rowH + titleH + 20f;

            var frame = (RectTransform)transform;
            var inner = UiKit.FramedBox(frame, "Caja");
            var boxRect = (RectTransform)inner.parent;
            boxRect.anchorMin = Vector2.zero; boxRect.anchorMax = Vector2.one; boxRect.offsetMin = boxRect.offsetMax = Vector2.zero;
            // Abajo, se pone ENCIMA de la caja de texto si está a la vista (como el Sí/No de los juegos).
            float reserve = anyHelp ? 70f : 0f;
            var box = UiRoot.Existing != null ? UiRoot.Existing.ExistingText : null;
            if (box != null && box.gameObject.activeSelf && (def.Anchor == MenuAnchor.BottomRight || def.Anchor == MenuAnchor.BottomLeft || def.Anchor == MenuAnchor.BottomFull))
                reserve = Math.Max(reserve, box.Height + 4f);
            UiKit.Anchor(frame, def.Anchor, w, h, 16f, reserve);

            if (titleH > 0)
            {
                var title = UiKit.Label(inner, "Título", TextTokens.Replace(def.Title, _vars ?? new Dictionary<string, string>()), font, UiKit.Ink);
                title.fontStyle = FontStyles.Bold;
                UiKit.TopLeft(title.rectTransform, 12, 8, w - 24, rowH);
            }

            for (int r = 0; r < _maxRows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int slot = r * cols + c;
                    var label = UiKit.Label(inner, "Opción " + slot, "", font, UiKit.Ink);
                    UiKit.TopLeft(label.rectTransform, 12 + c * colW, 8 + titleH + r * rowH, colW, rowH);
                    // Clic con el ratón: primero selecciona; si ya estaba, elige.
                    label.raycastTarget = true;
                    var button = label.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    int s = slot;
                    button.onClick.AddListener(() => Click(s));
                    _labels.Add(label);
                }

            _upArrow = UiKit.Label(inner, "Más arriba", "▲", font * 0.6f, UiKit.Highlight, TextAlignmentOptions.Center);
            UiKit.TopLeft(_upArrow.rectTransform, w - 40, 0, 24, 18);
            _downArrow = UiKit.Label(inner, "Más abajo", "▼", font * 0.6f, UiKit.Highlight, TextAlignmentOptions.Center);
            UiKit.TopLeft(_downArrow.rectTransform, w - 40, h - 30, 24, 18);

            if (anyHelp)
            {
                // La ayuda va en su propia caja abajo (hija del lienzo); se destruye al cerrar el menú.
                var helpInner = UiKit.FramedBox(UiRoot.Instance.Rect, "Ayuda del menú");
                _helpFrame = (RectTransform)helpInner.parent;
                UiKit.Anchor(_helpFrame, MenuAnchor.BottomFull, 0, 60f, 16f);
                _help = UiKit.Label(helpInner, "Texto", "", font * 0.8f, UiKit.Ink);
                _help.rectTransform.anchorMin = Vector2.zero; _help.rectTransform.anchorMax = Vector2.one;
                _help.rectTransform.offsetMin = new Vector2(14, 4); _help.rectTransform.offsetMax = new Vector2(-14, -4);
            }

            int start = startIndex >= 0 ? startIndex : def.RememberCursor && Remembered.TryGetValue(def.Id, out var r0) ? r0 : 0;
            _cursor = new MenuCursor(_options.Count, cols, def.Wrap, i => _enabled(_options[i]), Math.Min(start, Math.Max(0, _options.Count - 1)));
            _openedAt = Time.unscaledTime;
            Refresh();
        }

        private string Label(MenuOption o) => TextTokens.Replace(o.Label, _vars ?? new Dictionary<string, string>());

        // ---------------- Dibujo ----------------

        private void Refresh()
        {
            int cols = Math.Max(1, _def.Columns);
            int row = _cursor.Count == 0 ? 0 : _cursor.Index / cols;
            if (row < _topRow) _topRow = row;
            if (row >= _topRow + _maxRows) _topRow = row - _maxRows + 1;
            string cursor = GameInput.Settings.CursorSymbol;

            for (int slot = 0; slot < _labels.Count; slot++)
            {
                int i = _topRow * cols + slot;
                var label = _labels[slot];
                if (i >= _options.Count) { label.text = ""; continue; }
                bool sel = i == _cursor.Index;
                bool on = _enabled(_options[i]);
                label.text = TextSafety.Clean((sel ? cursor + " " : "   ") + Label(_options[i]), label.font);
                label.color = !on ? UiKit.Disabled : sel ? UiKit.Highlight : UiKit.Ink;
            }
            int totalRows = (_options.Count + cols - 1) / cols;
            _upArrow.gameObject.SetActive(_topRow > 0);
            _downArrow.gameObject.SetActive(_topRow + _maxRows < totalRows);
            if (_help != null) TextSafety.Set(_help, _cursor.Count > 0 ? _options[_cursor.Index].Help : "");
        }

        // ---------------- Entrada ----------------

        private void Update()
        {
            if (Done || _cursor == null) return;
            var box = UiRoot.Existing != null ? UiRoot.Existing.ExistingText : null;
            if (box != null && box.IsSaying) return; // primero se lee el texto
            // Si hay otro menú abierto ENCIMA, este espera.
            if (!MenuStack.IsTop(this)) return;

            var d = GameInput.RepeatedDirection();
            if (d != Direction.None && _cursor.Move(d)) { Refresh(); Moved?.Invoke(_cursor.Index); }

            if (GameInput.Pressed(GameButton.Confirm)) { GameInput.Consume(GameButton.Confirm); Choose(_cursor.Index); }
            else if (GameInput.Pressed(GameButton.Cancel) && Time.unscaledTime > _openedAt)
            {
                GameInput.Consume(GameButton.Cancel);
                GameInput.Consume(GameButton.Menu); // misma tecla en muchos juegos: que no reabra el menú
                Cancel();
            }
        }

        private void Click(int slot)
        {
            int i = _topRow * Math.Max(1, _def.Columns) + slot;
            if (i >= _options.Count || Done) return;
            if (i == _cursor.Index) Choose(i);
            else if (_enabled(_options[i])) { _cursor.Set(i); Refresh(); Moved?.Invoke(i); }
        }

        private void Choose(int i)
        {
            if (i < 0 || i >= _options.Count || !_enabled(_options[i])) return;
            if (_def.RememberCursor) Remembered[_def.Id] = i;
            Result = _options[i];
            ResultIndex = i;
            Done = true;
        }

        private void Cancel()
        {
            if (_def.RememberCursor) Remembered[_def.Id] = _cursor.Index;
            if (!_def.CancelCloses)
            {
                int i = _options.FindIndex(o => o.Id == _def.CancelOptionId);
                if (i >= 0) Choose(i);
                return; // Cancelar no hace nada en este menú (p. ej. el de combate)
            }
            Result = null;
            ResultIndex = -1;
            Done = true;
        }
    }
}
