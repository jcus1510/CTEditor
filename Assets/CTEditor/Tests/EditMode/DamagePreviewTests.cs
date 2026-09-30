using System.Collections.Generic;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// La calculadora de daño (TurnResolver.PreviewDamage) debe predecir EXACTAMENTE lo que hace el
    /// combate: todo golpe real cae dentro de [mín, máx] o, si es crítico, dentro de [crítMín, crítMáx].
    /// </summary>
    public class DamagePreviewTests
    {
        private static Move Tackle() => new Move(new Id<Move>("tackle"), "Placaje", new Id<ElementType>("normal"),
            MoveCategory.Physical, 40, null, 35, 0, MoveTarget.SingleEnemy);

        private static BattleParticipant Mon(string id, int speed) => new BattleParticipant(
            new Id<BattleParticipant>(id), new Id<Species>(id), 50,
            new StatBlock.Builder().Set(StatId.Hp, 5000).Set(StatId.Attack, 120).Set(StatId.Defense, 90)
                .Set(StatId.SpAttack, 80).Set(StatId.SpDefense, 80).Set(StatId.Speed, speed).Build(),
            5000,
            new List<Id<ElementType>> { new Id<ElementType>("normal") },
            new List<Id<Move>> { new Id<Move>("tackle") });

        private static TurnResolver Resolver(int seed) => new TurnResolver(
            new InMemoryCatalog<Move>(new[] { Tackle() }, m => m.Id.Value),
            new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(seed));

        [Test]
        public void Preview_is_read_only_and_ordered()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99), Mon("b", 1));
            var p = Resolver(1).PreviewDamage(battle.Player, battle.Enemy, new Id<Move>("tackle"));

            Assert.IsTrue(p.DealsDamage);
            Assert.IsTrue(p.Stab);                         // Normal usando un movimiento Normal
            Assert.LessOrEqual(p.Min, p.Max);
            Assert.Less(p.Max, p.CritMin + 1);             // el crítico pega más
            Assert.AreEqual(5000, battle.Enemy.CurrentHp); // la vista previa no dañó a nadie
        }

        [Test]
        public void Every_real_hit_falls_inside_the_predicted_range()
        {
            for (int seed = 1; seed <= 150; seed++)
            {
                var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99), Mon("b", 1));
                var resolver = Resolver(seed);
                var p = resolver.PreviewDamage(battle.Player, battle.Enemy, new Id<Move>("tackle"));

                var events = resolver.ResolveTurn(battle, new UseMove(new Id<Move>("tackle")), new UseMove(new Id<Move>("tackle")));

                // El primer golpe sobre 'b' (el defensor de la vista previa).
                bool crit = false;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i] is DamageDealtEvent d && d.Target.Value == "b")
                    {
                        crit = i + 1 < events.Count && events[i + 1] is CriticalHitEvent;
                        int lo = crit ? p.CritMin : p.Min, hi = crit ? p.CritMax : p.Max;
                        Assert.GreaterOrEqual(d.Amount, lo, $"semilla {seed}");
                        Assert.LessOrEqual(d.Amount, hi, $"semilla {seed}");
                        break;
                    }
                }
            }
        }

        [Test]
        public void Uses_to_ko_accounts_for_current_hp()
        {
            var p = new DamagePreview(true, 1f, false, false, 40, 50, 60, 75, 1, 1, null, 100, 200);
            var (best, worst) = p.UsesToKo();
            Assert.AreEqual(2, best);   // 100 PS / 50 = 2
            Assert.AreEqual(3, worst);  // 100 / 40 = 2.5 -> 3
            Assert.AreEqual(25f, p.PercentOfMaxHp(50));
        }
    }
}
