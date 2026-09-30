using System.Linq;
using NUnit.Framework;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Editor;

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

        [Test]
        public void Every_classic_item_survives_a_trip_through_the_excel_text()
        {
            var items = new[]
            {
                new ItemDefinition("potion", "p", ItemCategory.Medicine, healHp: 20),
                new ItemDefinition("full_restore", "p", ItemCategory.Medicine, healPercent: 100, curesAllStatus: true),
                new ItemDefinition("antidote", "p", ItemCategory.StatusCure, curesStatusId: "poison|toxic"),
                new ItemDefinition("elixir", "p", ItemCategory.PpRestore, restorePp: 10, restorePpAllMoves: true),
                new ItemDefinition("ultra", "p", ItemCategory.Ball, catchMultiplier: 1.5f),
                new ItemDefinition("x_attack", "p", ItemCategory.BattleBoost, battleStatId: "attack", battleStages: 2),
                new ItemDefinition("sitrus", "p", ItemCategory.Held, heldTriggerHpPercent: 50, heldTriggerHealPercent: 25),
                new ItemDefinition("comp", "p", ItemCategory.Held, extras: new ItemExtras
                {
                    ResistBerryType = "fire", BlackSludge = true, AirBalloon = true, ContactDamagePercent = 16.67f, QuickClawChance = 20,
                    FlinchChance = 10, SelfStatusEndOfTurn = "burn", ChoiceLock = true, CuresAnyStatus = true, SurviveFromFullHp = true,
                    WeatherTurnsBonus = 3, SuperEffectiveBoost = 1.2f,
                }),
            };
            foreach (var it in items)
            {
                string text = EffectText.Format(it.Effects);
                var back = EffectText.Parse(text);
                Assert.AreEqual(it.Effects.Count, back.Count, text);
                for (int i = 0; i < back.Count; i++) AssertSame(it.Effects[i], back[i]);
            }
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
