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

        [System.Serializable]
        public sealed class PassiveStatModifierData
        {
            public string statId;        // "attack", "speed"... (id de stat)
            public float multiplier = 1f;
        }
    }
}
