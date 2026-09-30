using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Moves;
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
    /// ABILITIES as EFFECT BLOCKS: the classic shapes become what the engine asks for (Intimidate, Blaze...), and any other
    /// combination runs like a held item's block (with its chance and uses per battle), so authors can invent abilities.
    /// </summary>
    public class AbilityBlockTests
    {
        private static Move M(string id, string type, int power)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), MoveCategory.Physical, power, null, 30, 0, MoveTarget.SingleEnemy, new MoveEffect[0]);

        private static readonly Move[] Moves = { M("tackle", "normal", 40), M("big_hit", "normal", 250) };

        private static AbilityDefinition Ab(string id, params EffectBlock[] blocks) => new AbilityDefinition(new AbilityId(id), id, blocks);

        private static readonly AbilityDefinition[] Abilities =
        {
            // Not a classic shape: «tras recibir un golpe, recupera el 10 %» (runs like a held item), once per battle.
            Ab("second_wind", new EffectBlock(EffectTrigger.AfterHit, EffectAction.HealPercent, 10f, maxPerBattle: 1)),
            // «Al debilitar a un rival, recupera el 50 %».
            Ab("feast", new EffectBlock(EffectTrigger.OnKo, EffectAction.HealPercent, 50f)),
            // Classic shape (a view): «siempre: stat attack x2» (Huge Power).
            Ab("huge_power", new EffectBlock(EffectTrigger.Passive, EffectAction.MultiplyStat, 2f, "attack")),
        };

        private static BattleParticipant Mon(string id, int hp, int maxHp = 300, string ability = null, int speed = 50)
            => new BattleParticipant(new Id<BattleParticipant>(id), new Id<CTEditor.GameDefinition.Domain.Species.Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, maxHp).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>("normal") }, new List<Id<Move>> { new Id<Move>("tackle"), new Id<Move>("big_hit") },
                null, ability == null ? (AbilityId?)null : new AbilityId(ability));

        private static TurnResolver Resolver() => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value), new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(7),
            new InMemoryCatalog<StatusConditionDefinition>(new StatusConditionDefinition[0], s => s.Id.Value),
            abilities: new InMemoryCatalog<AbilityDefinition>(Abilities, a => a.Id.Value));

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));

        [Test]
        public void A_free_ability_block_runs_like_a_held_item_and_respects_its_uses_per_battle()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("attacker", 300, speed: 99), Mon("holder", 300, ability: "second_wind"));
            var ev = r.ResolveTurn(b, Use("tackle"), Use("tackle")).ToList();
            Assert.IsTrue(ev.OfType<AbilityTriggeredEvent>().Any(e => e.Combatant.Value == "holder"), "se anuncia la habilidad");
            Assert.IsTrue(ev.OfType<HpRestoredEvent>().Any(e => e.Combatant.Value == "holder"), "recupera PS tras el golpe");
            var ev2 = r.ResolveTurn(b, Use("tackle"), Use("tackle")).ToList();
            Assert.IsFalse(ev2.OfType<HpRestoredEvent>().Any(e => e.Combatant.Value == "holder"), "solo una vez por combate");
        }

        [Test]
        public void A_new_moment_on_knocking_out_a_foe_runs()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("eater", 100, ability: "feast", speed: 99), Mon("victim", 1));
            var ev = r.ResolveTurn(b, Use("tackle"), Use("tackle")).ToList();
            Assert.IsTrue(b.Enemy.IsFainted);
            Assert.IsTrue(ev.OfType<HpRestoredEvent>().Any(e => e.Combatant.Value == "eater"), "al debilitar, recupera PS");
        }

        [Test]
        public void A_classic_shape_is_what_the_engine_already_uses()
        {
            var huge = Abilities.First(a => a.Id.Value == "huge_power");
            Assert.AreEqual(0, huge.GenericEffects.Count, "no se ejecuta dos veces");
            Assert.AreEqual(1, huge.PassiveModifiers.Count);
            Assert.AreEqual(2f, huge.PassiveModifiers[0].Multiplier, 0.001f);
        }
    }
}
