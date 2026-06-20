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

        [Tooltip("Probabilidad por turno de no poder actuar. 0 = nunca impide (p.ej. quemado).")]
        [SerializeField, Range(0f, 100f)] private float actionPreventionChance = 0f;

        [Tooltip("Si está marcado, el estado se quita al cambiar de monstruo (estados volátiles).")]
        [SerializeField] private bool clearedOnSwitch = false;

        public string Id => id;
        public string DisplayName => displayName;
        public float ResidualDamagePercent => residualDamagePercent;
        public float ActionPreventionChance => actionPreventionChance;
        public bool ClearedOnSwitch => clearedOnSwitch;
    }
}
