using System;
using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha donde el autor define la tabla de tipos: una lista de cruces (atacante, defensor,
    /// multiplicador). Solo define lo "interesante" (los x2, x0.5, x0); lo que no liste queda
    /// neutral por defecto, porque así lo resuelve el TypeChart del dominio.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Type Chart", fileName = "TypeChart")]
    public sealed class TypeChartData : ScriptableObject
    {
        [SerializeField] private MatchupEntry[] matchups;

        public MatchupEntry[] Matchups => matchups;

        /// <summary>Un cruce: el tipo atacante y el defensor (arrastrados) y su multiplicador.</summary>
        [Serializable]
        public struct MatchupEntry
        {
            public ElementTypeData attacking;
            public ElementTypeData defending;

            // Multiplicador del cruce: 2 = muy eficaz, 0.5 = poco, 0 = inmune. El 'Tooltip' es un
            // textito de ayuda que Unity muestra al pasar el ratón sobre el campo en el Inspector.
            [Tooltip("2 = muy eficaz, 0.5 = poco eficaz, 0 = inmune")]
            [Min(0f)] public float multiplier;
        }
    }
}
