using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Formulas;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// ACL de las curvas: GrowthCurveData (ficha cómoda) -> GrowthCurve (tabla pura). La generación
    /// de los presets vive en GrowthCurvePresets (dominio, matemática pura): UNA fuente de verdad.
    /// Aquí se decide qué preset usar, se aplica el multiplicador, o se toma la tabla personalizada.
    /// </summary>
    public static class GrowthCurveMapper
    {
        public static GrowthCurve ToDomain(GrowthCurveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var id = new Id<GrowthCurve>(data.Id);

            if (data.Formula == GrowthCurveFormula.Custom)
                return FromCustomTable(id, data.DisplayName, data.MaxLevel, data.CustomTable);

            if (data.Formula == GrowthCurveFormula.Expression)
                return Scale(FromFormulas(id, data.DisplayName, data.MaxLevel, data.FormulaSegments), data.XpMultiplierPercent);

            var preset = Preset(data.Formula, id, data.DisplayName, data.MaxLevel);
            return Scale(preset, data.XpMultiplierPercent);
        }

        /// <summary>
        /// Genera la curva de un preset clásico. Público para que el EDITOR dibuje las curvas de
        /// referencia en el gráfico con exactamente la misma matemática que el juego.
        /// </summary>
        public static GrowthCurve Preset(GrowthCurveFormula formula, Id<GrowthCurve> id, string name, int maxLevel)
        {
            switch (formula)
            {
                case GrowthCurveFormula.Fast:        return GrowthCurvePresets.Fast(id, name, maxLevel);
                case GrowthCurveFormula.Slow:        return GrowthCurvePresets.Slow(id, name, maxLevel);
                case GrowthCurveFormula.MediumSlow:  return GrowthCurvePresets.MediumSlow(id, name, maxLevel);
                case GrowthCurveFormula.Erratic:     return GrowthCurvePresets.Erratic(id, name, maxLevel);
                case GrowthCurveFormula.Fluctuating: return GrowthCurvePresets.Fluctuating(id, name, maxLevel);
                default:                             return GrowthCurvePresets.MediumFast(id, name, maxLevel);
            }
        }

        /// <summary>
        /// Las fórmulas OFICIALES de cada curva clásica, escritas en el lenguaje de fórmulas del editor.
        /// Dan exactamente la misma tabla que los presets (lo comprueba un test). Sirven para aprender y
        /// como punto de partida: "Convertir en fórmula" y luego retocar.
        /// </summary>
        public static (int from, string expr)[] OfficialFormula(GrowthCurveFormula f)
        {
            switch (f)
            {
                case GrowthCurveFormula.Fast:       return new[] { (2, "4*n^3/5") };
                case GrowthCurveFormula.MediumSlow: return new[] { (2, "6/5*n^3 - 15*n^2 + 100*n - 140") };
                case GrowthCurveFormula.Slow:       return new[] { (2, "5*n^3/4") };
                case GrowthCurveFormula.Erratic:    return new[]
                {
                    (2, "n^3*(100-n)/50"), (50, "n^3*(150-n)/100"),
                    (68, "n^3*floor((1911-10*n)/3)/500"), (98, "n^3*(160-n)/100"),
                };
                case GrowthCurveFormula.Fluctuating: return new[]
                {
                    (2, "n^3*(floor((n+1)/3)+24)/50"), (15, "n^3*(n+14)/50"), (36, "n^3*(floor(n/2)+32)/50"),
                };
                default: return new[] { (2, "n^3") };
            }
        }

        /// <summary>Variables que puede usar una fórmula de curva (para el editor y el validador).</summary>
        public static readonly string[] CurveVariables = { "n" };

        /// <summary>
        /// Curva desde tramos de fórmula. Los tramos con fórmula inválida se ignoran (el validador y el
        /// editor muestran el error exacto); si no queda ninguno válido, se usa la Media.
        /// </summary>
        public static GrowthCurve FromFormulas(Id<GrowthCurve> id, string name, int maxLevel, FormulaSegment[] segments)
        {
            var parsed = new List<(int fromLevel, MathExpression formula)>();
            if (segments != null)
                foreach (var seg in segments)
                    if (seg != null && MathExpression.TryParse(seg.expression, CurveVariables, out var expr, out _))
                        parsed.Add((seg.fromLevel, expr));
            return GrowthCurvePresets.FromSegments(id, name, maxLevel, parsed);
        }

        /// <summary>Escala una curva por un porcentaje (100 = sin cambio). Cálculo entero en long.</summary>
        public static GrowthCurve Scale(GrowthCurve curve, int percent)
        {
            if (percent == 100 || percent <= 0) return curve;
            var table = new List<int>(curve.MaxLevel + 1) { 0 };
            for (int level = 1; level <= curve.MaxLevel; level++)
            {
                long v = (long)curve.XpToReachLevel(level) * percent / 100;
                table.Add(v > int.MaxValue ? int.MaxValue : (int)v);
            }
            return new GrowthCurve(curve.Id, curve.DisplayName, table);
        }

        // Tabla personalizada. Si el autor escribió MENOS niveles que el máximo, la curva termina donde
        // termina su tabla (no se podrá subir más allá): repetir el último valor haría que, al alcanzarlo,
        // el monstruo saltara de golpe al nivel máximo. Si la tabla está vacía, se usa la Media para no
        // dejar el juego sin curva; el validador avisa en ambos casos.
        private static GrowthCurve FromCustomTable(Id<GrowthCurve> id, string name, int maxLevel, int[] custom)
        {
            if (custom == null || custom.Length < 3)
                return GrowthCurvePresets.MediumFast(id, name, maxLevel);

            int max = Math.Min(maxLevel < 2 ? 2 : maxLevel, custom.Length - 1);
            var table = new List<int>(max + 1);
            for (int level = 0; level <= max; level++)
            {
                int v = level <= 1 ? 0 : custom[level];
                table.Add(v < 0 ? 0 : v);
            }
            return new GrowthCurve(id, name, table);
        }
    }
}
