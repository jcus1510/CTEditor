using System;
using UnityEngine;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>Las teclas de un botón del juego.</summary>
    [Serializable]
    public sealed class KeyBindingData
    {
        public GameButton button;
        [Tooltip("Nombres de tecla (los de Unity): UpArrow, W, Z, Space, Return, Escape, Backspace...")]
        public string[] keys = new string[0];
    }

    /// <summary>
    /// AJUSTES DE INTERFAZ (normalmente una sola ficha, id «ajustes»): teclas de cada botón, velocidad y
    /// tamaño de la caja de texto, y los COLORES de cajas y menús. Si no hay ninguna, el juego usa la
    /// interfaz clásica.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Ajustes de interfaz", fileName = "AjustesInterfaz")]
    public sealed class InterfaceSettingsData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id = "ajustes";
        [SerializeField] private string displayName = "Ajustes de interfaz";

        [Header("Controles")]
        [SerializeField] private KeyBindingData[] bindings = new KeyBindingData[0];
        [Tooltip("Segundos manteniendo una flecha antes de que se repita.")]
        [SerializeField, Range(0.1f, 1f)] private float repeatDelay = 0.4f;
        [Tooltip("Segundos entre repeticiones al mantenerla.")]
        [SerializeField, Range(0.03f, 0.5f)] private float repeatInterval = 0.09f;

        [Header("Caja de texto")]
        [Tooltip("Caracteres por segundo (0 = aparece todo de golpe). Clásico: 40.")]
        [SerializeField, Range(0f, 200f)] private float textSpeed = 40f;
        [SerializeField, Range(1, 6)] private int linesPerPage = 2;
        [SerializeField, Range(10, 80)] private int charsPerLine = 36;
        [Tooltip("Si > 0, el texto avanza solo tras estos segundos.")]
        [SerializeField, Range(0f, 10f)] private float autoAdvanceSeconds = 0f;
        [Tooltip("¿Cancelar también avanza el texto? (clásico: sí)")]
        [SerializeField] private bool cancelAdvancesText = true;
        [Tooltip("¿Mantener Confirmar acelera el texto?")]
        [SerializeField] private bool holdToSpeedUp = true;

        [Header("Símbolos")]
        [Tooltip("Cursor de los menús. ► y ▼ salen con la fuente por defecto; otros símbolos pueden necesitar tu fuente.")]
        [SerializeField] private string cursorSymbol = "►";
        [SerializeField] private string moreSymbol = "▼";

        [Header("Colores y tamaño")]
        [SerializeField] private Color boxColor = new Color(0.97f, 0.97f, 0.99f, 1f);
        [SerializeField] private Color borderColor = new Color(0.25f, 0.35f, 0.55f, 1f);
        [SerializeField] private Color textColor = new Color(0.2f, 0.2f, 0.25f, 1f);
        [SerializeField] private Color highlightColor = new Color(0.85f, 0.35f, 0.25f, 1f);
        [SerializeField] private Color disabledColor = new Color(0.6f, 0.6f, 0.65f, 1f);
        [SerializeField, Range(14, 60)] private int fontSize = 28;

        [Header("Menús que usa el juego (ids)")]
        [Tooltip("El menú que abre el botón Menú. Clásico: pausa.")]
        [SerializeField] private string pauseMenuId = ClassicInterface.PauseMenu;

        public string Id => id;
        public string DisplayName => displayName;
        public KeyBindingData[] Bindings => bindings ?? new KeyBindingData[0];
        public float RepeatDelay => repeatDelay;
        public float RepeatInterval => repeatInterval;
        public float TextSpeed => textSpeed;
        public int LinesPerPage => linesPerPage;
        public int CharsPerLine => charsPerLine;
        public float AutoAdvanceSeconds => autoAdvanceSeconds;
        public bool CancelAdvancesText => cancelAdvancesText;
        public bool HoldToSpeedUp => holdToSpeedUp;
        public string CursorSymbol => cursorSymbol;
        public string MoreSymbol => moreSymbol;
        public Color BoxColor => boxColor;
        public Color BorderColor => borderColor;
        public Color TextColor => textColor;
        public Color HighlightColor => highlightColor;
        public Color DisabledColor => disabledColor;
        public int FontSize => fontSize;
        public string PauseMenuId => pauseMenuId;
    }
}
