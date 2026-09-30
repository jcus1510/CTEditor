using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Party.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// REGLAS DE GENERACIÓN y MECÁNICAS en el Ruleset: categoría por tipo, Especial único, sin habilidades, sin objetos
    /// equipados, sin naturalezas ni géneros; y la lista de mecánicas activas.
    /// </summary>
    public class GenerationRulesTests
    {
        private static Id<ElementType> T(string t) => new Id<ElementType>(t);

        private static readonly Move[] Moves =
        {
            new Move(new Id<Move>("tackle"), "tackle", T("normal"), MoveCategory.Physical, 60, null, 20, 0, MoveTarget.SingleEnemy),
            // Un ataque de FUEGO marcado como físico: con categoría por tipo pasa a ser especial.
            new Move(new Id<Move>("fire_punch"), "fire_punch", T("fire"), MoveCategory.Physical, 75, null, 20, 0, MoveTarget.SingleEnemy),
            new Move(new Id<Move>("calm"), "calm", T("psychic"), MoveCategory.Status, 0, null, 20, 0, MoveTarget.Self, new[]
            {
                new MoveEffect(new Percentage(100), MoveEffectKind.ChangeStatStage, EffectTarget.Self, default, new Percentage(0),
                    StatId.SpAttack, 2, null, false, null, 0, null, 0, null),
            }),
            new Move(new Id<Move>("wait"), "wait", T("normal"), MoveCategory.Status, 0, null, 20, 0, MoveTarget.Self),
        };

        private static readonly AbilityDefinition[] Abilities =
        {
            new AbilityDefinition(new AbilityId("pixilate"), "Piel Feérica", extras: new AbilityExtras { ConvertNormalTo = "fairy", ConvertBoost = 1.3f }),
        };

        private static readonly ItemDefinition[] Items =
        {
            new ItemDefinition("choice_band", "choice_band", ItemCategory.Held,
                effects: new[] { TestItems.Stat("attack", 1.5f) }),
        };

        private static TurnResolver Resolver(GenerationRules g) => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Set(T("fairy"), T("dragon"), 2f).Build(),
            new ClassicDamageFormula(), new SeededRng(1),
            new InMemoryCatalog<StatusConditionDefinition>(new StatusConditionDefinition[0], s => s.Id.Value),
            abilities: new InMemoryCatalog<AbilityDefinition>(Abilities, a => a.Id.Value),
            items: new InMemoryCatalog<ItemDefinition>(Items, i => i.Id),
            rules: new BattleRules(generation: g));

        private static BattleParticipant Mon(string id, string type = "normal", int atk = 100, int spa = 100, string item = null, string ability = null,
            params string[] moves)
            => new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, 500).Set(StatId.Attack, atk).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, spa).Set(StatId.SpDefense, 100).Set(StatId.Speed, 50).Build(),
                500, new List<Id<ElementType>> { T(type) }, (moves.Length == 0 ? new[] { "tackle" } : moves).Select(m => new Id<Move>(m)).ToList(),
                null, ability == null ? (AbilityId?)null : new AbilityId(ability), heldItem: item);

        private static CTEditor.Battle.Domain.Battle Duel(BattleParticipant a, BattleParticipant b) => new CTEditor.Battle.Domain.Battle(a, b);

        private static int Damage(GenerationRules g, BattleParticipant a, BattleParticipant b, string move)
        {
            var battle = Duel(a, b);
            return Resolver(g).PreviewDamage(battle.Player, battle.Enemy, new Id<Move>(move), battle).Max;
        }

        [Test]
        public void Generation_presets_are_faithful()
        {
            var g1 = GenerationRules.ForGeneration(1);
            Assert.IsTrue(g1.CategoryByType && g1.SingleSpecialStat);
            Assert.IsFalse(g1.Abilities || g1.HeldItems || g1.Natures || g1.Genders);
            var g2 = GenerationRules.ForGeneration(2);
            Assert.IsTrue(g2.CategoryByType && g2.HeldItems && g2.Genders);
            Assert.IsFalse(g2.Abilities || g2.Natures || g2.SingleSpecialStat);
            var g3 = GenerationRules.ForGeneration(3);
            Assert.IsTrue(g3.CategoryByType && g3.Abilities && g3.Natures);
            var g4 = GenerationRules.ForGeneration(4);
            Assert.IsFalse(g4.CategoryByType);
            Assert.IsTrue(g4.Abilities && g4.HeldItems && g4.Natures && g4.Genders);
            Assert.IsFalse(Ruleset.Classic.Generation.CategoryByType, "por defecto, moderno");
        }

        [Test]
        public void Category_by_type_turns_a_physical_fire_move_into_special()
        {
            var modern = new BattleRules();
            var old = new BattleRules(generation: GenerationRules.ForGeneration(3));
            Assert.AreEqual(MoveCategory.Physical, modern.CategoryOf(Moves[1]));
            Assert.AreEqual(MoveCategory.Special, old.CategoryOf(Moves[1]));
            Assert.AreEqual(MoveCategory.Status, old.CategoryOf(Moves[2]), "los de estado siguen siendo de estado");

            // En combate usa el Ataque Especial: con mucho más Atq. Esp. pega mucho más.
            int m = Damage(GenerationRules.Modern, Mon("a", atk: 50, spa: 200), Mon("b"), "fire_punch");
            int o = Damage(GenerationRules.ForGeneration(3), Mon("a", atk: 50, spa: 200), Mon("b"), "fire_punch");
            Assert.Greater(o, m * 2);
        }

        [Test]
        public void Without_held_items_the_item_does_nothing()
        {
            var noItems = GenerationRules.Modern.With(heldItems: false);
            int plain = Damage(noItems, Mon("a"), Mon("b"), "tackle");
            int band = Damage(noItems, Mon("a", item: "choice_band"), Mon("b"), "tackle");
            Assert.AreEqual(plain, band);
            Assert.Greater(Damage(GenerationRules.Modern, Mon("a", item: "choice_band"), Mon("b"), "tackle"), plain);
        }

        [Test]
        public void Without_abilities_the_ability_does_nothing()
        {
            var noAb = GenerationRules.Modern.With(abilities: false);
            int plain = Damage(noAb, Mon("a"), Mon("b", type: "dragon"), "tackle");
            int pix = Damage(noAb, Mon("a", ability: "pixilate"), Mon("b", type: "dragon"), "tackle");
            Assert.AreEqual(plain, pix);
            Assert.IsTrue(Damage(GenerationRules.Modern, Mon("a", ability: "pixilate"), Mon("b", type: "dragon"), "tackle") > plain * 1.5f);
        }

        [Test]
        public void Single_special_moves_both_special_stages()
        {
            var battle = Duel(Mon("a", moves: new[] { "calm" }), Mon("b", moves: new[] { "wait" }));
            Resolver(GenerationRules.ForGeneration(1)).ResolveTurn(battle, new UseMove(new Id<Move>("calm")), new UseMove(new Id<Move>("wait"))).ToList();
            Assert.AreEqual(2, battle.Player.GetStage(StatId.SpAttack));
            Assert.AreEqual(2, battle.Player.GetStage(StatId.SpDefense), "Especial único: sube también la Def. Esp.");

            var modern = Duel(Mon("a", moves: new[] { "calm" }), Mon("b", moves: new[] { "wait" }));
            Resolver(GenerationRules.Modern).ResolveTurn(modern, new UseMove(new Id<Move>("calm")), new UseMove(new Id<Move>("wait"))).ToList();
            Assert.AreEqual(0, modern.Player.GetStage(StatId.SpDefense));
        }

        [Test]
        public void Without_natures_or_genders_monsters_are_neutral_and_genderless()
        {
            var species = new Species(new Id<Species>("x"), "X", new List<Id<ElementType>> { T("normal") },
                new StatBlock.Builder().Set(StatId.Hp, 50).Set(StatId.Attack, 50).Set(StatId.Defense, 50)
                    .Set(StatId.SpAttack, 50).Set(StatId.SpDefense, 50).Set(StatId.Speed, 50).Build(),
                new List<LearnableMove>(), new List<Evolution>());
            var adamant = new Nature(new Id<Nature>("adamant"), "Firme", StatId.Attack, StatId.SpAttack);
            var gen1 = Ruleset.Classic.With(generation: GenerationRules.ForGeneration(1));
            var mon = MonsterFactory.Create(new Id<MonsterInstance>("m1"), species, 50, gen1, new ClassicStatGrowthFormula(), nature: adamant);
            Assert.IsNull(mon.Nature);
            Assert.AreEqual(Gender.Genderless, mon.Gender);

            var modern = MonsterFactory.Create(new Id<MonsterInstance>("m1"), species, 50, Ruleset.Classic, new ClassicStatGrowthFormula(), nature: adamant);
            Assert.IsNotNull(modern.Nature);
            Assert.AreNotEqual(Gender.Genderless, modern.Gender);
        }

        [Test]
        public void Ruleset_keeps_active_mechanics_without_duplicates()
        {
            var mega = MechanicDefinition.OfficialMega;
            var rules = Ruleset.Classic.With(mechanics: new[] { mega, MechanicDefinition.OfficialMega });
            Assert.AreEqual(1, rules.Mechanics.Count);
            Assert.IsTrue(rules.Has(MechanicKind.MegaEvolution));
            Assert.AreEqual(1, rules.Find(MechanicKind.MegaEvolution).Mega.MaxPerBattle);
            Assert.AreEqual("mega_ring", rules.Find(MechanicKind.MegaEvolution).Mega.RequiredKeyItem);
            Assert.IsFalse(Ruleset.Classic.Has(MechanicKind.MegaEvolution), "por defecto, ninguna");
            Assert.IsTrue(new MegaEvolutionSettings(0).HasUsesLeft(99), "0 = sin límite");
            Assert.IsNotNull(BattleRules.From(rules).Mechanic(MechanicKind.MegaEvolution));
        }
    }
}
