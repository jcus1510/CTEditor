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
