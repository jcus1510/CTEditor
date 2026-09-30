using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// LOTE E: mecánicas de la 3.ª y 4.ª generación (Mofa, Espacio Raro, Patada Baja por peso, Premonición,
    /// Sonámbulo, Meteorobola, Lodo Líquido, Ignorante, Levitón), niveles de IA, curación inteligente,
    /// «Preparar un reto» y el planificador con MT / tutor / huevo.
    /// </summary>
    public class Gen4MechanicsTests
    {
        private static readonly Percentage Always = new Percentage(100);
        private static MoveEffect Fx(MoveEffectKind k, EffectTarget t = EffectTarget.Opponent, string status = null, float amount = 0,
            string side = null, int turns = 0, string weather = null)
            => new MoveEffect(Always, k, t, status == null ? default : new StatusId(status), new Percentage(amount),
                default, 0, null, false, weather, 0, null, turns, side);

        private static Move M(string id, string type, MoveCategory cat, int power, int priority = 0, string formula = null,
            IReadOnlyDictionary<string, Id<ElementType>> byWeather = null, params MoveEffect[] fx)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), cat, power, null, 20, priority,
                cat == MoveCategory.Status && fx.All(e => e.Target == EffectTarget.Self) ? MoveTarget.Self : MoveTarget.SingleEnemy,
                fx, powerFormula: formula, typeByWeather: byWeather);

        private static readonly Move[] Moves =
        {
            M("tackle", "normal", MoveCategory.Physical, 40),
            M("growl", "normal", MoveCategory.Status, 0),
            M("wait", "normal", MoveCategory.Status, 0, fx: new MoveEffect[0]),
            M("taunt", "dark", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.InflictStatus, status: "taunt")),
            M("trick_room", "psychic", MoveCategory.Status, 0, priority: -7, fx: Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "trick_room")),
            M("low_kick", "fighting", MoveCategory.Physical, 1,
                formula: "20+20*paso(peso_rival,10)+20*paso(peso_rival,25)+20*paso(peso_rival,50)+20*paso(peso_rival,100)+20*paso(peso_rival,200)"),
            M("future_sight", "psychic", MoveCategory.Special, 120, fx: Fx(MoveEffectKind.DelayedDamage, turns: 3)),
            // Sonámbulo lleva la etiqueta «dormido»: se puede usar dormido (como Ronquido).
            new Move(new Id<Move>("sleep_talk"), "sleep_talk", new Id<ElementType>("normal"), MoveCategory.Status, 0, null, 10, 0, MoveTarget.Self,
                new[] { Fx(MoveEffectKind.CallOwnMove, EffectTarget.Self) }, tags: new[] { "dormido" }),
            M("weather_ball", "normal", MoveCategory.Special, 50, byWeather: new Dictionary<string, Id<ElementType>> { ["rain"] = new Id<ElementType>("water") }),
            M("rain_dance", "water", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.SetWeather, EffectTarget.Self, weather: "rain")),
            M("giga_drain", "grass", MoveCategory.Special, 75, fx: Fx(MoveEffectKind.Drain, EffectTarget.Self, amount: 50)),
            M("earthquake", "ground", MoveCategory.Physical, 100),
            M("belly", "normal", MoveCategory.Status, 0, fx: new MoveEffect(Always, MoveEffectKind.ChangeStatStage, EffectTarget.Self, default, default, StatId.Attack, 6)),
            M("magnet_rise", "electric", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.InflictStatus, EffectTarget.Self, status: "magnet_rise")),
        };

        private static StatusConditionDefinition V(string id, int dur, StatusExtras x)
            => new StatusConditionDefinition(new StatusId(id), id, new Percentage(0), new Percentage(0), clearedOnSwitch: true,
                durationTurns: dur, isVolatile: true, extras: x);

        private static readonly StatusConditionDefinition[] Statuses =
        {
            new StatusConditionDefinition(new StatusId("sleep"), "Dormido", new Percentage(0), new Percentage(100), durationTurns: 5),
            V("taunt", 3, new StatusExtras { BlocksStatusMoves = true }),
            V("magnet_rise", 5, new StatusExtras { TypeImmunities = new[] { new Id<ElementType>("ground") } }),
        };

        private static readonly AbilityDefinition[] Abilities =
        {
            new AbilityDefinition(new AbilityId("liquid_ooze"), "Lodo Líquido", new[] { new EffectBlock(EffectTrigger.Passive, EffectAction.Special, reference: "lodo_liquido") }),
            new AbilityDefinition(new AbilityId("unaware"), "Ignorante", new[] { new EffectBlock(EffectTrigger.Passive, EffectAction.Special, reference: "ignorante") }),
        };

        private static BattleParticipant Mon(string id, string type, int speed, float kg = 50f, string ability = null, int hp = 300,
            StatusId? status = null, params string[] moves)
            => new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, hp).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>(type) }, moves.Select(m => new Id<Move>(m)).ToList(),
                status, ability == null ? (AbilityId?)null : new AbilityId(ability), weightKg: kg);

        private static TurnResolver Resolver() => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder()
                .Set(new Id<ElementType>("water"), new Id<ElementType>("fire"), 2f)
                .Set(new Id<ElementType>("ground"), new Id<ElementType>("electric"), 2f).Build(),
            new ClassicDamageFormula(), new SeededRng(3),
            new InMemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value),
            abilities: new InMemoryCatalog<AbilityDefinition>(Abilities, a => a.Id.Value),
            weathers: new InMemoryCatalog<WeatherDefinition>(new[] { new WeatherDefinition("rain", "Lluvia") }, w => w.Id),
            sideConditions: new InMemoryCatalog<SideConditionDefinition>(new[] { new SideConditionDefinition("trick_room", "Espacio Raro", 5, reversesTurnOrder: true) }, s => s.Id));

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));

        [Test]
        public void Taunt_blocks_status_moves_but_not_attacks()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "dark", 99, moves: "taunt"), Mon("b", "normal", 1, moves: new[] { "growl", "tackle" }));
            r.ResolveTurn(b, Use("taunt"), Use("wait")).ToList();
            var ev = r.ResolveTurn(b, Use("taunt"), Use("growl")).ToList();
            Assert.IsTrue(ev.OfType<MoveRestrictedEvent>().Any(e => e.Combatant.Value == "b"), "Mofa impide Gruñido");
            Assert.IsFalse(r.CanChooseMove(b.Enemy, 0), "la IA y la pantalla no dejan elegirlo");
            Assert.IsTrue(r.CanChooseMove(b.Enemy, 1), "Placaje sí");
        }

        [Test]
        public void Trick_room_makes_the_slower_monster_move_first()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("fast", "normal", 200, moves: "tackle"), Mon("slow", "psychic", 10, moves: new[] { "trick_room", "tackle" }));
            r.ResolveTurn(b, Use("tackle"), Use("trick_room")).ToList();
            var ev = r.ResolveTurn(b, Use("tackle"), Use("tackle")).ToList();
            var first = ev.OfType<MoveUsedEvent>().First();
            Assert.AreEqual("slow", first.Attacker.Value);
        }

        [Test]
        public void Low_kick_hits_harder_against_heavier_targets()
        {
            var r = Resolver();
            var kick = Moves.First(m => m.Id.Value == "low_kick");
            var light = new CTEditor.Battle.Domain.Battle(Mon("a", "fighting", 50, moves: "low_kick"), Mon("light", "normal", 50, kg: 5f, moves: "tackle"));
            var heavy = new CTEditor.Battle.Domain.Battle(Mon("a", "fighting", 50, moves: "low_kick"), Mon("heavy", "normal", 50, kg: 400f, moves: "tackle"));
            Assert.AreEqual(20, r.EffectivePower(light.Player, light.Enemy, kick));
            Assert.AreEqual(120, r.EffectivePower(heavy.Player, heavy.Enemy, kick));
        }

        [Test]
        public void Future_sight_hits_two_turns_later_not_now()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "psychic", 99, moves: "future_sight"), Mon("b", "normal", 1, moves: "wait"));
            var t1 = r.ResolveTurn(b, Use("future_sight"), Use("wait")).ToList();
            Assert.IsFalse(t1.OfType<DamageDealtEvent>().Any(), "no golpea el primer turno");
            Assert.IsTrue(t1.OfType<DelayedEffectSetEvent>().Any());
            var t2 = r.ResolveTurn(b, Use("wait"), Use("wait")).ToList();
            Assert.IsFalse(t2.OfType<DelayedEffectTriggeredEvent>().Any());
            var t3 = r.ResolveTurn(b, Use("wait"), Use("wait")).ToList();
            Assert.IsTrue(t3.OfType<DelayedEffectTriggeredEvent>().Any(), "llega dos turnos después");
            Assert.IsTrue(t3.OfType<DamageDealtEvent>().Any(e => e.Target.Value == "b" && e.Amount > 0));
        }

        [Test]
        public void Sleep_talk_uses_another_known_move_while_asleep()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "normal", 99, status: new StatusId("sleep"), moves: new[] { "sleep_talk", "tackle" }),
                Mon("b", "normal", 1, moves: "wait"));
            var ev = r.ResolveTurn(b, Use("sleep_talk"), Use("wait")).ToList();
            Assert.IsTrue(ev.OfType<MoveCalledEvent>().Any(e => e.Called.Value == "tackle"));
            Assert.IsTrue(ev.OfType<DamageDealtEvent>().Any(e => e.Target.Value == "b" && e.Amount > 0));
        }

        [Test]
        public void Weather_ball_changes_type_with_the_rain()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "water", 99, moves: new[] { "rain_dance", "weather_ball" }), Mon("b", "fire", 1, moves: "wait"));
            var dry = r.ResolveTurn(b, Use("weather_ball"), Use("wait")).OfType<DamageDealtEvent>().First(e => e.Target.Value == "b");
            Assert.AreEqual(1f, dry.Effectiveness);
            r.ResolveTurn(b, Use("rain_dance"), Use("wait")).ToList();
            var wet = r.ResolveTurn(b, Use("weather_ball"), Use("wait")).OfType<DamageDealtEvent>().First(e => e.Target.Value == "b");
            Assert.AreEqual(2f, wet.Effectiveness, "con lluvia es de tipo Agua");
        }

        [Test]
        public void Liquid_ooze_hurts_whoever_drains_it()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "grass", 99, moves: "giga_drain"), Mon("b", "poison", 1, ability: "liquid_ooze", moves: "wait"));
            var ev = r.ResolveTurn(b, Use("giga_drain"), Use("wait")).ToList();
            Assert.IsFalse(ev.OfType<HpRestoredEvent>().Any(e => e.Combatant.Value == "a"));
            Assert.IsTrue(ev.OfType<AbilityTriggeredEvent>().Any(e => e.Detail == "lodo"));
            Assert.Less(b.Player.CurrentHp, b.Player.MaxHp);
        }

        [Test]
        public void Magnet_rise_makes_ground_moves_miss_the_target()
        {
            var r = Resolver();
            var b = new CTEditor.Battle.Domain.Battle(Mon("a", "ground", 1, moves: "earthquake"), Mon("b", "electric", 99, moves: "magnet_rise"));
            var ev = r.ResolveTurn(b, Use("earthquake"), Use("magnet_rise")).ToList();
            Assert.IsTrue(ev.OfType<DamageDealtEvent>().Where(e => e.Target.Value == "b").All(e => e.Amount == 0));
        }

        [Test]
        public void Unaware_ignores_the_attackers_boosts()
        {
            var r = Resolver();
            var tackle = new Id<Move>("tackle");
            int Hit(string targetAbility, bool boost)
            {
                var b = new CTEditor.Battle.Domain.Battle(Mon("a", "normal", 99, moves: new[] { "tackle", "belly" }), Mon("t", "normal", 1, ability: targetAbility, moves: "wait"));
                if (boost) r.ResolveTurn(b, Use("belly"), Use("wait")).ToList();
                return r.PreviewDamage(b.Player, b.Enemy, tackle).Max;
            }
            int baseline = Hit(null, false);
            Assert.AreEqual(baseline, Hit("unaware", true), "Ignorante: como si no tuviera mejoras");
            Assert.Greater(Hit(null, true), baseline * 3);
        }

        // ---------------- Niveles de IA ----------------

        [Test]
        public void Ai_levels_unlock_machines_tutors_and_egg_moves_step_by_step()
        {
            var p = Enumerable.Range(1, 5).Select(AiProfile.Classic).ToList();
            Assert.IsFalse(p[0].UseMachineMoves);
            Assert.IsTrue(p[1].UseMachineMoves && !p[1].UseTutorMoves, "Aficionado: MT");
            Assert.IsTrue(p[2].UseTutorMoves && !p[2].UseEggMoves, "Veterano: + tutor");
            Assert.IsTrue(p[3].UseEggMoves && p[4].UseEggMoves, "Élite y Campeón: + huevo");
            Assert.Greater(p[1].MistakePercent, p[3].MistakePercent);
            Assert.AreEqual(HealStyle.Smart, p[2].Heal);
        }

        private static Species Sp(string id, string type, int stat, int speed, IEnumerable<Evolution> evos = null, params string[] moves)
            => new Species(new Id<Species>(id), id, new[] { new Id<ElementType>(type) },
                new StatBlock.Builder().Set(StatId.Hp, stat).Set(StatId.Attack, stat).Set(StatId.Defense, stat)
                    .Set(StatId.SpAttack, stat).Set(StatId.SpDefense, stat).Set(StatId.Speed, speed).Build(),
                moves.Select(m => new LearnableMove(new Id<Move>(m), 1)).ToList(), (evos ?? new Evolution[0]).ToList());

        private sealed class HalfRng : IRng
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive + (maxExclusive - minInclusive) / 2;
            public float NextFloat() => 0.5f;
        }

        [Test]
        public void Smart_healing_skips_potions_that_would_be_wasted()
        {
            var species = new[] { Sp("fire", "fire", 120, 200, null, "flame"), Sp("leafy", "grass", 60, 10, null, "leaf"),
                                  Sp("wet", "water", 60, 10, null, "wave") };
            var moves = new[]
            {
                M("flame", "fire", MoveCategory.Special, 90), M("leaf", "grass", MoveCategory.Special, 40), M("wave", "water", MoveCategory.Special, 90),
            };
            var data = new GameData(new MemoryCatalog<Species>(species, s => s.Id.Value), new MemoryCatalog<Move>(moves, m => m.Id.Value),
                new TypeChart.Builder().Set(new Id<ElementType>("fire"), new Id<ElementType>("grass"), 2f).Build(), Ruleset.Classic);
            BattleParticipant P(string id, string sp, int hp, int maxStat, string mv) => new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(sp), 50,
                new StatBlock.Builder().Set(StatId.Hp, 150).Set(StatId.Attack, maxStat).Set(StatId.Defense, maxStat).Set(StatId.SpAttack, maxStat)
                    .Set(StatId.SpDefense, maxStat).Set(StatId.Speed, sp == "fire" ? 200 : 10).Build(), hp,
                data.Species.Get(new Id<Species>(sp)).Types, new[] { new Id<Move>(mv) });
            var trainer = new TrainerDefinition("t", "t", new[] { new TeamMemberSpec(new Id<Species>("leafy"), 50) }, aiLevel: 3);

            // 1) Contra un golpe que le quita más que la cura: curarse no sirve.
            var b1 = new CTEditor.Battle.Domain.Battle(new[] { P("p", "fire", 150, 160, "flame") }, new[] { P("e", "leafy", 30, 60, "leaf") }, true);
            var r1 = data.CreateResolver(new HalfRng());
            var brain1 = new TrainerBrain(data, b1, r1, trainer, new HalfRng());
            Assert.IsFalse(brain1.WorthHealing(b1.Enemy, b1.Player, 150, 20f, 35), "el rival lo debilita igual: no malgasta la poción");

            // 2) Contra golpes flojos: la cura le da varios turnos más.
            var b2 = new CTEditor.Battle.Domain.Battle(new[] { P("p", "leafy", 150, 60, "leaf") }, new[] { P("e", "wet", 30, 60, "wave") }, true);
            var r2 = data.CreateResolver(new HalfRng());
            var brain2 = new TrainerBrain(data, b2, r2, trainer, new HalfRng());
            Assert.IsTrue(brain2.WorthHealing(b2.Enemy, b2.Player, 150, 20f, 35), "con golpes flojos sí le compensa curarse");
        }

        // ---------------- Preparar un reto ----------------

        [Test]
        public void Challenge_builder_picks_the_right_stage_and_puts_the_ace_last()
        {
            var all = new[]
            {
                Sp("bulba", "grass", 45, 45, new[] { new Evolution(new Id<Species>("ivy"), EvolutionMethod.Level, 16) }, "tackle"),
                Sp("ivy", "grass", 60, 60, new[] { new Evolution(new Id<Species>("venu"), EvolutionMethod.Level, 32) }, "tackle"),
                Sp("venu", "grass", 80, 80, null, "tackle"),
            };
            var req = new ChallengeRequest { Id = "reto_1", AiLevel = 4, TeamSize = 3, MinLevel = 20, MaxLevel = 40, Pool = new[] { new Id<Species>("venu") } };
            var t = ChallengeBuilder.Build(all, req, new SeededRng(5));
            Assert.AreEqual(3, t.Team.Count);
            Assert.AreEqual(40, t.Team.Last().Level, "el as tiene el nivel máximo");
            foreach (var m in t.Team)
                Assert.AreEqual(m.Level >= 32 ? "venu" : m.Level >= 16 ? "ivy" : "bulba", m.Species.Value, "etapa según el nivel " + m.Level);
            Assert.AreEqual(4, t.AiLevel);
            Assert.AreEqual("Experto", t.TrainerClass);
        }

        // ---------------- Planificador con MT ----------------

        [Test]
        public void Planner_uses_machine_moves_only_when_they_fit_the_level()
        {
            var tackle = Moves.First(m => m.Id.Value == "tackle");
            var quake = Moves.First(m => m.Id.Value == "earthquake");
            var learnset = new[] { (tackle, 1) };
            var types = new[] { new Id<ElementType>("ground") };
            var opts = new PlanOptions { ExtraMoves = new[] { quake } };
            var low = MovesetPlanner.Plan(learnset, types, 100, 50, 20, MovesetStyle.Strong, 4, opts);
            var high = MovesetPlanner.Plan(learnset, types, 100, 50, 50, MovesetStyle.Strong, 4, opts);
            Assert.IsFalse(low.Contains(quake.Id), "a nivel 20 no se regala Terremoto (potencia > " + MovesetPlanner.PowerCapFor(20) + ")");
            Assert.IsTrue(high.Contains(quake.Id), "a nivel 50 sí");
        }
    }
}
