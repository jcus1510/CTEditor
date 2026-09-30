using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Events;
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
    /// PP: se gastan al usar, persisten en el resultado, se respetan los que traía el snapshot, y al
    /// agotarse todos se usa Forcejeo (retroceso de 1/4 de los PS máximos). Además: el retroceso y el
    /// drenaje ocurren aunque el golpe debilite al rival (fidelidad con los juegos).
    /// </summary>
    public class PpTests
    {
        private static Move Tackle() => new Move(new Id<Move>("tackle"), "Placaje", new Id<ElementType>("normal"),
            MoveCategory.Physical, 40, null, 35, 0, MoveTarget.SingleEnemy);

        private static Move Struggle() => new Move(new Id<Move>("struggle"), "Forcejeo", new Id<ElementType>("typeless"),
            MoveCategory.Physical, 50, null, 1, 0, MoveTarget.SingleEnemy,
            new[] { new MoveEffect(new Percentage(100), MoveEffectKind.RecoilMaxHp, amount: new Percentage(25)) });

        private static Move DoubleEdge() => new Move(new Id<Move>("double_edge"), "Doble Filo", new Id<ElementType>("normal"),
            MoveCategory.Physical, 250, null, 15, 0, MoveTarget.SingleEnemy,
            new[] { new MoveEffect(new Percentage(100), MoveEffectKind.Recoil, amount: new Percentage(33)) });

        private static BattleParticipant Mon(string id, int speed, int hp, string move, IReadOnlyList<int> pp = null) =>
            new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, hp).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 50).Set(StatId.SpDefense, 50).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>("normal") },
                new List<Id<Move>> { new Id<Move>(move) }, initialPp: pp);

        private static TurnResolver Resolver(bool usePp = true) => new TurnResolver(
            new InMemoryCatalog<Move>(new[] { Tackle(), Struggle(), DoubleEdge() }, m => m.Id.Value),
            new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(5),
            usePp: usePp, struggleMove: new Id<Move>("struggle"));

        private static readonly UseMove UseTackle = new UseMove(new Id<Move>("tackle"));

        [Test]
        public void Using_a_move_spends_one_pp_and_the_result_keeps_it()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, 500, "tackle"), Mon("b", 1, 500, "tackle"));
            var r = Resolver();
            Assert.AreEqual((35, 35), r.PpOf(battle.Player, 0));

            r.ResolveTurn(battle, UseTackle, UseTackle);

            Assert.AreEqual((34, 35), r.PpOf(battle.Player, 0));
            var result = battle.ToResult().Participants.First(p => p.ParticipantId.Value == "a");
            Assert.AreEqual(34, result.FinalPp[0]);
        }

        [Test]
        public void Snapshot_pp_is_respected()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, 500, "tackle", new[] { 3 }), Mon("b", 1, 500, "tackle"));
            Assert.AreEqual((3, 35), Resolver().PpOf(battle.Player, 0));
        }

        [Test]
        public void Without_pp_left_the_monster_struggles_and_takes_a_quarter_of_its_max_hp()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, 400, "tackle", new[] { 0 }), Mon("b", 1, 5000, "tackle"));
            var events = Resolver().ResolveTurn(battle, UseTackle, UseTackle);

            Assert.IsTrue(events.Any(e => e is StruggleEvent s && s.Combatant.Value == "a"));
            var used = events.OfType<MoveUsedEvent>().First(e => e.Attacker.Value == "a");
            Assert.AreEqual("struggle", used.Move.Value);
            var recoil = events.OfType<RecoilDamageEvent>().First(e => e.Combatant.Value == "a");
            Assert.AreEqual(100, recoil.Amount); // 400 / 4
        }

        [Test]
        public void With_the_rule_off_pp_are_not_spent()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, 500, "tackle"), Mon("b", 1, 500, "tackle"));
            var r = Resolver(usePp: false);
            r.ResolveTurn(battle, UseTackle, UseTackle);
            Assert.AreEqual(35, r.PpOf(battle.Player, 0).current);
        }

        [Test]
        public void Recoil_still_happens_when_the_hit_knocks_out_the_target()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, 500, "double_edge"), Mon("b", 1, 10, "tackle"));
            var events = Resolver().ResolveTurn(battle, new UseMove(new Id<Move>("double_edge")), UseTackle);

            Assert.IsTrue(events.Any(e => e is MonsterFaintedEvent f && f.Combatant.Value == "b"));
            Assert.IsTrue(events.Any(e => e is RecoilDamageEvent r && r.Combatant.Value == "a"),
                "Doble Filo debe dañar al usuario aunque debilite al rival.");
        }
    }
}
