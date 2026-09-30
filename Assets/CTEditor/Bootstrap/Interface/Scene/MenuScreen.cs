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
    /// UN MENÚ DISEÑADO EN LA ESCENA, con tus gráficos. Se pone en el objeto raíz del menú y dentro van las
    /// opciones (MenuItemView). El juego lo usa EN LUGAR del menú automático con el mismo id
    /// (pausa, si_no, equipo_miembro...). Crea uno desde GameObject → CTEditor UI.
    ///
    /// Todo lo visual lo haces con las herramientas normales de Unity: imágenes de fondo, marcos, fuentes,
    /// posiciones, animaciones... Aquí solo se configura:
    ///   • El id (qué menú del juego sustituye).
    ///   • Cómo se mueve el cursor: AUTOMÁTICO (va a la opción más cercana en la dirección de la flecha,
    ///     sirve para cualquier colocación), en vertical, en horizontal o en rejilla (orden de la jerarquía).
    ///   • Un CURSOR opcional (cualquier imagen) que se pone al lado de la opción elegida.
    ///   • LISTAS (equipo, mochila...): una opción de PLANTILLA que se copia para cada elemento.
    /// Mientras está cerrado, el objeto se oculta solo.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuScreen : MonoBehaviour, IMenuPresenter
    {
        public enum Navigation { Automatic, Vertical, Horizontal, Grid }
        public enum CancelMode { Close, ChooseOption, Nothing }

        [Tooltip("Qué menú del juego es: pausa, si_no, equipo_miembro, equipo_objeto, mochila_objeto, opciones, " +
                 "o una lista: equipo_lista, mochila_lista, olvidar, controles. También los tuyos (el id que abra otra opción).")]
        [SerializeField] private string screenId = ClassicInterface.PauseMenu;

        [Header("Cursor y flechas")]
        [SerializeField] private Navigation navigation = Navigation.Automatic;
        [Tooltip("Solo para «Rejilla»: cuántas opciones por fila.")]
        [SerializeField, Min(1)] private int columns = 2;
        [Tooltip("Al pasar del final, ¿vuelve al principio?")]
        [SerializeField] private bool wrap = true;
        [SerializeField] private bool rememberCursor = true;
        [Tooltip("Qué hace Cancelar: cerrar, elegir una opción (p. ej. «no» en Sí/No) o nada.")]
        [SerializeField] private CancelMode onCancel = CancelMode.Close;
        [SerializeField] private string cancelOptionId = "";
        [Tooltip("Opcional: una imagen o texto que se coloca junto a la opción elegida (una flecha, una mano...).")]
        [SerializeField] private RectTransform cursor;
        [Tooltip("Dónde se pone el cursor respecto al borde IZQUIERDO de la opción.")]
        [SerializeField] private Vector2 cursorOffset = new Vector2(-6, 0);

        [Header("Textos opcionales")]
        [Tooltip("Título (en listas lo pone el juego: «EQUIPO», «MOCHILA»...).")]
        [SerializeField] private TMP_Text titleText;
        [Tooltip("Aquí aparece la ayuda de la opción elegida.")]
        [SerializeField] private TMP_Text helpText;

        [Header("Lista (equipo, mochila...)")]
        [Tooltip("Si pones una opción de PLANTILLA, el menú es una LISTA: se copia para cada elemento (un miembro, un objeto).")]
        [SerializeField] private MenuItemView itemTemplate;
        [Tooltip("Cuántas filas se ven a la vez (las demás, bajando).")]
        [SerializeField, Min(1)] private int visibleRows = 6;
        [Tooltip("Opcional: se enseñan cuando hay más elementos arriba / abajo.")]
        [SerializeField] private GameObject moreAbove;
        [SerializeField] private GameObject moreBelow;

        // ---- registro de los menús de la escena ----
        private static readonly List<MenuScreen> Registry = new List<MenuScreen>();
        private static readonly Dictionary<string, int> Remembered = new Dictionary<string, int>();

        public string ScreenId => screenId;
        public bool IsList => itemTemplate != null;
        public MenuItemView ItemTemplate => itemTemplate;
        public Navigation NavigationMode => navigation;

        // ---- estado de la apertura actual ----
        private bool _isOpen;
        private readonly List<MenuItemView> _items = new List<MenuItemView>();     // las que se ven (o las filas de la lista)
        private readonly List<MenuOption> _options = new List<MenuOption>();       // una por elemento
        private Func<int, bool> _enabled = _ => true;
        private int _index, _top;
        private float _openedAt;
        private bool _remember;

        public bool Done { get; private set; } = true;
        public MenuOption Result { get; private set; }
        public int ResultIndex { get; private set; } = -1;

        /// <summary>Busca en la escena (también los ocultos) el menú con ese id.</summary>
        public static MenuScreen Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            Registry.RemoveAll(m => m == null);
            var found = Registry.FirstOrDefault(m => m.screenId == id);
            if (found != null) return found;
            foreach (var m in FindObjectsByType<MenuScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!Registry.Contains(m)) Registry.Add(m);
            return Registry.FirstOrDefault(m => m.screenId == id);
        }

        /// <summary>Todos los menús de escena (los usa el mapa de menús del editor).</summary>
        public static List<MenuScreen> AllInScene()
            => FindObjectsByType<MenuScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();

        /// <summary>Las opciones fijas (no la plantilla ni sus copias), en el orden de la jerarquía.</summary>
        public List<MenuItemView> StaticItems()
            => GetComponentsInChildren<MenuItemView>(true)
                .Where(i => !IsTemplateOrCopy(i) && i.GetComponentInParent<MenuScreen>(true) == this) // no las de un menú anidado
                .ToList();

        /// <summary>¿Se puede abrir? (sus padres deben estar activos; si no, el juego usa el menú automático)</summary>
        public bool CanOpen => transform.parent == null || transform.parent.gameObject.activeInHierarchy;

        private bool IsTemplateOrCopy(MenuItemView i)
            => itemTemplate != null && (i == itemTemplate || i.transform.parent == itemTemplate.transform.parent);

        private void Awake()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
            if (!_isOpen) gameObject.SetActive(false); // cerrado hasta que el juego lo abra
        }

        // Si lo desactivan desde fuera estando abierto (se oculta el lienzo...), cuenta como cancelado.
        private void OnDisable()
        {
            if (!_isOpen) return;
            _isOpen = false;
            Done = true; Result = null; ResultIndex = -1;
            MenuStack.Remove(this);
            UiRoot.Unregister(this);
        }

        /// <summary>Limpia lo recordado entre partidas (lo llama UiRoot al empezar el juego).</summary>
        internal static void ResetStatics() { Registry.Clear(); Remembered.Clear(); }

        private void OnDestroy()
        {
            Registry.Remove(this);
            MenuStack.Remove(this);
            UiRoot.Unregister(this);
        }

        // ---------------- Abrir ----------------

        /// <summary>Abre el menú con sus opciones fijas (las que se vean según las marcas).</summary>
        public IMenuPresenter Open(Func<string, bool> hasFlag = null, Func<MenuOption, bool> enabled = null, IReadOnlyDictionary<string, string> vars = null)
        {
            BeginOpen();
            _items.Clear(); _options.Clear();
            foreach (var item in StaticItems())
            {
                var option = item.ToOption();
                bool visible = option.IsVisible(hasFlag ?? (_ => false));
                item.gameObject.SetActive(visible);
                if (!visible) continue;
                item.Bind(this);
                item.ApplyVariables(vars);
                _items.Add(item);
                _options.Add(option);
            }
            var en = enabled ?? (_ => true);
            _enabled = i => i >= 0 && i < _options.Count && en(_options[i]);
            _remember = rememberCursor;
            StartAt(_remember && Remembered.TryGetValue(screenId, out var r) ? r : 0);
            return this;
        }

        /// <summary>Abre el menú como LISTA: una fila (copia de la plantilla) por texto.</summary>
        public IMenuPresenter OpenList(string title, IList<string> labels, Func<int, bool> enabled = null, IList<string> help = null, bool remember = false)
        {
            BeginOpen();
            if (titleText != null) TextSafety.Set(titleText, title ?? "");
            _options.Clear();
            for (int i = 0; i < labels.Count; i++)
                _options.Add(new MenuOption(i.ToString(), labels[i], MenuActionKind.ScreenAction, i.ToString(), help: help != null && i < help.Count ? help[i] : ""));
            _enabled = i => i >= 0 && i < _options.Count && (enabled == null || enabled(i));

            // Filas: la plantilla queda oculta; se copian las que hagan falta (y se reutilizan).
            itemTemplate.gameObject.SetActive(false);
            var parent = itemTemplate.transform.parent;
            var rows = parent.GetComponentsInChildren<MenuItemView>(true).Where(x => x != itemTemplate && x.transform.parent == parent).ToList();
            int needed = Math.Min(visibleRows, Math.Max(1, labels.Count));
            while (rows.Count < needed)
            {
                var copy = Instantiate(itemTemplate, parent);
                copy.name = itemTemplate.name + " " + (rows.Count + 1);
                rows.Add(copy);
            }
            _items.Clear();
            for (int r = 0; r < rows.Count; r++)
            {
                bool used = r < needed && r < labels.Count;
                rows[r].gameObject.SetActive(used);
                if (used) { rows[r].Bind(this); _items.Add(rows[r]); }
            }
            _remember = remember;
            StartAt(remember && Remembered.TryGetValue(screenId, out var rr) ? rr : 0);
            return this;
        }

        private void BeginOpen()
        {
            _isOpen = true;
            Done = false; Result = null; ResultIndex = -1;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            UiRoot.RaiseAboveBlocker(this);
            _openedAt = Time.unscaledTime;
            MenuStack.Push(this);
            UiRoot.Register(this);
        }

        private void StartAt(int index)
        {
            _top = 0;
            _index = Math.Max(0, Math.Min(index, _options.Count - 1));
            if (!_enabled(_index))
                for (int i = 0; i < _options.Count; i++) if (_enabled(i)) { _index = i; break; }
            Refresh();
        }

        public IEnumerator Wait() { while (!Done) yield return null; }

        public void Close()
        {
            if (this == null) return;
            if (_remember && ResultIndex >= 0) Remembered[screenId] = ResultIndex;
            Done = true;
            _isOpen = false;
            MenuStack.Remove(this);
            UiRoot.Unregister(this);
            gameObject.SetActive(false);
        }

        // ---------------- Dibujo ----------------

        private void Refresh()
        {
            if (IsList)
            {
                if (_index < _top) _top = _index;
                if (_index >= _top + _items.Count) _top = _index - _items.Count + 1;
                for (int r = 0; r < _items.Count; r++)
                {
                    int i = _top + r;
                    _items[r].SetText(i < _options.Count ? _options[i].Label : "");
                    _items[r].SetState(i == _index ? MenuItemView.State.Selected : _enabled(i) ? MenuItemView.State.Normal : MenuItemView.State.Disabled);
                }
                if (moreAbove != null) moreAbove.SetActive(_top > 0);
                if (moreBelow != null) moreBelow.SetActive(_top + _items.Count < _options.Count);
            }
            else
            {
                for (int i = 0; i < _items.Count; i++)
                    _items[i].SetState(i == _index ? MenuItemView.State.Selected : _enabled(i) ? MenuItemView.State.Normal : MenuItemView.State.Disabled);
            }

            var selected = SelectedRow();
            if (cursor != null)
            {
                cursor.gameObject.SetActive(selected != null);
                if (selected != null) PlaceCursor(selected);
            }
            if (helpText != null) TextSafety.Set(helpText, _index >= 0 && _index < _options.Count ? _options[_index].Help : "");
        }

        // El cursor se hace hijo de la opción elegida (sigue a su colocación, sea cual sea el diseño) y no
        // participa en su distribución (si la opción tiene un Layout Group).
        private void PlaceCursor(MenuItemView selected)
        {
            var le = cursor.GetComponent<LayoutElement>();
            if (le == null) le = cursor.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            cursor.SetParent(selected.transform, false);
            cursor.anchorMin = cursor.anchorMax = new Vector2(0, 0.5f);
            cursor.pivot = new Vector2(1, 0.5f);
            cursor.anchoredPosition = cursorOffset;
        }

        private MenuItemView SelectedRow()
        {
            if (_options.Count == 0) return null;
            int row = IsList ? _index - _top : _index;
            return row >= 0 && row < _items.Count ? _items[row] : null;
        }

        // ---------------- Entrada ----------------

        private void Update()
        {
            if (!_isOpen || Done || !MenuStack.IsTop(this)) return;
            var box = UiRoot.Existing != null ? UiRoot.Existing.ExistingText : null;
            if (box != null && box.IsSaying) return;

            var d = GameInput.RepeatedDirection();
            if (d != Direction.None) Move(d);

            if (GameInput.Pressed(GameButton.Confirm)) { GameInput.Consume(GameButton.Confirm); Choose(_index); }
            else if (GameInput.Pressed(GameButton.Cancel) && Time.unscaledTime > _openedAt)
            {
                GameInput.Consume(GameButton.Cancel);
                GameInput.Consume(GameButton.Menu);
                Cancel();
            }
        }

        private void Move(Direction d)
        {
            int before = _index;
            if (IsList || navigation == Navigation.Vertical)
            {
                if (d == Direction.Up || d == Direction.Down) _index = Step(_index, d == Direction.Down ? 1 : -1);
            }
            else if (navigation == Navigation.Horizontal)
            {
                if (d == Direction.Left || d == Direction.Right) _index = Step(_index, d == Direction.Right ? 1 : -1);
            }
            else if (navigation == Navigation.Grid)
            {
                var c = new MenuCursor(_options.Count, columns, wrap, _enabled, _index);
                c.Move(d);
                _index = c.Index;
            }
            else
            {
                // Posiciones en píxeles del lienzo (no del mundo): sirve igual con cualquier modo de lienzo y resolución.
                var canvas = GetComponentInParent<Canvas>();
                var space = canvas != null ? canvas.rootCanvas.transform : null;
                var pos = _items.Select(i =>
                {
                    var rt = (RectTransform)i.transform;
                    var p = rt.TransformPoint(rt.rect.center);
                    if (space != null) p = space.InverseTransformPoint(p);
                    return (p.x, p.y);
                }).ToList();
                _index = SpatialNavigator.Next(_index, d, pos, _enabled, wrap);
            }
            if (_index != before) Refresh();
        }

        // Paso en una lista, saltando las desactivadas.
        private int Step(int from, int step)
        {
            int n = _options.Count;
            for (int k = 1; k <= n; k++)
            {
                int i = from + step * k;
                if (!wrap && (i < 0 || i >= n)) return from;
                i = ((i % n) + n) % n;
                if (_enabled(i)) return i;
            }
            return from;
        }

        internal void Click(MenuItemView item)
        {
            if (!_isOpen || Done) return;
            int row = _items.IndexOf(item);
            if (row < 0) return;
            int i = IsList ? _top + row : row;
            if (i == _index) Choose(i);
            else if (_enabled(i)) { _index = i; Refresh(); }
        }

        private void Choose(int i)
        {
            if (!_enabled(i)) return;
            Result = _options[i];
            ResultIndex = i;
            Done = true;
        }

        private void Cancel()
        {
            switch (onCancel)
            {
                case CancelMode.Nothing: return;
                case CancelMode.ChooseOption:
                    int i = _options.FindIndex(o => o.Id == cancelOptionId);
                    if (i >= 0) Choose(i);
                    return;
                default:
                    if (_remember) Remembered[screenId] = _index;
                    Result = null; ResultIndex = -1; Done = true;
                    return;
            }
        }

        // ---------------- Para el editor ----------------

        public RectTransform Cursor => cursor;

        /// <summary>
        /// Vista previa en el editor: marca la opción 'index' como elegida (solo su aspecto). El cursor NO se
        /// mueve en el editor: así no acaba copiado dentro de una opción al duplicarla.
        /// </summary>
        public void PreviewSelect(int index)
        {
            var items = IsList ? new List<MenuItemView> { itemTemplate } : StaticItems();
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null) items[i].SetState(i == index ? MenuItemView.State.Selected : MenuItemView.State.Normal);
        }
    }
}
