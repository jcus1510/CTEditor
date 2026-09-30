using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.GameDefinition.Domain.Catalog;
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
    /// CIERRE DEL COMBATE: efectos de lado (Reflejo, Pantalla de Luz, Neblina, Velo Sagrado, Viento Afín),
    /// Sustituto, Niebla, Foco Energía, Anulación, Otra Vez, Contraataque, Manto Espejo, Venganza, Saña,
    /// Furia, Metrónomo, Espejo, Mimético, Transformación, Conversión, Ida y Vuelta, Relevo y Teletransporte.
    /// </summary>
    public class CombatClosureTests
    {
        // ---------------- Contenido de prueba ----------------

        private static readonly Percentage Always = new Percentage(100);
        private static MoveEffect Fx(MoveEffectKind kind, EffectTarget target = EffectTarget.Opponent, int stages = 0, float amount = 0,
            string status = null, StatId stat = default, int turns = 0, string side = null, string type = null)
            => new MoveEffect(Always, kind, target, status == null ? default : new StatusId(status), new Percentage(amount), stat, stages,
                turns: turns, sideConditionId: side, typeId: type);

        private static Move Hit(string id, int power, MoveCategory cat = MoveCategory.Physical, string type = "normal", int priority = 0,
            FixedDamageKind fixedKind = FixedDamageKind.None, params MoveEffect[] fx)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), cat, power, null, 20, priority, MoveTarget.SingleEnemy, fx, fixedDamage: fixedKind);

        private static Move Support(string id, int priority, params MoveEffect[] fx)
            => new Move(new Id<Move>(id), id, new Id<ElementType>("normal"), MoveCategory.Status, 0, null, 20, priority, MoveTarget.SingleEnemy, fx);

        private static readonly Move[] Moves =
        {
            Hit("tackle", 40),
            Hit("swift", 40, MoveCategory.Special),
            Hit("ember", 40, MoveCategory.Special, "fire"),
            Support("wait", 0),
            Support("reflect", 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "reflect")),
            Support("light_screen", 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "light_screen")),
            Support("mist", 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "mist")),
            Support("safeguard", 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "safeguard")),
            Support("tailwind", 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "tailwind")),
            Support("growl", 0, Fx(MoveEffectKind.ChangeStatStage, stat: StatId.Attack, stages: -1)),
            Support("swords_dance", 0, Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: StatId.Attack, stages: 2)),
            Support("hypnosis", 0, Fx(MoveEffectKind.InflictStatus, status: "sleep")),
            Support("substitute", 0, Fx(MoveEffectKind.Substitute, EffectTarget.Self, amount: 25)),
            Support("haze", 0, Fx(MoveEffectKind.ResetStages, EffectTarget.Self), Fx(MoveEffectKind.ResetStages)),
            Support("focus_energy", 0, Fx(MoveEffectKind.CritBoost, EffectTarget.Self, stages: 2)),
            Support("disable", 0, Fx(MoveEffectKind.DisableMove, turns: 4)),
            Support("encore", 0, Fx(MoveEffectKind.Encore, turns: 3)),
            Hit("counter", 0, MoveCategory.Physical, "fighting", -5, FixedDamageKind.ReturnPhysical),
            Hit("mirror_coat", 0, MoveCategory.Special, "psychic", -5, FixedDamageKind.ReturnSpecial),
            Hit("bide", 0, MoveCategory.Physical, "normal", 1, FixedDamageKind.Bide),
            Hit("thrash", 60, MoveCategory.Physical, "normal", 0, FixedDamageKind.None, Fx(MoveEffectKind.Rampage, EffectTarget.Self, turns: 3, status: "confusion")),
            Hit("rage", 20, MoveCategory.Physical, "normal", 0, FixedDamageKind.None, Fx(MoveEffectKind.Rage, EffectTarget.Self, stat: StatId.Attack, stages: 1)),
            Support("metronome", 0, Fx(MoveEffectKind.CallRandomMove, EffectTarget.Self)),
            Support("mirror_move", 0, Fx(MoveEffectKind.CallLastMove)),
            Support("mimic", 0, Fx(MoveEffectKind.CopyLastMove)),
            Support("transform", 0, Fx(MoveEffectKind.Transform)),
            Support("conversion", 0, Fx(MoveEffectKind.ChangeType, EffectTarget.Self)),
            Hit("u_turn", 30, MoveCategory.Physical, "bug", 0, FixedDamageKind.None, Fx(MoveEffectKind.SwitchSelf, EffectTarget.Self)),
            Support("baton_pass", 0, Fx(MoveEffectKind.SwitchSelf, EffectTarget.Self, stages: 1)),
            Support("teleport", -6, Fx(MoveEffectKind.Teleport, EffectTarget.Self)),
            Hit("struggle", 50),
        };

        private static StatBlock Stats(int all, int speed) => new StatBlock.Builder().Set(StatId.Hp, all).Set(StatId.Attack, all).Set(StatId.Defense, all)
            .Set(StatId.SpAttack, all).Set(StatId.SpDefense, all).Set(StatId.Speed, speed).Build();

        private static Species Sp(string id, string type, int stat, int speed)
            => new Species(new Id<Species>(id), id, new[] { new Id<ElementType>(type) }, Stats(stat, speed),
                new[] { new LearnableMove(new Id<Move>("tackle"), 1) }, new Evolution[0]);

        private static readonly Species[] AllSpecies =
        {
            Sp("hero", "normal", 100, 200), Sp("mate", "normal", 100, 150), Sp("slow", "normal", 100, 5),
            Sp("foe", "normal", 80, 50), Sp("foe2", "normal", 80, 50), Sp("psy", "psychic", 90, 60), Sp("wall", "normal", 250, 1),
        };

        private static readonly SideConditionDefinition[] Sides =
        {
            new SideConditionDefinition("reflect", "Reflejo", 5, physicalDamageMultiplier: 0.5f),
            new SideConditionDefinition("light_screen", "Pantalla de Luz", 5, specialDamageMultiplier: 0.5f),
            new SideConditionDefinition("mist", "Neblina", 5, blocksStatDrops: true),
            new SideConditionDefinition("safeguard", "Velo Sagrado", 5, blocksStatus: true),
            new SideConditionDefinition("tailwind", "Viento Afín", 4, speedMultiplier: 2f),
        };

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value),
            new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Build(),
            Ruleset.Classic,
            statuses: new MemoryCatalog<StatusConditionDefinition>(new[]
            {
                new StatusConditionDefinition(new StatusId("sleep"), "Dormido", new Percentage(0), new Percentage(100), durationTurns: 3),
                new StatusConditionDefinition(new StatusId("confusion"), "Confuso", new Percentage(0), new Percentage(0), isVolatile: true, durationTurns: 4),
            }, s => s.Id.Value),
            sideConditions: new MemoryCatalog<SideConditionDefinition>(Sides, d => d.Id));

        private sealed class FixedRng : IRng
        {
            private readonly float _f;
            public FixedRng(float f) { _f = f; }
            public int Next(int minInclusive, int maxExclusive) => minInclusive + (int)((maxExclusive - minInclusive) * _f * 0.999f);
            public float NextFloat() => _f;
        }

        private static TeamMemberSpec M(string species, int level, params string[] moves)
            => new TeamMemberSpec(new Id<Species>(species), level, moves.Select(m => new Id<Move>(m)).ToList(), fixedIv: 31);

        private static PlayerSave Save(GameData data, params TeamMemberSpec[] team)
            => TeamBuilder.NewGame(new TeamPreset("t", "Prueba", team), data, new FixedRng(0.5f));

        private static BattleSession Vs(GameData data, TeamMemberSpec[] player, params TeamMemberSpec[] enemy)
        {
            var s = BattleSession.Against(data, Save(data, player), new TrainerDefinition("rival", "Rival", enemy, ai: TrainerAi.Random), new FixedRng(0.5f));
            s.Begin();
            return s;
        }

        private static BattleSession VsWild(GameData data, TeamMemberSpec[] player, TeamMemberSpec wild)
        {
            var save = Save(data, player);
            var s = BattleSession.Wild(data, save, TeamBuilder.Build(wild, save.NewMonsterId(), data, new FixedRng(0.5f)), new FixedRng(0.5f));
            s.Begin();
            return s;
        }

        private static List<IDomainEvent> Use(BattleSession s, int slot)
        {
            var step = s.Submit(PlayerChoice.Fight(slot));
            Assert.IsTrue(step.Accepted, step.Message);
            return step.Events.ToList();
        }

        private static int DamageTo(List<IDomainEvent> ev, BattleSession s, bool toEnemy)
            => ev.OfType<DamageDealtEvent>().Where(d => s.IsPlayerSide(d.Target) != toEnemy).Sum(d => d.Amount);

        private static bool EnemyUsed(List<IDomainEvent> ev, BattleSession s, string move)
            => ev.OfType<MoveUsedEvent>().Any(e => !s.IsPlayerSide(e.Attacker) && e.Move.Value == move);

        // ---------------- Efectos de lado ----------------

        [Test]
        public void Reflect_halves_physical_damage_and_light_screen_special()
        {
            var data = Data();
            // Sin pantallas: lo que hace Placaje y Rapidez.
            var control = Vs(data, new[] { M("hero", 50, "wait", "tackle", "swift") }, M("wall", 50, "wait"));
            Use(control, 0);
            int tackle = DamageTo(Use(control, 1), control, true);
            int swift = DamageTo(Use(control, 2), control, true);

            var r = Vs(data, new[] { M("hero", 50, "wait", "tackle", "swift") }, M("wall", 50, "reflect"));
            Use(r, 0);                                 // el rival pone Reflejo
            Assert.IsTrue(r.Battle.EnemyTeam.HasSideCondition("reflect"));
            Assert.AreEqual(tackle / 2, DamageTo(Use(r, 1), r, true), 1, "Reflejo: la mitad del daño físico");
            Assert.AreEqual(swift, DamageTo(Use(r, 2), r, true), 1, "Reflejo no frena el especial");

            var l = Vs(data, new[] { M("hero", 50, "wait", "tackle", "swift") }, M("wall", 50, "light_screen"));
            Use(l, 0);
            Assert.AreEqual(swift / 2, DamageTo(Use(l, 2), l, true), 1, "Pantalla de Luz: la mitad del daño especial");
        }

        [Test]
        public void Side_conditions_end_after_their_turns()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "reflect", "wait") }, M("wall", 50, "wait"));
            Assert.AreEqual(1, Use(s, 0).OfType<SideConditionStartedEvent>().Count());
            Assert.IsTrue(Use(s, 0).OfType<MoveFailedEvent>().Any(), "no se pone dos veces");
            var ended = new List<IDomainEvent>();
            for (int i = 0; i < 4; i++) ended.AddRange(Use(s, 1));
            Assert.AreEqual(1, ended.OfType<SideConditionEndedEvent>().Count(e => e.PlayerSide && e.ConditionId == "reflect"));
            Assert.AreEqual(0, s.Battle.PlayerTeam.SideConditions.Count);
        }

        [Test]
        public void Mist_blocks_stat_drops_and_safeguard_blocks_status()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "mist", "safeguard", "wait") }, M("foe", 50, "growl", "hypnosis"));
            Use(s, 0); Use(s, 1);
            var ev = new List<IDomainEvent>();
            for (int i = 0; i < 3; i++) ev.AddRange(Use(s, 2));
            Assert.AreEqual(0, s.Battle.Player.GetStage(StatId.Attack), "Neblina: sin bajadas");
            Assert.IsFalse(s.Battle.Player.Status.HasValue, "Velo Sagrado: sin estados");
            Assert.IsTrue(ev.OfType<ProtectedBySideEvent>().Any());
        }

        [Test]
        public void Tailwind_doubles_speed()
        {
            var data = Data();
            var s = Vs(data, new[] { M("slow", 50, "tailwind", "tackle") }, M("foe", 50, "tackle"));
            var before = Use(s, 0);
            Assert.IsFalse(s.IsPlayerSide(before.OfType<MoveUsedEvent>().First().Attacker), "sin Viento Afín, el rival va primero");
            // Velocidad 5 × 2 sigue siendo menos que el rival: comprobamos el valor efectivo.
            Assert.AreEqual(2 * s.Resolver.CurrentStat(s.Battle.Player, StatId.Speed) / 2, s.Resolver.CurrentStat(s.Battle.Player, StatId.Speed));
            var fast = Vs(data, new[] { M("mate", 50, "tailwind", "tackle") }, M("hero", 50, "tackle")); // 150 vs 200
            Use(fast, 0);
            var ev = Use(fast, 1);
            Assert.IsTrue(fast.IsPlayerSide(ev.OfType<MoveUsedEvent>().First().Attacker), "con Viento Afín (150 × 2 > 200) va primero");
        }

        // ---------------- Sustituto, Niebla, Foco Energía ----------------

        [Test]
        public void Substitute_costs_a_quarter_takes_hits_blocks_status_and_breaks()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "substitute", "wait") }, M("foe", 50, "hypnosis"));
            int max = s.Battle.Player.MaxHp;
            var ev = Use(s, 0);
            Assert.AreEqual(1, ev.OfType<SubstituteCreatedEvent>().Count());
            Assert.AreEqual(max - max / 4, s.Battle.Player.CurrentHp);
            Assert.IsTrue(ev.OfType<SubstituteBlockedEvent>().Any(), "Hipnosis no pasa el sustituto");
            Assert.IsFalse(s.Battle.Player.Status.HasValue);
            Assert.IsTrue(Use(s, 0).OfType<MoveFailedEvent>().Any(), "no se crean dos");

            var hits = Vs(data, new[] { M("hero", 50, "substitute", "wait") }, M("foe", 70, "tackle"));
            Use(hits, 0);
            int hp = hits.Battle.Player.CurrentHp;
            var h = new List<IDomainEvent>();
            for (int i = 0; i < 4 && !h.OfType<SubstituteBrokeEvent>().Any(); i++) h.AddRange(Use(hits, 1));
            Assert.IsTrue(h.OfType<SubstituteDamagedEvent>().Any());
            Assert.IsTrue(h.OfType<SubstituteBrokeEvent>().Any(), "acaba rompiéndose");
            Assert.AreEqual(hp, hits.Battle.Player.CurrentHp, "mientras tuvo sustituto, él no perdió PS");
        }

        [Test]
        public void Haze_resets_every_stage_and_focus_energy_boosts_crits_once()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "swords_dance", "haze", "focus_energy") }, M("foe", 50, "swords_dance"));
            Use(s, 0);
            Assert.AreEqual(2, s.Battle.Player.GetStage(StatId.Attack));
            Assert.AreEqual(2, s.Battle.Enemy.GetStage(StatId.Attack));
            var ev = Use(s, 1);                         // Niebla (el rival, más lento, vuelve a subir después)
            Assert.AreEqual(0, s.Battle.Player.GetStage(StatId.Attack));
            Assert.AreEqual(2, ev.OfType<StagesResetEvent>().Count(), "los dos vuelven a 0");

            Assert.IsTrue(Use(s, 2).OfType<CritBoostedEvent>().Any());
            Assert.AreEqual(2, s.Battle.Player.CritBonus);
            Assert.IsTrue(Use(s, 2).OfType<MoveFailedEvent>().Any(), "no se acumula");
        }

        // ---------------- Anulación y Otra Vez ----------------

        [Test]
        public void Disable_blocks_the_last_move_for_the_player_and_the_ai()
        {
            var data = Data();
            // El rival (más lento) anula lo último que usamos: Placaje.
            var s = Vs(data, new[] { M("hero", 50, "tackle", "wait") }, M("wall", 50, "disable"));
            var ev = Use(s, 0);
            Assert.IsTrue(ev.OfType<MoveDisabledEvent>().Any(e => e.Move.Value == "tackle"));
            var rejected = s.Submit(PlayerChoice.Fight(0));
            Assert.IsFalse(rejected.Accepted);
            StringAssert.Contains("anulado", rejected.Message);
            Assert.IsTrue(Use(s, 1).Count > 0, "los demás sí");

            // Anulado en la IA: si solo sabe ese movimiento, pierde el turno.
            var ai = Vs(data, new[] { M("slow", 50, "disable", "wait") }, M("foe", 50, "tackle"));
            Use(ai, 0);                               // el rival usó Placaje antes; lo anulamos
            var t2 = Use(ai, 1);
            Assert.IsTrue(t2.OfType<DisabledMoveTriedEvent>().Any());
        }

        [Test]
        public void Encore_forces_the_last_move()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "wait", "tackle") }, M("wall", 50, "encore"));
            var ev = Use(s, 0);                        // usamos Esperar y el rival nos obliga a repetirlo
            Assert.IsTrue(ev.OfType<EncoreStartedEvent>().Any(e => e.Move.Value == "wait"));
            var no = s.Submit(PlayerChoice.Fight(1));
            Assert.IsFalse(no.Accepted, "hay que repetir Esperar");
            Use(s, 0);
        }

        // ---------------- Devolver daño ----------------

        [Test]
        public void Counter_returns_double_physical_damage_and_fails_without_it()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "counter") }, M("foe", 50, "tackle"));
            var ev = Use(s, 0);
            int taken = DamageTo(ev, s, false);
            Assert.Greater(taken, 0);
            Assert.AreEqual(taken * 2, DamageTo(ev, s, true), "el doble de lo recibido");

            var fail = Vs(data, new[] { M("hero", 50, "counter") }, M("foe", 50, "swift"));
            Assert.IsTrue(Use(fail, 0).OfType<MoveFailedEvent>().Any(), "Contraataque no devuelve daño especial");
            var coat = Vs(data, new[] { M("hero", 50, "mirror_coat") }, M("foe", 50, "swift"));
            var c = Use(coat, 0);
            Assert.AreEqual(DamageTo(c, coat, false) * 2, DamageTo(c, coat, true));
        }

        [Test]
        public void Bide_stores_two_turns_and_unleashes_double()
        {
            var data = Data();
            var s = Vs(data, new[] { M("wall", 50, "bide") }, M("foe", 50, "tackle"));
            var all = new List<IDomainEvent>();
            all.AddRange(Use(s, 0));
            Assert.IsTrue(s.PlayerIsLocked, "aguantando: no hay menú");
            all.AddRange(Use(s, 0));
            var last = Use(s, 0);
            all.AddRange(last);
            Assert.IsTrue(last.OfType<BideUnleashedEvent>().Any());
            int received = DamageTo(all.Take(all.Count - last.Count).ToList(), s, false) + DamageTo(last, s, false);
            // Lo que recibió ANTES de soltar (el golpe del turno de soltar llega después, rival más lento).
            int stored = all.Take(all.Count - last.Count).OfType<DamageDealtEvent>().Where(d => s.IsPlayerSide(d.Target)).Sum(d => d.Amount);
            Assert.AreEqual(stored * 2, DamageTo(last, s, true));
            Assert.IsFalse(s.PlayerIsLocked);
        }

        // ---------------- Saña y Furia ----------------

        [Test]
        public void Thrash_locks_for_two_or_three_turns_then_confuses()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "thrash", "wait") }, M("wall", 70, "wait"));
            Use(s, 0);
            Assert.IsTrue(s.PlayerIsLocked, "encadenado");
            var ev = new List<IDomainEvent>();
            for (int i = 0; i < 3 && s.PlayerIsLocked; i++) ev.AddRange(Use(s, 0));
            Assert.IsFalse(s.PlayerIsLocked);
            Assert.IsTrue(ev.OfType<RampageEndedEvent>().Any());
            Assert.IsTrue(s.Battle.Player.HasVolatile(new StatusId("confusion")), "acaba confuso");
        }

        [Test]
        public void Rage_raises_attack_every_time_it_is_hit()
        {
            var data = Data();
            var s = Vs(data, new[] { M("slow", 30, "rage", "wait") }, M("foe", 20, "tackle"));
            Use(s, 0);                                   // el rival (más rápido) golpea antes de que empiece la furia
            Use(s, 0);
            Use(s, 0);
            Assert.AreEqual(2, s.Battle.Player.GetStage(StatId.Attack), "dos golpes recibidos mientras usaba Furia");
            Use(s, 1);
            Assert.IsFalse(s.Battle.Player.Raging, "al usar otro movimiento se calma");
        }

        // ---------------- Llamar y copiar ----------------

        [Test]
        public void Metronome_calls_another_move_never_itself_or_struggle()
        {
            var data = Data();
            for (int i = 0; i < 5; i++)
            {
                var save = Save(data, M("hero", 50, "metronome"));
                var s = BattleSession.Against(data, save, new TrainerDefinition("r", "R", new[] { M("wall", 50, "wait") }, ai: TrainerAi.Random), new FixedRng(0.1f + i * 0.2f));
                s.Begin();
                var called = Use(s, 0).OfType<MoveCalledEvent>().Single();
                Assert.AreNotEqual("metronome", called.Called.Value);
                Assert.AreNotEqual("struggle", called.Called.Value);
                Assert.AreNotEqual("mirror_move", called.Called.Value);
            }
        }

        [Test]
        public void Mirror_move_uses_the_foes_last_move_and_mimic_copies_it_until_switching()
        {
            var data = Data();
            var s = Vs(data, new[] { M("slow", 50, "mirror_move", "mimic"), M("mate", 50, "tackle") }, M("foe", 50, "ember"));
            var ev = Use(s, 0);
            Assert.IsTrue(ev.OfType<MoveCalledEvent>().Any(c => c.Called.Value == "ember"), "Espejo lanza Ascuas");

            var copy = Use(s, 1);
            Assert.IsTrue(copy.OfType<MoveCopiedEvent>().Any(c => c.Move.Value == "ember"));
            Assert.AreEqual("ember", s.Battle.Player.Moves[1].Value);
            Assert.AreEqual(5, s.Battle.Player.PpAt(1), "el copiado tiene 5 PP");

            s.Submit(PlayerChoice.Switch(1));
            s.Submit(PlayerChoice.Switch(0));
            Assert.AreEqual("mimic", s.Battle.Player.Moves[1].Value, "al retirarse recupera Mimético");
            Assert.AreEqual("mimic", s.Save.Party.Members[0].Moves[1].Value, "la partida nunca cambió");
        }

        [Test]
        public void Transform_copies_types_stats_moves_but_keeps_its_hp()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 30, "transform") }, M("psy", 60, "swift", "ember"));
            int maxHp = s.Battle.Player.MaxHp;
            Assert.IsTrue(Use(s, 0).OfType<TransformedEvent>().Any());
            var me = s.Battle.Player;
            Assert.AreEqual("psychic", me.Types.Single().Value);
            Assert.AreEqual(s.Battle.Enemy.Stats.Of(StatId.Attack), me.Stats.Of(StatId.Attack));
            Assert.AreEqual(maxHp, me.MaxHp, "los PS no cambian");
            CollectionAssert.AreEqual(new[] { "swift", "ember" }, me.Moves.Select(m => m.Value).ToArray());
            Assert.AreEqual(5, me.PpAt(0));
        }

        [Test]
        public void Conversion_changes_type_to_its_first_other_move()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "conversion", "ember") }, M("wall", 50, "wait"));
            var ev = Use(s, 0);
            Assert.AreEqual("fire", ev.OfType<TypeChangedEvent>().Single().Types.Single().Value);
            Assert.AreEqual("fire", s.Battle.Player.Types.Single().Value);
            Assert.IsTrue(Use(s, 0).OfType<MoveFailedEvent>().Any(), "ya es de tipo Fuego");
        }

        // ---------------- Cambios propios ----------------

        [Test]
        public void U_turn_pauses_the_turn_until_the_player_picks_who_comes_in()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "u_turn"), M("mate", 50, "tackle") }, M("wall", 50, "tackle"));
            var ev = Use(s, 0);
            Assert.IsTrue(DamageTo(ev, s, true) > 0, "primero golpea");
            Assert.AreEqual(SessionPhase.ChooseReplacement, s.Phase);
            Assert.IsTrue(s.IsSelfSwitchPending);
            Assert.IsFalse(EnemyUsed(ev, s, "tackle"), "el rival aún no actuó");
            Assert.IsFalse(s.ChooseReplacement(0).Accepted, "no puede volver a entrar el mismo");

            var resumed = s.ChooseReplacement(1);
            Assert.IsTrue(resumed.Accepted, resumed.Message);
            Assert.AreEqual("mate", s.MonsterOf(s.Battle.Player.Id).SpeciesId.Value);
            Assert.IsTrue(EnemyUsed(resumed.Events.ToList(), s, "tackle"), "el turno siguió: el rival golpea al que entró");
            Assert.AreEqual(SessionPhase.ChooseAction, s.Phase);
        }

        [Test]
        public void Baton_pass_passes_stat_stages()
        {
            var data = Data();
            var s = Vs(data, new[] { M("hero", 50, "swords_dance", "baton_pass"), M("mate", 50, "tackle") }, M("wall", 50, "wait"));
            Use(s, 0);
            Use(s, 1);
            s.ChooseReplacement(1);
            Assert.AreEqual(2, s.Battle.Player.GetStage(StatId.Attack), "el que entra hereda +2");
        }

        [Test]
        public void An_enemy_u_turn_switches_it_at_once_and_u_turn_without_reserves_just_hits()
        {
            var data = Data();
            var s = Vs(data, new[] { M("slow", 50, "wait") }, M("foe", 50, "u_turn"), M("foe2", 50, "u_turn"));
            var ev = Use(s, 0);
            Assert.IsTrue(ev.OfType<SelfSwitchedEvent>().Any());
            Assert.AreEqual("foe2", s.MonsterOf(s.Battle.Enemy.Id).SpeciesId.Value);

            var alone = Vs(data, new[] { M("hero", 50, "u_turn") }, M("wall", 50, "wait"));
            Use(alone, 0);
            Assert.AreEqual(SessionPhase.ChooseAction, alone.Phase, "sin compañeros no hay cambio");
        }

        [Test]
        public void Teleport_escapes_wild_battles()
        {
            var data = Data();
            var s = VsWild(data, new[] { M("hero", 50, "teleport") }, M("foe", 5, "wait"));
            var ev = Use(s, 0);
            Assert.IsTrue(ev.OfType<TeleportedEvent>().Any());
            Assert.AreEqual(BattleOutcome.Fled, s.Outcome);
        }
    }
}
