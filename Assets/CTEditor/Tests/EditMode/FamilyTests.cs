using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Content;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// The family tree of the application: evolutions, battle forms and form changes are read and written back the same
    /// (every species of the packs), and the graph finds roots, members and problems.
    /// </summary>
    public class FamilyTests
    {
        private static string Norm(string s) => string.Join("|", (s ?? "").Split('|').Select(x => x.Trim()).Where(x => x.Length > 0));

        [Test]
        public void Evolutions_forms_and_changes_of_every_pack_survive_a_round_trip()
        {
            var packs = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../../Assets/GameContent/Packs"));
            if (!Directory.Exists(packs)) Assert.Ignore("Packs no encontrados en esta copia.");
            int n = 0;
            foreach (var dir in Directory.GetDirectories(packs))
            {
                var t = ContentDatabase.Load(dir).Table(ContentSchemas.Species);
                foreach (var r in t.Records)
                {
                    var id = t.IdOf(r);
                    Assert.AreEqual(Norm(r["evoluciona"]), Norm(Evolutions.Write(Evolutions.Parse(r["evoluciona"]))), id);
                    Assert.AreEqual(Norm(r["formas"]), Norm(SpeciesForms.WriteForms(SpeciesForms.ParseForms(r["formas"]))), id);
                    Assert.AreEqual(Norm(r["cambios_forma"]), Norm(SpeciesForms.WriteChanges(SpeciesForms.ParseChanges(r["cambios_forma"]))), id);
                    n++;
                }
            }
            Assert.Greater(n, 1000);
        }

        [Test]
        public void Evolutions_read_methods_and_extra_conditions()
        {
            var e = Evolutions.Parse("weavile@subir+lleva:razor_claw+hora:noche | steelix@intercambio:metal_coat | ivysaur@16 | x@amistad+!lleva:everstone");
            Assert.AreEqual(4, e.Count);
            Assert.AreEqual("subir", e[0].Method);
            Assert.AreEqual("razor_claw", e[0].Condition("lleva"));
            Assert.AreEqual("noche", e[0].Condition("hora"));
            Assert.AreEqual("intercambio", e[1].Method);
            Assert.AreEqual("metal_coat", e[1].Value);
            Assert.AreEqual("16", e[2].Value);
            Assert.IsTrue(e[3].Conditions[0].Not);
            Assert.AreEqual("Al subir de nivel · llevando razor_claw · de noche", Evolutions.Describe(e[0], null));
            Assert.AreEqual("Nv. 16", Evolutions.Describe(e[2], (c, i) => i));
        }

        [Test]
        public void Battle_forms_and_their_changes()
        {
            var forms = SpeciesForms.ParseForms("mega_x;Mega-Charizard X;fire/dragon;130/111/130/85/100;tough_claws|zen;Modo Daruma;;;;vuelve");
            Assert.AreEqual("dragon", forms[0].Type2);
            Assert.AreEqual(130, forms[0].Stats[0]);
            Assert.IsTrue(forms[1].Reverts);
            Assert.IsFalse(forms[1].HasStats);
            var changes = SpeciesForms.ParseChanges(">mega_x:mega:charizardite_x|>mega:mega;sabe=dragon_ascent|>zen:ps_bajo:50;con=zen_mode");
            Assert.AreEqual("charizardite_x", changes[0].Value);
            Assert.AreEqual("dragon_ascent", changes[1].Knows);
            Assert.AreEqual("zen_mode", changes[2].Ability);
            Assert.AreEqual("ps_bajo", changes[2].Trigger);
            Assert.AreEqual(1, SpeciesForms.Into(changes, "zen").Count());
        }

        [Test]
        public void The_graph_finds_roots_members_variants_and_problems()
        {
            var root = Path.Combine(Path.GetTempPath(), "ct_family_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "especies.csv"), "id;nombre;tipos;evoluciona;forma_de\r\n" +
                    "eevee;Eevee;normal;vaporeon@objeto:water_stone | espeon@amistad+hora:dia;\r\n" +
                    "vaporeon;Vaporeon;water;;\r\nespeon;Espeon;psychic;;\r\n" +
                    "rotom;Rotom;electric/ghost;;\r\nrotom_wash;Rotom Lavado;electric/water;;rotom\r\n" +
                    "a;A;normal;b@20;\r\nb;B;normal;c@10 | zz@5;\r\nc;C;normal;;\r\nlone;Solo;normal;;\r\n");
                var db = ContentDatabase.Load(root);
                var g = FamilyGraph.For(db);
                Assert.AreSame(g, FamilyGraph.For(db));
                Assert.AreEqual("eevee", g.RootOf("espeon"));
                Assert.AreEqual("rotom", g.RootOf("rotom_wash"));
                CollectionAssert.AreEquivalent(new[] { "eevee", "vaporeon", "espeon" }, g.Members("eevee"));
                CollectionAssert.AreEqual(new[] { "rotom_wash" }, g.VariantsOf("rotom").ToList());
                CollectionAssert.DoesNotContain(g.Roots(false), "lone");
                CollectionAssert.Contains(g.Roots(true), "lone");
                var warnings = g.Warnings("a", id => db.Has(ContentSchemas.Species, id)).ToList();
                Assert.IsTrue(warnings.Any(w => w.Contains("zz")));
                Assert.IsTrue(warnings.Any(w => w.Contains("nivel 10")));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
