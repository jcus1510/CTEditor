using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// UNA OPCIÓN de un menú de escena (hija de un MenuScreen). Es un objeto de la escena normal y corriente:
    /// muévelo, cámbiale el tamaño, pon tu imagen de fondo, tu fuente y tu texto como en cualquier UI de Unity.
    ///
    /// Aquí solo se dice:
    ///   • QUÉ HACE: abrir otro menú (por su id), el equipo, la mochila, cerrar, o una acción de pantalla.
    ///   • CÓMO SE VE en cada estado: normal / elegido (cursor encima) / desactivado:
    ///     cambiar la imagen de fondo (sprites), teñirla, teñir el texto, enseñar un marcador o agrandarse.
    /// Si dejas un sprite vacío, se mantiene el que tenga la imagen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuItemView : MonoBehaviour
    {
        public enum State { Normal, Selected, Disabled }

        [Header("Qué hace")]
        [Tooltip("Id de la opción (único en su menú). Las acciones de pantalla usan ids fijos: summary, swap, give, take, use...")]
        [SerializeField] private string optionId = "";
        [SerializeField] private MenuActionKind action = MenuActionKind.Close;
        [Tooltip("Abrir otro menú: su id (p. ej. equipo_objeto). Acción de pantalla: el id de la acción.")]
        [SerializeField] private string target = "";
        [Tooltip("Solo se ve si la partida tiene esta MARCA (vacío = siempre).")]
        [SerializeField] private string requiredFlag = "";
        [Tooltip("Se oculta si la partida tiene esta marca.")]
        [SerializeField] private string hiddenByFlag = "";
        [Tooltip("Texto de ayuda (se ve en el «Texto de ayuda» del menú, si tiene).")]
        [SerializeField, TextArea(1, 3)] private string help = "";

        [Header("Cómo se ve (arrastra aquí tus textos e imágenes)")]
        [Tooltip("El texto de la opción. Escríbelo directamente en el texto de la escena; admite {jugador}, {dinero}...")]
        [SerializeField] private TMP_Text label;
        [Tooltip("La imagen de fondo de la opción (opcional).")]
        [SerializeField] private Image background;
        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite selectedSprite;
        [SerializeField] private Sprite disabledSprite;
        [Tooltip("Teñir la imagen de fondo según el estado.")]
        [SerializeField] private bool tintBackground = true;
        [SerializeField] private Color backgroundNormal = Color.white;
        [SerializeField] private Color backgroundSelected = new Color(1f, 0.9f, 0.6f, 1f);
        [SerializeField] private Color backgroundDisabled = new Color(0.75f, 0.75f, 0.78f, 1f);
        [Tooltip("Teñir el texto según el estado.")]
        [SerializeField] private bool tintText = true;
        [SerializeField] private Color textNormal = new Color(0.2f, 0.2f, 0.25f);
        [SerializeField] private Color textSelected = new Color(0.85f, 0.35f, 0.25f);
        [SerializeField] private Color textDisabled = new Color(0.6f, 0.6f, 0.65f);
        [Tooltip("Objeto que solo se ve cuando está ELEGIDA (una flecha, un brillo...).")]
        [SerializeField] private GameObject selectedMarker;
        [Tooltip("Tamaño cuando está elegida (1 = igual).")]
        [SerializeField, Range(0.8f, 1.5f)] private float selectedScale = 1f;

        private string _rawLabel;
        private MenuScreen _owner;

        public string OptionId { get => optionId; set => optionId = value; }
        public MenuActionKind Action { get => action; set => action = value; }
        public string Target { get => target; set => target = value; }
        public TMP_Text Label => label;
        public string Help => help;

        /// <summary>El texto tal como lo escribió el autor (con sus {variables}).</summary>
        public string RawLabel
        {
            get
            {
                if (_rawLabel == null) _rawLabel = label != null ? label.text : name;
                return _rawLabel;
            }
        }

        public MenuOption ToOption() => new MenuOption(optionId, RawLabel, action, target, requiredFlag, hiddenByFlag, help);

        internal void Bind(MenuScreen owner)
        {
            _owner = owner;
            // Para el ratón: clic = elegir. Si ya tiene un botón, se usa el suyo.
            var button = GetComponent<Button>();
            if (button == null)
            {
                if (background == null && GetComponent<Graphic>() == null)
                {
                    var hit = gameObject.AddComponent<Image>();
                    hit.color = new Color(0, 0, 0, 0);
                }
                button = gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
            }
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
        }

        private void OnClick() { if (_owner != null) _owner.Click(this); }

        /// <summary>Pone el texto (con las variables ya sustituidas).</summary>
        public void SetText(string text)
        {
            if (label != null) TextSafety.Set(label, text);
        }

        /// <summary>Texto del autor con las variables sustituidas.</summary>
        public void ApplyVariables(IReadOnlyDictionary<string, string> vars)
            => SetText(TextTokens.Replace(RawLabel, vars ?? new Dictionary<string, string>()));

        /// <summary>Aspecto según el estado (también se usa en el editor para la vista previa).</summary>
        public void SetState(State state)
        {
            if (background != null)
            {
                var sprite = state == State.Selected ? selectedSprite : state == State.Disabled ? disabledSprite : normalSprite;
                if (sprite != null) background.sprite = sprite;
                if (tintBackground) background.color = state == State.Selected ? backgroundSelected : state == State.Disabled ? backgroundDisabled : backgroundNormal;
            }
            if (label != null && tintText) label.color = state == State.Selected ? textSelected : state == State.Disabled ? textDisabled : textNormal;
            if (selectedMarker != null) selectedMarker.SetActive(state == State.Selected);
            float s = state == State.Selected ? selectedScale : 1f;
            transform.localScale = new Vector3(s, s, 1f);
        }

        // Rellena solas las referencias al añadir el componente (el texto y la imagen de dentro).
        private void Reset()
        {
            label = GetComponentInChildren<TMP_Text>(true);
            background = GetComponent<Image>();
            if (string.IsNullOrEmpty(optionId)) optionId = name.ToLowerInvariant().Replace(' ', '_');
        }
    }
}
