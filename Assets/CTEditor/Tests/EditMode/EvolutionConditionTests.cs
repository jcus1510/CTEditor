using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// EVOLUCIONES CON VARIAS CONDICIONES A LA VEZ: amistad + momento del día, nivel + Ataque/Defensa,
    /// objeto equipado + noche (y "al revés"), movimiento de un tipo, especie en el equipo, marcas de la
    /// partida, personalidad estable y su formato en Excel.
    /// </summary>
    public class EvolutionConditionTests
    {
        private static Id<Species> S(string id) => new Id<Species>(id);
        private static EvolutionCondition C(EvolutionConditionKind k, int value = 0, string id = null, DayTime time = DayTime.Day,
            StatRelation rel = StatRelation.AttackHigher, bool negate = false) => new EvolutionCondition(k, value, id, time, rel, negate);

        private static StatBlock Stats(int hp, int atk, int def) => new StatBlock.Builder().Set(StatId.Hp, hp).Set(StatId.Attack, atk)
            .Set(StatId.Defense, def).Set(StatId.SpAttack, 50).Set(StatId.SpDefense, 50).Set(StatId.Speed, 50).Build();

        private static Species Sp(string id, StatBlock stats, string type = "normal", params Evolution[] evos)
            => new Species(S(id), id, new[] { new Id<ElementType>(type) }, stats,
                new[] { new LearnableMove(new Id<Move>("tackle"), 1), new LearnableMove(new Id<Move>("charm"), 1) }, evos);

        private static readonly Species[] AllSpecies =
        {
            Sp("eevee", Stats(55, 55, 50), "normal",
                new Evolution(S("espeon"), EvolutionMethod.Friendship, conditions: new[] { C(EvolutionConditionKind.TimeOfDay, time: DayTime.Day) }),
                new Evolution(S("umbreon"), EvolutionMethod.Friendship, conditions: new[] { C(EvolutionConditionKind.TimeOfDay, time: DayTime.Night) }),
                new Evolution(S("sylveon"), EvolutionMethod.LevelUp, conditions: new[] { C(EvolutionConditionKind.MinFriendship, 160), C(EvolutionConditionKind.KnowsMoveOfType, id: "fairy") })),
            Sp("tyrogue", Stats(35, 35, 35), "fighting",
                new Evolution(S("hitmonlee"), EvolutionMethod.Level, 20, conditions: new[] { C(EvolutionConditionKind.StatRelation, rel: StatRelation.AttackHigher) }),
                new Evolution(S("hitmonchan"), EvolutionMethod.Level, 20, conditions: new[] { C(EvolutionConditionKind.StatRelation, rel: StatRelation.DefenseHigher) }),
                new Evolution(S("hitmontop"), EvolutionMethod.Level, 20, conditions: new[] { C(EvolutionConditionKind.StatRelation, rel: StatRelation.AttackEqualsDefense) })),
            Sp("sneasel", Stats(55, 95, 55), "dark",
                new Evolution(S("weavile"), EvolutionMethod.LevelUp, conditions: new[] { C(EvolutionConditionKind.HoldsItem, id: "razor_claw"), C(EvolutionConditionKind.TimeOfDay, time: DayTime.Night) })),
            Sp("pikachu", Stats(35, 55, 40), "electric",
                // Con la piedra... salvo que lleve la Piedra Eterna (condición "al revés").
                new Evolution(S("raichu"), EvolutionMethod.Item, itemId: "thunder_stone", conditions: new[] { C(EvolutionConditionKind.HoldsItem, id: "everstone", negate: true) })),
            Sp("mantyke", Stats(45, 20, 50), "water",
                new Evolution(S("mantine"), EvolutionMethod.LevelUp, conditions: new[] { C(EvolutionConditionKind.PartyHasSpecies, id: "remoraid") })),
            Sp("secret", Stats(50, 50, 50), "normal",
                new Evolution(S("secret2"), EvolutionMethod.LevelUp, conditions: new[] { C(EvolutionConditionKind.GameFlag, id: "vencio_alto_mando") })),
            Sp("wurmple", Stats(45, 45, 35), "bug",
                new Evolution(S("silcoon"), EvolutionMethod.Level, 7, conditions: new[] { C(EvolutionConditionKind.Chance, 50) }),
                new Evolution(S("cascoon"), EvolutionMethod.Level, 7)),
            Sp("espeon", Stats(65, 65, 60)), Sp("umbreon", Stats(95, 65, 110)), Sp("sylveon", Stats(95, 65, 65)),
            Sp("hitmonlee", Stats(50, 120, 53)), Sp("hitmonchan", Stats(50, 105, 79)), Sp("hitmontop", Stats(50, 95, 95)),
            Sp("weavile", Stats(70, 120, 65)), Sp("raichu", Stats(60, 90, 55)), Sp("mantine", Stats(85, 40, 70)),
            Sp("remoraid", Stats(35, 65, 35), "water"), Sp("secret2", Stats(80, 80, 80)), Sp("silcoon", Stats(50, 35, 55)), Sp("cascoon", Stats(50, 35, 55)),
        };

        private static readonly Move[] Moves =
        {
            new Move(new Id<Move>("tackle"), "tackle", new Id<ElementType>("normal"), MoveCategory.Physical, 40, null, 35, 0, MoveTarget.SingleEnemy),
            new Move(new Id<Move>("charm"), "charm", new Id<ElementType>("fairy"), MoveCategory.Status, 0, null, 20, 0, MoveTarget.SingleEnemy),
        };

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value), new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Build(), Ruleset.Classic,
            items: new MemoryCatalog<ItemDefinition>(new[]
            {
                new ItemDefinition("thunder_stone", "Piedra Trueno", ItemCategory.Evolution, usableOutsideBattle: true),
                new ItemDefinition("everstone", "Piedra Eterna", ItemCategory.Held, consumable: false),
                new ItemDefinition("razor_claw", "Garra Afilada", ItemCategory.Held, consumable: false),
            }, i => i.Id));

        private sealed class FixedRng : IRng
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => 0.5f;
        }

        private static PlayerSave Save(GameData data, params (string species, int level, string[] moves)[] team)
            => TeamBuilder.NewGame(new TeamPreset("t", "Prueba", team.Select(m => new TeamMemberSpec(S(m.species), m.level,
                (m.moves ?? new string[0]).Select(x => new Id<Move>(x)).ToList(), fixedIv: 0)).ToList()), data, new FixedRng());

        private static string Evolves(GameData data, PlayerSave save, int index, EvolutionTrigger trigger = EvolutionTrigger.LevelUp, string item = null)
        {
            var mon = save.Party.Members[index];
            var evo = EvolutionRules.Find(mon, data.SpeciesOf(mon), trigger, item, data.EvolutionContextFor(save));
            return evo?.Target.Value;
        }

        [Test]
        public void Friendship_and_time_of_day_together()
        {
            var data = Data();
            var save = Save(data, ("eevee", 10, new[] { "tackle" }));
            Assert.IsNull(Evolves(data, save, 0), "sin amistad no evoluciona");
            save.Party.Members[0].SetFriendship(230);
            save.World.Hour = 12;
            Assert.AreEqual("espeon", Evolves(data, save, 0));
            save.World.Hour = 22;
            Assert.AreEqual("umbreon", Evolves(data, save, 0));
        }

        [Test]
        public void Friendship_plus_a_move_of_a_type()
        {
            var data = Data();
            var save = Save(data, ("eevee", 10, new[] { "tackle", "charm" }));
            save.Party.Members[0].SetFriendship(170);  // 160 ≤ 170 < 220: ni Espeon ni Umbreon
            Assert.AreEqual("sylveon", Evolves(data, save, 0));
            var noFairy = Save(data, ("eevee", 10, new[] { "tackle" }));
            noFairy.Party.Members[0].SetFriendship(170);
            Assert.IsNull(Evolves(data, noFairy, 0), "sin un movimiento de tipo Hada no");
        }

        [Test]
        public void Level_plus_attack_versus_defense()
        {
            var data = Data();
            var save = Save(data, ("tyrogue", 20, null));
            Assert.AreEqual("hitmontop", Evolves(data, save, 0), "Ataque = Defensa (mismas stats base e IV 0)");
            var low = Save(data, ("tyrogue", 19, null));
            Assert.IsNull(Evolves(data, low, 0), "sin llegar al nivel 20 no");
        }

        [Test]
        public void Held_item_and_night_and_the_reversed_condition()
        {
            var data = Data();
            var save = Save(data, ("sneasel", 30, null), ("pikachu", 20, null));
            var sneasel = save.Party.Members[0];
            save.World.Hour = 23;
            Assert.IsNull(Evolves(data, save, 0), "de noche pero sin la Garra Afilada");
            sneasel.SetHeldItem("razor_claw");
            Assert.AreEqual("weavile", Evolves(data, save, 0));
            save.World.Hour = 10;
            Assert.IsNull(Evolves(data, save, 0), "con la Garra pero de día");

            Assert.AreEqual("raichu", Evolves(data, save, 1, EvolutionTrigger.UseItem, "thunder_stone"));
            save.Party.Members[1].SetHeldItem("everstone");
            Assert.IsNull(Evolves(data, save, 1, EvolutionTrigger.UseItem, "thunder_stone"), "con la Piedra Eterna, NO");
        }

        [Test]
        public void A_stone_is_not_spent_when_its_conditions_fail()
        {
            var data = Data();
            var save = Save(data, ("pikachu", 20, null));
            save.Bag.Add("thunder_stone");
            save.Party.Members[0].SetHeldItem("everstone");
            Assert.IsFalse(FieldActions.UseItem(data, save, 0, "thunder_stone").Done);
            Assert.AreEqual(1, save.Bag.Count("thunder_stone"));
            save.Party.Members[0].SetHeldItem(null);
            var r = FieldActions.UseItem(data, save, 0, "thunder_stone");
            Assert.IsTrue(r.Done, r.ToString());
            Assert.AreEqual("raichu", save.Party.Members[0].SpeciesId.Value);
        }

        [Test]
        public void Species_in_the_party_and_story_flags()
        {
            var data = Data();
            var alone = Save(data, ("mantyke", 20, null));
            Assert.IsNull(Evolves(data, alone, 0));
            var withFriend = Save(data, ("mantyke", 20, null), ("remoraid", 20, null));
            Assert.AreEqual("mantine", Evolves(data, withFriend, 0));

            var save = Save(data, ("secret", 5, null));
            Assert.IsNull(Evolves(data, save, 0));
            save.World.SetFlag("vencio_alto_mando");
            Assert.AreEqual("secret2", Evolves(data, save, 0));
        }

        [Test]
        public void Personality_split_is_stable_and_roughly_half()
        {
            var data = Data();
            int silcoon = 0;
            for (int i = 0; i < 200; i++)
            {
                var mon = TeamBuilder.Build(new TeamMemberSpec(S("wurmple"), 7), new Id<MonsterInstance>("w" + i), data, new FixedRng());
                var first = EvolutionRules.Find(mon, data.SpeciesOf(mon), EvolutionTrigger.LevelUp)?.Target.Value;
                Assert.AreEqual(first, EvolutionRules.Find(mon, data.SpeciesOf(mon), EvolutionTrigger.LevelUp)?.Target.Value, "siempre el mismo");
                if (first == "silcoon") silcoon++;
            }
            Assert.IsTrue(silcoon >= 60 && silcoon <= 140, $"{silcoon} de 200");
        }

        [Test]
        public void Evolution_conditions_round_trip_in_excel()
        {
            string cell = "espeon@amistad+hora:dia | hitmonlee@20+stats:atq>def | weavile@subir+lleva:razor_claw+hora:noche | raichu@objeto:thunder_stone+!lleva:everstone | silcoon@7+azar:50";
            var evos = CsvCodecs.ParseEvolutions(cell);
            Assert.AreEqual(5, evos.Count);
            Assert.AreEqual(EvolutionMethod.LevelUp, evos[2].Method);
            Assert.AreEqual(2, evos[2].Conditions.Count);
            Assert.AreEqual(DayTime.Night, evos[2].Conditions[1].Time);
            Assert.IsTrue(evos[3].Conditions[0].Negate);
            Assert.AreEqual(StatRelation.AttackHigher, evos[1].Conditions[0].Relation);
            Assert.AreEqual(cell.Replace(" | ", "|"), CsvCodecs.FormatEvolutions(evos).Replace(" | ", "|"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseEvolutions("espeon@amistad+hora:mediodia"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseEvolutions("espeon@amistad+volar:1"));
        }
    }
}
