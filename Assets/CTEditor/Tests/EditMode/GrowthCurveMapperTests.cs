using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Lote 4: la ficha de curva (preset, multiplicador, tabla personalizada) se traduce bien al
    /// dominio, y los 6 presets coinciden con los totales oficiales al nivel 100.
    /// </summary>
    public class GrowthCurveMapperTests
    {
        // Los campos de la ficha son privados ([SerializeField] private): en un test los fijamos por
        // reflexión, igual que Unity los rellena al cargar un asset.
        private static GrowthCurveData Curve(GrowthCurveFormula formula, int maxLevel = 100, int multiplier = 100, int[] table = null)
        {
            var data = ScriptableObject.CreateInstance<GrowthCurveData>();
            Set(data, "id", "test");
            Set(data, "displayName", "Test");
            Set(data, "formula", formula);
            Set(data, "maxLevel", maxLevel);
            Set(data, "xpMultiplierPercent", multiplier);
            Set(data, "customTable", table);
            return data;
        }

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        [Test]
        public void Presets_match_official_totals_at_level_100()
        {
            Assert.AreEqual(800000, GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.Fast)).XpToReachLevel(100));
            Assert.AreEqual(1000000, GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.MediumFast)).XpToReachLevel(100));
            Assert.AreEqual(1059860, GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.MediumSlow)).XpToReachLevel(100));
            Assert.AreEqual(1250000, GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.Slow)).XpToReachLevel(100));
            Assert.AreEqual(600000, GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.Erratic)).XpToReachLevel(100));
            Assert.AreEqual(1640000, GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.Fluctuating)).XpToReachLevel(100));
        }

        [Test]
        public void Multiplier_scales_the_xp_needed()
        {
            var half = GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.MediumFast, multiplier: 50));
            var twice = GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.MediumFast, multiplier: 200));
            Assert.AreEqual(500, half.XpToReachLevel(10));    // 10³ = 1000 → la mitad
            Assert.AreEqual(2000, twice.XpToReachLevel(10));  // el doble
            Assert.AreEqual(0, half.XpToReachLevel(1));       // ser nivel 1 nunca cuesta XP
        }

        [Test]
        public void Custom_table_is_used_and_ends_where_the_table_ends()
        {
            var curve = GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.Custom, maxLevel: 50,
                table: new[] { 0, 0, 10, 30, 70 }));

            Assert.AreEqual(4, curve.MaxLevel);          // la tabla solo llega al nivel 4
            Assert.AreEqual(30, curve.XpToReachLevel(3));
            Assert.AreEqual(4, curve.LevelForXp(1000000)); // no salta más allá de la tabla
        }

        [Test]
        public void Empty_custom_table_falls_back_to_the_normal_curve()
        {
            var curve = GrowthCurveMapper.ToDomain(Curve(GrowthCurveFormula.Custom, table: new int[0]));
            Assert.AreEqual(1000, curve.XpToReachLevel(10)); // Media: n³
        }
    }
}
