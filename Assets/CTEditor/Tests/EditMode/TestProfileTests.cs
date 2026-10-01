using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>Test profiles: what you start with when you play from the editor.</summary>
    public sealed class TestProfileTests
    {
        [Test]
        public void Profiles_are_saved_validated_and_give_the_hour()
        {
            var root = Path.Combine(Path.GetTempPath(), "ct_perfiles_" + Guid.NewGuid().ToString("N"));
            try
            {
                var p = new TestProfile("ruta3", "Ruta 3 de noche") { Badges = 2, Money = 5000, Time = TimeOfDay.Night };
                p.Party.Add(new TestMember("pikachu", 12));
                p.Party.Add(new TestMember("pidgey", 150));
                p.Items["pocion"] = 5;
                p.Flags.Add("liga_vencida");
                var repo = new JsonTestProfileRepository(root);
                repo.Save(new[] { p, TestProfile.Default() });

                var back = repo.Load();
                Assert.AreEqual(2, back.Count);
                var b = back[0];
                Assert.AreEqual(("Ruta 3 de noche", 2, 5000, TimeOfDay.Night), (b.Name, b.Badges, b.Money, b.Time.Value));
                Assert.AreEqual(100, b.Party[1].Level, "levels are clamped to 100");
                Assert.AreEqual(5, b.Items["POCION"], "item ids ignore case");
                Assert.IsTrue(b.FlagOn("liga_vencida"));
                Assert.AreEqual(TimeOfDay.Night, b.TimeNow(new DateTime(2026, 1, 1, 12, 0, 0)), "fixed hour wins over the clock");
                Assert.AreEqual(TimeOfDay.Day, back[1].TimeNow(new DateTime(2026, 1, 1, 12, 0, 0)), "no hour: the clock");

                Assert.IsEmpty(b.Problems(id => true));
                Assert.IsTrue(b.Problems(id => id != "pidgey").Any(x => x.Contains("pidgey")));
                for (int i = 0; i < 6; i++) b.Party.Add(new TestMember("rattata", 3));
                Assert.IsTrue(b.Problems().Any(x => x.Contains("como mucho 6")));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
