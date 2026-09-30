using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;
using B = CTEditor.Battle.Domain.Battle;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Lote B (estados volátiles: se apilan con el principal; atrapar, drenadoras, protección, aguante)
    /// y Lote A (condiciones, potencia con modificadores y fórmula, stats del daño, críticos
    /// configurables, curar a otros / curar estados, clima, amistad, dado compartido).
    /// </summary>
    public class LoteABTests
    {
        // ---------------- Contenido de prueba ----------------

        private static MoveEffect Fx(MoveEffectKind k, EffectTarget t = EffectTarget.Opponent, string status = null,
            float amount = 0, string stat = null, int stages = 0, float chance = 100, bool shared = false,
            Condition[] when = null, string weather = null)
            => new MoveEffect(new Percentage(chance), k, t, status == null ? default : new StatusId(status),
                new Percentage(amount), stat == null ? default : new StatId(stat), stages, when, shared, weather);

        private static Move M(string id, string type, MoveCategory cat, int power, int priority = 0, MoveTarget target = MoveTarget.SingleEnemy,
            IReadOnlyList<PowerModifier> mods = null, string formula = null, string atk = null, string def = null,
            bool fromTarget = false, string[] tags = null, bool contact = false, params MoveEffect[] fx)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), cat, power, null, 30, priority, target, fx,
                makesContact: contact, powerModifiers: mods, powerFormula: formula,
                attackStat: atk == null ? (StatId?)null : new StatId(atk), defenseStat: def == null ? (StatId?)null : new StatId(def),
                attackStatFromTarget: fromTarget, tags: tags);

        private static readonly Move[] Moves =
        {
            M("tackle", "normal", MoveCategory.Physical, 40, contact: true),
            M("punch", "normal", MoveCategory.Physical, 40, tags: new[] { "puño" }),
            M("wait", "normal", MoveCategory.Status, 0, target: MoveTarget.Self),
            M("burn_it", "normal", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.InflictStatus, status: "burn")),
            M("paralyze_it", "normal", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.InflictStatus, status: "paralysis")),
            M("confuse_it", "normal", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.InflictStatus, status: "confusion")),
            M("wrap", "normal", MoveCategory.Physical, 15, fx: Fx(MoveEffectKind.InflictStatus, status: "trapped")),
            M("leech_seed", "grass", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.InflictStatus, status: "leech_seed")),
            M("protect", "normal", MoveCategory.Status, 0, priority: 4, target: MoveTarget.Self, fx: Fx(MoveEffectKind.InflictStatus, EffectTarget.Self, "protect")),
            M("endure", "normal", MoveCategory.Status, 0, priority: 4, target: MoveTarget.Self, fx: Fx(MoveEffectKind.InflictStatus, EffectTarget.Self, "endure")),
            M("nuke", "normal", MoveCategory.Special, 250),
            M("facade", "normal", MoveCategory.Physical, 70,
                mods: new[] { new PowerModifier(2f, new[] { new Condition(ConditionKind.HasAnyStatus, ConditionSubject.Self) }) }),
            M("eruption", "fire", MoveCategory.Special, 150, formula: "max(1, floor(150 * vida / 100))"),
            M("return", "normal", MoveCategory.Physical, 102, formula: "max(1, floor(amistad / 2.5))"),
            M("water_gun", "water", MoveCategory.Special, 40),
            M("rain_dance", "water", MoveCategory.Status, 0, target: MoveTarget.Self, fx: Fx(MoveEffectKind.SetWeather, EffectTarget.Self, weather: "rain")),
            M("sandstorm", "rock", MoveCategory.Status, 0, target: MoveTarget.Self, fx: Fx(MoveEffectKind.SetWeather, EffectTarget.Self, weather: "sandstorm")),
            M("psyshock", "psychic", MoveCategory.Special, 80, def: "defense"),
            M("foul_play", "dark", MoveCategory.Physical, 95, fromTarget: true),
            M("heal_pulse", "psychic", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.Heal, EffectTarget.Opponent, amount: 50)),
            M("refresh", "normal", MoveCategory.Status, 0, target: MoveTarget.Self, fx: Fx(MoveEffectKind.CureStatus, EffectTarget.Self)),
            M("desperation", "normal", MoveCategory.Status, 0, target: MoveTarget.Self, fx:
                Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "attack", stages: 2,
                   when: new[] { new Condition(ConditionKind.HpPercent, ConditionSubject.Self, Comparison.LessOrEqual, 50) })),
            M("ancient_power", "rock", MoveCategory.Special, 60, fx: new[]
            {
                Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "attack", stages: 1, chance: 10),
                Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "defense", stages: 1, chance: 10, shared: true),
                Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "speed", stages: 1, chance: 10, shared: true),
            }),
        };

        private static readonly StatusConditionDefinition[] Statuses =
        {
            new StatusConditionDefinition(new StatusId("burn"), "Quemado", new Percentage(6.25f), new Percentage(0),
                passiveModifiers: new[] { new StatPassiveModifier(StatId.Attack, 0.5f) }, immuneTypes: new[] { new Id<ElementType>("fire") }),
            new StatusConditionDefinition(new StatusId("paralysis"), "Parálisis", new Percentage(0), new Percentage(0)),
            new StatusConditionDefinition(new StatusId("confusion"), "Confusión", new Percentage(0), new Percentage(100),
                clearedOnSwitch: true, durationTurns: 2, selfDamageOnPreventedPercent: new Percentage(10), isVolatile: true, durationMaxTurns: 5),
            new StatusConditionDefinition(new StatusId("trapped"), "Atrapado", new Percentage(12.5f), new Percentage(0),
                durationTurns: 4, isVolatile: true, durationMaxTurns: 5, preventsSwitch: true),
            new StatusConditionDefinition(new StatusId("leech_seed"), "Drenadoras", new Percentage(12.5f), new Percentage(0),
                immuneTypes: new[] { new Id<ElementType>("grass") }, isVolatile: true, residualHealsOpponent: true),
            new StatusConditionDefinition(new StatusId("protect"), "Protegido", new Percentage(0), new Percentage(0),
                durationTurns: 1, isVolatile: true, blocksIncomingMoves: true, harderWhenRepeated: true),
            new StatusConditionDefinition(new StatusId("endure"), "Aguante", new Percentage(0), new Percentage(0),
                durationTurns: 1, isVolatile: true, survivesLethalHit: true),
        };

        private static readonly WeatherDefinition[] Weathers =
        {
            new WeatherDefinition("rain", "Lluvia", 5, new Dictionary<Id<ElementType>, float>
                { [new Id<ElementType>("water")] = 1.5f, [new Id<ElementType>("fire")] = 0.5f }),
            new WeatherDefinition("sandstorm", "Arena", 2, null, new Percentage(6.25f), new[] { new Id<ElementType>("rock") }),
        };

        private static readonly AbilityDefinition[] Abilities =
        {
            new AbilityDefinition(new AbilityId("technician"), "Experto",
                offensivePowerModifiers: new[] { new PowerModifier(1.5f, new[] { new Condition(ConditionKind.MovePower, comparison: Comparison.LessOrEqual, number: 60) }) }),
            new AbilityDefinition(new AbilityId("iron_fist"), "Puño Férreo",
                offensivePowerModifiers: new[] { new PowerModifier(1.2f, new[] { new Condition(ConditionKind.MoveHasTag, text: "puño") }) }),
            new AbilityDefinition(new AbilityId("fluffy"), "Peluche",
                defensivePowerModifiers: new[] { new PowerModifier(0.5f, new[] { new Condition(ConditionKind.MoveMakesContact) }) }),
        };

        private static BattleParticipant Mon(string id, string move = "wait", string type = "normal", int speed = 50, int hp = 400,
            int level = 50, string ability = null, int friendship = 70, int atk = 100, int def = 100, int spdef = 100) =>
            new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), level,
                new StatBlock.Builder().Set(StatId.Hp, hp).Set(StatId.Attack, atk).Set(StatId.Defense, def)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, spdef).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>(type) }, new List<Id<Move>> { new Id<Move>(move) },
                ability: ability == null ? (AbilityId?)null : new AbilityId(ability), friendship: friendship);

        private static TurnResolver Resolver(IRng rng = null, BattleRules rules = null) => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Build(), new ClassicDamageFormula(), rng ?? new SeededRng(7),
            new InMemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value),
            abilities: new InMemoryCatalog<AbilityDefinition>(Abilities, a => a.Id.Value),
            weathers: new InMemoryCatalog<WeatherDefinition>(Weathers, w => w.Id), rules: rules);

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));
        private static Move Get(string id) => Moves.First(m => m.Id.Value == id);

        /// <summary>Azar fijo: NextFloat siempre devuelve 'f'; Next siempre el mínimo.</summary>
        private sealed class FixedRng : IRng
        {
            private readonly float _f;
            public FixedRng(float f) { _f = f; }
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => _f;
        }

        // ---------------- Lote B: volátiles ----------------

        [Test]
        public void Volatile_statuses_stack_with_the_principal_but_principals_are_exclusive()
        {
            var b = new B(Mon("a", "burn_it", speed: 99), Mon("b"));
            var r = Resolver();
            r.ResolveTurn(b, Use("burn_it"), Use("wait"));
            r.ResolveTurn(b, Use("paralyze_it"), Use("wait"));   // no pisa la quemadura
            r.ResolveTurn(b, Use("confuse_it"), Use("wait"));    // pero la confusión SÍ se suma
            Assert.AreEqual("burn", b.Enemy.Status.Value.Value);
            Assert.IsTrue(b.Enemy.HasVolatile(new StatusId("confusion")));
            Assert.IsTrue(b.Enemy.HasStatus(new StatusId("burn")) && b.Enemy.HasStatus(new StatusId("confusion")));
        }

        [Test]
        public void A_confused_and_burned_monster_suffers_both()
        {
            var b = new B(Mon("a", "burn_it", speed: 99), Mon("b", "tackle"));
            var r = Resolver();
            r.ResolveTurn(b, Use("burn_it"), Use("wait"));
            r.ResolveTurn(b, Use("confuse_it"), Use("wait"));
            var ev = r.ResolveTurn(b, Use("wait"), Use("tackle"));
            Assert.IsTrue(ev.OfType<ActionPreventedEvent>().Any(e => e.Status.Value == "confusion"), "la confusión (100%) le impide actuar");
            Assert.IsTrue(ev.OfType<StatusDamageEvent>().Any(e => e.Status.Value == "confusion"), "y se golpea a sí mismo");
            Assert.IsTrue(ev.OfType<StatusDamageEvent>().Any(e => e.Status.Value == "burn"), "y además le quema");
        }

        [Test]
        public void Trapped_monsters_cannot_switch_or_flee_and_lose_hp_each_turn()
        {
            var b = new B(new[] { Mon("a", "wait"), Mon("a2") }, new[] { Mon("b", "wrap", speed: 99) });
            var r = Resolver();
            r.ResolveTurn(b, Use("wait"), Use("wrap"));
            var ev = r.ResolveTurn(b, new SwitchMonster(new Id<BattleParticipant>("a2")), Use("wait"));
            Assert.IsTrue(ev.OfType<SwitchPreventedEvent>().Any());
            Assert.AreEqual("a", b.Player.Id.Value, "sigue en el campo");
            Assert.IsTrue(ev.OfType<StatusDamageEvent>().Any(e => e.Status.Value == "trapped"), "pierde 1/8 por turno");

            ev = r.ResolveTurn(b, new Flee(), Use("wait"));
            Assert.IsFalse(b.IsOver, "tampoco puede huir");
        }

        [Test]
        public void Leech_seed_heals_the_opponent_and_grass_is_immune()
        {
            var b = new B(Mon("a", "leech_seed", speed: 99, hp: 400), Mon("b", hp: 400));
            var r = Resolver();
            r.ResolveTurn(b, Use("tackle"), Use("tackle")); // ambos se hacen daño primero
            int before = b.Player.CurrentHp;
            var ev = r.ResolveTurn(b, Use("leech_seed"), Use("wait"));
            Assert.IsTrue(ev.OfType<StatusDamageEvent>().Any(e => e.Status.Value == "leech_seed"));
            Assert.Greater(b.Player.CurrentHp, before, "quien plantó la semilla se cura");

            var grass = new B(Mon("a", "leech_seed", speed: 99), Mon("g", type: "grass"));
            Resolver().ResolveTurn(grass, Use("leech_seed"), Use("wait"));
            Assert.IsFalse(grass.Enemy.HasAnyStatus, "Planta es inmune");
        }

        [Test]
        public void Protect_blocks_attacks_and_fails_when_repeated_with_bad_luck()
        {
            var b = new B(Mon("a", "protect"), Mon("b", "tackle", speed: 99));
            var r = Resolver(new FixedRng(0.99f)); // "mala suerte" en todas las tiradas
            var ev = r.ResolveTurn(b, Use("protect"), Use("tackle"));
            Assert.IsTrue(ev.OfType<MoveBlockedEvent>().Any(), "prioridad +4: se protege antes del golpe");
            Assert.AreEqual(400, b.Player.CurrentHp);

            ev = r.ResolveTurn(b, Use("protect"), Use("tackle"));
            Assert.IsTrue(ev.OfType<StatusFailedEvent>().Any(), "la segunda vez seguida falla (1/3)");
            Assert.Less(b.Player.CurrentHp, 400);
        }

        [Test]
        public void Endure_leaves_one_hp()
        {
            var b = new B(Mon("a", "endure", hp: 50), Mon("b", "nuke", speed: 99));
            var ev = Resolver().ResolveTurn(b, Use("endure"), Use("nuke"));
            Assert.AreEqual(1, b.Player.CurrentHp);
            Assert.IsTrue(ev.OfType<EnduredEvent>().Any());
        }

        [Test]
        public void Switching_out_clears_volatiles_and_stat_stages_but_keeps_the_principal()
        {
            var b = new B(new[] { Mon("a"), Mon("a2") }, new[] { Mon("b", "burn_it", speed: 99) });
            var r = Resolver();
            r.ResolveTurn(b, Use("wait"), Use("burn_it"));
            r.ResolveTurn(b, Use("wait"), Use("confuse_it"));
            var a = b.Player;
            r.ResolveTurn(b, new SwitchMonster(new Id<BattleParticipant>("a2")), Use("wait"));
            Assert.IsFalse(a.HasVolatile(new StatusId("confusion")));
            Assert.AreEqual("burn", a.Status.Value.Value);
        }

        [Test]
        public void Confusion_duration_is_rolled_between_min_and_max()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var b = new B(Mon("a", "confuse_it", speed: 99), Mon("b"));
                Resolver(new SeededRng(seed)).ResolveTurn(b, Use("confuse_it"), Use("wait"));
                int d = b.Enemy.Volatiles.First().Duration;
                Assert.IsTrue(d >= 2 && d <= 5, $"duración {d}");
            }
        }

        // ---------------- Lote A: potencia, condiciones, stats ----------------

        [Test]
        public void Power_modifiers_apply_only_when_their_conditions_hold()
        {
            var r = Resolver();
            var b = new B(Mon("a", "facade"), Mon("b", "burn_it", speed: 99));
            Assert.AreEqual(70, r.EffectivePower(b.Player, b.Enemy, Get("facade")));
            r.ResolveTurn(b, Use("wait"), Use("burn_it"));
            Assert.AreEqual(140, r.EffectivePower(b.Player, b.Enemy, Get("facade")), "Fachada ×2 con estado");
        }

        [Test]
        public void Power_formulas_use_hp_and_friendship()
        {
            var r = Resolver();
            var full = new B(Mon("a", friendship: 255), Mon("b"));
            Assert.AreEqual(150, r.EffectivePower(full.Player, full.Enemy, Get("eruption")));
            Assert.AreEqual(102, r.EffectivePower(full.Player, full.Enemy, Get("return")));

            var low = new B(Mon("a", hp: 400, friendship: 0), Mon("b", "nuke", speed: 99));
            r.ResolveTurn(low, Use("wait"), Use("nuke"));
            int hpPct = low.Player.CurrentHp * 100 / low.Player.MaxHp;
            Assert.Less(r.EffectivePower(low.Player, low.Enemy, Get("eruption")), 150);
            Assert.AreEqual(1, r.EffectivePower(low.Player, low.Enemy, Get("return")), "amistad 0 → mínimo 1");
        }

        [Test]
        public void Abilities_can_boost_or_reduce_power_with_conditions()
        {
            var r = Resolver();
            var tech = new B(Mon("a", ability: "technician"), Mon("b"));
            Assert.AreEqual(60, r.EffectivePower(tech.Player, tech.Enemy, Get("tackle")), "40 ≤ 60 → ×1,5");
            Assert.AreEqual(250, r.EffectivePower(tech.Player, tech.Enemy, Get("nuke")), "250 > 60 → sin cambio");

            var fist = new B(Mon("a", ability: "iron_fist"), Mon("b"));
            Assert.AreEqual(48, r.EffectivePower(fist.Player, fist.Enemy, Get("punch")), "etiqueta 'puño' → ×1,2");

            var fluffy = new B(Mon("a"), Mon("b", ability: "fluffy"));
            Assert.AreEqual(20, r.EffectivePower(fluffy.Player, fluffy.Enemy, Get("tackle")), "contacto contra Peluche → ×0,5");
        }

        [Test]
        public void Effects_can_have_conditions()
        {
            var r = Resolver();
            var healthy = new B(Mon("a", "desperation", speed: 99), Mon("b"));
            var ev = r.ResolveTurn(healthy, Use("desperation"), Use("wait"));
            Assert.IsFalse(ev.OfType<StatStageChangedEvent>().Any(), "con toda la vida no se cumple");

            var hurt = new B(Mon("a", "desperation", hp: 120), Mon("b", "nuke", speed: 99, level: 30));
            r = Resolver(rules: new BattleRules(new[] { 0 })); // sin críticos: el golpe deja entre el 40% y el 49%
            r.ResolveTurn(hurt, Use("wait"), Use("nuke"));
            Assert.LessOrEqual(hurt.Player.CurrentHp * 100 / hurt.Player.MaxHp, 50);
            ev = r.ResolveTurn(hurt, Use("desperation"), Use("wait"));
            Assert.IsTrue(ev.OfType<StatStageChangedEvent>().Any(e => e.Delta == 2), "con la mitad de vida o menos, sí");
        }

        [Test]
        public void Damage_stats_can_be_changed_per_move()
        {
            var r = Resolver();
            // Psicocarga (especial) golpea la DEFENSA: contra alguien con mucha Def. Esp. y poca Defensa, pega más.
            var b = new B(Mon("a"), Mon("b", def: 50, spdef: 300));
            var psyshock = r.PreviewDamage(b.Player, b.Enemy, new Id<Move>("psyshock"));
            Assert.Greater(psyshock.Max, 0);
            var weak = new B(Mon("a"), Mon("b", def: 300, spdef: 50));
            Assert.Greater(psyshock.Max, r.PreviewDamage(weak.Player, weak.Enemy, new Id<Move>("psyshock")).Max);

            // Juego Sucio usa el Ataque DEL RIVAL.
            var strongFoe = new B(Mon("a", atk: 10), Mon("b", atk: 300));
            var weakFoe = new B(Mon("a", atk: 10), Mon("b", atk: 30));
            Assert.Greater(r.PreviewDamage(strongFoe.Player, strongFoe.Enemy, new Id<Move>("foul_play")).Max,
                           r.PreviewDamage(weakFoe.Player, weakFoe.Enemy, new Id<Move>("foul_play")).Max);
        }

        [Test]
        public void The_crit_table_is_configurable()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var never = new B(Mon("a", "tackle", speed: 99), Mon("b"));
                var ev = Resolver(new SeededRng(seed), new BattleRules(new[] { 0 })).ResolveTurn(never, Use("tackle"), Use("wait"));
                Assert.IsFalse(ev.OfType<CriticalHitEvent>().Any(), "tabla {0} = nunca");

                var always = new B(Mon("a", "tackle", speed: 99), Mon("b"));
                ev = Resolver(new SeededRng(seed), new BattleRules(new[] { 1 })).ResolveTurn(always, Use("tackle"), Use("wait"));
                Assert.IsTrue(ev.OfType<CriticalHitEvent>().Any(), "tabla {1} = siempre");
            }
        }

        [Test]
        public void Heal_can_target_the_opponent_and_statuses_can_be_cured()
        {
            var r = Resolver();
            var b = new B(Mon("a", "nuke", speed: 99), Mon("b", hp: 400));
            r.ResolveTurn(b, Use("nuke"), Use("wait"));
            int hurt = b.Enemy.CurrentHp;
            r.ResolveTurn(b, Use("heal_pulse"), Use("wait"));
            Assert.Greater(b.Enemy.CurrentHp, hurt, "Pulso Cura cura al RIVAL");

            var c = new B(Mon("a", "refresh"), Mon("b", "burn_it", speed: 99));
            r.ResolveTurn(c, Use("wait"), Use("burn_it"));
            Assert.IsTrue(c.Player.Status.HasValue);
            r.ResolveTurn(c, Use("refresh"), Use("wait"));
            Assert.IsFalse(c.Player.Status.HasValue, "Alivio cura el estado principal");
        }

        [Test]
        public void Shared_roll_raises_all_stats_together_or_none()
        {
            int all = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var b = new B(Mon("a", "ancient_power", speed: 99), Mon("b", hp: 999));
                var ev = Resolver(new SeededRng(seed)).ResolveTurn(b, Use("ancient_power"), Use("wait"));
                int n = ev.OfType<StatStageChangedEvent>().Count(e => e.Combatant.Value == "a");
                Assert.IsTrue(n == 0 || n == 3, $"semilla {seed}: subió {n} stats (debe ser 0 o 3)");
                if (n == 3) all++;
            }
            Assert.Greater(all, 0, "alguna vez sale el 10%");
        }

        [Test]
        public void Weather_boosts_types_damages_and_ends()
        {
            var r = Resolver();
            var b = new B(Mon("a", "rain_dance", speed: 99), Mon("b"));
            Assert.AreEqual(40, r.EffectivePower(b.Player, b.Enemy, Get("water_gun")));
            var ev = r.ResolveTurn(b, Use("rain_dance"), Use("wait"));
            Assert.IsTrue(ev.OfType<WeatherStartedEvent>().Any(e => e.WeatherId == "rain"));
            Assert.AreEqual("rain", b.WeatherId);
            Assert.AreEqual(60, r.EffectivePower(b.Player, b.Enemy, Get("water_gun")), "Agua ×1,5 con lluvia");
            Assert.AreEqual(75, r.EffectivePower(b.Player, b.Enemy, Get("eruption")), "Fuego ×0,5 con lluvia");

            var sand = new B(Mon("a", "sandstorm", speed: 99), Mon("r", type: "rock"));
            ev = r.ResolveTurn(sand, Use("sandstorm"), Use("wait"));
            Assert.IsTrue(ev.OfType<WeatherDamageEvent>().Any(e => e.Combatant.Value == "a"));
            Assert.IsFalse(ev.OfType<WeatherDamageEvent>().Any(e => e.Combatant.Value == "r"), "Roca es inmune");
            ev = r.ResolveTurn(sand, Use("wait"), Use("wait"));
            Assert.IsTrue(ev.OfType<WeatherEndedEvent>().Any(), "la arena dura 2 turnos");
            Assert.IsNull(sand.WeatherId);
        }

        [Test]
        public void Weather_friendship_and_level_conditions()
        {
            var r = Resolver();
            var rainy = new Move(new Id<Move>("x"), "x", new Id<ElementType>("normal"), MoveCategory.Physical, 50, null, 5, 0, MoveTarget.SingleEnemy,
                powerModifiers: new[]
                {
                    new PowerModifier(2f, new[] { new Condition(ConditionKind.Weather, text: "rain") }),
                    new PowerModifier(3f, new[] { new Condition(ConditionKind.Friendship, ConditionSubject.Self, Comparison.GreaterOrEqual, 200) }),
                    new PowerModifier(5f, new[] { new Condition(ConditionKind.LevelDifference, ConditionSubject.Self, Comparison.Greater, 0) }),
                });
            var b = new B(Mon("a", "rain_dance", speed: 99, friendship: 220, level: 60), Mon("b", level: 50));
            Assert.AreEqual(50 * 3 * 5, r.EffectivePower(b.Player, b.Enemy, rainy), "amistad ≥ 200 y más nivel");
            r.ResolveTurn(b, Use("rain_dance"), Use("wait"));
            Assert.AreEqual(50 * 2 * 3 * 5, r.EffectivePower(b.Player, b.Enemy, rainy), "y además llueve");
        }
    }
}
