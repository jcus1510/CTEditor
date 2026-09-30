using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
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
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Events;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// IA DE ENTRENADORES: niveles Novato / Listo / Experto, mochila del entrenador (curar, quitar estados,
    /// mejoras), el interruptor de objetos, remates seguros, evitar inmunidades y cambiar de monstruo.
    /// </summary>
    public class TrainerAiTests
    {
        private static readonly Percentage Always = new Percentage(100);

        private static Move Hit(string id, string type, int power, float? acc = null)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), MoveCategory.Physical, power,
                acc.HasValue ? new Percentage(acc.Value) : (Percentage?)null, 30, 0, MoveTarget.SingleEnemy, new MoveEffect[0]);

        private static readonly Move[] Moves =
        {
            Hit("tackle", "normal", 40),
            Hit("blizzard", "normal", 120, 50f),
            Hit("ember", "fire", 40),
            Hit("flame", "fire", 90),
            Hit("leaf", "grass", 40),
            Hit("splash_hit", "water", 90),
            new Move(new Id<Move>("fang"), "fang", new Id<ElementType>("normal"), MoveCategory.Physical, 0, null, 30, 0,
                MoveTarget.SingleEnemy, new MoveEffect[0], fixedDamage: FixedDamageKind.HalfTargetHp),
            new Move(new Id<Move>("spore"), "spore", new Id<ElementType>("normal"), MoveCategory.Status, 0, null, 30, 0,
                MoveTarget.SingleEnemy, new[] { new MoveEffect(Always, MoveEffectKind.InflictStatus, EffectTarget.Opponent, new StatusId("sleep")) }),
            new Move(new Id<Move>("wait"), "wait", new Id<ElementType>("normal"), MoveCategory.Status, 0, null, 30, 0, MoveTarget.Self, new MoveEffect[0]),
            Hit("struggle", "normal", 50),
        };

        private static StatBlock Stats(int all, int speed) => new StatBlock.Builder().Set(StatId.Hp, all).Set(StatId.Attack, all).Set(StatId.Defense, all)
            .Set(StatId.SpAttack, all).Set(StatId.SpDefense, all).Set(StatId.Speed, speed).Build();

        private static Species Sp(string id, string type, int stat, int speed, params string[] moves)
            => new Species(new Id<Species>(id), id, new[] { new Id<ElementType>(type) }, Stats(stat, speed),
                moves.Select(m => new LearnableMove(new Id<Move>(m), 1)).ToList(), new Evolution[0]);

        private static readonly Species[] AllSpecies =
        {
            Sp("hero", "normal", 100, 200, "fang"),
            Sp("ghosty", "ghost", 100, 200, "wait"),
            Sp("firebird", "fire", 110, 200, "flame"),
            Sp("dummy", "normal", 60, 10, "wait"),
            Sp("leafy", "grass", 60, 10, "leaf"),
            Sp("wet", "water", 60, 10, "splash_hit"),
        };

        private static readonly ItemDefinition[] Items =
        {
            new ItemDefinition("potion", "Poción", ItemCategory.Medicine, usableInBattle: true, usableOutsideBattle: true, healHp: 20),
            new ItemDefinition("full_heal", "Cura Total", ItemCategory.StatusCure, usableInBattle: true, curesAllStatus: true),
            new ItemDefinition("x_attack", "Ataque X", ItemCategory.BattleBoost, usableInBattle: true, battleStatId: "attack", battleStages: 2),
        };

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value),
            new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder()
                .Set(new Id<ElementType>("normal"), new Id<ElementType>("ghost"), 0f)
                .Set(new Id<ElementType>("fire"), new Id<ElementType>("grass"), 2f)
                .Set(new Id<ElementType>("grass"), new Id<ElementType>("fire"), 0.5f)
                .Set(new Id<ElementType>("water"), new Id<ElementType>("fire"), 2f)
                .Set(new Id<ElementType>("fire"), new Id<ElementType>("water"), 0.5f)
                .Build(),
            Ruleset.Classic,
            statuses: new MemoryCatalog<StatusConditionDefinition>(new[]
                { new StatusConditionDefinition(new StatusId("sleep"), "Dormido", new Percentage(0), new Percentage(100), durationTurns: 5) }, s => s.Id.Value),
            items: new MemoryCatalog<ItemDefinition>(Items, i => i.Id));

        private sealed class FixedRng : IRng
        {
            private readonly float _f;
            public FixedRng(float f) { _f = f; }
            public int Next(int minInclusive, int maxExclusive) => minInclusive + (int)((maxExclusive - minInclusive) * _f * 0.999f);
            public float NextFloat() => _f;
        }

        private static TeamMemberSpec M(string species, int level, params string[] moves)
            => new TeamMemberSpec(new Id<Species>(species), level, moves.Select(m => new Id<Move>(m)).ToList(), fixedIv: 31);

        private static BattleSession Fight(GameData data, TeamMemberSpec player, TrainerAi ai, TrainerAiSettings settings,
            float rng = 0.5f, params TeamMemberSpec[] enemy)
        {
            var save = TeamBuilder.NewGame(new TeamPreset("t", "Prueba", new[] { player }), data, new FixedRng(0.5f));
            var trainer = new TrainerDefinition("rival", "Rival", enemy, "Entrenador", ai, 20, aiSettings: settings);
            var s = BattleSession.Against(data, save, trainer, new FixedRng(rng));
            s.Begin();
            return s;
        }

        private static List<IDomainEvent> Play(BattleSession s, int move = 0)
        {
            var step = s.Submit(PlayerChoice.Fight(move));
            Assert.IsTrue(step.Accepted, step.Message);
            return step.Events.ToList();
        }

        private static bool EnemyUsedItem(List<IDomainEvent> ev, string item)
            => ev.OfType<BagItemUsedEvent>().Any(e => !e.ByPlayer && e.ItemId == item);

        // ---------------- Objetos ----------------

        [Test]
        public void A_smart_trainer_heals_when_low_and_spends_its_own_bag()
        {
            var data = Data();
            var settings = new TrainerAiSettings(true, new[] { ("potion", 1) }, healBelowPercent: 60);
            var s = Fight(data, M("hero", 50, "fang"), TrainerAi.Smart, settings, 0.5f, M("dummy", 20, "wait"));

            var t1 = Play(s);                        // a la mitad de PS
            Assert.IsFalse(EnemyUsedItem(t1, "potion"), "sano: no se cura");
            var t2 = Play(s);
            Assert.IsTrue(EnemyUsedItem(t2, "potion"), "con 50 % (≤ 60) se cura");
            Assert.AreEqual(0, s.TrainerBag.Count, "gastó su única Poción");
            var t3 = Play(s);
            Assert.IsFalse(t3.OfType<BagItemUsedEvent>().Any(), "ya no le quedan");
            Assert.AreEqual(1, settings.Items.Single().quantity, "la ficha del entrenador no pierde nada");
        }

        [Test]
        public void With_items_switched_off_it_never_uses_them()
        {
            var data = Data();
            var settings = new TrainerAiSettings(false, new[] { ("potion", 3) }, healBelowPercent: 100);
            var s = Fight(data, M("hero", 50, "fang"), TrainerAi.Smart, settings, 0.5f, M("dummy", 20, "wait"));
            for (int i = 0; i < 3; i++) Assert.IsFalse(Play(s).OfType<BagItemUsedEvent>().Any());
        }

        [Test]
        public void It_cures_a_status_with_the_right_item()
        {
            var data = Data();
            var settings = new TrainerAiSettings(true, new[] { ("full_heal", 1) });
            var s = Fight(data, M("hero", 50, "spore", "wait"), TrainerAi.Smart, settings, 0.5f, M("dummy", 20, "wait"));
            Play(s, 0);                               // lo duerme
            Assert.IsTrue(s.Battle.Enemy.Status.HasValue);
            var t2 = Play(s, 1);                      // (esperamos: si volviéramos a dormirlo, se dormiría otra vez)
            Assert.IsTrue(EnemyUsedItem(t2, "full_heal"));
            Assert.IsFalse(s.Battle.Enemy.Status.HasValue, "despierta");
        }

        [Test]
        public void X_items_are_used_once_per_monster_when_healthy()
        {
            var data = Data();
            var settings = new TrainerAiSettings(true, new[] { ("x_attack", 2) });
            var s = Fight(data, M("ghosty", 50, "wait"), TrainerAi.Smart, settings, 0.5f, M("dummy", 20, "wait"));
            Assert.IsTrue(EnemyUsedItem(Play(s), "x_attack"));
            Assert.AreEqual(2, s.Battle.Enemy.GetStage(StatId.Attack));
            Assert.IsFalse(Play(s).OfType<BagItemUsedEvent>().Any(), "no repite la mejora con el mismo monstruo");
        }

        [Test]
        public void A_rookie_sometimes_forgets_its_items()
        {
            var data = Data();
            var settings = new TrainerAiSettings(true, new[] { ("x_attack", 1) });
            var forgets = Fight(data, M("ghosty", 50, "wait"), TrainerAi.Random, settings, 0.5f, M("dummy", 20, "wait"));
            Assert.IsFalse(Play(forgets).OfType<BagItemUsedEvent>().Any(), "con la tirada alta se despista");
            var remembers = Fight(data, M("ghosty", 50, "wait"), TrainerAi.Random, settings, 0.2f, M("dummy", 20, "wait"));
            Assert.IsTrue(EnemyUsedItem(Play(remembers), "x_attack"), "con la tirada baja se acuerda");
        }

        // ---------------- Experto ----------------

        [Test]
        public void The_expert_goes_for_the_safe_knockout_while_the_smart_one_swings_big()
        {
            var data = Data();
            foreach (var (ai, expected) in new[] { (TrainerAi.Expert, "tackle"), (TrainerAi.Smart, "blizzard") })
            {
                var save = TeamBuilder.NewGame(new TeamPreset("t", "Prueba", new[] { M("hero", 50, "wait") }), data, new FixedRng(0.5f));
                save.Party.Members[0].TakeDamage(save.Party.Members[0].MaxHp - 3); // le quedan 3 PS
                var trainer = new TrainerDefinition("rival", "Rival", new[] { M("dummy", 30, "tackle", "blizzard") }, "", ai, 20);
                // 0,5: sin «fallos al azar» (el Aficionado se equivoca un 20 % de las veces).
                var s = BattleSession.Against(data, save, trainer, new FixedRng(0.5f));
                s.Begin();
                var ev = Play(s);
                var used = ev.OfType<MoveUsedEvent>().First(e => !s.IsPlayerSide(e.Attacker));
                Assert.AreEqual(expected, used.Move.Value, $"IA {ai}");
            }
        }

        [Test]
        public void The_expert_never_uses_a_move_the_target_is_immune_to()
        {
            var data = Data();
            for (int i = 0; i < 3; i++)
            {
                var s = Fight(data, M("ghosty", 50, "wait"), TrainerAi.Expert, TrainerAiSettings.Default, 0.1f * (i + 1),
                    M("dummy", 30, "tackle", "ember"));
                var used = Play(s).OfType<MoveUsedEvent>().First(e => !s.IsPlayerSide(e.Attacker));
                Assert.AreEqual("ember", used.Move.Value, "Placaje no afecta a un Fantasma");
            }
        }

        [Test]
        public void The_expert_switches_to_a_teammate_that_wins_the_matchup()
        {
            var data = Data();
            var s = Fight(data, M("firebird", 40, "flame"), TrainerAi.Expert, TrainerAiSettings.Default, 0.5f,
                M("leafy", 25, "leaf"), M("wet", 25, "splash_hit"));
            var ev = Play(s);
            Assert.AreEqual(1, ev.OfType<MonsterWithdrawnEvent>().Count(e => !s.IsPlayerSide(e.Combatant)), "retira a la planta");
            Assert.AreEqual("wet", s.MonsterOf(s.Battle.Enemy.Id).SpeciesId.Value, "saca al de agua");

            // Con el interruptor apagado se queda y pelea.
            var stay = Fight(data, M("firebird", 40, "flame"), TrainerAi.Expert, new TrainerAiSettings(canSwitch: false), 0.5f,
                M("leafy", 25, "leaf"), M("wet", 25, "splash_hit"));
            Assert.IsFalse(Play(stay).OfType<MonsterWithdrawnEvent>().Any(e => !stay.IsPlayerSide(e.Combatant)));
        }

        [Test]
        public void Smart_trainers_never_switch()
        {
            var data = Data();
            var s = Fight(data, M("firebird", 40, "flame"), TrainerAi.Smart, TrainerAiSettings.Default, 0.5f,
                M("leafy", 25, "leaf"), M("wet", 25, "splash_hit"));
            Assert.IsFalse(Play(s).OfType<MonsterWithdrawnEvent>().Any(e => !s.IsPlayerSide(e.Combatant)));
        }
    }
}
