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
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;

using CTEditor.GameDefinition.Domain.Effects;
using static CTEditor.Tests.EditMode.TestItems;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// EL EQUIPO Y LA MOCHILA FUERA DEL COMBATE: curar, revivir, piedras evolutivas (con el movimiento
    /// que se aprende al evolucionar), dar y quitar objetos equipados, ordenar el equipo y el resumen.
    /// </summary>
    public class FieldActionsTests
    {
        private static Move Mv(string id, int power, string type = "normal")
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), power > 0 ? MoveCategory.Physical : MoveCategory.Status,
                power, null, 20, 0, MoveTarget.SingleEnemy, new MoveEffect[0]);

        private static readonly Move[] Moves =
        {
            Mv("tackle", 40), Mv("growl", 0), Mv("tail_whip", 0), Mv("scratch", 40), Mv("thunder", 110, "electric"),
        };

        private static StatBlock Stats(int all) => new StatBlock.Builder().Set(StatId.Hp, all).Set(StatId.Attack, all).Set(StatId.Defense, all)
            .Set(StatId.SpAttack, all).Set(StatId.SpDefense, all).Set(StatId.Speed, all).Build();

        private static LearnableMove L(int level, string move) => new LearnableMove(new Id<Move>(move), level);

        private static readonly Species[] AllSpecies =
        {
            // "mouse" evoluciona con la Piedra Trueno en "rat_king", que aprende "thunder" a nivel 10.
            new Species(new Id<Species>("mouse"), "Ratoncito", new[] { new Id<ElementType>("electric") }, Stats(40),
                new[] { L(1, "tackle"), L(1, "growl"), L(1, "tail_whip"), L(1, "scratch") },
                new[] { new Evolution(new Id<Species>("rat_king"), EvolutionMethod.Item, itemId: "thunder_stone") }),
            new Species(new Id<Species>("rat_king"), "Rey Rata", new[] { new Id<ElementType>("electric") }, Stats(90),
                new[] { L(1, "tackle"), L(10, "thunder") }, new Evolution[0]),
            new Species(new Id<Species>("pal"), "Compi", new[] { new Id<ElementType>("normal") }, Stats(50),
                new[] { L(1, "tackle") }, new Evolution[0]),
        };

        private static readonly ItemDefinition[] Items =
        {
            new ItemDefinition("potion", "Poción", ItemCategory.Medicine, usableInBattle: true, usableOutsideBattle: true, effects: new[] { OnUse(EffectAction.HealHp, 20) }),
            new ItemDefinition("revive", "Revivir", ItemCategory.Revive, usableOutsideBattle: true, effects: new[] { OnUse(EffectAction.Revive, 50) }),
            new ItemDefinition("antidote", "Antídoto", ItemCategory.StatusCure, usableOutsideBattle: true, effects: new[] { OnUse(EffectAction.CureStatus, 0, "poison") }),
            new ItemDefinition("ether", "Éter", ItemCategory.PpRestore, usableOutsideBattle: true, effects: new[] { OnUse(EffectAction.RestorePp, 10) }),
            new ItemDefinition("thunder_stone", "Piedra Trueno", ItemCategory.Evolution, usableOutsideBattle: true),
            new ItemDefinition("leftovers", "Restos", ItemCategory.Held, consumable: false, effects: new[] { EndOfTurnHeal(6.25f) }),
            new ItemDefinition("charcoal", "Carbón", ItemCategory.Held, consumable: false, effects: new[] { EndOfTurnHeal(1f) }),
            new ItemDefinition("bike", "Bici", ItemCategory.Key, consumable: false),
        };

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value),
            new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Build(),
            Ruleset.Classic,
            statuses: new MemoryCatalog<StatusConditionDefinition>(new[]
                { new StatusConditionDefinition(new StatusId("poison"), "Envenenado", new Percentage(12.5f), new Percentage(0)) }, s => s.Id.Value),
            items: new MemoryCatalog<ItemDefinition>(Items, i => i.Id));

        private sealed class FixedRng : IRng
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => 0.5f;
        }

        private static PlayerSave NewSave(GameData data)
            => TeamBuilder.NewGame(new TeamPreset("t", "Prueba",
                new[] { new TeamMemberSpec(new Id<Species>("mouse"), 10, fixedIv: 31), new TeamMemberSpec(new Id<Species>("pal"), 8, fixedIv: 31) },
                items: new[] { ("potion", 2), ("revive", 1), ("antidote", 1), ("ether", 1), ("thunder_stone", 1), ("leftovers", 1), ("charcoal", 1), ("bike", 1) }),
                data, new FixedRng());

        [Test]
        public void A_potion_heals_and_is_spent_only_when_it_does_something()
        {
            var data = Data(); var save = NewSave(data);
            var mon = save.Party.Members[0];

            var nothing = FieldActions.UseItem(data, save, 0, "potion");
            Assert.IsFalse(nothing.Done, "con la vida llena no se gasta");
            Assert.AreEqual(2, save.Bag.Count("potion"));

            mon.TakeDamage(15);
            var r = FieldActions.UseItem(data, save, 0, "potion");
            Assert.IsTrue(r.Done, r.ToString());
            Assert.AreEqual(mon.MaxHp, mon.CurrentHp);
            Assert.AreEqual(1, save.Bag.Count("potion"));
        }

        [Test]
        public void Revive_antidote_and_ether_work_outside_battle()
        {
            var data = Data(); var save = NewSave(data);
            var mon = save.Party.Members[1];
            mon.TakeDamage(999);
            Assert.IsTrue(FieldActions.UseItem(data, save, 1, "revive").Done);
            Assert.AreEqual(mon.MaxHp / 2, mon.CurrentHp);

            mon.SetStatus(new StatusId("poison"));
            Assert.IsTrue(FieldActions.UseItem(data, save, 1, "antidote").Done);
            Assert.IsFalse(mon.Status.HasValue);

            mon.SetCurrentPp(new[] { 3 });
            Assert.IsTrue(FieldActions.UseItem(data, save, 1, "ether").Done);
            Assert.AreEqual(13, mon.CurrentPp[0]);
            Assert.AreEqual(0, save.Bag.Count("ether"));
        }

        [Test]
        public void A_stone_evolves_at_once_and_asks_for_the_move_it_cannot_fit()
        {
            var data = Data(); var save = NewSave(data);
            var mon = save.Party.Members[0];
            int hpBefore = mon.MaxHp;

            var r = FieldActions.UseItem(data, save, 0, "thunder_stone");
            Assert.IsTrue(r.Done, r.ToString());
            Assert.AreEqual("rat_king", mon.SpeciesId.Value);
            Assert.IsTrue(r.Evolved.HasValue);
            Assert.Greater(mon.MaxHp, hpBefore, "las stats se recalculan");
            Assert.AreEqual(0, save.Bag.Count("thunder_stone"));
            // Ya sabe 4: "thunder" (nivel 10) queda pendiente de preguntar.
            Assert.AreEqual("thunder", r.PendingMoves.Single().Value);

            var learn = FieldActions.LearnMove(data, save, 0, r.PendingMoves[0], 1);
            Assert.IsTrue(learn.Done);
            Assert.AreEqual("thunder", mon.Moves[1].Value, "olvidó el del hueco 2");
            Assert.AreEqual(4, mon.Moves.Count);
        }

        [Test]
        public void A_stone_that_does_not_apply_is_not_spent()
        {
            var data = Data(); var save = NewSave(data);
            var r = FieldActions.UseItem(data, save, 1, "thunder_stone");
            Assert.IsFalse(r.Done);
            Assert.AreEqual(1, save.Bag.Count("thunder_stone"));
        }

        [Test]
        public void Give_swap_and_take_held_items_through_the_bag()
        {
            var data = Data(); var save = NewSave(data);
            var mon = save.Party.Members[0];

            Assert.IsTrue(FieldActions.GiveItem(data, save, 0, "leftovers").Done);
            Assert.AreEqual("leftovers", mon.HeldItem);
            Assert.AreEqual(0, save.Bag.Count("leftovers"));

            var swap = FieldActions.GiveItem(data, save, 0, "charcoal");
            Assert.IsTrue(swap.Done);
            Assert.AreEqual("charcoal", mon.HeldItem);
            Assert.AreEqual(1, save.Bag.Count("leftovers"), "el que llevaba vuelve a la mochila");

            Assert.IsTrue(FieldActions.TakeItem(data, save, 0).Done);
            Assert.IsNull(mon.HeldItem);
            Assert.AreEqual(1, save.Bag.Count("charcoal"));
            Assert.IsFalse(FieldActions.TakeItem(data, save, 0).Done, "ya no lleva nada");

            Assert.IsFalse(FieldActions.GiveItem(data, save, 0, "bike").Done, "los objetos clave no se llevan");
            Assert.AreEqual(1, save.Bag.Count("bike"));
        }

        [Test]
        public void Swap_changes_who_goes_first()
        {
            var data = Data(); var save = NewSave(data);
            var second = save.Party.Members[1];
            Assert.IsTrue(FieldActions.Swap(data, save, 0, 1).Done);
            Assert.AreSame(second, save.Party.Members[0]);
            Assert.IsFalse(FieldActions.Swap(data, save, 0, 5).Done);
        }

        [Test]
        public void Summary_shows_stats_ivs_moves_and_experience()
        {
            var data = Data(); var save = NewSave(data);
            var mon = save.Party.Members[0];
            mon.SetCurrentPp(new[] { 5, 20, 20, 20 });
            var s = FieldActions.Summary(data, mon);
            Assert.AreEqual("Ratoncito", s.Name);
            Assert.AreEqual(10, s.Level);
            Assert.AreEqual(6, s.Stats.Count);
            Assert.IsTrue(s.Stats.All(st => st.Iv == 31));
            Assert.AreEqual(4, s.Moves.Count);
            Assert.AreEqual(5, s.Moves[0].Pp);
            Assert.AreEqual(20, s.Moves[0].MaxPp);
            Assert.Greater(s.ExperienceToNext, 0);
            Assert.Greater(s.Lines().Count, 10);
            Assert.AreEqual(2, FieldActions.UsableItems(data, save).Count(u => u.item.Id == "potion" || u.item.Id == "revive"));
            Assert.IsFalse(FieldActions.GivableItems(data, save).Any(g => g.item.Id == "bike"));
        }
    }
}
