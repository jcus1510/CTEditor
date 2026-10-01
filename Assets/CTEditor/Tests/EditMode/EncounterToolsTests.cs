using System.Linq;
using NUnit.Framework;
using CTEditor.World.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>Encounter simulator, zone templates and the (optional) terrain check.</summary>
    public sealed class EncounterToolsTests
    {
        [Test]
        public void The_simulator_follows_the_rate_and_the_weights()
        {
            var t = new EncounterTable("hierba");
            t.Slots.Add(new EncounterSlot("pidgey", 2, 4, 75));
            t.Slots.Add(new EncounterSlot("rattata", 3, 3, 25));
            t.Slots.Add(new EncounterSlot("hoothoot", 5, 5, 50, TimeOfDay.Night));
            var r = EncounterSimulator.Run(t, 10, TimeOfDay.Day, _ => false, 20000, seed: 7);
            Assert.AreEqual(2000, r.Encounters, 120, "about 10 % of the steps");
            Assert.AreEqual(10, r.StepsPerEncounter, 0.7);
            var rows = r.Rows().ToList();
            Assert.AreEqual("pidgey", rows[0].species);
            Assert.AreEqual(75, rows[0].percent, 3);
            Assert.IsFalse(r.Species.ContainsKey("hoothoot"), "not at day");
            Assert.AreEqual((3, 3), (r.Species["rattata"].minLevel, r.Species["rattata"].maxLevel));
            Assert.AreEqual((2, 4), (r.Species["pidgey"].minLevel, r.Species["pidgey"].maxLevel));
            Assert.IsTrue(EncounterSimulator.Run(t, 10, null, _ => false, 20000).Species.ContainsKey("hoothoot"), "all hours");

            t.DoublePercent = 50;
            var d = EncounterSimulator.Run(t, 100, TimeOfDay.Day, _ => false, 1000, seed: 3);
            Assert.AreEqual(500, d.Doubles, 60);
            Assert.AreEqual(0, EncounterSimulator.Run(new EncounterTable("x"), 10, null, null, 100).Encounters, "empty table");
        }

        [Test]
        public void Templates_fill_a_zone_with_known_methods_and_species()
        {
            var area = new EncounterArea("z", "Cueva", false);
            var cave = EncounterTemplate.BuiltIn.First(x => x.Id == "cueva");
            cave.ApplyTo(area, EncounterMethodCatalog.Classic(), id => id != "onix");
            Assert.IsNotNull(area.TableFor("cueva"));
            Assert.IsNotNull(area.TableFor("golpe_roca"));
            Assert.IsFalse(area.Tables.SelectMany(x => x.Slots).Any(s => s.SpeciesId == "onix"), "unknown species are skipped");
            cave.ApplyTo(area, EncounterMethodCatalog.Classic(), null);
            Assert.AreEqual(5, area.TableFor("cueva").Slots.Count, "applying again replaces, does not duplicate");
        }

        [Test]
        public void The_terrain_check_finds_grass_painted_where_there_is_no_grass()
        {
            var map = new MapDefinition("m", "M", 4, 4);
            var area = new EncounterArea("z", "Hierba", false);
            area.SetCells(new[] { (0, 0), (1, 0) });
            area.GetOrAddTable("hierba");
            var off = EncounterChecks.CellsOffTerrain(map, MapTilesets.None, area, EncounterMethodCatalog.Classic());
            Assert.AreEqual(2, off.Count, "no tiles: no grass tag anywhere");
            area.Tables.Clear();
            area.GetOrAddTable("cueva");
            Assert.AreEqual(0, EncounterChecks.CellsOffTerrain(map, MapTilesets.None, area, EncounterMethodCatalog.Classic()).Count, "caves work on any step");
        }
    }
}
