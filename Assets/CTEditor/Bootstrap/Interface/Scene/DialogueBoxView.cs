using System.Linq;
using UnityEngine;
using TMPro;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// LA CAJA DE TEXTO DISEÑADA EN LA ESCENA: pon este componente en tu caja (con tu imagen de fondo, tu
    /// marco y tu fuente) y arrastra aquí el texto y el indicador «hay más». El juego la usa en lugar de la
    /// automática para todo: mensajes, preguntas Sí/No, diálogos... Crea una desde GameObject → CTEditor UI.
    /// Mientras no hay nada que decir, se oculta sola.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueBoxView : MonoBehaviour
    {
        [Tooltip("El texto donde se escribe (TextMeshPro). Su tamaño decide cuánto cabe: ajusta «Líneas por caja» en Controles y caja de texto.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Opcional: lo que parpadea cuando hay que pulsar para seguir (una flecha ▼, un icono...).")]
        [SerializeField] private GameObject moreIndicator;

        public TMP_Text Text => text;
        public GameObject MoreIndicator => moreIndicator;

        public static DialogueBoxView FindInScene()
            => FindObjectsByType<DialogueBoxView>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

        private void Reset() => text = GetComponentInChildren<TMP_Text>(true);

        // Al empezar el juego se oculta: aparece cuando hay algo que decir.
        private void Awake()
        {
            if (moreIndicator != null) moreIndicator.SetActive(false);
            if (GetComponent<TextBox>() == null) gameObject.SetActive(false);
        }
    }
}
