using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// FLECHAS PARA CUALQUIER PANTALLA HECHA CON BOTONES (el combate, el Laboratorio o las tuyas): se
    /// pone en la raíz y ya está.
    ///   • Flechas: va al botón visible más cercano en esa dirección (como la cruceta).
    ///   • Confirmar: pulsa el botón señalado.  • Cancelar: pulsa el primer botón «Atrás» visible.
    ///   • Un cursor ▶ marca el botón elegido. El ratón sigue funcionando igual.
    /// Mientras hay un menú o texto del juego abierto (UiRoot.ModalOpen), no hace nada.
    /// </summary>
    [DefaultExecutionOrder(-50)] // antes que el menú de pausa: Cancelar («Atrás») gana a Menú si comparten tecla
    public sealed class KeyboardNavigator : MonoBehaviour
    {
        [Tooltip("Botones que hace Cancelar, en orden de preferencia (el primero visible y activo).")]
        [SerializeField] private List<Button> cancelButtons = new List<Button>();
        [Tooltip("Botones que las flechas NO visitan (p. ej. la zona invisible para avanzar el texto).")]
        [SerializeField] private List<Button> ignore = new List<Button>();

        private Button _current;
        private TextMeshProUGUI _marker;
        private readonly List<Button> _buttons = new List<Button>();
        private readonly Dictionary<string, Button> _rememberedByGroup = new Dictionary<string, Button>();

        /// <summary>Configura desde código (lo hacen el combate y el Laboratorio al arrancar).</summary>
        public void Configure(IEnumerable<Button> cancel, IEnumerable<Button> ignored)
        {
            cancelButtons = cancel.Where(b => b != null).ToList();
            ignore = ignored.Where(b => b != null).ToList();
        }

        public Button Current => _current;

        // Sin la navegación propia de uGUI: si no, Intro pulsaría también el último botón clicado.
        private void OnEnable() => UiRoot.EnsureEventSystem();

        private void Update()
        {
            if (UiRoot.ModalOpen) { HideMarker(); return; }
            Collect();
            if (_buttons.Count == 0) { HideMarker(); _current = null; return; }

            // Si el señalado desapareció (cambió el panel), se recupera el último de ese grupo o el primero.
            if (_current == null || !_buttons.Contains(_current))
            {
                string group = GroupKey();
                _current = _rememberedByGroup.TryGetValue(group, out var b) && _buttons.Contains(b) ? b : FirstInReadingOrder();
            }

            var d = GameInput.RepeatedDirection();
            if (d != Direction.None)
            {
                var pos = _buttons.Select(Center).ToList();
                int next = SpatialNavigator.Next(_buttons.IndexOf(_current), d, pos);
                if (next >= 0 && next < _buttons.Count) _current = _buttons[next];
            }
            _rememberedByGroup[GroupKey()] = _current;
            PlaceMarker();

            if (GameInput.Pressed(GameButton.Confirm))
            {
                GameInput.Consume(GameButton.Confirm);
                var pressed = _current;
                pressed.onClick.Invoke();
            }
            else if (GameInput.Pressed(GameButton.Cancel))
            {
                var back = cancelButtons.FirstOrDefault(IsUsable);
                if (back != null) { GameInput.Consume(GameButton.Cancel); GameInput.Consume(GameButton.Menu); back.onClick.Invoke(); }
            }
        }

        private void Collect()
        {
            _buttons.Clear();
            foreach (var b in GetComponentsInChildren<Button>(false))
                if (IsUsable(b) && !ignore.Contains(b)) _buttons.Add(b);
        }

        private static bool IsUsable(Button b) => b != null && b.isActiveAndEnabled && b.interactable && b.gameObject.activeInHierarchy;

        // El "grupo" es el conjunto de padres visibles: así se recuerda el cursor por panel.
        private string GroupKey() => string.Join("|", _buttons.Select(b => b.transform.parent != null ? b.transform.parent.GetInstanceID() : 0).Distinct());

        // Centro del botón en píxeles de SU lienzo (no del mundo): igual con cualquier modo de lienzo.
        private static (float x, float y) Center(Button b)
        {
            var rt = (RectTransform)b.transform;
            var c = rt.TransformPoint(rt.rect.center);
            var canvas = b.GetComponentInParent<Canvas>();
            if (canvas != null) c = canvas.rootCanvas.transform.InverseTransformPoint(c);
            return (c.x, c.y);
        }

        private Button FirstInReadingOrder()
        {
            var pos = _buttons.Select(Center).ToList();
            int i = SpatialNavigator.Next(-1, Direction.Down, pos);
            return i >= 0 && i < _buttons.Count ? _buttons[i] : _buttons[0];
        }

        // ---------------- Cursor ▶ ----------------

        private void PlaceMarker()
        {
            if (_current == null) { HideMarker(); return; }
            if (_marker == null)
            {
                var go = new GameObject("Cursor ▶", typeof(RectTransform));
                _marker = go.AddComponent<TextMeshProUGUI>();
                _marker.raycastTarget = false;
                _marker.alignment = TextAlignmentOptions.Center;
                _marker.fontStyle = FontStyles.Bold;
            }
            TextSafety.Set(_marker, GameInput.Settings.CursorSymbol);
            _marker.color = UiKit.Highlight;
            var rt = (RectTransform)_marker.transform;
            if (rt.parent != _current.transform)
            {
                rt.SetParent(_current.transform, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
                rt.pivot = new Vector2(1, 0.5f);
                rt.anchoredPosition = new Vector2(-2, 0);
                rt.sizeDelta = new Vector2(30, 30);
                _marker.fontSize = 26;
                // Si el botón está pegado al borde de la pantalla, el cursor va dentro.
                var corners = new Vector3[4];
                ((RectTransform)_current.transform).GetWorldCorners(corners);
                if (corners[0].x < 30f) { rt.pivot = new Vector2(0, 0.5f); rt.anchoredPosition = new Vector2(4, 0); }
            }
            _marker.gameObject.SetActive(true);
        }

        private void HideMarker() { if (_marker != null) _marker.gameObject.SetActive(false); }

        private void OnDisable() => HideMarker();
    }
}
