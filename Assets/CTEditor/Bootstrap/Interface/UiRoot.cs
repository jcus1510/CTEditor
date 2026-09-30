using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CTEditor.Adventure.Domain;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// EL LIENZO DE LA INTERFAZ DEL JUEGO: se crea solo la primera vez que hace falta (no hay que montar
    /// nada en la escena) y queda por encima de todo. Aquí viven la CAJA DE TEXTO y los MENÚS.
    /// ModalOpen = hay un menú o pantalla abierta: el resto (Laboratorio, navegación con flechas) espera.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        private static UiRoot _instance;
        private TextBox _textBox;
        private GameObject _blocker;

        // Lo que está abierto AHORA (menús, textos que se están leyendo, el menú de pausa). Se calcula con
        // objetos vivos, no con un contador: si algo se destruye o se desactiva a medias, deja de contar solo.
        private static readonly HashSet<object> Modal = new HashSet<object>();

        public static void Register(object who) { if (who != null) Modal.Add(who); }
        public static void Unregister(object who) { if (who != null) Modal.Remove(who); }

        /// <summary>¿Hay un menú, un texto o una pantalla del juego abierta? (el resto espera)</summary>
        public static bool ModalOpen
        {
            get
            {
                Modal.RemoveWhere(o => o is UnityEngine.Object u && u == null); // destruidos
                return Modal.Count > 0;
            }
        }

        public RectTransform Rect { get; private set; }

        /// <summary>La interfaz si ya existe (sin crearla: para limpiar al cerrar la escena).</summary>
        public static UiRoot Existing => _instance;

        // Al empezar el juego (también sin recarga de dominio): nada de lo de la partida anterior.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Modal.Clear();
            MenuStack.Clear();
            MenuScreen.ResetStatics();
            UiContent.Reload();
            GameInput.ResetStatics();
        }

        public static UiRoot Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Interfaz del juego (CTEditor)", typeof(RectTransform));
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.matchWidthOrHeight = 0.5f;
                go.AddComponent<GraphicRaycaster>();
                _instance = go.AddComponent<UiRoot>();
                _instance.Rect = (RectTransform)go.transform;

                // Bloqueador: con un menú abierto, los clics no llegan a los botones de la escena de debajo.
                _instance._blocker = new GameObject("Bloqueador de clics", typeof(RectTransform), typeof(Image));
                _instance._blocker.transform.SetParent(go.transform, false);
                var bi = _instance._blocker.GetComponent<Image>();
                bi.color = new Color(0, 0, 0, 0.001f);
                bi.raycastTarget = true;
                var brt = (RectTransform)_instance._blocker.transform;
                brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
                _instance._blocker.SetActive(false);

                EnsureEventSystem();
                return _instance;
            }
        }

        /// <summary>
        /// Sin EventSystem no funciona el ratón: se crea uno si falta, con el módulo del sistema de entrada ACTIVO.
        /// Además se apaga la navegación propia de uGUI (flechas/Intro sobre el último botón pulsado): de eso
        /// se encargan nuestros menús, y si no, Intro pulsaría DOS botones a la vez.
        /// </summary>
        public static void EnsureEventSystem()
        {
            var es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                es = go.GetComponent<EventSystem>();
#if CTEDITOR_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                go.AddComponent<StandaloneInputModule>();
#endif
            }
            es.sendNavigationEvents = false;
        }

        /// <summary>
        /// Los menús y la caja de texto de la ESCENA deben quedar por encima del bloqueador de clics (orden 500):
        /// si su lienzo está por debajo, se sube a 510. (Pon tus menús en su propio lienzo, no en el del juego.)
        /// </summary>
        public static void RaiseAboveBlocker(Component c)
        {
            var canvas = c != null ? c.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            if (canvas.sortingOrder <= 500) canvas.sortingOrder = 510;
        }

        /// <summary>
        /// La caja de texto compartida: la de la ESCENA (DialogueBoxView, con tus gráficos) si hay una;
        /// si no, la automática.
        /// </summary>
        public TextBox Text
        {
            get
            {
                if (_textBox != null) return _textBox;
                var view = DialogueBoxView.FindInScene();
                _textBox = view != null ? TextBox.Attach(view) : TextBox.Create(Rect);
                return _textBox;
            }
        }
        /// <summary>La caja de texto si ya existe (sin crearla).</summary>
        public TextBox ExistingText => _textBox;

        private void Update()
        {
            // Los menús de escena en un lienzo que no es «Overlay» no pueden quedar por encima del bloqueador:
            // con ellos abiertos no se bloquea (el teclado sigue funcionando igual).
            bool block = ModalOpen && !Modal.Any(m => m is MenuScreen s && s != null && IsNotOverlay(s));
            if (_blocker != null && _blocker.activeSelf != block)
            {
                _blocker.SetActive(block);
                _blocker.transform.SetAsFirstSibling();
            }
        }

        private static bool IsNotOverlay(Component c)
        {
            var canvas = c.GetComponentInParent<Canvas>();
            return canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay;
        }

        private void OnDestroy() { if (_instance == this) { _instance = null; Modal.Clear(); } }
    }

    /// <summary>
    /// PIEZAS DE INTERFAZ hechas por código con los colores de los Ajustes de interfaz: cajas con borde,
    /// textos TMP y colocación por esquinas. Así los menús y la caja de texto no necesitan prefabs.
    /// </summary>
    public static class UiKit
    {
        public static Color Box => GameInput.Style != null ? GameInput.Style.BoxColor : new Color(0.97f, 0.97f, 0.99f);
        public static Color Border => GameInput.Style != null ? GameInput.Style.BorderColor : new Color(0.25f, 0.35f, 0.55f);
        public static Color Ink => GameInput.Style != null ? GameInput.Style.TextColor : new Color(0.2f, 0.2f, 0.25f);
        public static Color Highlight => GameInput.Style != null ? GameInput.Style.HighlightColor : new Color(0.85f, 0.35f, 0.25f);
        public static Color Disabled => GameInput.Style != null ? GameInput.Style.DisabledColor : new Color(0.6f, 0.6f, 0.65f);
        public static float FontSize => GameInput.Style != null ? GameInput.Style.FontSize : 28f;

        /// <summary>Caja con borde. Devuelve el INTERIOR (donde se ponen los textos).</summary>
        public static RectTransform FramedBox(RectTransform parent, string name)
        {
            var frame = new GameObject(name, typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(parent, false);
            frame.GetComponent<Image>().color = Border;
            var inner = new GameObject("Interior", typeof(RectTransform), typeof(Image));
            inner.transform.SetParent(frame.transform, false);
            inner.GetComponent<Image>().color = Box;
            var rt = (RectTransform)inner.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4, 4); rt.offsetMax = new Vector2(-4, -4);
            return rt;
        }

        public static TextMeshProUGUI Label(RectTransform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Coloca por píxeles desde la esquina superior izquierda del padre.</summary>
        public static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Coloca una caja de tamaño w×h en una zona de la pantalla (1280×720 de referencia).</summary>
        public static void Anchor(RectTransform frame, MenuAnchor anchor, float w, float h, float margin = 16f, float bottomReserve = 0f)
        {
            Vector2 a;
            switch (anchor)
            {
                case MenuAnchor.TopLeft: a = new Vector2(0, 1); break;
                case MenuAnchor.BottomLeft: a = new Vector2(0, 0); break;
                case MenuAnchor.BottomRight: a = new Vector2(1, 0); break;
                case MenuAnchor.Center: a = new Vector2(0.5f, 0.5f); break;
                case MenuAnchor.BottomFull: a = new Vector2(0.5f, 0); break;
                default: a = new Vector2(1, 1); break;
            }
            if (anchor == MenuAnchor.BottomFull)
            {
                // A lo ancho: se estira con la pantalla (sirve para cualquier proporción).
                frame.anchorMin = new Vector2(0, 0); frame.anchorMax = new Vector2(1, 0);
                frame.pivot = new Vector2(0.5f, 0);
                frame.anchoredPosition = new Vector2(0, margin + bottomReserve);
                frame.sizeDelta = new Vector2(-margin * 2, h);
                return;
            }
            frame.anchorMin = frame.anchorMax = a;
            frame.pivot = a;
            float x = a.x == 0 ? margin : a.x == 1 ? -margin : 0;
            float y = a.y == 0 ? margin + bottomReserve : a.y == 1 ? -margin : 0;
            frame.anchoredPosition = new Vector2(x, y);
            frame.sizeDelta = new Vector2(w, h);
        }
    }

    /// <summary>
    /// CONTENIDO DE LA INTERFAZ en ejecución: los menús del autor (Resources/Menus) o los clásicos, y las
    /// variables de los textos ({jugador}, {dinero}...).
    /// </summary>
    public static class UiContent
    {
        private static Dictionary<string, MenuDefinition> _menus;

        public static MenuDefinition Menu(string id)
        {
            if (_menus == null)
            {
                _menus = new Dictionary<string, MenuDefinition>();
                foreach (var d in Resources.LoadAll<MenuData>(ContentFolders.Menus))
                    if (d != null && !string.IsNullOrWhiteSpace(d.Id)) _menus[d.Id] = InterfaceMapper.ToDomain(d);
            }
            return _menus.TryGetValue(id ?? "", out var m) ? m : ClassicInterface.Find(id);
        }

        public static void Reload() => _menus = null;

        /// <summary>¿El autor creó su propio menú con ese id? (si no, se usa el clásico)</summary>
        public static bool HasAuthored(string id) { Menu(id); return _menus.ContainsKey(id ?? ""); }

        public static Dictionary<string, string> Variables(PlayerSave save)
        {
            var v = new Dictionary<string, string>();
            if (save == null) return v;
            v["jugador"] = save.PlayerName;
            v["dinero"] = save.Money.ToString();
            v["hora"] = save.World.Hour.ToString("00") + ":00";
            v["equipo"] = save.Party.Count.ToString();
            return v;
        }
    }
}
