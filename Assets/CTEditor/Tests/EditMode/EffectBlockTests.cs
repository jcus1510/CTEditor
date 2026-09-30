using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using B = CTEditor.Battle.Domain.Battle;

namespace CTEditor.Tests.EditMode
{
    /// <summary>Items built from EFFECT BLOCKS: conditions, chance, uses per battle, new triggers and the legacy conversion.</summary>
    public class EffectBlockTests
    {
        private static readonly Move[] Moves =
        {
            new Move(new Id<Move>("wait"), "wait", new Id<ElementType>("normal"), MoveCategory.Status, 0, null, 30, 0, MoveTarget.Self, new MoveEffect[0]),
            new Move(new Id<Move>("ember"), "ember", new Id<ElementType>("fire"), MoveCategory.Special, 40, null, 30, 0, MoveTarget.SingleEnemy, new MoveEffect[0]),
        };

        private static ItemDefinition Item(string id, params EffectBlock[] blocks)
            => new ItemDefinition(id, id, ItemCategory.Held, consumable: false, effects: blocks);

        private static BattleParticipant P(string id, string move = "wait", int hp = 200, string held = null, string type = "normal", int speed = 50) =>
            new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, 200).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>(type) }, new List<Id<Move>> { new Id<Move>(move) },
                heldItem: held);

        private static TurnResolver Resolver(params ItemDefinition[] items) => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value), new TypeChart.Builder().Build(),
            new ClassicDamageFormula(), new SeededRng(3), new InMemoryCatalog<StatusConditionDefinition>(new StatusConditionDefinition[0], s => s.Id.Value),
            rules: new BattleRules(critDenominators: new[] { 0 }),
            items: new InMemoryCatalog<ItemDefinition>(items, i => i.Id));

        private static UseMove Wait => new UseMove(new Id<Move>("wait"));

        [Test]
        public void An_end_of_turn_block_only_fires_when_its_conditions_hold()
        {
            var waterCharm = Item("water_charm", new EffectBlock(EffectTrigger.EndOfTurn, EffectAction.HealHp, 20,
                conditions: new[] { new Condition(ConditionKind.IsType, text: "water") }));

            var normal = new B(P("a", hp: 100, held: "water_charm"), P("b"));
            Resolver(waterCharm).ResolveTurn(normal, Wait, Wait);
            Assert.AreEqual(100, normal.Player.CurrentHp, "no es de tipo Agua: no cura");

            var water = new B(P("a", hp: 100, held: "water_charm", type: "water"), P("b"));
            Resolver(waterCharm).ResolveTurn(water, Wait, Wait);
            Assert.AreEqual(120, water.Player.CurrentHp);
        }

        [Test]
        public void Uses_per_battle_and_chance_limit_a_block()
        {
            var once = Item("once", new EffectBlock(EffectTrigger.EndOfTurn, EffectAction.HealHp, 10, maxPerBattle: 1));
            var b = new B(P("a", hp: 100, held: "once"), P("b"));
            var r = Resolver(once);
            r.ResolveTurn(b, Wait, Wait);
            r.ResolveTurn(b, Wait, Wait);
            Assert.AreEqual(110, b.Player.CurrentHp, "solo una vez por combate");

            var never = Item("never", new EffectBlock(EffectTrigger.EndOfTurn, EffectAction.HealHp, 10, chance: 0));
            var n = new B(P("a", hp: 100, held: "never"), P("b"));
            Resolver(never).ResolveTurn(n, Wait, Wait);
            Assert.AreEqual(100, n.Player.CurrentHp, "0 % nunca se activa");
        }

        [Test]
        public void On_entry_blocks_fire_at_the_start_of_the_battle()
        {
            var band = Item("war_band", new EffectBlock(EffectTrigger.OnEntry, EffectAction.ChangeStage, 1, "attack"));
            var b = new B(P("a", held: "war_band"), P("b"));
            Resolver(band).ResolveBattleStart(b);
            Assert.AreEqual(1, b.Player.GetStage(StatId.Attack));
        }

        [Test]
        public void A_damage_taken_block_without_consuming_works_on_every_hit()
        {
            var shield = Item("fire_shield", new EffectBlock(EffectTrigger.BeforeHit, EffectAction.DamageTakenMultiplier, 0.5f,
                conditions: new[] { new Condition(ConditionKind.MoveType, text: "fire") }));
            int Taken(string held)
            {
                var b = new B(P("a", "ember", speed: 99), P("b", held: held));
                var r = Resolver(shield);
                r.ResolveTurn(b, new UseMove(new Id<Move>("ember")), Wait);
                int first = 200 - b.Enemy.CurrentHp;
                r.ResolveTurn(b, new UseMove(new Id<Move>("ember")), Wait);
                Assert.AreEqual(held, b.Enemy.HeldItem, "no se gasta");
                return first;
            }
            int plain = Taken(null), shielded = Taken("fire_shield");
            Assert.That(shielded, Is.InRange(plain / 2 - 1, plain / 2 + 1));
        }

        [Test]
        public void Old_item_fields_become_blocks_and_the_old_properties_still_read_them()
        {
            var potion = new ItemDefinition("potion", "Poción", ItemCategory.Medicine, healHp: 20, usableInBattle: true);
            Assert.AreEqual(EffectTrigger.OnUse, potion.Effects.Single().Trigger);
            Assert.AreEqual(20, potion.HealHp);

            var occa = new ItemDefinition("occa", "Baya Caoca", ItemCategory.Held, extras: new ItemExtras { ResistBerryType = "fire" });
            var block = occa.Effects.Single();
            Assert.AreEqual(EffectAction.DamageTakenMultiplier, block.Action);
            Assert.IsTrue(block.Consumes);
            Assert.AreEqual(2, block.Conditions.Count, "tipo Fuego + muy eficaz");
            Assert.IsTrue(occa.IsBerry, "se reconoce como baya (Nerviosismo, Cosecha...)");

            var oran = new ItemDefinition("oran", "Baya Aranja", ItemCategory.Held, heldTriggerHpPercent: 50, heldTriggerHealHp: 10);
            Assert.AreEqual(50f, oran.HeldTriggerHpPercent);
            Assert.AreEqual(10, oran.HeldTriggerHealHp);
        }
    }
}
