using System.Collections.Generic;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.Party.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Valida el núcleo de XP (Lote 1): la curva traduce XP&lt;->nivel, y un individuo sube de nivel
    /// al recibir XP, recalculando sus stats. Todo en dominio puro, sin Unity.
    /// </summary>
    public class GrowthCurveAndXpTests
    {
        // Curva "MediumFast" (n^3) hasta nivel 10: [_,0,8,27,64,125,216,343,512,729,1000].
        private static GrowthCurve MediumFast10()
        {
            var table = new List<int> { 0, 0 };
            for (int n = 2; n <= 10; n++) table.Add(n * n * n);
            return new GrowthCurve(new Id<GrowthCurve>("medium_fast"), "Medium Fast", table);
        }

        private static Species DummySpecies()
        {
            var types = new List<Id<ElementType>> { new Id<ElementType>("normal") };
            var stats = new StatBlock.Builder()
                .Set(StatId.Hp, 45).Set(StatId.Attack, 49).Set(StatId.Defense, 49)
                .Set(StatId.SpAttack, 65).Set(StatId.SpDefense, 65).Set(StatId.Speed, 45)
                .Build();
            return new Species(new Id<Species>("dummy"), "Dummy", types, stats,
                new List<LearnableMove>(), new List<Evolution>());
        }

        [Test]
        public void Curve_maps_levels_and_xp()
        {
            var curve = MediumFast10();
            Assert.AreEqual(0, curve.XpToReachLevel(1));
            Assert.AreEqual(8, curve.XpToReachLevel(2));
            Assert.AreEqual(125, curve.XpToReachLevel(5));
            Assert.AreEqual(5, curve.LevelForXp(125));
            Assert.AreEqual(5, curve.LevelForXp(200));   // entre 125 (5) y 216 (6)
            Assert.AreEqual(7, curve.LevelForXp(343));
            Assert.AreEqual(1, curve.LevelForXp(7));     // por debajo del umbral del 2
        }

        [Test]
        public void Monster_levels_up_and_recomputes_stats()
        {
            var curve = MediumFast10();
            var growth = new ClassicStatGrowthFormula();
            var species = DummySpecies();

            var mon = MonsterFactory.Create(
                new Id<MonsterInstance>("m1"), species, level: 5,
                ruleset: Ruleset.Classic, growth: growth, chosenMoves: null, curve: curve);

            Assert.AreEqual(5, mon.Level.Value);
            Assert.AreEqual(125, mon.Experience.Value);   // XP de arranque coherente con el nivel
            int atkAt5 = mon.Stats.Of(StatId.Attack);
            int hpAt5 = mon.MaxHp;

            // Suficiente XP para llegar exactamente a 343 (nivel 7): dos niveles.
            var result = mon.AddExperience(343 - 125, curve, species.BaseStats, growth);

            Assert.IsTrue(result.LeveledUp);
            Assert.AreEqual(2, result.LevelsGained);
            Assert.AreEqual(7, result.NewLevel);
            Assert.AreEqual(7, mon.Level.Value);
            Assert.AreEqual(343, mon.Experience.Value);
            Assert.Greater(mon.Stats.Of(StatId.Attack), atkAt5); // stats recalculados al subir
            Assert.Greater(mon.MaxHp, hpAt5);
        }

        [Test]
        public void Xp_below_threshold_does_not_level_up()
        {
            var curve = MediumFast10();
            var growth = new ClassicStatGrowthFormula();
            var species = DummySpecies();
            var mon = MonsterFactory.Create(
                new Id<MonsterInstance>("m2"), species, 5, Ruleset.Classic, growth, null, curve);

            var result = mon.AddExperience(10, curve, species.BaseStats, growth); // 125 -> 135, sigue nivel 5
            Assert.IsFalse(result.LeveledUp);
            Assert.AreEqual(5, mon.Level.Value);
            Assert.AreEqual(135, mon.Experience.Value);
        }
    }
}
