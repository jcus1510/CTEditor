using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha que rellena el AUTOR para crear una HABILIDAD (Lote 1). El ACL (AbilityMapper) la
    /// traduce a AbilityDefinition (dominio puro). Cubre: modificadores pasivos de stat, inmunidad a
    /// estados e inmunidad a tipos.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Ability", fileName = "NewAbility")]
    public sealed class AbilityData : ScriptableObject
    {
        [Tooltip("Id estable de la habilidad (clave de texto, p.ej. \"levitate\").")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [Tooltip("Multiplica stats mientras el portador está en combate (p.ej. attack x2). Se combina en EffectiveStat.")]
        [SerializeField] private PassiveStatModifierData[] passiveModifiers;

        [Tooltip("Estados que esta habilidad IMPIDE (no se pueden infligir al portador).")]
        [StatusIdReference, SerializeField] private string[] statusImmunities;

        [Tooltip("Tipos a los que el portador es INMUNE (los movimientos de ese tipo no le afectan).")]
        [SerializeField] private ElementTypeData[] typeImmunities;

        [Tooltip("Si es inmune a un tipo, % de PS máx que CURA al recibirlo (Absorbe Agua). 0 = solo anula.")]
        [Range(0f, 100f)] [SerializeField] private float absorbImmuneHealPercent = 0f;

        [Tooltip("Estado a infligir al ATACANTE cuando le pega un golpe de CONTACTO (Estática, Cuerpo Llama). Vacío = ninguno.")]
        [StatusIdReference, SerializeField] private string contactReactionStatus;

        [Tooltip("Probabilidad de la reacción por contacto.")]
        [Range(0f, 100f)] [SerializeField] private float contactReactionChance = 0f;

        [Tooltip("Al ENTRAR al campo, cambia una etapa de stat (Intimidación: stat \"attack\", -1, al rival). Vacío = nada.")]
        [SerializeField] private string onEntryStatId;

        [Range(-6, 6)] [SerializeField] private int onEntryStages = 0;

        [Tooltip("Si está marcado, el cambio al entrar es sobre uno mismo; si no, sobre el rival.")]
        [SerializeField] private bool onEntryTargetsSelf = false;

        [Header("Condicionales (Lote 3)")]
        [Tooltip("Stat que se multiplica SOLO si el portador tiene un estado (Agallas: \"attack\"; Pies Rápidos: \"speed\"). Vacío = nada.")]
        [SerializeField] private string statusStatBoostStatId;

        [SerializeField] private float statusStatBoostMultiplier = 1f;

        [Tooltip("Tipo de movimiento potenciado al atacar con poca vida (Espesura: fuego). Vacío = nada.")]
        [SerializeField] private ElementTypeData lowHpBoostType;

        [Tooltip("Umbral de PS (% del máx) bajo el cual se activa el refuerzo a poca vida.")]
        [Range(0f, 100f)] [SerializeField] private float lowHpThresholdPercent = 33f;

        [SerializeField] private float lowHpBoostMultiplier = 1f;

        [Tooltip("Daño ENTRANTE por tipo: multiplicadores (Sebo: Fuego x0.5 y Hielo x0.5).")]
        [SerializeField] private IncomingTypeMultiplierData[] incomingTypeMultipliers;

        [Header("Prioridad / fin de turno / al cambiar / prevención (Lote 4)")]
        [Tooltip("Multiplicador STAB explícito (Adaptable = 2). 0 = clásico (1.5).")]
        [SerializeField] private float stabMultiplierOverride = 0f;

        [Tooltip("Bonus de prioridad para movimientos de ESTADO (Vista Lince = 1).")]
        [SerializeField] private int statusMovePriorityBonus = 0;

        [Tooltip("Impide que el RIVAL le baje etapas de stat (Cuerpo Puro).")]
        [SerializeField] private bool preventsStatReduction = false;

        [Tooltip("Al cambiarse (salir), cura su estado alterado (Cura Natural).")]
        [SerializeField] private bool curesStatusOnSwitchOut = false;

        [Tooltip("Al cambiarse, recupera este % de PS máx (Regeneración = 33).")]
        [Range(0f, 100f)] [SerializeField] private float healPercentOnSwitchOut = 0f;

        [Tooltip("Al fin de turno, cambia una etapa de stat propia (Impulso: \"speed\"). Vacío = nada.")]
        [SerializeField] private string endOfTurnStatId;

        [Range(-6, 6)] [SerializeField] private int endOfTurnStages = 0;

        [Tooltip("Al fin de turno, recupera este % de PS máx (Cura Veneno = 12.5).")]
        [Range(0f, 100f)] [SerializeField] private float endOfTurnHealPercent = 0f;

        [Tooltip("Si está marcado, esa curación solo ocurre si el portador TIENE estado (Cura Veneno).")]
        [SerializeField] private bool endOfTurnHealRequiresStatus = false;

        [Tooltip("Al fin de turno, probabilidad de curarse el estado (Mudar = 30).")]
        [Range(0f, 100f)] [SerializeField] private float endOfTurnCureStatusChance = 0f;

        [Tooltip("Anula el daño residual de estados (Cura Veneno / Guardia Mágica).")]
        [SerializeField] private bool negatesStatusDamage = false;

        public string Id => id;
        public string DisplayName => displayName;
        public PassiveStatModifierData[] PassiveModifiers => passiveModifiers;
        public string[] StatusImmunities => statusImmunities;
        public ElementTypeData[] TypeImmunities => typeImmunities;
        public float AbsorbImmuneHealPercent => absorbImmuneHealPercent;
        public string ContactReactionStatus => contactReactionStatus;
        public float ContactReactionChance => contactReactionChance;
        public string OnEntryStatId => onEntryStatId;
        public int OnEntryStages => onEntryStages;
        public bool OnEntryTargetsSelf => onEntryTargetsSelf;
        public string StatusStatBoostStatId => statusStatBoostStatId;
        public float StatusStatBoostMultiplier => statusStatBoostMultiplier;
        public ElementTypeData LowHpBoostType => lowHpBoostType;
        public float LowHpThresholdPercent => lowHpThresholdPercent;
        public float LowHpBoostMultiplier => lowHpBoostMultiplier;
        public IncomingTypeMultiplierData[] IncomingTypeMultipliers => incomingTypeMultipliers;
        public float StabMultiplierOverride => stabMultiplierOverride;
        public int StatusMovePriorityBonus => statusMovePriorityBonus;
        public bool PreventsStatReduction => preventsStatReduction;
        public bool CuresStatusOnSwitchOut => curesStatusOnSwitchOut;
        public float HealPercentOnSwitchOut => healPercentOnSwitchOut;
        public string EndOfTurnStatId => endOfTurnStatId;
        public int EndOfTurnStages => endOfTurnStages;
        public float EndOfTurnHealPercent => endOfTurnHealPercent;
        public bool EndOfTurnHealRequiresStatus => endOfTurnHealRequiresStatus;
        public float EndOfTurnCureStatusChance => endOfTurnCureStatusChance;
        public bool NegatesStatusDamage => negatesStatusDamage;

        [System.Serializable]
        public sealed class PassiveStatModifierData
        {
            public string statId;        // "attack", "speed"... (id de stat)
            public float multiplier = 1f;
        }

        [System.Serializable]
        public sealed class IncomingTypeMultiplierData
        {
            public ElementTypeData type;
            public float multiplier = 1f;
        }
    }
}
