using System;
using UnityEngine;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>Una opción de un menú, tal como se guarda en la ficha.</summary>
    [Serializable]
    public sealed class MenuOptionData
    {
        [Tooltip("Id de la opción (único en el menú). Las acciones de pantalla usan ids fijos: summary, swap, give, take, use...")]
        public string id = "";
        [Tooltip("Texto que se ve. Admite variables: {jugador}, {rival}, {dinero}.")]
        public string label = "";
        [Tooltip("Qué hace al elegirla.")]
        public MenuActionKind action = MenuActionKind.Close;
        [Tooltip("Abrir otro menú: su id. Acción de la pantalla: el id de la acción.")]
        public string target = "";
        [Tooltip("Solo se ve si la partida tiene esta MARCA (vacío = siempre). P. ej. tiene_pokedex.")]
        public string requiredFlag = "";
        [Tooltip("Se oculta si la partida tiene esta marca (vacío = nunca).")]
        public string hiddenByFlag = "";
        [Tooltip("Ayuda que se ve abajo al pasar por la opción.")]
        public string help = "";
    }

    /// <summary>
    /// La ficha de un MENÚ (pausa, combate, Sí/No, equipo, mochila... o los tuyos): opciones en orden,
    /// columnas, dónde aparece y cómo se mueve el cursor. La traduce InterfaceMapper al dominio.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Menú", fileName = "NuevoMenu")]
    public sealed class MenuData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("Título opcional encima de las opciones (vacío = sin título).")]
        [SerializeField] private string title = "";

        [Header("Opciones (en el orden en que se ven)")]
        [SerializeField] private MenuOptionData[] options = new MenuOptionData[0];

        [Header("Forma y cursor")]
        [Tooltip("1 = lista vertical; 2 o más = rejilla (se rellena por filas, como LUCHAR/MOCHILA).")]
        [SerializeField, Range(1, 6)] private int columns = 1;
        [Tooltip("Al pasar del final, ¿vuelve al principio?")]
        [SerializeField] private bool wrap = true;
        [Tooltip("¿Recuerda dónde estaba el cursor la próxima vez?")]
        [SerializeField] private bool rememberCursor = true;
        [Tooltip("¿Cancelar cierra el menú? Si no, elige la opción de abajo (en Sí/No: «no»).")]
        [SerializeField] private bool cancelCloses = true;
        [SerializeField] private string cancelOptionId = "";
        [Tooltip("Dónde aparece la caja del menú.")]
        [SerializeField] private MenuAnchor anchor = MenuAnchor.TopRight;

        public string Id => id;
        public string DisplayName => displayName;
        public string Title => title;
        public MenuOptionData[] Options => options ?? new MenuOptionData[0];
        public int Columns => columns;
        public bool Wrap => wrap;
        public bool RememberCursor => rememberCursor;
        public bool CancelCloses => cancelCloses;
        public string CancelOptionId => cancelOptionId;
        public MenuAnchor Anchor => anchor;
    }
}
