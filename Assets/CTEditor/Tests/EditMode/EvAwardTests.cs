using System.Collections.Generic;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Lote 3: los EVs se ganan en combate. Al caer (o ser capturado) un rival, cada participante
    /// recibe su rendimiento de EVs COMPLETO, y el BattleResult lo entrega a Party.
    /// </summary>
    public class EvAwardTests
    {
        // Captura garantizada, para probar el camino "capturado" sin depender del azar.
        private sealed class AlwaysCatch : ICatchFormula
        {
            public CatchAttempt Attempt(CatchContext context, IRng rng) => new CatchAttempt(true, 3);
        }

        private static Move Blast() => new Move(
            new Id<Move>("blast"), "Blast", new Id<ElementType>("fire"), MoveCategory.Physical,
            250, null, 25, 0, MoveTarget.SingleEnemy);

        private static BattleParticipant Fighter(string id, int speed, IReadOnlyList<EvYieldEntry> evYield = null) =>
            new BattleParticipant(
                new Id<BattleParticipant>(id), new Id<Species>(id), 10,
                new StatBlock.Builder()
                    .Set(StatId.Hp, 30).Set(StatId.Attack, 100).Set(StatId.Defense, 10)
                    .Set(StatId.SpAttack, 10).Set(StatId.SpDefense, 10).Set(StatId.Speed, speed).Build(),
                30,
                new List<Id<ElementType>> { new Id<ElementType>("fire") },
                new List<Id<Move>> { new Id<Move>("blast") },
                baseExpYield: 64, evYield: evYield);

        private static TurnResolver Resolver(ICatchFormula catchFormula = null) =>
            new TurnResolver(
                new InMemoryCatalog<Move>(new[] { Blast() }, m => m.Id.Value),
                new TypeChart.Builder().Build(),
                new ClassicDamageFormula(), new SeededRng(3),
                catchFormula: catchFormula, xpFormula: new ClassicXpFormula());

        private static readonly IReadOnlyList<EvYieldEntry> TwoAttackOneSpeed = new List<EvYieldEntry>
        {
            new EvYieldEntry(StatId.Attack, 2),
            new EvYieldEntry(StatId.Speed, 1)
        };

        private static int EvsFor(BattleResult r, string participant, StatId stat)
        {
            int total = 0;
            foreach (var a in r.EvAwards)
                if (a.ParticipantId.Value == participant && a.Stat == stat) total += a.Amount;
            return total;
        }

        [Test]
        public void Defeating_an_enemy_awards_its_full_ev_yield()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Fighter("p1", 99), Fighter("e1", 1, TwoAttackOneSpeed));
            Resolver().ResolveTurn(battle, new UseMove(new Id<Move>("blast")), new UseMove(new Id<Move>("blast")));

            var result = battle.ToResult();
            Assert.AreEqual(2, EvsFor(result, "p1", StatId.Attack));
            Assert.AreEqual(1, EvsFor(result, "p1", StatId.Speed));
            Assert.AreEqual(0, EvsFor(result, "p1", StatId.Defense));
        }

        [Test]
        public void Capturing_an_enemy_also_awards_evs()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Fighter("p1", 99), Fighter("e1", 1, TwoAttackOneSpeed));
            Resolver(new AlwaysCatch()).ResolveTurn(battle, new Capturar(), new UseMove(new Id<Move>("blast")));

            Assert.AreEqual(BattleOutcome.Caught, battle.Outcome);
            Assert.AreEqual(2, EvsFor(battle.ToResult(), "p1", StatId.Attack));
        }

        [Test]
        public void Evs_can_be_disabled_by_the_author()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Fighter("p1", 99), Fighter("e1", 1, TwoAttackOneSpeed));
            var resolver = new TurnResolver(
                new InMemoryCatalog<Move>(new[] { Blast() }, m => m.Id.Value),
                new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(3),
                xpFormula: new ClassicXpFormula(), awardEffortValues: false);

            resolver.ResolveTurn(battle, new UseMove(new Id<Move>("blast")), new UseMove(new Id<Move>("blast")));

            var result = battle.ToResult();
            Assert.AreEqual(0, result.EvAwards.Count);
            Assert.AreEqual(1, result.XpAwards.Count); // la XP sigue funcionando
        }
    }
}
