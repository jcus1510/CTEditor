using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// MOVIMIENTOS Z (7.ª gen.): con la mecánica activa y su cristal Z, un movimiento se convierte en su movimiento Z una vez por
    /// combate y lado; la potencia sale de la tabla de la ficha (o es la del Z exclusivo), nunca falla, atraviesa Protección
    /// con parte del daño, y los de estado hacen primero su efecto Z.
    /// </summary>
    public class ZMoveTests
    {
        private static Id<ElementType> T(string t) => new Id<ElementType>(t);
        private static Percentage P(float p) => new Percentage(p);

        private static readonly Move[] Moves =
        {
            new Move(new Id<Move>("thunderbolt"), "Rayo", T("electric"), MoveCategory.Special, 90, P(100), 15, 0, MoveTarget.SingleEnemy),
            new Move(new Id<Move>("volt_tackle"), "Placaje Eléc.", T("electric"), MoveCategory.Physical, 120, P(100), 15, 0, MoveTarget.SingleEnemy),
            new Move(new Id<Move>("growl"), "Gruñido", T("normal"), MoveCategory.Status, 0, P(100), 40, 0, MoveTarget.SingleEnemy,
                new[] { new MoveEffect(P(100), MoveEffectKind.ChangeStatStage, EffectTarget.Opponent, stat: StatId.Attack, stages: -1) },
                zEffects: new[] { new MoveEffect(P(100), MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: StatId.Defense, stages: 1) }),
            new Move(new Id<Move>("protect"), "Protección", T("normal"), MoveCategory.Status, 0, null, 10, 4, MoveTarget.Self,
                new[] { new MoveEffect(P(100), MoveEffectKind.InflictStatus, EffectTarget.Self, status: new StatusId("protect")) }),
            new Move(new Id<Move>("wait"), "Esperar", T("normal"), MoveCategory.Status, 0, null, 40, 0, MoveTarget.Self),
            new Move(new Id<Move>("gigavolt_havoc"), "Gigavoltio Destructor", T("electric"), MoveCategory.Physical, 1, null, 1, 0, MoveTarget.SingleEnemy, tags: new[] { "z" }),
            new Move(new Id<Move>("catastropika"), "Pikavoltio Letal", T("electric"), MoveCategory.Physical, 210, null, 1, 0, MoveTarget.SingleEnemy, tags: new[] { "z" }),
            new Move(new Id<Move>("breakneck_blitz"), "Arrollamiento Z", T("normal"), MoveCategory.Physical, 1, null, 1, 0, MoveTarget.SingleEnemy, tags: new[] { "z" }),
        };

        private static EffectBlock Crystal(string zMove, params Condition[] conds) => new EffectBlock(EffectTrigger.Passive, EffectAction.ZMove, 0, zMove, conds);

        private static readonly ItemDefinition[] Items =
        {
            new ItemDefinition("electrium_z", "Electrostal Z", ItemCategory.Held, consumable: false,
                effects: new[] { Crystal("gigavolt_havoc", new Condition(ConditionKind.MoveType, text: "electric")) }),
            new ItemDefinition("normalium_z", "Normastal Z", ItemCategory.Held, consumable: false,
                effects: new[] { Crystal("breakneck_blitz", new Condition(ConditionKind.MoveType, text: "normal")) }),
            new ItemDefinition("pikanium_z", "Pikastal Z", ItemCategory.Held, consumable: false,
                effects: new[] { Crystal("catastropika", new Condition(ConditionKind.IsSpecies, text: "pikachu"),
                    new Condition(ConditionKind.MoveIs, text: "volt_tackle")) }),
        };

        private static readonly StatusConditionDefinition[] Statuses =
        {
            new StatusConditionDefinition(new StatusId("protect"), "Protegido", P(0), P(0), durationTurns: 1, isVolatile: true, blocksIncomingMoves: true),
        };

        private static TurnResolver Resolver(bool withZ = true) => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value), new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(5),
            new InMemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value),
            items: new InMemoryCatalog<ItemDefinition>(Items, i => i.Id),
            rules: new BattleRules(mechanics: withZ ? new[] { MechanicDefinition.OfficialZ } : null));

        private static BattleParticipant Mon(string id, string species, string item, int speed, params string[] moves)
            => new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(species), 50,
                new StatBlock.Builder().Set(StatId.Hp, 400).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                400, new List<Id<ElementType>> { T("normal") }, moves.Select(m => new Id<Move>(m)).ToList(), heldItem: item);

        private static UseMove Use(string m, bool z = false) => new UseMove(new Id<Move>(m), false, z);

        [Test]
        public void A_type_crystal_turns_the_move_into_its_Z_move_once_per_battle()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "raichu", "electrium_z", 99, "thunderbolt", "growl"), Mon("b", "x", null, 1, "wait"));
            Assert.IsTrue(r.CanZMove(b, true, new Id<Move>("thunderbolt")));
            Assert.IsFalse(r.CanZMove(b, false), "el rival no lleva cristal");
            var z = r.ZMoveFor(b.Player, new Id<Move>("thunderbolt"));
            Assert.AreEqual("gigavolt_havoc", z.Id.Value);
            Assert.AreEqual(175, z.Power, "90 de potencia → 175 (tabla oficial)");
            Assert.AreEqual(MoveCategory.Special, z.Category, "la categoría es la del movimiento base");

            var ev = r.ResolveTurn(b, Use("thunderbolt", true), Use("wait")).ToList();
            Assert.AreEqual(1, ev.OfType<ZMoveUsedEvent>().Count());
            Assert.IsTrue(ev.OfType<MoveUsedEvent>().Any(e => e.Move.Value == "gigavolt_havoc"));
            Assert.AreEqual(14, r.PpOf(b.Player, 0).current, "gasta los PP del movimiento base");
            Assert.IsFalse(r.CanZMove(b, true), "solo uno por combate");
            var again = r.ResolveTurn(b, Use("thunderbolt", true), Use("wait")).ToList();
            Assert.IsFalse(again.OfType<ZMoveUsedEvent>().Any());
            Assert.IsTrue(again.OfType<MoveUsedEvent>().Any(e => e.Move.Value == "thunderbolt"), "el segundo es el normal");
        }

        [Test]
        public void Without_the_mechanic_the_crystal_does_nothing()
        {
            var r = Resolver(withZ: false);
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "raichu", "electrium_z", 99, "thunderbolt"), Mon("b", "x", null, 1, "wait"));
            Assert.IsFalse(r.CanZMove(b, true));
            var ev = r.ResolveTurn(b, Use("thunderbolt", true), Use("wait")).ToList();
            Assert.IsFalse(ev.OfType<ZMoveUsedEvent>().Any());
        }

        [Test]
        public void An_exclusive_crystal_needs_its_species_and_move()
        {
            var r = Resolver();
            var pika = new CTEditor.Battle.Domain.Battle(Mon("a", "pikachu", "pikanium_z", 99, "volt_tackle", "thunderbolt"), Mon("b", "x", null, 1, "wait"));
            Assert.AreEqual("catastropika", r.ZMoveFor(pika.Player, new Id<Move>("volt_tackle")).Id.Value);
            Assert.AreEqual(210, r.ZMoveFor(pika.Player, new Id<Move>("volt_tackle")).Power, "el exclusivo usa su propia potencia");
            Assert.IsNull(r.ZMoveFor(pika.Player, new Id<Move>("thunderbolt")), "otro movimiento no");
            var raichu = new CTEditor.Battle.Domain.Battle(Mon("a", "raichu", "pikanium_z", 99, "volt_tackle"), Mon("b", "x", null, 1, "wait"));
            Assert.IsNull(r.ZMoveFor(raichu.Player, new Id<Move>("volt_tackle")), "otra especie no");
        }

        [Test]
        public void A_status_Z_move_does_its_Z_effect_first()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "x", "normalium_z", 99, "growl"), Mon("b", "x", null, 1, "wait"));
            r.ResolveTurn(b, Use("growl", true), Use("wait")).ToList();
            Assert.AreEqual(1, b.Player.GetStage(StatId.Defense), "Gruñido Z: +1 Defensa");
            Assert.AreEqual(-1, b.Enemy.GetStage(StatId.Attack), "y después hace lo suyo");
        }

        [Test]
        public void A_Z_move_goes_through_protect_with_a_quarter_of_the_damage()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "raichu", "electrium_z", 1, "thunderbolt"), Mon("b", "x", null, 99, "protect"));
            var ev = r.ResolveTurn(b, Use("thunderbolt", true), Use("protect")).ToList();
            int dealt = 400 - b.Enemy.CurrentHp;
            Assert.IsTrue(ev.OfType<MoveBlockedEvent>().Any(), "la protección se nota");
            Assert.Greater(dealt, 0, "pero pasa parte del daño");

            var free = new CTEditor.Battle.Domain.Battle(Mon("a", "raichu", "electrium_z", 1, "thunderbolt"), Mon("b", "x", null, 99, "wait"));
            Resolver().ResolveTurn(free, Use("thunderbolt", true), Use("wait")).ToList();
            int full = 400 - free.Enemy.CurrentHp;
            Assert.Less(dealt, full / 2, "mucho menos que sin protección");
        }
    }
}
