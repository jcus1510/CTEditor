using System;
using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Cómo se genera la tabla de XP de una curva: uno de los 6 grupos clásicos, o una tabla
    /// PERSONALIZADA escrita nivel a nivel por el autor.
    /// </summary>
    public enum GrowthCurveFormula
    {
        Fast,        // 4n³/5  (sube rápido)
        MediumFast,  // n³     (la "normal")
        MediumSlow,  // 6/5 n³ - 15n² + 100n - 140
        Slow,        // 5n³/4  (sube lento)
        Erratic,     // por tramos: rapidísima al final (600k al Nv.100)
        Fluctuating, // por tramos: lentísima al final (1.64M al Nv.100)
        Custom,      // tabla escrita a mano en 'customTable'
        Expression   // FÓRMULAS propias por tramos en 'formulaSegments' (variable n = nivel)
    }

    /// <summary>Un tramo de fórmula: desde qué nivel se aplica y la expresión (n = nivel).</summary>
    [Serializable]
    public sealed class FormulaSegment
    {
        [Tooltip("Nivel desde el que se usa esta fórmula (hasta el siguiente tramo).")]
        [Min(2)] public int fromLevel = 2;

        [Tooltip("XP TOTAL para ser nivel n. Ej: n^3   0.8*n^3   floor(n^3*(100-n)/50)   100*n + 5*n^2")]
        public string expression = "n^3";
    }

    /// <summary>
    /// Ficha de una CURVA DE EXPERIENCIA (asset en GameContent/Resources/Curves).
    ///
    /// Cuatro niveles de libertad, de menos a más:
    ///   1) Elegir un preset clásico (lo típico).
    ///   2) Preset + MULTIPLICADOR: "en mi juego todo sube un 50% más rápido" = 67%.
    ///   3) Tabla PERSONALIZADA: el autor escribe la XP total de cada nivel (el editor de curvas puede
    ///      rellenarla a partir de un preset con un botón, para afinarla después).
    ///   4) FÓRMULAS POR TRAMOS: el autor escribe expresiones matemáticas (n = nivel), cada una válida
    ///      desde un nivel. El multiplicador también se aplica.
    ///
    /// El dominio (GrowthCurve) solo ve una tabla acumulada; toda esta flexibilidad vive aquí y en el
    /// mapper, sin tocar el motor.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Curva de experiencia", fileName = "NuevaCurva")]
    public sealed class GrowthCurveData : ScriptableObject, IContentAsset
    {
        [Tooltip("Id estable (lo referencia la Species). Único entre curvas.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Tooltip("Cómo se genera la tabla: un preset clásico o Custom (tabla escrita a mano).")]
        [SerializeField] private GrowthCurveFormula formula = GrowthCurveFormula.MediumFast;

        [Tooltip("Nivel máximo que cubre la curva.")]
        [SerializeField, Min(2)] private int maxLevel = 100;

        [Tooltip("Escala la XP necesaria del preset: 100 = igual, 50 = la mitad (sube el doble de rápido), 200 = el doble. No afecta a Custom.")]
        [SerializeField, Min(1)] private int xpMultiplierPercent = 100;

        [Tooltip("Solo para Custom: XP TOTAL para ser cada nivel. Índice = nivel (0 y 1 se ignoran, valen 0).")]
        [SerializeField] private int[] customTable;

        [Tooltip("Solo para Expression: tramos de fórmula (n = nivel). El primero debería empezar en el nivel 2.")]
        [SerializeField] private FormulaSegment[] formulaSegments = { new FormulaSegment() };

        public string Id => id;
        public string DisplayName => displayName;
        public GrowthCurveFormula Formula => formula;
        public int MaxLevel => maxLevel;
        public int XpMultiplierPercent => xpMultiplierPercent;
        public int[] CustomTable => customTable;
        public FormulaSegment[] FormulaSegments => formulaSegments;
    }
}
