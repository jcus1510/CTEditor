using System;
using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha de un CLIMA (lluvia, sol, arena, nieve... o uno inventado: "niebla tóxica"). Un movimiento
    /// lo activa con el efecto "Cambiar clima"; mientras dure potencia o debilita tipos y puede dañar
    /// cada turno. Las condiciones "si hace este clima" lo leen por su id.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Clima", fileName = "NuevoClima")]
    public sealed class WeatherData : ScriptableObject, IContentAsset
    {
        [Tooltip("Id estable (lo usan los movimientos y las condiciones). Ej.: rain")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Tooltip("Turnos que dura cuando un movimiento lo activa sin indicar duración. 0 = hasta que otro clima lo reemplace.")]
        [SerializeField, Min(0)] private int defaultTurns = 5;

        [Tooltip("Multiplicadores de potencia por tipo mientras dura. Lluvia: Agua ×1,5 y Fuego ×0,5.")]
        [SerializeField] private TypeMultiplierData[] typePowerMultipliers = new TypeMultiplierData[0];

        [Tooltip("Daño a TODOS al final de cada turno (% de PS máx). Arena/granizo: 6,25. 0 = nada.")]
        [SerializeField, Range(0f, 100f)] private float residualDamagePercent = 0f;

        [Tooltip("Tipos que NO reciben ese daño (arena: Roca, Tierra y Acero).")]
        [SerializeField] private ElementTypeData[] immuneTypes = new ElementTypeData[0];

        [Tooltip("Color para la interfaz (fondo del combate, etiquetas).")]
        [SerializeField] private Color color = new Color(0.55f, 0.7f, 0.9f);

        public string Id => id;
        public string DisplayName => displayName;
        public int DefaultTurns => defaultTurns;
        public TypeMultiplierData[] TypePowerMultipliers => typePowerMultipliers;
        public float ResidualDamagePercent => residualDamagePercent;
        public ElementTypeData[] ImmuneTypes => immuneTypes;
        public Color Color => color;

        [Serializable]
        public sealed class TypeMultiplierData
        {
            public ElementTypeData type;
            [Min(0f)] public float multiplier = 1.5f;
        }
    }
}
