using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Formulas;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// El evaluador de fórmulas del autor: precedencia, funciones, errores claros, y que las fórmulas
    /// OFICIALES de las 6 curvas escritas en este lenguaje dan EXACTAMENTE las mismas tablas que los presets.
    /// </summary>
    public class MathExpressionTests
    {
        private static double Eval(string f, double n = 0) => MathExpression.Parse(f, new[] { "n" }).Evaluate("n", n);

        [Test]
        public void Respects_operator_precedence_and_parentheses()
        {
            Assert.AreEqual(14.0, Eval("2 + 3 * 4"));
            Assert.AreEqual(20.0, Eval("(2 + 3) * 4"));
            Assert.AreEqual(512.0, Eval("2^3^2"));   // la potencia asocia a la derecha: 2^9
            Assert.AreEqual(-8.0, Eval("-2^3"));
            Assert.AreEqual(125.0, Eval("n^3", 5));
        }

        [Test]
        public void Supports_functions_in_english_and_spanish()
        {
            Assert.AreEqual(3.0, Eval("floor(3.9)"));
            Assert.AreEqual(4.0, Eval("techo(3.1)"));
            Assert.AreEqual(5.0, Eval("max(2, 5)"));
            Assert.AreEqual(5.0, Eval("max(2; 5)"));       // separador ';' para Excel en español
            Assert.AreEqual(10.0, Eval("clamp(n, 0, 10)", 99));
            Assert.AreEqual(3.0, Eval("raiz(9)"));
        }

        [Test]
        public void Reports_clear_errors()
        {
            Assert.IsFalse(MathExpression.TryParse("n^3 +", new[] { "n" }, out _, out var e1));
            Assert.IsTrue(e1.Contains("termina"));
            Assert.IsFalse(MathExpression.TryParse("(n+1", new[] { "n" }, out _, out var e2));
            Assert.IsTrue(e2.Contains(")"));
            Assert.IsFalse(MathExpression.TryParse("x*2", new[] { "n" }, out _, out var e3));
            Assert.IsTrue(e3.Contains("Variable desconocida"));
            Assert.IsFalse(MathExpression.TryParse("foo(2)", new[] { "n" }, out _, out var e4));
            Assert.IsTrue(e4.Contains("Función desconocida"));
        }

        [Test]
        public void Official_formulas_reproduce_every_classic_curve_exactly()
        {
            var id = new Id<GrowthCurve>("t");
            foreach (GrowthCurveFormula f in new[] { GrowthCurveFormula.Fast, GrowthCurveFormula.MediumFast,
                         GrowthCurveFormula.MediumSlow, GrowthCurveFormula.Slow, GrowthCurveFormula.Erratic,
                         GrowthCurveFormula.Fluctuating })
            {
                var preset = GrowthCurveMapper.Preset(f, id, "p", 100);

                var segs = GrowthCurveMapper.OfficialFormula(f);
                var data = new FormulaSegment[segs.Length];
                for (int i = 0; i < segs.Length; i++)
                    data[i] = new FormulaSegment { fromLevel = segs[i].from, expression = segs[i].expr };
                var fromFormula = GrowthCurveMapper.FromFormulas(id, "f", 100, data);

                for (int level = 1; level <= 100; level++)
                    Assert.AreEqual(preset.XpToReachLevel(level), fromFormula.XpToReachLevel(level), $"{f} nivel {level}");
            }
        }
    }
}
