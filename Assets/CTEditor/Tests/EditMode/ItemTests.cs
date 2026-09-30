using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;
using B = CTEditor.Battle.Domain.Battle;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Lote de OBJETOS: mochila, usar objetos fuera del combate (curar, revivir, estados, PP, amistad),
    /// evoluciones (nivel, piedra, amistad, intercambio) y objetos EQUIPADOS en combate
    /// (Restos, bayas que se consumen, Carbón que potencia, objetos X que suben etapas).
    /// </summary>
    public class ItemTests
    {
        private static readonly ClassicStatGrowthFormula Growth = new ClassicStatGrowthFormula();

        // ---------------- Contenido de prueba ----------------

        private static readonly ItemDefinition Potion = new ItemDefinition("potion", "Poción", ItemCategory.Medicine,
            usableInBattle: true, usableOutsideBattle: true, healHp: 20);
        private static readonly ItemDefinition MaxPotion = new ItemDefinition("max_potion", "Poción Máxima", ItemCategory.Medicine,
            usableInBattle: true, usableOutsideBattle: true, healPercent: 100);
        private static readonly ItemDefinition Antidote = new ItemDefinition("antidote", "Antídoto", ItemCategory.StatusCure,
            usableInBattle: true, usableOutsideBattle: true, curesStatusId: "poison|toxic");
        private static readonly ItemDefinition Revive = new ItemDefinition("revive", "Revivir", ItemCategory.Revive,
            usableInBattle: true, usableOutsideBattle: true, revives: true, reviveHpPercent: 50);
        private static readonly ItemDefinition Ether = new ItemDefinition("ether", "Éter", ItemCategory.PpRestore,
            usableInBattle: true, usableOutsideBattle: true, restorePp: 10);
        private static readonly ItemDefinition Elixir = new ItemDefinition("elixir", "Elixir", ItemCategory.PpRestore,
            usableInBattle: true, usableOutsideBattle: true, restorePp: 10, restorePpAllMoves: true);
        private static readonly ItemDefinition Candy = new ItemDefinition("soothe", "Caramelo amistoso", ItemCategory.Vitamin,
            usableOutsideBattle: true, friendshipChange: 50);
        private static readonly ItemDefinition FireStone = new ItemDefinition("fire_stone", "Piedra Fuego", ItemCategory.Evolution,
            usableOutsideBattle: true);
        private static readonly ItemDefinition MasterBall = new ItemDefinition("master_ball", "Master Ball", ItemCategory.Ball,
            usableInBattle: true, catchMultiplier: 255);
        private static readonly ItemDefinition XAttack = new ItemDefinition("x_attack", "Ataque X", ItemCategory.BattleBoost,
            usableInBattle: true, battleStatId: "attack", battleStages: 2);
        private static readonly ItemDefinition Leftovers = new ItemDefinition("leftovers", "Restos", ItemCategory.Held,
            consumable: false, heldEndOfTurnHealPercent: 6.25f);
        private static readonly ItemDefinition OranBerry = new ItemDefinition("oran_berry", "Baya Aranja", ItemCategory.Held,
            heldTriggerHpPercent: 50, heldTriggerHealHp: 10);
        private static readonly ItemDefinition Charcoal = new ItemDefinition("charcoal", "Carbón", ItemCategory.Held,
            consumable: false, heldPowerModifiers: new[] { new PowerModifier(2f, new[] { new Condition(ConditionKind.MoveType, text: "fire") }) });

        private static Species Sp(string id, params Evolution[] evos) => new Species(
            new Id<Species>(id), id, new List<Id<ElementType>> { new Id<ElementType>("normal") },
            new StatBlock.Builder().Set(StatId.Hp, 50).Set(StatId.Attack, 50).Set(StatId.Defense, 50)
                .Set(StatId.SpAttack, 50).Set(StatId.SpDefense, 50).Set(StatId.Speed, 50).Build(),
            new List<LearnableMove>(), new List<Evolution>(evos));

        private static MonsterInstance Mon(Species s, int level = 20)
            => MonsterFactory.Create(new Id<MonsterInstance>("m"), s, level, Ruleset.Classic, Growth, fixedIv: 31);

        // ---------------- Mochila ----------------

        [Test]
        public void Bag_counts_adds_removes_and_caps()
        {
            var bag = new Bag(maxPerItem: 10);
            bag.Add("potion", 3);
            Assert.AreEqual(3, bag.Count("potion"));
            Assert.IsTrue(bag.Has("potion", 3));
            Assert.IsFalse(bag.Has("potion", 4));
            Assert.IsTrue(bag.Remove("potion", 2));
            Assert.AreEqual(1, bag.Count("potion"));
            Assert.IsFalse(bag.Remove("potion", 5), "no puedes gastar más de los que tienes");
            bag.Add("potion", 50);
            Assert.AreEqual(10, bag.Count("potion"), "tope por objeto");
            Assert.AreEqual(0, bag.Count("nada"));
        }

        // ---------------- Usar objetos fuera del combate ----------------

        [Test]
        public void Potion_heals_and_is_spent_but_not_on_full_hp()
        {
            var mon = Mon(Sp("a"));
            var bag = new Bag(); bag.Add("potion", 2);

            var r = ItemUse.UseOn(Potion, mon, bag);
            Assert.IsFalse(r.Used, "con la vida llena no se gasta");
            Assert.AreEqual(2, bag.Count("potion"));

            mon.TakeDamage(30);
            int before = mon.CurrentHp;
            r = ItemUse.UseOn(Potion, mon, bag);
            Assert.IsTrue(r.Used);
            Assert.AreEqual(before + 20, mon.CurrentHp);
            Assert.AreEqual(1, bag.Count("potion"));
        }

        [Test]
        public void Max_potion_heals_by_percent()
        {
            var mon = Mon(Sp("a"));
            mon.TakeDamage(mon.MaxHp - 1);
            Assert.IsTrue(ItemUse.UseOn(MaxPotion, mon, null).Used);
            Assert.AreEqual(mon.MaxHp, mon.CurrentHp);
        }

        [Test]
        public void Antidote_cures_only_its_statuses()
        {
            var mon = Mon(Sp("a"));
            mon.SetStatus(new StatusId("burn"));
            Assert.IsFalse(ItemUse.UseOn(Antidote, mon, null).Used, "el Antídoto no cura quemaduras");
            mon.SetStatus(new StatusId("toxic"));
            Assert.IsTrue(ItemUse.UseOn(Antidote, mon, null).Used, "pero sí envenenamiento grave (lista con |)");
            Assert.IsFalse(mon.Status.HasValue);
        }

        [Test]
        public void Revive_only_works_on_fainted_and_uses_its_percent()
        {
            var mon = Mon(Sp("a"));
            Assert.IsFalse(ItemUse.UseOn(Revive, mon, null).Used, "no se gasta en alguien que no está debilitado");
            mon.TakeDamage(mon.MaxHp);
            Assert.IsTrue(mon.IsFainted);
            Assert.IsTrue(ItemUse.UseOn(Revive, mon, null).Used);
            Assert.AreEqual(mon.MaxHp / 2, mon.CurrentHp);

            var fainted = Mon(Sp("b"));
            fainted.TakeDamage(fainted.MaxHp);
            Assert.IsFalse(ItemUse.UseOn(Potion, fainted, null).Used, "una Poción no levanta a un debilitado");
        }

        [Test]
        public void Ether_restores_one_move_and_Elixir_all()
        {
            var sp = Sp("a");
            var mon = MonsterFactory.Create(new Id<MonsterInstance>("m"), sp, 20, Ruleset.Classic, Growth,
                chosenMoves: new[] { new Id<Move>("x"), new Id<Move>("y") });
            mon.SetCurrentPp(new[] { 0, 0 });

            Assert.IsTrue(ItemUse.UseOn(Ether, mon, null, maxPpOf: _ => 20).Used);
            CollectionAssert.AreEqual(new[] { 10, 0 }, mon.CurrentPp.ToArray(), "Éter: solo el primero gastado");

            Assert.IsTrue(ItemUse.UseOn(Elixir, mon, null, maxPpOf: _ => 20).Used);
            CollectionAssert.AreEqual(new[] { 20, 10 }, mon.CurrentPp.ToArray(), "Elixir: todos, sin pasarse del máximo");
        }

        [Test]
        public void Friendship_items_change_friendship()
        {
            var mon = Mon(Sp("a"));
            int before = mon.Friendship;
            Assert.IsTrue(ItemUse.UseOn(Candy, mon, null).Used);
            Assert.AreEqual(before + 50, mon.Friendship);
        }

        // ---------------- Evoluciones ----------------

        [Test]
        public void Level_evolution_triggers_only_on_level_up_at_the_right_level()
        {
            var sp = Sp("charmander", new Evolution(new Id<Species>("charmeleon"), EvolutionMethod.Level, requiredLevel: 16));
            Assert.IsNull(EvolutionRules.Find(Mon(sp, 15), sp, EvolutionTrigger.LevelUp));
            Assert.AreEqual("charmeleon", EvolutionRules.Find(Mon(sp, 16), sp, EvolutionTrigger.LevelUp).Target.Value);
            Assert.IsNull(EvolutionRules.Find(Mon(sp, 16), sp, EvolutionTrigger.UseItem, "fire_stone"));
        }

        [Test]
        public void Stone_evolution_needs_the_right_item_and_spends_it()
        {
            var sp = Sp("vulpix", new Evolution(new Id<Species>("ninetales"), EvolutionMethod.Item, itemId: "fire_stone"));
            var mon = Mon(sp);
            var bag = new Bag(); bag.Add("fire_stone");

            Assert.IsNull(EvolutionRules.Find(mon, sp, EvolutionTrigger.UseItem, "water_stone"));
            Assert.IsTrue(EvolutionRules.ItemCanEvolve(sp, "fire_stone"));

            var r = ItemUse.UseOn(FireStone, mon, bag, sp);
            Assert.IsTrue(r.Used);
            Assert.AreEqual("ninetales", r.Evolution.Target.Value);
            Assert.AreEqual(0, bag.Count("fire_stone"));

            var nothing = ItemUse.UseOn(FireStone, Mon(Sp("pidgey")), null, Sp("pidgey"));
            Assert.IsFalse(nothing.Used, "una piedra sobre quien no evoluciona con ella no se gasta");
        }

        [Test]
        public void Friendship_evolution_needs_enough_friendship()
        {
            var sp = Sp("pichu", new Evolution(new Id<Species>("pikachu"), EvolutionMethod.Friendship, minFriendship: 220));
            var mon = Mon(sp);
            mon.SetFriendship(219);
            Assert.IsNull(EvolutionRules.Find(mon, sp, EvolutionTrigger.LevelUp));
            mon.SetFriendship(220);
            Assert.IsNotNull(EvolutionRules.Find(mon, sp, EvolutionTrigger.LevelUp));
        }

        [Test]
        public void Trade_evolution_can_require_a_held_item()
        {
            var plain = Sp("kadabra", new Evolution(new Id<Species>("alakazam"), EvolutionMethod.Trade));
            Assert.IsNotNull(EvolutionRules.Find(Mon(plain), plain, EvolutionTrigger.Trade));
            Assert.IsNull(EvolutionRules.Find(Mon(plain), plain, EvolutionTrigger.LevelUp));

            var withItem = Sp("onix", new Evolution(new Id<Species>("steelix"), EvolutionMethod.Trade, itemId: "metal_coat"));
            var mon = Mon(withItem);
            Assert.IsNull(EvolutionRules.Find(mon, withItem, EvolutionTrigger.Trade));
            mon.SetHeldItem("metal_coat");
            Assert.IsNotNull(EvolutionRules.Find(mon, withItem, EvolutionTrigger.Trade));
        }

        [Test]
        public void Evolve_changes_species_and_recomputes_stats_keeping_damage()
        {
            var mon = Mon(Sp("small"), 30);
            mon.TakeDamage(10);
            int lost = mon.MaxHp - mon.CurrentHp;
            int oldAtk = mon.Stats.Of(StatId.Attack);
            var big = new StatBlock.Builder().Set(StatId.Hp, 100).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, 100).Build();

            mon.Evolve(new Id<Species>("big"), big, Growth);

            Assert.AreEqual("big", mon.SpeciesId.Value);
            Assert.Greater(mon.Stats.Of(StatId.Attack), oldAtk);
            Assert.AreEqual(lost, mon.MaxHp - mon.CurrentHp, "conserva el daño recibido");
        }

        // ---------------- En combate ----------------

        private static readonly Move[] Moves =
        {
            new Move(new Id<Move>("wait"), "wait", new Id<ElementType>("normal"), MoveCategory.Status, 0, null, 30, 0, MoveTarget.Self, new MoveEffect[0]),
            new Move(new Id<Move>("tackle"), "tackle", new Id<ElementType>("normal"), MoveCategory.Physical, 40, null, 30, 0, MoveTarget.SingleEnemy, new MoveEffect[0]),
            new Move(new Id<Move>("ember"), "ember", new Id<ElementType>("fire"), MoveCategory.Special, 40, null, 30, 0, MoveTarget.SingleEnemy, new MoveEffect[0]),
        };

        private static BattleParticipant P(string id, string move = "wait", int hp = 200, int maxHp = 200, int speed = 50, string held = null) =>
            new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, maxHp).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>("normal") }, new List<Id<Move>> { new Id<Move>(move) },
                heldItem: held);

        private static TurnResolver Resolver() => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value), new TypeChart.Builder().Build(),
            new ClassicDamageFormula(), new SeededRng(3), new InMemoryCatalog<StatusConditionDefinition>(new StatusConditionDefinition[0], s => s.Id.Value),
            rules: new BattleRules(critDenominators: new[] { 0 }),
            items: new InMemoryCatalog<ItemDefinition>(new[] { Leftovers, OranBerry, Charcoal }, i => i.Id));

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));

        [Test]
        public void Leftovers_heal_at_end_of_turn_and_stay()
        {
            var b = new B(P("a", hp: 100, held: "leftovers"), P("b"));
            var ev = Resolver().ResolveTurn(b, Use("wait"), Use("wait"));
            Assert.AreEqual(112, b.Player.CurrentHp, "6,25% de 200 = 12");
            Assert.IsTrue(ev.OfType<HeldItemActivatedEvent>().Any());
            Assert.AreEqual("leftovers", b.Player.HeldItem, "no se consume");
        }

        [Test]
        public void Oran_berry_triggers_at_half_hp_and_is_consumed()
        {
            var b = new B(P("a", hp: 90, held: "oran_berry"), P("b"));
            var ev = Resolver().ResolveTurn(b, Use("wait"), Use("wait"));
            Assert.AreEqual(100, b.Player.CurrentHp);
            Assert.IsNull(b.Player.HeldItem, "la baya se consume");
            Assert.IsTrue(ev.OfType<HeldItemActivatedEvent>().Any());

            var healthy = new B(P("a", hp: 150, held: "oran_berry"), P("b"));
            Resolver().ResolveTurn(healthy, Use("wait"), Use("wait"));
            Assert.AreEqual("oran_berry", healthy.Player.HeldItem, "por encima del 50% no se activa");
        }

        [Test]
        public void Charcoal_boosts_only_its_type()
        {
            int Damage(string move, string held)
            {
                var b = new B(P("a", move, speed: 99, held: held), P("b", hp: 999, maxHp: 999));
                Resolver().ResolveTurn(b, Use(move), Use("wait"));
                return 999 - b.Enemy.CurrentHp;
            }
            Assert.Greater(Damage("ember", "charcoal"), (int)(Damage("ember", null) * 1.5f), "Fuego ×2 con Carbón");
            Assert.AreEqual(Damage("tackle", null), Damage("tackle", "charcoal"), "Normal no cambia");
        }

        [Test]
        public void X_items_raise_stat_stages_in_battle()
        {
            var b = new B(P("a"), P("b"));
            Resolver().ResolveTurn(b, new UseItemAction(b.Player.Id, BattleItemEffect.From(XAttack)), Use("wait"));
            Assert.AreEqual(2, b.Player.GetStage(StatId.Attack));
        }

        [Test]
        public void Battle_item_effect_copies_the_item_definition()
        {
            var fx = BattleItemEffect.From(Antidote);
            Assert.IsTrue(fx.CuresStatus);
            Assert.AreEqual("poison|toxic", fx.CuresStatusId);
            var rev = BattleItemEffect.From(Revive);
            Assert.IsTrue(rev.Revives);
            Assert.AreEqual(50f, rev.ReviveHpPercent);
            Assert.IsTrue(MasterBall.IsBall);
            Assert.IsFalse(Potion.IsBall);
        }
    }
}
