using System;
using System.IO;
using NUnit.Framework;
using CTEditor.Content;
using CTEditor.GameDefinition.Text;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// The block editor of the application reads every effect of items and abilities of the packs (Gen1…Gen7): none is
    /// lost when it is opened and saved again.
    /// </summary>
    public class PackEffectsTests
    {
        [Test]
        public void Every_item_and_ability_effect_of_the_packs_is_read_by_the_block_editor()
        {
            var packs = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../../Assets/GameContent/Packs"));
            if (!Directory.Exists(packs)) Assert.Ignore("Packs no encontrados en esta copia.");
            int read = 0;
            foreach (var dir in Directory.GetDirectories(packs))
            {
                var db = ContentDatabase.Load(dir);
                foreach (var cat in new[] { ContentSchemas.Items, ContentSchemas.Abilities })
                    foreach (var r in db.Table(cat).Records)
                    {
                        var v = r["efectos"];
                        if (v.Trim().Length == 0) continue;
                        var blocks = EffectText.Parse(v); // throws with the reason if a pack has something the editor cannot show
                        Assert.AreEqual(blocks.Count, EffectText.Parse(EffectText.Format(blocks)).Count, $"{Path.GetFileName(dir)} {cat} {r["id"]}");
                        read++;
                    }
            }
            Assert.Greater(read, 500);
        }
    }
}
