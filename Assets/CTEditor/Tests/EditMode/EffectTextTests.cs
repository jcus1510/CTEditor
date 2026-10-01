using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.GameDefinition.Text;

namespace CTEditor.Tests.EditMode
{
    /// <summary>The Excel text of effect blocks («antes_de_golpe [si mov.tipo=fire]: daño_recibido x0,5; se_gasta»).</summary>
    public class EffectTextTests
    {
        private static void AssertSame(EffectBlock a, EffectBlock b)
        {
            Assert.AreEqual(a.Trigger, b.Trigger);
            Assert.AreEqual(a.Action, b.Action);
            Assert.AreEqual(a.Target, b.Target);
            Assert.AreEqual(a.Ref, b.Ref);
            Assert.AreEqual(a.Amount, b.Amount, 0.001f);
            Assert.AreEqual(a.Threshold, b.Threshold, 0.001f);
            Assert.AreEqual(a.Consumes, b.Consumes);
            Assert.AreEqual(a.Chance, b.Chance, 0.001f);
            Assert.AreEqual(a.MaxPerBattle, b.MaxPerBattle);
            Assert.AreEqual(a.Conditions.Count, b.Conditions.Count);
            for (int i = 0; i < a.Conditions.Count; i++)
            {
                Assert.AreEqual(a.Conditions[i].Kind, b.Conditions[i].Kind);
                Assert.AreEqual(a.Conditions[i].Text, b.Conditions[i].Text);
                Assert.AreEqual(a.Conditions[i].Negate, b.Conditions[i].Negate);
            }
        }

        /// <summary>A file of the repository, from Unity (project root) or from Tools/compilar_unity (walks up).</summary>
        private static string RepoFile(string rel)
        {
            if (File.Exists(rel) || Directory.Exists(rel)) return rel;
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string full = Path.Combine(dir.FullName, rel);
                if (File.Exists(full) || Directory.Exists(full)) return full;
            }
            Assert.Fail("No se encuentra " + rel);
            return null;
        }

        /// <summary>The source sheet the pack generators take the item effects from (Tools/datos_fuente/objetos.csv).</summary>
        private static string LibraryFile() => RepoFile("Tools/datos_fuente/objetos.csv");

        [Test]
        public void Every_item_of_the_source_sheet_is_read_and_written_back_without_losses()
        {
            var rows = CsvTable.Load(LibraryFile()).Rows;
            Assert.Greater(rows.Count, 100);
            var ids = new HashSet<string>();
            foreach (var r in rows)
            {
                Assert.IsTrue(ids.Add(r["id"]), "id repetido: " + r["id"]);
                var blocks = EffectText.Parse(r["efectos"]);
                var back = EffectText.Parse(EffectText.Format(blocks));
                Assert.AreEqual(blocks.Count, back.Count, r["id"]);
                for (int i = 0; i < back.Count; i++) AssertSame(blocks[i], back[i]);
                foreach (var b in blocks) Assert.IsTrue(EffectRules.IsSupported(b), $"{r["id"]}: {EffectText.Format(b)} no lo aplica el motor");
            }
        }

        [Test]
        public void The_effects_of_every_pack_are_read()
        {
            var packs = RepoFile("Assets/GameContent/Packs");
            int files = 0;
            foreach (var file in Directory.GetFiles(packs, "objetos.csv", SearchOption.AllDirectories))
            {
                files++;
                foreach (var r in CsvTable.Load(file).Rows)
                    Assert.DoesNotThrow(() => EffectText.Parse(r["efectos"]), $"{file}: {r["id"]}");
            }
            Assert.Greater(files, 0);
        }

        [Test]
        public void Every_ability_of_every_pack_is_read_and_fully_understood_by_the_engine()
        {
            int files = 0;
            foreach (var file in Directory.GetFiles(RepoFile("Assets/GameContent/Packs"), "habilidades.csv", SearchOption.AllDirectories)
                         .Concat(new[] { RepoFile("Tools/datos_fuente/habilidades.csv") }))
            {
                files++;
                foreach (var r in CsvTable.Load(file).Rows)
                {
                    var blocks = EffectText.Parse(r["efectos"]);
                    Assert.AreEqual(r["efectos"], EffectText.Format(blocks), $"{file}: {r["id"]} no se reescribe igual");
                    var ability = new AbilityDefinition(new AbilityId(r["id"]), r["nombre"], blocks);
                    // The abilities of the 6th gen. and before are all ability shapes (nothing left to the generic runner); newer
                    // ones may also use free blocks, which run like a held item's.
                    if (file.Contains("Packs") && !file.Contains("Gen7")) Assert.AreEqual(0, ability.GenericEffects.Count, $"{r["id"]}: {EffectText.Format(ability.GenericEffects)}");
                    foreach (var b in blocks) Assert.IsTrue(AbilityEffects.IsSupported(b), $"{r["id"]}: {EffectText.Format(b)}");
                }
            }
            Assert.Greater(files, 1);
        }

        [Test]
        public void Ability_blocks_are_read_into_what_the_engine_uses()
        {
            var blaze = new AbilityDefinition(new AbilityId("blaze"), "Mar Llamas", EffectText.Parse("siempre [si mov.tipo=fire & propio.vida<=33]: daño x1,5"));
            Assert.AreEqual("fire", blaze.LowHpBoostType.Value.Value);
            Assert.AreEqual(1.5f, blaze.LowHpBoostMultiplier, 0.001f);
            Assert.AreEqual(33f, blaze.LowHpThreshold.Value, 0.001f);

            var intimidate = new AbilityDefinition(new AbilityId("intimidate"), "Intimidación", EffectText.Parse("al_entrar: etapa attack -1; al_rival"));
            Assert.AreEqual("attack", intimidate.OnEntryStat.Value.Value);
            Assert.AreEqual(-1, intimidate.OnEntryStages);
            Assert.IsFalse(intimidate.OnEntryTargetsSelf);

            var spore = new AbilityDefinition(new AbilityId("effect_spore"), "Efecto Espora", EffectText.Parse("contacto: poner_estado poison,paralysis,sleep; al_rival; prob=30"));
            Assert.AreEqual(3, spore.Extras.ContactReactionStatuses.Count);
            Assert.AreEqual(30f, spore.ContactReactionChance.Value, 0.001f);

            var trace = new AbilityDefinition(new AbilityId("trace"), "Rastro", EffectText.Parse("al_entrar: rastro"));
            Assert.IsTrue(trace.Extras.TraceOnEntry);
            Assert.AreEqual("al_entrar: rastro", EffectText.Format(trace.Effects));

            // A combination that is not a classic shape is kept for the generic runner (like a held item).
            var custom = new AbilityDefinition(new AbilityId("x"), "X", EffectText.Parse("al_entrar: curar 10%"));
            Assert.AreEqual(1, custom.GenericEffects.Count);
            Assert.IsTrue(AbilityEffects.IsSupported(custom.GenericEffects[0]));
        }

        [Test]
        public void Hand_written_effects_are_read()
        {
            var b = EffectText.Parse("poca_vida@25: etapa attack +1; se_gasta | siempre [si mov.tipo=water]: potencia x1,2 | " +
                                     "al_sufrir_estado: curar_estado paralysis,sleep; se_gasta; veces=1 | fin_de_turno: curar 10%; prob=50");
            Assert.AreEqual(4, b.Count);
            Assert.AreEqual(EffectTrigger.LowHp, b[0].Trigger);
            Assert.AreEqual(25f, b[0].Threshold);
            Assert.AreEqual("attack", b[0].Ref);
            Assert.AreEqual(1f, b[0].Amount);
            Assert.IsTrue(b[0].Consumes);
            Assert.AreEqual(ConditionKind.MoveType, b[1].Conditions.Single().Kind);
            Assert.AreEqual("paralysis|sleep", b[2].Ref);
            Assert.AreEqual(1, b[2].MaxPerBattle);
            Assert.AreEqual(EffectAction.HealPercent, b[3].Action);
            Assert.AreEqual(50f, b[3].Chance);
        }

        [Test]
        public void Mistakes_are_explained_in_spanish()
        {
            var e = Assert.Throws<System.FormatException>(() => EffectText.Parse("cuando_sea: curar 10"));
            StringAssert.Contains("no es un momento válido", e.Message);
            e = Assert.Throws<System.FormatException>(() => EffectText.Parse("fin_de_turno: bailar"));
            StringAssert.Contains("no es una acción válida", e.Message);
        }
    }
}
