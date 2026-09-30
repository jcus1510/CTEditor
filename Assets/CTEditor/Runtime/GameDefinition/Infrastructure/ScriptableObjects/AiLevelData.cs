using UnityEngine;
using CTEditor.GameDefinition.Domain.Trainers;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Ficha de un NIVEL DE IA (1 a 5): cómo piensa un entrenador de ese nivel. Hay 5 clásicos (Novato,
    /// Aficionado, Veterano, Élite y Campeón) y el autor puede ajustarlos. Si falta alguno, se usa el clásico.
    /// La traduce AiLevelMapper al dominio (AiProfile).
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Nivel de IA", fileName = "NivelIA")]
    public sealed class AiLevelData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [Tooltip("Nombre del nivel (Novato, Aficionado, Veterano, Élite, Campeón...).")]
        [SerializeField] private string displayName;
        [Tooltip("Qué nivel es (1 = el más fácil, 5 = el más difícil). Cada entrenador elige uno.")]
        [SerializeField, Range(1, 5)] private int level = 1;
        [Tooltip("Para quién es (texto de ayuda en el editor).")]
        [SerializeField, TextArea(2, 3)] private string description = "";

        [Header("Cómo elige el movimiento")]
        [Tooltip("Al azar, el más eficaz según los tipos, o experto (daño real, remates, estados y mejoras con cabeza).")]
        [SerializeField] private MoveBrain brain = MoveBrain.Aggressive;
        [Tooltip("% de turnos en que se equivoca y usa un movimiento al azar. 0 = nunca.")]
        [SerializeField, Range(0, 100)] private int mistakePercent = 10;

        [Header("Objetos y cambios")]
        [Tooltip("% de veces que se acuerda de usar sus objetos cuando debería.")]
        [SerializeField, Range(0, 100)] private int itemUsePercent = 100;
        [Tooltip("Nunca, simple (en cuanto baja del umbral) o inteligente (solo si le sirve: si el rival lo tumba igual, ataca).")]
        [SerializeField] private HealStyle heal = HealStyle.Simple;
        [Tooltip("Piensa en curarse con este % de PS o menos.")]
        [SerializeField, Range(1, 100)] private int healBelowPercent = 25;
        [Tooltip("¿Retira a su monstruo si pierde claramente el duelo?")]
        [SerializeField] private bool canSwitch;
        [Tooltip("Mochila por defecto si el entrenador no trae la suya.")]
        [SerializeField] private BagEntryData[] defaultBag = new BagEntryData[0];

        [Header("Movimientos automáticos")]
        [Tooltip("Para los miembros sin movimientos escritos: clásico (4 últimos), equilibrado o fuerte.")]
        [SerializeField] private MovesetStyle moveset = MovesetStyle.Balanced;
        [Tooltip("Busca combinaciones: Hipnosis + Comesueños, Danza Lluvia + Agua, Danza Espada + ataques físicos...")]
        [SerializeField] private bool synergies;
        [Tooltip("Puede usar movimientos de MT/MO.")]
        [SerializeField] private bool useMachineMoves;
        [Tooltip("Puede usar movimientos de tutor.")]
        [SerializeField] private bool useTutorMoves;
        [Tooltip("Puede usar movimientos huevo.")]
        [SerializeField] private bool useEggMoves;
        [Tooltip("A los miembros sin objeto equipado les pone uno útil (Restos, bayas, objetos de tipo).")]
        [SerializeField] private bool autoHeldItems;

        public string Id => id;
        public string DisplayName => displayName;
        public int Level => level;
        public string Description => description;
        public MoveBrain Brain => brain;
        public int MistakePercent => mistakePercent;
        public int ItemUsePercent => itemUsePercent;
        public HealStyle Heal => heal;
        public int HealBelowPercent => healBelowPercent;
        public bool CanSwitch => canSwitch;
        public BagEntryData[] DefaultBag => defaultBag;
        public MovesetStyle Moveset => moveset;
        public bool Synergies => synergies;
        public bool UseMachineMoves => useMachineMoves;
        public bool UseTutorMoves => useTutorMoves;
        public bool UseEggMoves => useEggMoves;
        public bool AutoHeldItems => autoHeldItems;
    }
}
