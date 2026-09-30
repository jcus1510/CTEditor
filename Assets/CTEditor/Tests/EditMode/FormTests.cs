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
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// FORMAS: de combate (Modo Daruma por PS, Aegislash por movimiento, Giratina por objeto, vuelven al retirarse) y
    /// VARIANTES fuera del combate (un objeto cambia de una especie de la familia a otra).
    /// </summary>
    public class FormTests
    {
        private static Id<ElementType> T(string t) => new Id<ElementType>(t);
        private static StatBlock Stats(int hp, int atk, int def, int spa, int spd, int spe) => new StatBlock.Builder()
            .Set(StatId.Hp, hp).Set(StatId.Attack, atk).Set(StatId.Defense, def).Set(StatId.SpAttack, spa).Set(StatId.SpDefense, spd).Set(StatId.Speed, spe).Build();

        private static readonly Move[] Moves =
        {
            new Move(new Id<Move>("tackle"), "tackle", T("normal"), MoveCategory.Physical, 40, null, 20, 0, MoveTarget.SingleEnemy),
            new Move(new Id<Move>("smash"), "smash", T("normal"), MoveCategory.Physical, 250, null, 20, 0, MoveTarget.SingleEnemy),
            new Move(new Id<Move>("wait"), "wait", T("normal"), MoveCategory.Status, 0, null, 20, 0, MoveTarget.Self),
            new Move(new Id<Move>("relic_song"), "relic_song", T("normal"), MoveCategory.Special, 75, null, 10, 0, MoveTarget.SingleEnemy),
        };

        private static readonly AbilityDefinition[] Abilities =
        {
            new AbilityDefinition(new AbilityId("zen_mode"), "Modo Daruma"),
            new AbilityDefinition(new AbilityId("stance_change"), "Cambio Táctico"),
        };

        private static TurnResolver Resolver(GenerationRules g = null) => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value), new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(2),
            new InMemoryCatalog<StatusConditionDefinition>(new StatusConditionDefinition[0], s => s.Id.Value),
            abilities: new InMemoryCatalog<AbilityDefinition>(Abilities, a => a.Id.Value),
            rules: new BattleRules(generation: g));

        private static BattleParticipant Mon(string id, string ability = null, string item = null, int speed = 50,
            BattleForm[] forms = null, FormChange[] changes = null, string[] moves = null)
            => new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50, Stats(300, 100, 100, 100, 100, speed), 300,
                new List<Id<ElementType>> { T("normal") }, (moves ?? new[] { "tackle", "wait", "smash", "relic_song" }).Select(m => new Id<Move>(m)).ToList(),
                null, ability == null ? (AbilityId?)null : new AbilityId(ability), heldItem: item, forms: forms, formChanges: changes);

        private static UseMove Use(string m) => new UseMove(new Id<Move>(m));

        private static readonly BattleForm Zen = new BattleForm("zen", "Modo Daruma", Stats(300, 30, 105, 140, 105, 55), new[] { T("fire"), T("psychic") });
        private static readonly FormChange[] ZenRules =
        {
            new FormChange("", "zen", FormTrigger.HpBelow, hpPercent: 50, requiredAbility: "zen_mode"),
            new FormChange("zen", "", FormTrigger.HpAtLeast, hpPercent: 50, requiredAbility: "zen_mode"),
        };

        [Test]
        public void Zen_mode_changes_form_at_the_end_of_turn_below_half_hp()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("darm", "zen_mode", forms: new[] { Zen }, changes: ZenRules, speed: 10), Mon("foe", speed: 99));
            var ev = r.ResolveTurn(b, Use("wait"), Use("wait")).ToList();
            Assert.AreEqual("", b.Player.FormId, "con toda la vida no cambia");

            TakeDamage(b.Player, 200);
            ev = r.ResolveTurn(b, Use("wait"), Use("wait")).ToList();
            Assert.AreEqual("zen", b.Player.FormId);
            Assert.AreEqual(140, b.Player.Stats.Of(StatId.SpAttack));
            Assert.AreEqual(300, b.Player.MaxHp, "los PS máximos no cambian");
            CollectionAssert.AreEqual(new[] { T("fire"), T("psychic") }, b.Player.Types.ToList());
            Assert.IsTrue(ev.OfType<FormChangedEvent>().Any(e => e.To == "zen" && e.FormName == "Modo Daruma"));
        }

        [Test]
        public void Without_the_ability_or_with_abilities_off_there_is_no_form_change()
        {
            var noAbility = new CTEditor.Battle.Domain.Battle(Mon("darm", forms: new[] { Zen }, changes: ZenRules), Mon("foe"));
            TakeDamage(noAbility.Player, 200);
            Resolver().ResolveTurn(noAbility, Use("wait"), Use("wait")).ToList();
            Assert.AreEqual("", noAbility.Player.FormId);

            var off = new CTEditor.Battle.Domain.Battle(Mon("darm", "zen_mode", forms: new[] { Zen }, changes: ZenRules), Mon("foe"));
            TakeDamage(off.Player, 200);
            Resolver(GenerationRules.ForGeneration(2)).ResolveTurn(off, Use("wait"), Use("wait")).ToList();
            Assert.AreEqual("", off.Player.FormId);
        }

        [Test]
        public void Stance_change_switches_to_blade_before_attacking_and_back_on_switch_out()
        {
            var blade = new BattleForm("blade", "Filo", Stats(300, 150, 50, 150, 50, 60), revertsOnSwitch: true);
            var rules = new[] { new FormChange("", "blade", FormTrigger.DamagingMove, requiredAbility: "stance_change") };
            var r = Resolver();
            var withForm = new CTEditor.Battle.Domain.Battle(
                new[] { Mon("aegi", "stance_change", forms: new[] { blade }, changes: rules, speed: 99), Mon("mate") },
                new[] { Mon("foe", speed: 1) });
            var plain = new CTEditor.Battle.Domain.Battle(Mon("aegi", speed: 99), Mon("foe", speed: 1));

            var ev = r.ResolveTurn(withForm, Use("tackle"), Use("wait")).ToList();
            r.ResolveTurn(plain, Use("tackle"), Use("wait")).ToList();
            Assert.AreEqual("blade", withForm.Player.FormId);
            int formIdx = ev.FindIndex(e => e is FormChangedEvent), usedIdx = ev.FindIndex(e => e is MoveUsedEvent mu && mu.Attacker.Value == "aegi");
            Assert.Less(formIdx, usedIdx, "cambia ANTES de golpear");
            Assert.Greater(300 - withForm.Enemy.CurrentHp, 300 - plain.Enemy.CurrentHp, "golpea con el Ataque de la forma Filo");

            var aegi = withForm.Player;
            r.ResolveTurn(withForm, new SwitchMonster(new Id<BattleParticipant>("mate")), Use("wait")).ToList();
            Assert.AreEqual("", aegi.FormId, "al retirarse vuelve a la normal");
        }

        [Test]
        public void A_move_can_toggle_a_form_after_it_is_used()
        {
            var pirouette = new BattleForm("pirouette", "Danza", Stats(300, 128, 90, 77, 77, 128), new[] { T("normal"), T("fighting") });
            var rules = new[]
            {
                new FormChange("", "pirouette", FormTrigger.UseMove, move: "relic_song", afterMove: true),
                new FormChange("pirouette", "", FormTrigger.UseMove, move: "relic_song", afterMove: true),
            };
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("melo", forms: new[] { pirouette }, changes: rules, speed: 99), Mon("foe", speed: 1));
            var ev = r.ResolveTurn(b, Use("relic_song"), Use("wait")).ToList();
            Assert.AreEqual("pirouette", b.Player.FormId);
            Assert.Greater(ev.FindIndex(e => e is FormChangedEvent), ev.FindIndex(e => e is MoveUsedEvent), "cambia DESPUÉS");
            r.ResolveTurn(b, Use("relic_song"), Use("wait")).ToList();
            Assert.AreEqual("", b.Player.FormId, "y vuelve con el mismo movimiento");
        }

        [Test]
        public void A_held_item_gives_its_form_when_entering_the_field()
        {
            var origin = new BattleForm("origin", "Origen", Stats(300, 120, 100, 120, 100, 90), new[] { T("ghost"), T("dragon") });
            var rules = new[] { new FormChange("", "origin", FormTrigger.HeldItem, item: "griseous_orb") };
            var b = new CTEditor.Battle.Domain.Battle(Mon("gira", item: "griseous_orb", forms: new[] { origin }, changes: rules), Mon("foe"));
            Resolver().ResolveBattleStart(b).ToList();
            Assert.AreEqual("origin", b.Player.FormId);

            var without = new CTEditor.Battle.Domain.Battle(Mon("gira", forms: new[] { origin }, changes: rules), Mon("foe"));
            Resolver().ResolveBattleStart(without).ToList();
            Assert.AreEqual("", without.Player.FormId);
        }

        // ---------------- Orquestador y variantes ----------------

        private static readonly Species[] AllSpecies =
        {
            new Species(new Id<Species>("darm"), "Darmanitan", new[] { T("fire") }, Stats(105, 140, 55, 30, 55, 95), new LearnableMove[0], new Evolution[0],
                ability: new AbilityId("zen_mode"),
                forms: new[] { new SpeciesForm("zen", "Modo Daruma", new[] { T("fire"), T("psychic") }, Stats(105, 30, 105, 140, 105, 55)) },
                formChanges: ZenRules),
            new Species(new Id<Species>("sky_hog"), "Hedgi", new[] { T("grass") }, Stats(100, 100, 100, 100, 100, 100), new LearnableMove[0], new Evolution[0]),
            new Species(new Id<Species>("sky_hog_sky"), "Hedgi Cielo", new[] { T("grass"), T("flying") }, Stats(100, 103, 75, 120, 75, 127),
                new LearnableMove[0], new Evolution[0], formOf: new Id<Species>("sky_hog"), variantItem: "gracidea"),
        };

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value), new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Build(), Ruleset.Classic,
            items: new MemoryCatalog<ItemDefinition>(new[] { new ItemDefinition("gracidea", "Gracídea", ItemCategory.Key, usableOutsideBattle: true, consumable: false) }, i => i.Id));

        private sealed class FixedRng : IRng
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => 0.5f;
        }

        [Test]
        public void The_snapshot_carries_the_forms_with_the_individual_stats()
        {
            var data = Data();
            var mon = TeamBuilder.Build(new TeamMemberSpec(new Id<Species>("darm"), 50, fixedIv: 31), new Id<MonsterInstance>("m"), data, new FixedRng());
            var snap = data.Snapshot(mon);
            Assert.AreEqual(1, snap.Forms.Count);
            Assert.AreEqual(2, snap.FormChanges.Count);
            var zen = snap.Forms[0];
            Assert.AreEqual(mon.StatsFor(Stats(105, 30, 105, 140, 105, 55), data.Growth).Of(StatId.SpAttack), zen.Stats.Of(StatId.SpAttack));
            Assert.Greater(zen.Stats.Of(StatId.SpAttack), mon.Stats.Of(StatId.SpAttack));
        }

        [Test]
        public void A_variant_item_switches_between_the_base_and_its_variant()
        {
            var data = Data();
            var save = TeamBuilder.NewGame(new TeamPreset("t", "Prueba", new[] { new TeamMemberSpec(new Id<Species>("sky_hog"), 30, fixedIv: 31) },
                items: new[] { ("gracidea", 1) }), data, new FixedRng());
            var mon = save.Party.Members[0];
            var r = FieldActions.UseItem(data, save, 0, "gracidea");
            Assert.IsTrue(r.Done);
            Assert.AreEqual("sky_hog_sky", mon.SpeciesId.Value);
            Assert.AreEqual(1, save.Bag.Count("gracidea"), "no se gasta");
            FieldActions.UseItem(data, save, 0, "gracidea");
            Assert.AreEqual("sky_hog", mon.SpeciesId.Value, "y vuelve a la base");

            Assert.IsTrue(FieldActions.ChangeVariant(data, save, 0, "sky_hog_sky").Done, "o con un personaje");
            Assert.IsFalse(FieldActions.ChangeVariant(data, save, 0, "darm").Done, "no a otra familia");
        }

        private static void TakeDamage(Combatant c, int amount)
            => typeof(Combatant).GetMethod("TakeDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(c, new object[] { amount });
    }
}
