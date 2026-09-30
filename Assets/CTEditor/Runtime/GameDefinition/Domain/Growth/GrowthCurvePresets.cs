using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Formulas;

namespace CTEditor.GameDefinition.Domain.Growth
{
    /// <summary>
    /// Las SEIS curvas de experiencia de los juegos clásicos, como generadores de tabla. Cada método
    /// devuelve un GrowthCurve ya armado (tabla acumulada hasta maxLevel). El mapper de contenido las
    /// usa para los presets, y el runtime puede pedir una por defecto (MediumFast) cuando una especie
    /// no eligió curva. Es matemática pura: puede vivir en el dominio sin ensuciar nada.
    ///
    /// XP TOTAL para SER nivel n (fórmulas clásicas):
    ///   Fast:        4n³/5
    ///   MediumFast:  n³
    ///   MediumSlow:  6/5·n³ − 15n² + 100n − 140   (negativa en niveles bajos: se recorta a 0)
    ///   Slow:        5n³/4
    ///   Erratic:     por tramos (total 600.000 al nivel 100)
    ///   Fluctuating: por tramos (total 1.640.000 al nivel 100)
    /// </summary>
    public static class GrowthCurvePresets
    {
        public static GrowthCurve Fast(Id<GrowthCurve> id, string name, int maxLevel)
            => Build(id, name, maxLevel, n => (long)n * n * n * 4 / 5);

        public static GrowthCurve MediumFast(Id<GrowthCurve> id, string name, int maxLevel)
            => Build(id, name, maxLevel, n => (long)n * n * n);

        public static GrowthCurve MediumSlow(Id<GrowthCurve> id, string name, int maxLevel)
            => Build(id, name, maxLevel, n => (long)n * n * n * 6 / 5 - 15L * n * n + 100L * n - 140L);

        public static GrowthCurve Slow(Id<GrowthCurve> id, string name, int maxLevel)
            => Build(id, name, maxLevel, n => (long)n * n * n * 5 / 4);

        public static GrowthCurve Erratic(Id<GrowthCurve> id, string name, int maxLevel)
            => Build(id, name, maxLevel, n =>
            {
                long n3 = (long)n * n * n;
                if (n < 50) return n3 * (100 - n) / 50;
                if (n < 68) return n3 * (150 - n) / 100;
                if (n < 98) return n3 * ((1911 - 10L * n) / 3) / 500;
                return n3 * (160 - n) / 100;
            });

        public static GrowthCurve Fluctuating(Id<GrowthCurve> id, string name, int maxLevel)
            => Build(id, name, maxLevel, n =>
            {
                long n3 = (long)n * n * n;
                if (n < 15) return n3 * ((n + 1) / 3 + 24) / 50;
                if (n < 36) return n3 * (n + 14) / 50;
                return n3 * (n / 2 + 32) / 50;
            });

        /// <summary>
        /// Curva definida por FÓRMULAS POR TRAMOS escritas por el autor. Cada tramo (desde, fórmula) se
        /// aplica desde su nivel hasta el nivel anterior al siguiente tramo; la variable 'n' es el nivel.
        /// Ejemplo (la Errática oficial): desde 2 "n^3*(100-n)/50", desde 50 "n^3*(150-n)/100"...
        /// Resultados negativos, no numéricos o infinitos se tratan como 0 / tope, para no romper el juego
        /// (el validador avisa al autor).
        /// </summary>
        public static GrowthCurve FromSegments(Id<GrowthCurve> id, string name, int maxLevel,
            IReadOnlyList<(int fromLevel, MathExpression formula)> segments)
        {
            if (segments == null || segments.Count == 0) return MediumFast(id, name, maxLevel);

            var ordered = new List<(int fromLevel, MathExpression formula)>(segments);
            ordered.Sort((a, b) => a.fromLevel.CompareTo(b.fromLevel));

            return Build(id, name, maxLevel, n =>
            {
                MathExpression f = ordered[0].formula;
                foreach (var seg in ordered)
                    if (n >= seg.fromLevel) f = seg.formula;
                double v = Math.Floor(f.Evaluate("n", n));
                if (double.IsNaN(v) || v < 0) return 0;
                if (v > int.MaxValue) return int.MaxValue;
                return (long)v;
            });
        }

        // Genera la tabla acumulada [0, nivel1=0, nivel2, ...] evaluando la fórmula por nivel.
        // Se calcula en long y se recorta a int (los totales clásicos caben de sobra en int).
        private static GrowthCurve Build(Id<GrowthCurve> id, string name, int maxLevel, Func<int, long> totalXp)
        {
            if (maxLevel < 2) maxLevel = 2;
            var table = new List<int>(maxLevel + 1);
            for (int n = 0; n <= maxLevel; n++)
            {
                long v = n <= 1 ? 0 : totalXp(n);
                if (v < 0) v = 0;
                if (v > int.MaxValue) v = int.MaxValue;
                table.Add((int)v);
            }
            return new GrowthCurve(id, name, table);
        }
    }
}
