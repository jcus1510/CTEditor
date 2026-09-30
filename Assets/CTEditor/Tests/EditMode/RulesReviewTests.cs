using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>«Adaptar a las reglas» del asistente de cambio de generación: qué no encaja de un equipo escrito a mano.</summary>
    public class RulesReviewTests
    {
        private static StatBlock Stats() => new StatBlock.Builder().Set(StatId.Hp, 80).Set(StatId.Attack, 80).Set(StatId.Defense, 80)
            .Set(StatId.SpAttack, 80).Set(StatId.SpDefense, 80).Set(StatId.Speed, 80).Build();

        private static readonly Species Zard = new Species(new Id<Species>("zard"), "Zard", new[] { new Id<ElementType>("fire") }, Stats(),
            new LearnableMove[0], new Evolution[0], ability: new AbilityId("blaze"),
            forms: new[] { new SpeciesForm("mega", "Mega") }, formChanges: new[] { new FormChange("", "mega", FormTrigger.MegaEvolution, item: "zardite") });

        private static GameData Data(Ruleset rules) => new GameData(
            new MemoryCatalog<Species>(new[] { Zard }, s => s.Id.Value),
            new MemoryCatalog<Move>(new[] { new Move(new Id<Move>("ember"), "ember", new Id<ElementType>("fire"), MoveCategory.Special, 40, null, 25, 0, MoveTarget.SingleEnemy) }, m => m.Id.Value),
            new TypeChart.Builder().Build(), rules,
            abilities: new MemoryCatalog<AbilityDefinition>(new[] { new AbilityDefinition(new AbilityId("blaze"), "Mar Llamas") }, a => a.Id.Value),
            items: new MemoryCatalog<ItemDefinition>(new[] { new ItemDefinition("zardite", "Zardita", ItemCategory.Held) }, i => i.Id),
            natures: new MemoryCatalog<Nature>(new[] { new Nature(new Id<Nature>("adamant"), "Firme", StatId.Attack, StatId.SpAttack) }, n => n.Id.Value));

        private static MemberToReview Member() => new MemberToReview
        {
            Owner = "rival", SpeciesId = "zard", ItemId = "zardite", NatureId = "adamant", AbilityId = "blaze", Evs = "252 Atq", GenderFixed = true,
            Moves = { "ember", "flamethrower" },
        };

        [Test]
        public void Gen6_rules_with_megas_only_report_missing_content()
        {
            var rules = Ruleset.Classic.With(generation: GenerationRules.ForGeneration(6), mechanics: new[] { MechanicDefinition.OfficialMega });
            var c = RulesReview.Review(Member(), Data(rules));
            Assert.AreEqual(1, c.Count, string.Join("\n", c.Select(x => x.Description)));
            Assert.AreEqual(ConflictKind.MissingMove, c[0].Kind);
            Assert.AreEqual("flamethrower", c[0].Value);
        }

        [Test]
        public void Gen1_rules_report_everything_the_generation_does_not_have()
        {
            var rules = Ruleset.Classic.With(generation: GenerationRules.ForGeneration(1), maxEvPerStat: 0, maxEvTotal: 0);
            var kinds = RulesReview.Review(Member(), Data(rules)).Select(x => x.Kind).ToList();
            CollectionAssert.IsSubsetOf(new[] { ConflictKind.MissingMove, ConflictKind.HeldItemsOff, ConflictKind.NaturesOff,
                ConflictKind.GendersOff, ConflictKind.AbilitiesOff, ConflictKind.EvsOff }, kinds);
        }

        [Test]
        public void Mega_stones_without_the_mechanic_and_missing_species_are_reported()
        {
            var c = RulesReview.Review(Member(), Data(Ruleset.Classic));
            Assert.IsTrue(c.Any(x => x.Kind == ConflictKind.MegaStoneWithoutMega && x.Value == "zardite"));

            var gone = Member(); gone.SpeciesId = "missingno";
            var g = RulesReview.Review(gone, Data(Ruleset.Classic));
            Assert.AreEqual(1, g.Count);
            Assert.AreEqual(ConflictKind.MissingSpecies, g[0].Kind);
        }
    }
}
