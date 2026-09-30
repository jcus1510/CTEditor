using System.Collections.Generic;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.Party.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Lote 3: la "genética" del individuo. Valida la fórmula contra un ejemplo REAL de los juegos,
    /// los topes de EVs, las naturalezas y cómo el factory decide los IVs. Dominio puro, sin Unity.
    /// </summary>
    public class GeneticsTests
    {
        private static readonly ClassicStatGrowthFormula Formula = new ClassicStatGrowthFormula();

        private static Nature Adamant() => new Nature(new Id<Nature>("adamant"), "Firme", StatId.Attack, StatId.SpAttack);
        private static Nature Serious() => new Nature(new Id<Nature>("serious"), "Seria", StatId.Speed, StatId.Speed);

        private static Species Garchomp() => new Species(
            new Id<Species>("garchomp"), "Garchomp",
            new List<Id<ElementType>> { new Id<ElementType>("dragon") },
            new StatBlock.Builder()
                .Set(StatId.Hp, 108).Set(StatId.Attack, 130).Set(StatId.Defense, 95)
                .Set(StatId.SpAttack, 80).Set(StatId.SpDefense, 85).Set(StatId.Speed, 102).Build(),
            new List<LearnableMove>(), new List<Evolution>());

        // --- Fórmula ---

        [Test]
        public void Formula_matches_the_official_Garchomp_example()
        {
            // Ejemplo de Bulbapedia: Garchomp Nv.78, naturaleza Firme (+Ataque).
            int atk = Formula.Compute(StatId.Attack, 130, 78, iv: 12, ev: 190, naturePercent: 110);
            int hp = Formula.Compute(StatId.Hp, 108, 78, iv: 24, ev: 74, naturePercent: 100);
            Assert.AreEqual(278, atk, "Ataque");
            Assert.AreEqual(289, hp, "PS");
        }

        [Test]
        public void Formula_without_genetics_equals_the_old_formula()
        {
            // Compatibilidad: sin IV/EV y neutra debe dar lo mismo que antes del Lote 3,
            // así los combates y tests viejos no cambian.
            for (int level = 1; level <= 100; level += 7)
            {
                Assert.AreEqual((2 * 80 * level) / 100 + 5, Formula.Compute(StatId.Speed, 80, level));
                Assert.AreEqual((2 * 80 * level) / 100 + level + 10, Formula.Compute(StatId.Hp, 80, level));
                Assert.AreEqual(Formula.Compute(StatId.Attack, 80, level),
                                Formula.Compute(StatId.Attack, 80, level, 0, 0, 100));
            }
        }

        [Test]
        public void Nature_never_affects_hp()
        {
            int neutral = Formula.Compute(StatId.Hp, 100, 50, 31, 252, 100);
            int boosted = Formula.Compute(StatId.Hp, 100, 50, 31, 252, 110);
            Assert.AreEqual(neutral, boosted);
        }

        // --- Naturalezas ---

        [Test]
        public void Nature_boosts_one_stat_and_hinders_another()
        {
            var n = Adamant();
            Assert.AreEqual(110, n.PercentFor(StatId.Attack));
            Assert.AreEqual(90, n.PercentFor(StatId.SpAttack));
            Assert.AreEqual(100, n.PercentFor(StatId.Speed));
            Assert.IsFalse(n.IsNeutral);
        }

        [Test]
        public void Nature_that_boosts_and_hinders_the_same_stat_is_neutral()
        {
            var n = Serious();
            Assert.IsTrue(n.IsNeutral);
            Assert.AreEqual(100, n.PercentFor(StatId.Speed));
        }

        // --- EVs ---

        [Test]
        public void Effort_values_respect_per_stat_and_total_caps()
        {
            var evs = new EffortValues(perStatCap: 252, totalCap: 510);

            Assert.AreEqual(252, evs.Add(StatId.Attack, 300)); // recortado al tope por stat
            Assert.AreEqual(0, evs.Add(StatId.Attack, 10));    // la stat ya está llena
            Assert.AreEqual(252, evs.Add(StatId.Speed, 252));
            Assert.AreEqual(6, evs.Add(StatId.Hp, 100));       // solo quedaban 510-504 = 6
            Assert.AreEqual(510, evs.Total);
            Assert.AreEqual(0, evs.Add(StatId.Defense, 4));    // total lleno
        }

        [Test]
        public void Gaining_effort_raises_stats_after_recompute()
        {
            var species = Garchomp();
            var mon = MonsterFactory.Create(new Id<MonsterInstance>("g"), species, 100,
                Ruleset.Classic, Formula);
            int before = mon.Stats.Of(StatId.Attack);

            mon.AddEffort(StatId.Attack, 252);
            mon.RecomputeStats(species.BaseStats, Formula);

            Assert.AreEqual(before + 63, mon.Stats.Of(StatId.Attack)); // 252/4 = +63 al Nv.100
        }

        // --- IVs en el factory ---

        [Test]
        public void Factory_fixed_ivs_apply_to_every_stat_and_are_clamped()
        {
            var mon = MonsterFactory.Create(new Id<MonsterInstance>("g"), Garchomp(), 50,
                Ruleset.Classic, Formula, fixedIv: 99); // 99 > 31 -> se recorta a 31
            foreach (var stat in StatId.Classic)
                Assert.AreEqual(31, mon.IvOf(stat), stat.Value);
        }

        [Test]
        public void Factory_random_ivs_stay_within_range()
        {
            var rng = new SeededRng(42);
            for (int i = 0; i < 50; i++)
            {
                var mon = MonsterFactory.Create(new Id<MonsterInstance>("g" + i), Garchomp(), 50,
                    Ruleset.Classic, Formula, ivRng: rng);
                foreach (var stat in StatId.Classic)
                {
                    Assert.GreaterOrEqual(mon.IvOf(stat), 0);
                    Assert.LessOrEqual(mon.IvOf(stat), 31);
                }
            }
        }

        [Test]
        public void Ruleset_without_ivs_never_generates_them()
        {
            var noIvs = Ruleset.Classic.With(maxIv: 0);
            var mon = MonsterFactory.Create(new Id<MonsterInstance>("g"), Garchomp(), 50,
                noIvs, Formula, ivRng: new SeededRng(1), fixedIv: 31);
            Assert.IsNull(mon.Ivs);
            Assert.AreEqual(0, mon.IvOf(StatId.Attack));
        }

        [Test]
        public void Factory_applies_nature_to_starting_stats()
        {
            var neutral = MonsterFactory.Create(new Id<MonsterInstance>("a"), Garchomp(), 50, Ruleset.Classic, Formula);
            var adamant = MonsterFactory.Create(new Id<MonsterInstance>("b"), Garchomp(), 50, Ruleset.Classic, Formula,
                nature: Adamant());

            Assert.Greater(adamant.Stats.Of(StatId.Attack), neutral.Stats.Of(StatId.Attack));
            Assert.Less(adamant.Stats.Of(StatId.SpAttack), neutral.Stats.Of(StatId.SpAttack));
            Assert.AreEqual(neutral.Stats.Of(StatId.Speed), adamant.Stats.Of(StatId.Speed));
        }
    }
}
