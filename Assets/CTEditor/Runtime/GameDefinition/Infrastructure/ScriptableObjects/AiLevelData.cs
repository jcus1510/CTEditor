using UnityEngine;
using CTEditor.GameDefinition.Domain.Trainers;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Ficha de un NIVEL DE IA (1 a 7): cómo piensa un entrenador de ese nivel, en 4 bloques (Conocimiento,
    /// Decisión, Gestión y Equipo). Hay 7 clásicos (Novato, Aficionado, Veterano, Élite, Campeón, Maestro e
    /// Injusto) y el autor puede ajustarlos. Si falta alguno, se usa el clásico.
    /// La traduce AiLevelMapper al dominio (AiProfile).
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Nivel de IA", fileName = "NivelIA")]
    public sealed class AiLevelData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [Tooltip("Nombre del nivel (Novato, Aficionado, Veterano, Élite, Campeón...).")]
        [SerializeField] private string displayName;
        [Tooltip("Qué nivel es (1 = el más fácil, 7 = injusto). Cada entrenador elige uno.")]
        [SerializeField, Range(1, 7)] private int level = 1;
        [Tooltip("Para quién es (texto de ayuda en el editor).")]
        [SerializeField, TextArea(2, 3)] private string description = "";

        [Header("🧠 Conocimiento: qué sabe de ti")]
        [Tooltip("Nada (solo ve tu Pokémon y sus tipos) · lo visto en este combate (tus movimientos y cuánto dañan) · memoria " +
                 "(lo guarda para la revancha, con tus IVs/EVs estimados por el daño) · todo (lo sabe desde el principio: injusto).")]
        [SerializeField] private AiKnowledge knowledge = AiKnowledge.Battle;

        [Header("🎯 Decisión: cómo elige")]
        [Tooltip("Al azar · el más eficaz según los tipos · experto (daño real, remates, estados y mejoras con cabeza) · " +
                 "predictor (además anticipa tu jugada: cambia al que resiste tu golpe y castiga tus cambios).")]
        [SerializeField] private MoveBrain brain = MoveBrain.Aggressive;
        [Tooltip("% de turnos en que se equivoca y usa un movimiento al azar. 0 = nunca.")]
        [SerializeField, Range(0, 100)] private int mistakePercent = 10;
        [Tooltip("Solo con «Predictor»: % de turnos en que juega según lo que cree que vas a hacer (100 = siempre).")]
        [SerializeField, Range(0, 100)] private int predictPercent = 60;

        [Header("🛡️ Gestión: objetos y cambios")]
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

        [Header("🎒 Equipo: movimientos, objetos y entrenamiento")]
        [Tooltip("Para los miembros sin movimientos escritos: clásico (4 últimos), equilibrado, fuerte o de competición.")]
        [SerializeField] private MovesetStyle moveset = MovesetStyle.Balanced;
        [Tooltip("Busca combinaciones: Hipnosis + Comesueños, Danza Lluvia + Agua, Danza Espada + ataques físicos...")]
        [SerializeField] private bool synergies;
        [Tooltip("Puede usar movimientos de MT/MO.")]
        [SerializeField] private bool useMachineMoves;
        [Tooltip("Puede usar movimientos de tutor.")]
        [SerializeField] private bool useTutorMoves;
        [Tooltip("Puede usar movimientos huevo.")]
        [SerializeField] private bool useEggMoves;
        [Tooltip("Objetos equipados que reparte a los que no llevan: ninguno · básicos (Restos, bayas, de tipo) · de competición " +
                 "(Elección, Vidasfera, Banda Focus, Chaleco Asalto, Mineral Evolutivo...).")]
        [SerializeField] private HeldItemStyle heldItems = HeldItemStyle.None;
        [Tooltip("IVs perfectos, 252 EVs en sus dos mejores estadísticas y naturaleza a juego (como un equipo de competición).")]
        [SerializeField] private bool competitiveTraining;
        // Fichas anteriores al Lote F: el interruptor antiguo se convierte en «Básicos» la primera vez.
        [SerializeField, HideInInspector] private bool autoHeldItems;
        [SerializeField, HideInInspector] private bool heldItemsMigrated;

        private void OnValidate()
        {
            if (heldItemsMigrated) return;
            if (autoHeldItems && heldItems == HeldItemStyle.None) heldItems = HeldItemStyle.Basic;
            heldItemsMigrated = true;
        }

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
        public HeldItemStyle HeldItems => heldItemsMigrated ? heldItems : (autoHeldItems ? HeldItemStyle.Basic : heldItems);
        public AiKnowledge Knowledge => knowledge;
        public int PredictPercent => predictPercent;
        public bool CompetitiveTraining => competitiveTraining;
    }
}
