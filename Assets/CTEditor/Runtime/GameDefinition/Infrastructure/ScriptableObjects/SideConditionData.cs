using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha de un EFECTO DE LADO (Reflejo, Pantalla de Luz, Neblina, Velo Sagrado, Viento Afín...):
    /// protege o ayuda a todo un bando durante unos turnos. Se pone con un movimiento (efecto «Poner un
    /// efecto de lado»). La traduce SideConditionMapper al dominio.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Efecto de lado", fileName = "NuevoEfectoDeLado")]
    public sealed class SideConditionData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("Color para reconocerlo en los editores.")]
        [SerializeField] private Color color = new Color(0.55f, 0.75f, 0.95f);

        [Header("Duración")]
        [Tooltip("Cuántos turnos dura, contando el turno en que se pone. Clásico: 5 (Viento Afín: 4).")]
        [SerializeField, Min(1)] private int turns = 5;

        [Header("Daño que recibe este lado")]
        [Tooltip("Multiplicador del daño FÍSICO recibido. 1 = normal; Reflejo: 0,5. Los críticos lo ignoran.")]
        [SerializeField, Range(0f, 2f)] private float physicalDamageMultiplier = 1f;
        [Tooltip("Multiplicador del daño ESPECIAL recibido. 1 = normal; Pantalla de Luz: 0,5. Los críticos lo ignoran.")]
        [SerializeField, Range(0f, 2f)] private float specialDamageMultiplier = 1f;

        [Header("Protecciones")]
        [Tooltip("El rival no puede BAJAR las estadísticas de este lado (Neblina).")]
        [SerializeField] private bool blocksStatDrops;
        [Tooltip("El rival no puede poner ESTADOS a este lado (Velo Sagrado).")]
        [SerializeField] private bool blocksStatus;

        [Header("Velocidad")]
        [Tooltip("Multiplicador de la Velocidad de este lado. 1 = normal; Viento Afín: 2.")]
        [SerializeField, Range(0.25f, 4f)] private float speedMultiplier = 1f;

        [Header("Más efectos (3.ª y 4.ª gen.)")]
        [Tooltip("Los más lentos actúan primero (Espacio Raro). Ponlo en los DOS lados (el movimiento pone «lado» y «lado_rival»).")]
        [SerializeField] private bool reversesTurnOrder;
        [Tooltip("Multiplica la precisión de lo que se lanza contra este lado (Gravedad: 1,67).")]
        [SerializeField, Range(0.1f, 3f)] private float accuracyMultiplier = 1f;
        [Tooltip("Este lado queda en el suelo: le afecta Tierra aunque vuele o levite (Gravedad).")]
        [SerializeField] private bool groundsTargets;
        [Tooltip("Los golpes contra este lado no son críticos (Conjuro).")]
        [SerializeField] private bool blocksCrits;
        [Tooltip("Daño recibido según el tipo del movimiento. Ej.: electric ×0,33 (Chapoteo Lodo).")]
        [SerializeField] private TypeMultiplierEntry[] typeDamageMultipliers = new TypeMultiplierEntry[0];

        [Header("Efectos de campo y campos (5.ª y 6.ª gen.)")]
        [Tooltip("Se intercambian Defensa y Def. Esp. para el daño (Zona Extraña).")]
        [SerializeField] private bool swapsDefenses;
        [Tooltip("Los objetos equipados no hacen nada (Zona Mágica).")]
        [SerializeField] private bool suppressesItems;
        [Tooltip("Grupo excluyente: al ponerse, quita los demás del mismo grupo en los dos lados (los campos usan «campo»).")]
        [SerializeField] private string group = "";
        [Tooltip("% de PS que recuperan al final del turno los que pisan el suelo (Campo de Hierba: 6,25).")]
        [SerializeField, Range(0f, 100f)] private float endOfTurnHealPercent;
        [Tooltip("Estados que no se pueden poner a los que pisan el suelo: * = todos (Campo de Niebla), sleep (Campo Eléctrico).")]
        [SerializeField] private string[] groundedStatusBlock = new string[0];
        [Tooltip("Potencia de los movimientos de un tipo si el atacante pisa el suelo (Campo Eléctrico: electric ×1,5).")]
        [SerializeField] private TypeMultiplierEntry[] typePowerMultipliers = new TypeMultiplierEntry[0];
        [Tooltip("Los movimientos con prioridad fallan contra quien pisa el suelo (Campo Psíquico).")]
        [SerializeField] private bool blocksPriorityOnGrounded;

        public bool SwapsDefenses => swapsDefenses;
        public bool SuppressesItems => suppressesItems;
        public string Group => group;
        public float EndOfTurnHealPercent => endOfTurnHealPercent;
        public string[] GroundedStatusBlock => groundedStatusBlock;
        public TypeMultiplierEntry[] TypePowerMultipliers => typePowerMultipliers;
        public bool BlocksPriorityOnGrounded => blocksPriorityOnGrounded;

        [System.Serializable]
        public sealed class TypeMultiplierEntry
        {
            public ElementTypeData type;
            [Range(0f, 4f)] public float multiplier = 0.5f;
        }

        public bool ReversesTurnOrder => reversesTurnOrder;
        public float AccuracyMultiplier => accuracyMultiplier;
        public bool GroundsTargets => groundsTargets;
        public bool BlocksCrits => blocksCrits;
        public TypeMultiplierEntry[] TypeDamageMultipliers => typeDamageMultipliers;

        public string Id => id;
        public string DisplayName => displayName;
        public Color Color => color;
        public int Turns => turns;
        public float PhysicalDamageMultiplier => physicalDamageMultiplier;
        public float SpecialDamageMultiplier => specialDamageMultiplier;
        public bool BlocksStatDrops => blocksStatDrops;
        public bool BlocksStatus => blocksStatus;
        public float SpeedMultiplier => speedMultiplier;
    }
}
