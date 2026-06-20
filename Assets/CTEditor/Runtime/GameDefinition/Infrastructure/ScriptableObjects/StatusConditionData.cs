using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha que rellena el AUTOR para un estado alterado. "Quemado", "Veneno", o cualquier estado
    /// que invente. El combate no codifica ninguno: ejecuta lo que diga esta ficha (E.4). Su mapper la
    /// traduce a StatusConditionDefinition (dominio puro).
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Status Condition", fileName = "NewStatus")]
    public sealed class StatusConditionData : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Tooltip("Daño por turno como % de los PS máximos. 0 = no hace daño (p.ej. parálisis).")]
        [SerializeField, Range(0f, 100f)] private float residualDamagePercent = 0f;

        [Tooltip("Si está marcado, el daño residual escala cada turno (envenenamiento grave / tóxico).")]
        [SerializeField] private bool progressiveResidual = false;

        [Tooltip("Probabilidad por turno de no poder actuar. 0 = nunca impide (p.ej. quemado).")]
        [SerializeField, Range(0f, 100f)] private float actionPreventionChance = 0f;

        [Tooltip("Si está marcado, el estado se quita al cambiar de monstruo (estados volátiles).")]
        [SerializeField] private bool clearedOnSwitch = false;

        [Tooltip("Duración en turnos. 0 = permanente (hasta curar). >0 = se quita solo tras esos turnos (p.ej. sueño).")]
        [SerializeField, Min(0)] private int durationTurns = 0;

        [Tooltip("Modificadores pasivos de stats mientras dura (p.ej. Attack ×0.5 para quemado).")]
        [SerializeField] private PassiveStatModifierData[] passiveModifiers;

        public string Id => id;
        public string DisplayName => displayName;
        public float ResidualDamagePercent => residualDamagePercent;
        public bool ProgressiveResidual => progressiveResidual;
        public float ActionPreventionChance => actionPreventionChance;
        public bool ClearedOnSwitch => clearedOnSwitch;
        public int DurationTurns => durationTurns;
        public PassiveStatModifierData[] PassiveModifiers => passiveModifiers;

        /// <summary>Sub-ficha de un modificador pasivo: qué stat y por cuánto la multiplica (0.5 = la mitad).</summary>
        [System.Serializable]
        public sealed class PassiveStatModifierData
        {
            public string statId;        // "attack", "speed"... (id de stat)
            public float multiplier = 1f;
        }
    }
}
