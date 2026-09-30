using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Encounters;
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
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// La PARTIDA y la SESIÓN DE COMBATE con reglas reales: equipos que persisten, subida de nivel en
    /// mitad del combate, aprender movimientos, captura con sacudidas (al equipo o al PC), entrenadores
    /// (no se huye ni se captura, premio en dinero, sacan al siguiente), derrota (pierde dinero y se
    /// cura), evoluciones al terminar y huida clásica por velocidad.
    /// </summary>
    public class AdventureTests
    {
        // ---------------- Contenido de prueba ----------------

        private static Move Mv(string id, int power, MoveCategory cat = MoveCategory.Physical, int pp = 30, string type = "normal")
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), cat, power, null, pp, 0, cat == MoveCategory.Status ? MoveTarget.Self : MoveTarget.SingleEnemy, new MoveEffect[0]);

        private static readonly Move[] Moves =
        {
            Mv("tackle", 40), Mv("scratch", 40), Mv("growl", 0, MoveCategory.Status), Mv("tail_whip", 0, MoveCategory.Status),
            Mv("ember", 40, MoveCategory.Special, 25, "fire"), Mv("nuke", 250, MoveCategory.Special), Mv("wait", 0, MoveCategory.Status),
            Mv("flame_wheel", 60, MoveCategory.Physical, 25, "fire"), Mv("struggle", 50, pp: 1),
        };

        private static StatBlock Stats(int all) => new StatBlock.Builder().Set(StatId.Hp, all).Set(StatId.Attack, all).Set(StatId.Defense, all)
            .Set(StatId.SpAttack, all).Set(StatId.SpDefense, all).Set(StatId.Speed, all).Build();

        private static Species Sp(string id, int baseStat, LearnableMove[] learnset, Evolution[] evos = null, int exp = 64, int catchRate = 45)
            => new Species(new Id<Species>(id), id, new[] { new Id<ElementType>("normal") }, Stats(baseStat), learnset,
                evos ?? new Evolution[0], baseExpYield: exp, catchRate: catchRate);

        private static LearnableMove L(int level, string move) => new LearnableMove(new Id<Move>(move), level);

        private static readonly Species[] AllSpecies =
        {
            // Pequeño: aprende "ember" al 6 y evoluciona al 7.
            Sp("cub", 50, new[] { L(1, "tackle"), L(1, "growl"), L(6, "ember"), L(7, "flame_wheel") },
               new[] { new Evolution(new Id<Species>("bear"), 7) }),
            Sp("bear", 80, new[] { L(1, "tackle"), L(7, "flame_wheel") }),
            // Luchador fuerte para ganar seguro.
            Sp("titan", 150, new[] { L(1, "nuke"), L(1, "wait") }),
            // Rival débil que da mucha experiencia.
            Sp("rat", 20, new[] { L(1, "scratch") }, exp: 250, catchRate: 255),
            Sp("rock", 200, new[] { L(1, "wait") }, catchRate: 3),
            // Con 4 movimientos ya al nivel 5 y uno nuevo al 6 (para la pregunta de olvidar).
            Sp("full", 50, new[] { L(1, "tackle"), L(2, "growl"), L(3, "tail_whip"), L(4, "scratch"), L(6, "ember") }),
        };

        private static readonly ItemDefinition[] Items =
        {
            new ItemDefinition("potion", "Poción", ItemCategory.Medicine, usableInBattle: true, usableOutsideBattle: true, healHp: 20),
            new ItemDefinition("poke_ball", "Poké Ball", ItemCategory.Ball, usableInBattle: true, catchMultiplier: 1f),
            new ItemDefinition("master_ball", "Master Ball", ItemCategory.Ball, usableInBattle: true, catchMultiplier: 255f),
        };

        private static readonly StatusConditionDefinition[] Statuses =
        {
            new StatusConditionDefinition(new StatusId("sleep"), "Dormido", new Percentage(0), new Percentage(100), catchMultiplier: 2.5f),
        };

        private static GameData Data(AdventureRules rules = null) => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value),
            new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Build(),
            Ruleset.Classic.With(adventure: rules),
            statuses: new MemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value),
            items: new MemoryCatalog<ItemDefinition>(Items, i => i.Id));

        private static TeamMemberSpec M(string species, int level, params string[] moves)
            => new TeamMemberSpec(new Id<Species>(species), level, moves.Select(m => new Id<Move>(m)).ToList(), fixedIv: 31);

        private static PlayerSave NewSave(GameData data, params TeamMemberSpec[] members)
            => TeamBuilder.NewGame(new TeamPreset("test", "Prueba", members,
                items: new[] { ("potion", 3), ("poke_ball", 5), ("master_ball", 1) }), data, new FixedRng(0.5f));

        private static MonsterInstance Wild(GameData data, PlayerSave save, string species, int level)
            => TeamBuilder.Build(new TeamMemberSpec(new Id<Species>(species), level, fixedIv: 0), save.NewMonsterId(), data, new FixedRng(0.5f));

        /// <summary>Azar fijo: NextFloat = f; Next = el mínimo (tiradas "buenas" para el que tira).</summary>
        private sealed class FixedRng : IRng
        {
            private readonly float _f;
            public FixedRng(float f) { _f = f; }
            public int Next(int minInclusive, int maxExclusive) => minInclusive + (int)((maxExclusive - minInclusive) * _f * 0.999f);
            public float NextFloat() => _f;
        }

        private static List<IDomainEvent> Play(BattleSession s, PlayerChoice c)
        {
            var step = s.Submit(c);
            Assert.IsTrue(step.Accepted, step.Message);
            return step.Events.ToList();
        }

        // ---------------- Partida ----------------

        [Test]
        public void New_game_from_a_preset_builds_team_bag_and_money()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 5), M("titan", 50, "nuke"));
            Assert.AreEqual(2, save.Party.Count);
            Assert.AreEqual(3000, save.Money, "dinero inicial de las reglas");
            Assert.AreEqual(3, save.Bag.Count("potion"));
            Assert.AreEqual("nuke", save.Party.Members[1].Moves[0].Value, "respeta los movimientos elegidos");
            CollectionAssert.AreEqual(new[] { "tackle", "growl" }, save.Party.Members[0].Moves.Select(m => m.Value).ToArray(), "vacío = los de su nivel");
        }

        [Test]
        public void The_first_monster_that_can_fight_goes_out()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 5), M("titan", 50, "nuke"));
            save.Party.Members[0].TakeDamage(999);
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 2), new FixedRng(0.5f));
            Assert.AreEqual(save.Party.Members[1].Id.Value, s.Battle.Player.Id.Value);
        }

        [Test]
        public void Cannot_battle_without_usable_monsters_and_box_rules()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 5));
            Assert.IsFalse(save.Deposit(0).IsSuccess, "no puedes dejar el equipo sin nadie que luche");
            save.Party.Members[0].TakeDamage(999);
            Assert.Throws<System.InvalidOperationException>(() => BattleSession.Wild(data, save, Wild(data, save, "rat", 2)));
        }

        // ---------------- Salvajes y captura ----------------

        [Test]
        public void Master_ball_always_catches_and_the_capture_joins_the_team_with_its_damage()
        {
            var data = Data();
            var save = NewSave(data, M("titan", 50, "nuke"));
            var wild = Wild(data, save, "rock", 30);
            var s = BattleSession.Wild(data, save, wild, new FixedRng(0.99f));
            s.Begin();
            var ev = Play(s, PlayerChoice.ThrowBall("master_ball"));

            Assert.IsTrue(ev.OfType<MonsterCapturedEvent>().Any());
            Assert.AreEqual(BattleOutcome.Caught, s.Outcome);
            Assert.AreEqual(SessionPhase.Finished, s.Phase);
            Assert.AreEqual(2, save.Party.Count, "entra al equipo");
            Assert.AreEqual(0, save.Bag.Count("master_ball"), "la bola se gasta");
            Assert.AreEqual(StoredIn.Party, ev.OfType<CaptureStoredEvent>().Single().Where);
        }

        [Test]
        public void With_a_full_team_the_capture_goes_to_the_box()
        {
            var data = Data();
            var save = NewSave(data, M("titan", 50, "nuke"), M("cub", 5), M("cub", 5), M("cub", 5), M("cub", 5), M("cub", 5));
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 3), new FixedRng(0.99f));
            s.Begin();
            var ev = Play(s, PlayerChoice.ThrowBall("master_ball"));
            Assert.AreEqual(StoredIn.Box, ev.OfType<CaptureStoredEvent>().Single().Where);
            Assert.AreEqual(1, save.Box.Count);
        }

        [Test]
        public void Classic_catch_formula_rewards_low_hp_status_and_better_balls()
        {
            double P(int hp, float ball, float status = 1f) => ClassicCatchFormula.Probability(new CatchContext(100, hp, 45, ball, status));
            Assert.Greater(P(10, 1f), P(100, 1f), "menos PS = más fácil");
            Assert.Greater(P(100, 1f, 2.5f), P(100, 1f), "dormido = más fácil");
            Assert.Greater(P(100, 2f), P(100, 1f), "Ultra Ball mejor que Poké Ball");
            Assert.AreEqual(1.0, ClassicCatchFormula.Probability(new CatchContext(100, 100, 3, 255f)), 1e-9, "Master Ball");
            Assert.Greater(ClassicCatchFormula.Probability(new CatchContext(100, 1, 255, 1f)), 0.99, "fácil y herido = casi seguro (99,3% en la fórmula real)");

            // Las sacudidas: con azar malo se escapa a la primera.
            var failed = new ClassicCatchFormula().Attempt(new CatchContext(100, 100, 3, 1f), new FixedRng(0.99f));
            Assert.IsFalse(failed.Caught);
            Assert.AreEqual(0, failed.Shakes);
        }

        // ---------------- Huir ----------------

        [Test]
        public void Faster_monsters_always_escape_slower_ones_may_fail_and_retry_is_easier()
        {
            var data = Data();
            var save = NewSave(data, M("titan", 50, "nuke"));
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 2), new FixedRng(0.99f));
            s.Begin();
            Assert.AreEqual(1.0, s.FleeChance, 1e-9, "más rápido: siempre escapa");
            Play(s, PlayerChoice.Run());
            Assert.AreEqual(BattleOutcome.Fled, s.Outcome);

            var slow = NewSave(data, M("cub", 3));
            var s2 = BattleSession.Wild(data, slow, Wild(data, slow, "rock", 60), new FixedRng(0.99f));
            s2.Begin();
            double first = s2.FleeChance;
            Assert.Less(first, 1.0);
            var ev = Play(s2, PlayerChoice.Run());
            Assert.IsTrue(ev.OfType<FleeFailedEvent>().Any(), "con mala suerte, no escapa");
            Assert.Greater(s2.FleeChance, first, "cada intento facilita el siguiente");
        }

        [Test]
        public void Flee_always_works_when_the_rule_says_so()
        {
            var data = Data(new AdventureRules(fleeAlwaysWorks: true));
            var save = NewSave(data, M("cub", 3));
            var s = BattleSession.Wild(data, save, Wild(data, save, "rock", 60), new FixedRng(0.99f));
            s.Begin();
            Play(s, PlayerChoice.Run());
            Assert.AreEqual(BattleOutcome.Fled, s.Outcome);
        }

        // ---------------- Entrenadores ----------------

        private static TrainerDefinition Youngster() => new TrainerDefinition("joven", "Joaquín",
            new[] { M("rat", 2), M("rat", 4) }, "Joven", TrainerAi.Random, baseMoney: 16,
            introLine: "¡Mis ratas son lo más!", defeatLine: "¡Oh, no!", victoryLine: "¡Gané!");

        [Test]
        public void Trainer_battles_forbid_fleeing_and_catching_without_spending_the_turn_or_the_ball()
        {
            var data = Data();
            var save = NewSave(data, M("titan", 50, "nuke"));
            var s = BattleSession.Against(data, save, Youngster(), new FixedRng(0.5f));
            var intro = s.Begin();
            Assert.AreEqual("¡Mis ratas son lo más!", intro.OfType<BattleIntroEvent>().Single().IntroLine);

            var run = s.Submit(PlayerChoice.Run());
            Assert.IsFalse(run.Accepted);
            StringAssert.Contains("entrenador", run.Message);

            var ball = s.Submit(PlayerChoice.ThrowBall("poke_ball"));
            Assert.IsFalse(ball.Accepted);
            Assert.AreEqual(5, save.Bag.Count("poke_ball"), "no se gastó la bola");
            Assert.AreEqual(SessionPhase.ChooseAction, s.Phase, "sigue siendo su turno");
        }

        [Test]
        public void Trainer_sends_the_next_monster_and_pays_the_prize_when_defeated()
        {
            var data = Data();
            var save = NewSave(data, M("titan", 50, "nuke"));
            var s = BattleSession.Against(data, save, Youngster(), new FixedRng(0.5f));
            s.Begin();

            var ev = Play(s, PlayerChoice.Fight(0));
            Assert.IsTrue(ev.OfType<SentOutEvent>().Any(e => !e.IsPlayer), "el entrenador saca al siguiente");
            Assert.AreEqual(SessionPhase.ChooseAction, s.Phase);

            ev = Play(s, PlayerChoice.Fight(0));
            Assert.AreEqual(BattleOutcome.PlayerWon, s.Outcome);
            Assert.AreEqual(16 * 4, ev.OfType<MoneyWonEvent>().Single().Amount, "premio = base × nivel del último");
            Assert.AreEqual(3000 + 64, save.Money);
            Assert.IsTrue(save.HasDefeated("joven"));
            Assert.IsTrue(ev.OfType<TrainerSaysEvent>().Any(t => t.Line == "¡Oh, no!"));
        }

        [Test]
        public void Losing_costs_half_the_money_and_heals_the_team()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 2, "wait"));
            var bully = new TrainerDefinition("matón", "Matón", new[] { M("titan", 60, "nuke") }, ai: TrainerAi.Smart, baseMoney: 50,
                victoryLine: "¡Demasiado fácil!");
            var s = BattleSession.Against(data, save, bully, new FixedRng(0.5f));
            s.Begin();
            var ev = Play(s, PlayerChoice.Fight(0));

            Assert.AreEqual(BattleOutcome.PlayerLost, s.Outcome);
            var blackout = ev.OfType<BlackoutEvent>().Single();
            Assert.AreEqual(1500, blackout.MoneyLost);
            Assert.AreEqual(1500, save.Money);
            Assert.IsFalse(save.Party.Members[0].IsFainted, "vuelve curado del Centro");
            Assert.IsTrue(ev.OfType<TrainerSaysEvent>().Any(t => t.Line == "¡Demasiado fácil!"));
        }

        // ---------------- Experiencia, niveles, movimientos y evolución ----------------

        [Test]
        public void Level_ups_happen_mid_battle_and_new_moves_are_learned_in_free_slots()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 5, "nuke"));   // "nuke" para ganar seguro; 1 hueco ocupado
            var mon = save.Party.Members[0];
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 10), new FixedRng(0.5f));
            s.Begin();
            int statsBefore = s.Battle.Player.Stats.Of(StatId.Attack);

            var ev = Play(s, PlayerChoice.Fight(0));
            var ups = ev.OfType<LevelUpEvent>().ToList();
            Assert.Greater(ups.Count, 0, "subió de nivel");
            Assert.AreEqual(mon.Level.Value, s.Battle.Player.Level, "el nivel sube DENTRO del combate");
            Assert.Greater(s.Battle.Player.Stats.Of(StatId.Attack), statsBefore, "y sus estadísticas también");
            Assert.IsTrue(ev.OfType<MoveLearnedEvent>().Any(m => m.Move.Value == "ember"), "aprende Ascuas al 6");
            Assert.IsTrue(mon.KnowsMove(new Id<Move>("ember")));
        }

        [Test]
        public void With_four_moves_it_asks_which_one_to_forget()
        {
            var data = Data();
            var save = NewSave(data, M("full", 5), M("titan", 50, "nuke"));
            var mon = save.Party.Members[0];
            Assert.AreEqual(4, mon.Moves.Count);
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 12), new FixedRng(0.5f));
            s.Begin();
            // El "full" es débil: el que gana es el titan... así que que "full" luche primero y cambie.
            Play(s, PlayerChoice.Switch(1));
            var ev = Play(s, PlayerChoice.Fight(0));
            // Ambos lucharon (full estuvo en el campo): la XP se reparte y "full" sube al 6.
            Assert.AreEqual(SessionPhase.LearnMove, s.Phase);
            Assert.AreEqual("ember", ev.OfType<MoveLearnPromptEvent>().Single().Move.Value);

            var step = s.AnswerLearnMove(1); // olvida Gruñido
            Assert.IsTrue(step.Accepted);
            Assert.AreEqual("ember", mon.Moves[1].Value);
            Assert.AreEqual("growl", step.Events.OfType<MoveLearnedEvent>().Single().Forgotten.Value.Value);
        }

        [Test]
        public void Evolution_is_asked_after_the_battle_and_can_be_cancelled()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 6, "nuke"));
            var mon = save.Party.Members[0];
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 12), new FixedRng(0.5f));
            s.Begin();
            var ev = Play(s, PlayerChoice.Fight(0));
            while (s.Phase == SessionPhase.LearnMove) ev.AddRange(s.AnswerLearnMove(-1).Events);

            Assert.AreEqual(SessionPhase.Evolution, s.Phase);
            Assert.AreEqual("bear", s.PendingEvolution.Value.target.Id.Value);
            var step = s.AnswerEvolution(false);
            Assert.IsFalse(step.Events.OfType<EvolutionResultEvent>().Single().Evolved);
            Assert.AreEqual("cub", mon.SpeciesId.Value, "cancelada: sigue igual");
            Assert.AreEqual(SessionPhase.Finished, s.Phase);

            // En el siguiente combate, vuelve a intentarlo (sube otro nivel) y esta vez se acepta.
            var s2 = BattleSession.Wild(data, save, Wild(data, save, "rat", 20), new FixedRng(0.5f));
            s2.Begin();
            Play(s2, PlayerChoice.Fight(0));
            while (s2.Phase == SessionPhase.LearnMove) s2.AnswerLearnMove(-1);
            Assert.AreEqual(SessionPhase.Evolution, s2.Phase);
            s2.AnswerEvolution(true);
            Assert.AreEqual("bear", mon.SpeciesId.Value);
        }

        [Test]
        public void The_team_keeps_hp_pp_and_status_after_the_battle()
        {
            var data = Data();
            var save = NewSave(data, M("titan", 50, "nuke"));
            var mon = save.Party.Members[0];
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 2), new FixedRng(0.5f));
            s.Begin();
            Play(s, PlayerChoice.Fight(0));
            Assert.AreEqual(BattleOutcome.PlayerWon, s.Outcome);
            Assert.AreEqual(29, mon.CurrentPp[0], "gastó 1 PP y lo conserva");
        }

        [Test]
        public void A_potion_on_full_hp_is_refused_and_not_spent()
        {
            var data = Data();
            var save = NewSave(data, M("titan", 50, "nuke"));
            var s = BattleSession.Wild(data, save, Wild(data, save, "rat", 2), new FixedRng(0.5f));
            s.Begin();
            var step = s.Submit(PlayerChoice.UseItem("potion", 0));
            Assert.IsFalse(step.Accepted);
            Assert.AreEqual("No tendría ningún efecto.", step.Message);
            Assert.AreEqual(3, save.Bag.Count("potion"));
        }

        [Test]
        public void When_the_active_faints_the_player_chooses_the_replacement()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 2, "wait"), M("titan", 50, "nuke"));
            var bully = new TrainerDefinition("matón", "Matón", new[] { M("titan", 30, "nuke"), M("rat", 3) }, ai: TrainerAi.Smart);
            var s = BattleSession.Against(data, save, bully, new FixedRng(0.5f));
            s.Begin();
            Play(s, PlayerChoice.Fight(0));
            Assert.AreEqual(SessionPhase.ChooseReplacement, s.Phase);
            Assert.IsFalse(s.ChooseReplacement(0).Accepted, "no puede salir un debilitado");
            var step = s.ChooseReplacement(1);
            Assert.IsTrue(step.Accepted);
            Assert.IsTrue(step.Events.OfType<SentOutEvent>().Any(e => e.IsPlayer));
            Assert.AreEqual(SessionPhase.ChooseAction, s.Phase);
        }

        [Test]
        public void Exp_share_rule_gives_experience_to_the_bench()
        {
            var off = Data();
            var save = NewSave(off, M("titan", 50, "nuke"), M("cub", 5));
            var s = BattleSession.Wild(off, save, Wild(off, save, "rat", 10), new FixedRng(0.5f));
            s.Begin();
            var ev = Play(s, PlayerChoice.Fight(0));
            Assert.AreEqual(1, ev.OfType<ExperienceAwardedEvent>().Count(), "clásico: solo quien luchó");

            var on = Data(new AdventureRules(expShareAll: true));
            var save2 = NewSave(on, M("titan", 50, "nuke"), M("cub", 5));
            var s2 = BattleSession.Wild(on, save2, Wild(on, save2, "rat", 10), new FixedRng(0.5f));
            s2.Begin();
            ev = Play(s2, PlayerChoice.Fight(0));
            Assert.AreEqual(2, ev.OfType<ExperienceAwardedEvent>().Count(), "Repartir Experiencia: también el de la banca");
            Assert.Greater(save2.Party.Members[1].Level.Value, 5);
        }

        [Test]
        public void Wild_zone_picks_species_by_weight_and_level_in_range()
        {
            var data = Data();
            var save = NewSave(data, M("cub", 5));
            var zone = new EncounterZone("ruta1", "Ruta 1", new[]
            {
                new EncounterEntry(new Id<Species>("rat"), 2, 4, 90),
                new EncounterEntry(new Id<Species>("rock"), 10, 10, 10),
            });
            var low = TeamBuilder.Wild(zone, save.NewMonsterId(), data, new FixedRng(0.1f));
            Assert.AreEqual("rat", low.SpeciesId.Value);
            Assert.IsTrue(low.Level.Value >= 2 && low.Level.Value <= 4, "nivel en el rango");
            var high = TeamBuilder.Wild(zone, save.NewMonsterId(), data, new FixedRng(0.95f));
            Assert.AreEqual("rock", high.SpeciesId.Value);
        }
    }
}
