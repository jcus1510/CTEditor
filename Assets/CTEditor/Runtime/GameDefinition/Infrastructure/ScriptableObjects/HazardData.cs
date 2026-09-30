using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha de una TRAMPA DE CAMPO (Púas, Trampa Rocas, Púas Tóxicas, Red Viscosa...). Se coloca con
    /// un movimiento (efecto «Poner una trampa») y afecta a cada monstruo que ENTRE en ese lado.
    /// La traduce HazardMapper al dominio.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Trampa de campo", fileName = "NuevaTrampa")]
    public sealed class HazardData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("Color para reconocerla en los editores.")]
        [SerializeField] private Color color = new Color(0.7f, 0.6f, 0.45f);

        [Header("Capas")]
        [Tooltip("Cuántas veces se puede poner (Púas: 3, Púas Tóxicas: 2, Trampa Rocas: 1).")]
        [SerializeField, Min(1)] private int maxLayers = 1;

        [Header("Daño al entrar")]
        [Tooltip("% de los PS máximos que quita al entrar, por capa (1.ª, 2.ª, 3.ª...). Púas: 12,5 · 16,67 · 25. Vacío = no daña.")]
        [SerializeField] private float[] damagePercentByLayer = new float[0];
        [Tooltip("Si eliges un tipo, el daño se multiplica por su eficacia contra el que entra (Trampa Rocas: Roca → un Charizard recibe ×4).")]
        [SerializeField] private ElementTypeData damageScalesWithType;

        [Header("Estado y estadísticas al entrar")]
        [Tooltip("Estado que inflige, por capa (Púas Tóxicas: poison, toxic). Vacío = ninguno.")]
        [StatusIdReference, SerializeField] private string[] statusByLayer = new string[0];
        [Tooltip("Estadística cuya etapa cambia al entrar (Red Viscosa: speed). Vacío = ninguna.")]
        [StatIdReference, SerializeField] private string statId = "";
        [Tooltip("Etapas que cambia (Red Viscosa: -1).")]
        [SerializeField, Range(-6, 6)] private int stages = 0;

        [Header("Quién se libra")]
        [Tooltip("Tipos que no la notan (Volador, para las que están en el suelo).")]
        [SerializeField] private ElementTypeData[] immuneTypes = new ElementTypeData[0];
        [Tooltip("Si la habilidad del que entra le hace inmune a ESTE tipo, no la nota (Levitación → Tierra).")]
        [SerializeField] private ElementTypeData immuneIfAbilityBlocksType;
        [Tooltip("Tipos que, al entrar, la RETIRAN de su lado (Veneno retira las Púas Tóxicas).")]
        [SerializeField] private ElementTypeData[] absorbedByTypes = new ElementTypeData[0];

        public string Id => id;
        public string DisplayName => displayName;
        public Color Color => color;
        public int MaxLayers => maxLayers;
        public float[] DamagePercentByLayer => damagePercentByLayer;
        public ElementTypeData DamageScalesWithType => damageScalesWithType;
        public string[] StatusByLayer => statusByLayer;
        public string StatId => statId;
        public int Stages => stages;
        public ElementTypeData[] ImmuneTypes => immuneTypes;
        public ElementTypeData ImmuneIfAbilityBlocksType => immuneIfAbilityBlocksType;
        public ElementTypeData[] AbsorbedByTypes => absorbedByTypes;
    }
}
