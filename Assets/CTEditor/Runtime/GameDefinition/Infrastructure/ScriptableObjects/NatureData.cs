using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Ficha de una NATURALEZA (asset en GameContent/Resources/Natures). El autor escribe qué stat
    /// sube y cuál baja por su id ("attack", "speed"... o una inventada) y el porcentaje.
    /// Ambas vacías (o iguales) = naturaleza neutra.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Naturaleza", fileName = "NuevaNaturaleza")]
    public sealed class NatureData : ScriptableObject, IContentAsset
    {
        [Tooltip("Id estable (lo guarda cada individuo). Único entre naturalezas. Ej: 'adamant'.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Tooltip("Id de la estadística que SUBE (hp, attack, defense, sp_attack, sp_defense, speed o inventada). Vacío = ninguna.")]
        [StatIdReference, SerializeField] private string boostedStatId;

        [Tooltip("Id de la estadística que BAJA. Vacío = ninguna.")]
        [StatIdReference, SerializeField] private string hinderedStatId;

        [Tooltip("Porcentaje de inclinación. Clásico: 10 (= ×1.1 / ×0.9).")]
        [SerializeField, Min(0)] private int boostPercent = 10;

        public string Id => id;
        public string DisplayName => displayName;
        public string BoostedStatId => boostedStatId;
        public string HinderedStatId => hinderedStatId;
        public int BoostPercent => boostPercent;
    }
}
