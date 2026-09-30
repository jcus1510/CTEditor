using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;

namespace CTEditor.Tests.EditMode
{
    /// <summary>Importar y exportar equipos en formato SHOWDOWN (nombres en inglés ↔ ids del juego).</summary>
    public class ShowdownTests
    {
        private const string Team = @"=== [gen6ou] Prueba ===

Chompy (Garchomp) (F) @ Choice Scarf
Ability: Rough Skin
Level: 62
Tera Type: Ground
EVs: 252 Atk / 4 SpD / 252 Spe
Jolly Nature
IVs: 0 SpA
- Earthquake
- Dragon Claw
- U-turn
- Hidden Power [Fire]

Rotom-Wash @ Leftovers
Ability: Levitate
EVs: 252 HP / 252 Def / 4 SpA
Bold Nature
- Hydro Pump
- Will-O-Wisp
- King's Shield
";

        private static ShowdownConverter Converter()
        {
            var c = new ShowdownConverter();
            c.Species.Add("garchomp", "Garchomp", "Garchomp");
            c.Species.Add("rotom_wash", "", "Rotom Lavado");            // sin nombre en inglés: se deduce del id
            foreach (var m in new[] { "earthquake", "dragon_claw", "u_turn", "hidden_power", "hydro_pump", "will_o_wisp", "kings_shield" })
                c.Moves.Add(m);
            c.Items.Add("choice_scarf", "Choice Scarf", "Pañuelo Elección");
            c.Items.Add("leftovers", "", "Restos");
            c.Abilities.Add("rough_skin", "", "Piel Tosca");
            c.Abilities.Add("levitate", "", "Levitación");
            c.Natures.Add("jolly", "Jolly", "Alegre");
            c.Natures.Add("bold", "", "Osada");
            return c;
        }

        [Test]
        public void Parses_a_showdown_team()
        {
            var sets = ShowdownFormat.Parse(Team);
            Assert.AreEqual(2, sets.Count);
            var g = sets[0];
            Assert.AreEqual("Garchomp", g.Species);
            Assert.AreEqual("Chompy", g.Nickname);
            Assert.AreEqual("F", g.Gender);
            Assert.AreEqual("Choice Scarf", g.Item);
            Assert.AreEqual(62, g.Level);
            Assert.AreEqual(252, g.Evs.Of(StatId.Attack));
            Assert.AreEqual(0, g.Ivs.Of(StatId.SpAttack));
            Assert.AreEqual("Jolly", g.Nature);
            CollectionAssert.AreEqual(new[] { "Earthquake", "Dragon Claw", "U-turn", "Hidden Power" }, g.Moves);
            Assert.IsNull(sets[1].Level, "sin «Level:» = 100 en Showdown");
            Assert.AreEqual("Leftovers", sets[1].Item);
        }

        [Test]
        public void Resolves_names_to_ids_even_without_english_names()
        {
            var problems = new List<string>();
            var members = Converter().Import(Team, 50, problems);
            Assert.AreEqual(2, members.Count);
            var g = members[0];
            Assert.AreEqual("garchomp", g.SpeciesId);
            Assert.AreEqual("choice_scarf", g.ItemId);
            Assert.AreEqual("rough_skin", g.AbilityId);
            Assert.AreEqual("jolly", g.NatureId);
            CollectionAssert.AreEqual(new[] { "earthquake", "dragon_claw", "u_turn", "hidden_power" }, g.MoveIds);
            var r = members[1];
            Assert.AreEqual("rotom_wash", r.SpeciesId);
            Assert.AreEqual(50, r.Level, "nivel por defecto del importador");
            Assert.AreEqual("will_o_wisp", r.MoveIds[1]);
            Assert.AreEqual("kings_shield", r.MoveIds[2]);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }

        [Test]
        public void Unknown_names_are_reported_and_left_automatic()
        {
            var problems = new List<string>();
            var members = Converter().Import("Pikachu @ Light Ball\n- Thunderbolt\n\nGarchomp @ Rocky Helmet\n- Earthquake\n- Outrage", 100, problems);
            Assert.AreEqual(1, members.Count, "especie desconocida: se salta");
            Assert.AreEqual("", members[0].ItemId);
            CollectionAssert.AreEqual(new[] { "earthquake" }, members[0].MoveIds);
            Assert.IsTrue(problems.Any(p => p.Contains("Pikachu")));
            Assert.IsTrue(problems.Any(p => p.Contains("Rocky Helmet")));
            Assert.IsTrue(problems.Any(p => p.Contains("Outrage")));
        }

        [Test]
        public void Exports_and_reimports_the_same_team()
        {
            var c = Converter();
            var members = c.Import(Team);
            string text = c.Export(members);
            StringAssert.Contains("Chompy (Garchomp) (F) @ Choice Scarf", text);
            StringAssert.Contains("Rotom-Wash @ Leftovers", text);
            StringAssert.Contains("Ability: Rough Skin", text);
            StringAssert.Contains("EVs: 252 HP / 252 Def / 4 SpA", text);
            StringAssert.Contains("Level: 62", text);
            StringAssert.Contains("- U Turn", text, "sin nombre en inglés se deduce del id (se reconoce igual al volver)");
            var again = c.Import(text);
            Assert.AreEqual(members.Count, again.Count);
            CollectionAssert.AreEqual(members[0].MoveIds, again[0].MoveIds);
            Assert.AreEqual(members[1].Evs.Total, again[1].Evs.Total);
            Assert.AreEqual(62, again[0].Level);
        }
    }
}
