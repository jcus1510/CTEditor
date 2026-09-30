using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Hazards;
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
    /// TRAMPAS DE CAMPO (Púas, Trampa Rocas, Púas Tóxicas, Red Viscosa) y CAMBIOS FORZADOS (Rugido,
    /// Remolino, Cola Dragón): capas, daño por eficacia, inmunes, absorción, quitar trampas, el combate
    /// salvaje que termina, el relevo al azar contra entrenadores y el relevo que cae por las trampas.
    /// </summary>
    public class HazardTests
    {
        // ---------------- Contenido de prueba ----------------

        private static readonly Percentage Always = new Percentage(100);

        private static Move Status(string id, int priority, params MoveEffect[] effects)
            => new Move(new Id<Move>(id), id, new Id<ElementType>("normal"), MoveCategory.Status, 0, null, 30, priority, MoveTarget.SingleEnemy, effects);

        private static MoveEffect Set(string hazard) => new MoveEffect(Always, MoveEffectKind.SetHazard, EffectTarget.Opponent, hazardId: hazard);

        private static readonly Move[] Moves =
        {
            Status("spikes", 0, Set("spikes")),
            Status("stealth_rock", 0, Set("stealth_rock")),
            Status("toxic_spikes", 0, Set("toxic_spikes")),
            Status("sticky_web", 0, Set("sticky_web")),
            Status("doom_field", 0, Set("doom")),
            Status("rapid_spin", 0, new MoveEffect(Always, MoveEffectKind.ClearHazards, EffectTarget.Self)),
            Status("roar", -6, new MoveEffect(Always, MoveEffectKind.ForceSwitch, EffectTarget.Opponent)),
            // Empujón inventado: fuerza el cambio con prioridad normal (para ver que el que entra no actúa).
            Status("push", 0, new MoveEffect(Always, MoveEffectKind.ForceSwitch, EffectTarget.Opponent)),
            Status("wait", 0),
            new Move(new Id<Move>("scratch"), "scratch", new Id<ElementType>("normal"), MoveCategory.Physical, 40, null, 30, 0, MoveTarget.SingleEnemy, new MoveEffect[0]),
            new Move(new Id<Move>("nuke"), "nuke", new Id<ElementType>("normal"), MoveCategory.Special, 250, null, 30, 0, MoveTarget.SingleEnemy, new MoveEffect[0]),
            new Move(new Id<Move>("struggle"), "struggle", new Id<ElementType>("normal"), MoveCategory.Physical, 50, null, 1, 0, MoveTarget.SingleEnemy, new MoveEffect[0]),
        };

        private static StatBlock Stats(int all, int speed) => new StatBlock.Builder().Set(StatId.Hp, all).Set(StatId.Attack, all).Set(StatId.Defense, all)
            .Set(StatId.SpAttack, all).Set(StatId.SpDefense, all).Set(StatId.Speed, speed).Build();

        private static Species Sp(string id, string[] types, int stat, int speed, params string[] moves)
            => new Species(new Id<Species>(id), id, types.Select(t => new Id<ElementType>(t)).ToList(), Stats(stat, speed),
                moves.Select(m => new LearnableMove(new Id<Move>(m), 1)).ToList(), new Evolution[0], baseExpYield: 64, catchRate: 45);

        private static readonly Species[] AllSpecies =
        {
            // El del jugador: rapidísimo, con todo el repertorio (se eligen por movimientos al crearlo).
            Sp("setter", new[] { "normal" }, 150, 250, "wait"),
            Sp("dummy", new[] { "normal" }, 60, 10, "wait"),
            Sp("dummy2", new[] { "normal" }, 60, 10, "wait"),
            Sp("clawer", new[] { "normal" }, 60, 10, "scratch"),
            Sp("bird", new[] { "flying" }, 60, 10, "wait"),
            Sp("firebird", new[] { "fire", "flying" }, 60, 10, "wait"),
            Sp("snake", new[] { "poison" }, 60, 10, "wait"),
            Sp("spiker", new[] { "normal" }, 60, 10, "spikes"),
        };

        private static readonly HazardDefinition[] Hazards =
        {
            new HazardDefinition("spikes", "Púas", 3, new[] { 12.5f, 16.67f, 25f },
                immuneTypes: new[] { new Id<ElementType>("flying") }),
            new HazardDefinition("stealth_rock", "Trampa Rocas", 1, new[] { 12.5f }, damageScalesWithType: new Id<ElementType>("rock")),
            new HazardDefinition("toxic_spikes", "Púas Tóxicas", 2, statusByLayer: new[] { "poison", "toxic" },
                immuneTypes: new[] { new Id<ElementType>("flying") }, absorbedByTypes: new[] { new Id<ElementType>("poison") }),
            new HazardDefinition("sticky_web", "Red Viscosa", 1, stat: StatId.Speed, stages: -1,
                immuneTypes: new[] { new Id<ElementType>("flying") }),
            // Trampa inventada que debilita al entrar (para probar relevos que caen por las trampas).
            new HazardDefinition("doom", "Perdición", 1, new[] { 100f }),
        };

        private static readonly StatusConditionDefinition[] Statuses =
        {
            new StatusConditionDefinition(new StatusId("poison"), "Envenenado", new Percentage(0), new Percentage(0)),
            new StatusConditionDefinition(new StatusId("toxic"), "Gravemente envenenado", new Percentage(0), new Percentage(0)),
        };

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value),
            new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder()
                .Set(new Id<ElementType>("rock"), new Id<ElementType>("fire"), 2f)
                .Set(new Id<ElementType>("rock"), new Id<ElementType>("flying"), 2f)
                .Build(),
            Ruleset.Classic,
            statuses: new MemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value),
            hazards: new MemoryCatalog<HazardDefinition>(Hazards, h => h.Id));

        private static TeamMemberSpec M(string species, int level, params string[] moves)
            => new TeamMemberSpec(new Id<Species>(species), level, moves.Select(m => new Id<Move>(m)).ToList(), fixedIv: 31);

        /// <summary>El jugador con un solo monstruo y los movimientos pedidos.</summary>
        private static PlayerSave Save(GameData data, params string[] moves)
            => TeamBuilder.NewGame(new TeamPreset("t", "Prueba", new[] { M("setter", 50, moves) }), data, new FixedRng(0.5f));

        private static TrainerDefinition Trainer(params string[] team)
            => new TrainerDefinition("rival", "Rival", team.Select(s => M(s, 20)).ToList(), ai: TrainerAi.Random);

        private sealed class FixedRng : IRng
        {
            private readonly float _f;
            public FixedRng(float f) { _f = f; }
            public int Next(int minInclusive, int maxExclusive) => minInclusive + (int)((maxExclusive - minInclusive) * _f * 0.999f);
            public float NextFloat() => _f;
        }

        private static List<IDomainEvent> Use(BattleSession s, int moveSlot)
        {
            var step = s.Submit(PlayerChoice.Fight(moveSlot));
            Assert.IsTrue(step.Accepted, step.Message);
            return step.Events.ToList();
        }

        private static BattleSession VsTrainer(GameData data, string[] playerMoves, params string[] team)
        {
            var s = BattleSession.Against(data, Save(data, playerMoves), Trainer(team), new FixedRng(0.5f));
            s.Begin();
            return s;
        }

        // ---------------- Capas y daño ----------------

        [Test]
        public void Spikes_stack_up_to_three_layers_and_then_fail()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "spikes", "roar" }, "dummy", "dummy2");
            for (int i = 1; i <= 3; i++)
            {
                var ev = Use(s, 0);
                Assert.AreEqual(i, ev.OfType<HazardSetEvent>().Single().Layers);
            }
            Assert.AreEqual(3, s.Battle.EnemyTeam.LayersOf("spikes"));
            var fourth = Use(s, 0);
            Assert.IsEmpty(fourth.OfType<HazardSetEvent>().ToList());
            Assert.AreEqual(1, fourth.OfType<MoveFailedEvent>().Count(), "la cuarta capa falla");
        }

        [Test]
        public void Three_layers_of_spikes_take_a_quarter_on_entry()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "spikes", "roar" }, "dummy", "dummy2");
            for (int i = 0; i < 3; i++) Use(s, 0);
            var ev = Use(s, 1); // Rugido: entra dummy2 y pisa las púas
            Assert.AreEqual(1, ev.OfType<ForcedOutEvent>().Count());
            var entering = s.Battle.Enemy;
            Assert.AreEqual("dummy2", s.MonsterOf(entering.Id).SpeciesId.Value);
            var dmg = ev.OfType<HazardDamageEvent>().Single();
            Assert.AreEqual((int)(entering.MaxHp * 0.25f), dmg.Amount);
            Assert.AreEqual(entering.MaxHp - dmg.Amount, entering.CurrentHp);
        }

        [Test]
        public void Stealth_rock_scales_with_rock_effectiveness_four_times_is_half()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "stealth_rock", "roar" }, "dummy", "firebird");
            Use(s, 0);
            var ev = Use(s, 1);
            var entering = s.Battle.Enemy;
            Assert.AreEqual((int)(entering.MaxHp * 0.125f * 4f), ev.OfType<HazardDamageEvent>().Single().Amount, "×4 contra Fuego/Volador = 50%");
        }

        [Test]
        public void Flying_types_ignore_spikes_and_sticky_web()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "spikes", "sticky_web", "roar" }, "dummy", "bird");
            Use(s, 0); Use(s, 1);
            var ev = Use(s, 2);
            Assert.IsEmpty(ev.OfType<HazardDamageEvent>().ToList());
            Assert.AreEqual(0, s.Battle.Enemy.GetStage(StatId.Speed));
            Assert.AreEqual(1, s.Battle.EnemyTeam.LayersOf("spikes"), "las púas siguen puestas");
        }

        [Test]
        public void Sticky_web_lowers_speed_on_entry()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "sticky_web", "roar" }, "dummy", "dummy2");
            Use(s, 0);
            Use(s, 1);
            Assert.AreEqual(-1, s.Battle.Enemy.GetStage(StatId.Speed));
        }

        [Test]
        public void Toxic_spikes_poison_with_one_layer_and_badly_with_two()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "toxic_spikes", "roar" }, "dummy", "dummy2");
            Use(s, 0);
            Use(s, 1);
            Assert.AreEqual("poison", s.Battle.Enemy.Status.Value.Value);

            var s2 = VsTrainer(data, new[] { "toxic_spikes", "roar" }, "dummy", "dummy2");
            Use(s2, 0); Use(s2, 0);
            Use(s2, 1);
            Assert.AreEqual("toxic", s2.Battle.Enemy.Status.Value.Value);
        }

        [Test]
        public void A_poison_type_absorbs_toxic_spikes()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "toxic_spikes", "roar" }, "dummy", "snake");
            Use(s, 0);
            var ev = Use(s, 1);
            Assert.AreEqual(1, ev.OfType<HazardAbsorbedEvent>().Count());
            Assert.AreEqual(0, s.Battle.EnemyTeam.LayersOf("toxic_spikes"), "la retira de su lado");
            Assert.IsFalse(s.Battle.Enemy.Status.HasValue);
        }

        [Test]
        public void Rapid_spin_clears_hazards_from_its_own_side()
        {
            var data = Data();
            // El rival solo sabe poner púas (en NUESTRO lado); nosotros somos más rápidos.
            var s = VsTrainer(data, new[] { "rapid_spin" }, "spiker");
            Use(s, 0);
            Assert.AreEqual(1, s.Battle.PlayerTeam.LayersOf("spikes"), "el rival puso una capa en nuestro lado");
            var ev = Use(s, 0);         // la quitamos... y el rival vuelve a poner otra después
            var cleared = ev.OfType<HazardsClearedEvent>().Single();
            Assert.IsTrue(cleared.PlayerSide);
            Assert.AreEqual(1, s.Battle.PlayerTeam.LayersOf("spikes"), "quitó la vieja; la nueva es de después");
            Assert.AreEqual(1, ev.OfType<HazardSetEvent>().Single().Layers, "tras quitarlas se vuelve a empezar desde la capa 1");
        }

        // ---------------- Cambios forzados ----------------

        [Test]
        public void Roar_ends_a_wild_battle()
        {
            var data = Data();
            var save = Save(data, "roar");
            var wild = TeamBuilder.Build(M("dummy", 5), save.NewMonsterId(), data, new FixedRng(0.5f));
            var s = BattleSession.Wild(data, save, wild, new FixedRng(0.5f));
            s.Begin();
            var ev = Use(s, 0);
            Assert.IsTrue(ev.OfType<ForcedOutEvent>().Single().BattleEnded);
            Assert.AreEqual(SessionPhase.Finished, s.Phase);
            Assert.AreEqual(BattleOutcome.Fled, s.Outcome);
        }

        [Test]
        public void Roar_fails_when_the_trainer_has_no_reserves()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "roar" }, "dummy");
            var ev = Use(s, 0);
            Assert.AreEqual(1, ev.OfType<MoveFailedEvent>().Count());
            Assert.AreEqual(SessionPhase.ChooseAction, s.Phase);
        }

        [Test]
        public void The_monster_dragged_in_does_not_act_that_turn()
        {
            var data = Data();
            // El rival saca a "clawer" (araña); lo empujamos antes de que actúe y entra "clawer" de reserva.
            var s = VsTrainer(data, new[] { "push" }, "clawer", "clawer");
            var enemyBefore = s.Battle.Enemy.Id;
            var ev = Use(s, 0);
            Assert.AreNotEqual(enemyBefore.Value, s.Battle.Enemy.Id.Value, "ha cambiado");
            Assert.IsFalse(ev.OfType<MoveUsedEvent>().Any(e => !s.IsPlayerSide(e.Attacker)), "ni el que se fue ni el que entró atacan");
        }

        // ---------------- Relevos que caen por las trampas ----------------

        [Test]
        public void Replacements_that_faint_on_hazards_make_the_trainer_send_the_next_until_the_end()
        {
            var data = Data();
            var s = VsTrainer(data, new[] { "doom_field", "nuke" }, "dummy", "dummy2", "clawer");
            Use(s, 0);                  // Perdición en su lado
            var ev = Use(s, 1);         // debilita al primero; los dos relevos caen al entrar
            Assert.AreEqual(2, ev.OfType<HazardDamageEvent>().Count());
            Assert.AreEqual(3, ev.OfType<MonsterFaintedEvent>().Count());
            Assert.AreEqual(SessionPhase.Finished, s.Phase);
            Assert.AreEqual(BattleOutcome.PlayerWon, s.Outcome);
        }

        [Test]
        public void Hazards_disappear_when_the_battle_ends()
        {
            var data = Data();
            var save = Save(data, "spikes");
            var s = BattleSession.Against(data, save, Trainer("dummy"), new FixedRng(0.5f));
            s.Begin();
            Use(s, 0);
            // Un combate nuevo empieza con el campo limpio.
            var s2 = BattleSession.Against(data, save, Trainer("dummy"), new FixedRng(0.5f));
            Assert.AreEqual(0, s2.Battle.EnemyTeam.Hazards.Count);
            Assert.AreEqual(0, s2.Battle.PlayerTeam.Hazards.Count);
        }
    }
}
