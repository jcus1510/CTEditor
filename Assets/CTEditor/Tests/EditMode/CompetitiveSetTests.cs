using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>SETS DE COMPETICIÓN (Smogon): elegir con peso por uso, aplicarlos respetando lo escrito, IA por nivel, fijo o cambiante.</summary>
    public class CompetitiveSetTests
    {
        private static Id<ElementType> T(string t) => new Id<ElementType>(t);
        private static StatBlock Stats(int v) => new StatBlock.Builder().Set(StatId.Hp, v).Set(StatId.Attack, v).Set(StatId.Defense, v)
            .Set(StatId.SpAttack, v).Set(StatId.SpDefense, v).Set(StatId.Speed, v).Build();

        private static Move M(string id) => new Move(new Id<Move>(id), id, T("normal"), MoveCategory.Physical, 50, null, 20, 0, MoveTarget.SingleEnemy);

        private static readonly Species Chomp = new Species(new Id<Species>("chomp"), "Chomp", new[] { T("dragon") }, Stats(100),
            new[] { new LearnableMove(new Id<Move>("tackle"), 1) }, new Evolution[0],
            ability: new AbilityId("sand_veil"), hiddenAbility: new AbilityId("rough_skin"));

        private static readonly CompetitiveSet Scarf = new CompetitiveSet("chomp_ou_1", Chomp.Id, "ou", "Choice Scarf", 80,
            new[] { "choice_scarf" }, new[] { "rough_skin" }, new[] { "jolly" }, StatSpread.Parse("252 Atq/4 PS/252 Vel"), null,
            new[] { new[] { "earthquake" }, new[] { "outrage", "dragon_claw" }, new[] { "stone_edge" }, new[] { "fire_fang", "nonexistent" } });
        private static readonly CompetitiveSet Dance = new CompetitiveSet("chomp_uu_1", Chomp.Id, "uu", "Swords Dance", 10,
            new[] { "life_orb" }, null, new[] { "adamant" }, StatSpread.Parse("252 Atq/252 Vel"), null,
            new[] { new[] { "swords_dance" }, new[] { "earthquake" } });

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(new[] { Chomp }, s => s.Id.Value),
            new MemoryCatalog<Move>(new[] { "tackle", "earthquake", "outrage", "dragon_claw", "stone_edge", "fire_fang", "swords_dance" }.Select(M).ToArray(), m => m.Id.Value),
            new TypeChart.Builder().Build(), Ruleset.Classic,
            abilities: new MemoryCatalog<AbilityDefinition>(new[] { new AbilityDefinition(new AbilityId("sand_veil"), "Velo Arena"), new AbilityDefinition(new AbilityId("rough_skin"), "Piel Tosca") }, a => a.Id.Value),
            items: new MemoryCatalog<ItemDefinition>(new[] { new ItemDefinition("choice_scarf", "Pañuelo Elección", ItemCategory.Held), new ItemDefinition("life_orb", "Vidasfera", ItemCategory.Held) }, i => i.Id),
            natures: new MemoryCatalog<Nature>(new[] { new Nature(new Id<Nature>("jolly"), "Alegre", StatId.Speed, StatId.SpAttack), new Nature(new Id<Nature>("adamant"), "Firme", StatId.Attack, StatId.SpAttack) }, n => n.Id.Value),
            sets: new[] { Scarf, Dance });

        private sealed class SeqRng : IRng
        {
            private int _i; private readonly int[] _values;
            public SeqRng(params int[] values) { _values = values; }
            public int Next(int minInclusive, int maxExclusive) => System.Math.Max(minInclusive, System.Math.Min(maxExclusive - 1, _values[_i++ % _values.Length]));
            public float NextFloat() => 0.5f;
        }

        [Test]
        public void Cells_parse_options_and_slots()
        {
            var slots = CompetitiveSet.ParseSlots("swords_dance / earthquake, stone_edge / outrage");
            Assert.AreEqual(3, slots.Count);
            CollectionAssert.AreEqual(new[] { "earthquake", "stone_edge" }, slots[1]);
            Assert.AreEqual("swords_dance / earthquake, stone_edge / outrage", CompetitiveSet.FormatSlots(slots));
        }

        [Test]
        public void Choose_weights_by_score_and_filters_formats()
        {
            Assert.AreSame(Scarf, CompetitiveSet.Choose(new[] { Dance, Scarf }, Chomp.Id, null, null), "sin azar: el más usado");
            Assert.AreSame(Dance, CompetitiveSet.Choose(new[] { Dance, Scarf }, Chomp.Id, new[] { "uu" }, null), "solo UU");
            Assert.IsNull(CompetitiveSet.Choose(new[] { Dance, Scarf }, Chomp.Id, new[] { "lc" }, null));
            // Pesos: Scarf 85, Dance 15 (puntuación + 5). Una tirada de 90 cae en Dance si va primero... en el orden dado.
            Assert.AreSame(Dance, CompetitiveSet.Choose(new[] { Scarf, Dance }, Chomp.Id, null, new SeqRng(90)));
            Assert.AreSame(Scarf, CompetitiveSet.Choose(new[] { Scarf, Dance }, Chomp.Id, null, new SeqRng(10)));
        }

        [Test]
        public void Apply_fills_only_what_is_empty_and_skips_what_does_not_exist()
        {
            var data = Data();
            var spec = Scarf.ApplyTo(new TeamMemberSpec(Chomp.Id, 60), null, data.Exists);
            CollectionAssert.AreEqual(new[] { "earthquake", "outrage", "stone_edge", "fire_fang" }, spec.Moves.Select(m => m.Value));
            Assert.AreEqual("choice_scarf", spec.HeldItem);
            Assert.AreEqual("rough_skin", spec.Ability.Value.Value);
            Assert.AreEqual("jolly", spec.Nature.Value.Value);
            Assert.AreEqual(252, spec.Evs.Of(StatId.Speed));
            Assert.AreEqual(31, spec.FixedIv);

            var written = Scarf.ApplyTo(new TeamMemberSpec(Chomp.Id, 60, heldItem: "life_orb", nature: new Id<Nature>("adamant")), null, data.Exists);
            Assert.AreEqual("life_orb", written.HeldItem, "lo escrito se respeta");
            Assert.AreEqual("adamant", written.Nature.Value.Value);
        }

        private static TrainerDefinition Rival(int level, bool variable = false, string[] formats = null, TeamMemberSpec member = null)
            => new TrainerDefinition("rival", "Rival", new[] { member ?? new TeamMemberSpec(Chomp.Id, 60) }, aiLevel: level,
                setFormats: formats, variableSets: variable);

        [Test]
        public void Champion_level_ais_use_sets_and_lower_levels_do_not()
        {
            var data = Data();
            var champ = TeamBuilder.TrainerTeam(Rival(5), data, new SeqRng(0))[0];
            Assert.AreEqual(4, champ.Moves.Count);
            Assert.IsTrue(champ.Moves.Any(m => m.Value == "earthquake"));
            Assert.Greater(champ.EvTotal, 0);

            var elite = TeamBuilder.TrainerTeam(Rival(4), data, new SeqRng(0))[0];
            Assert.IsFalse(elite.Moves.Any(m => m.Value == "earthquake"), "Élite: su planificador, no sets");

            var written = TeamBuilder.TrainerTeam(Rival(5, member: new TeamMemberSpec(Chomp.Id, 60, new[] { new Id<Move>("tackle") })), data, new SeqRng(0))[0];
            CollectionAssert.AreEqual(new[] { "tackle" }, written.Moves.Select(m => m.Value), "con movimientos escritos no se toca");
        }

        [Test]
        public void Fixed_sets_repeat_and_variable_sets_follow_the_battle_rng()
        {
            var data = Data();
            var a = TeamBuilder.TrainerTeam(Rival(6), data, new SeqRng(0))[0];
            var b = TeamBuilder.TrainerTeam(Rival(6), data, new SeqRng(99))[0];
            CollectionAssert.AreEqual(a.Moves, b.Moves, "FIJO: el mismo set aunque cambie el azar del combate");

            var high = TeamBuilder.TrainerTeam(Rival(6, variable: true), data, new SeqRng(95))[0];
            var low = TeamBuilder.TrainerTeam(Rival(6, variable: true), data, new SeqRng(0))[0];
            Assert.IsTrue(high.Moves.Any(m => m.Value == "swords_dance"), "CAMBIANTE: con esta tirada sale Swords Dance");
            Assert.IsFalse(low.Moves.Any(m => m.Value == "swords_dance"), "y con esta, Choice Scarf");

            var uuOnly = TeamBuilder.TrainerTeam(Rival(6, formats: new[] { "uu" }), data, new SeqRng(0))[0];
            Assert.IsTrue(uuOnly.Moves.Any(m => m.Value == "swords_dance"), "solo sets de UU");
        }

        [Test]
        public void Suggest_proposes_the_most_used_set_for_set_users()
        {
            var sug = TeamBuilder.Suggest(new TeamMemberSpec(Chomp.Id, 60), Data(), AiProfile.Classic(5), MovesetStyle.ByAi);
            Assert.AreEqual("ou: Choice Scarf", sug.SetName);
            Assert.AreEqual("rough_skin", sug.AbilityId);
            Assert.AreEqual("choice_scarf", sug.HeldItem);
        }
    }
}
