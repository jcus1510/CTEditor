using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Editor;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Lote D: Pokédex, movimientos con requisitos (Comesueños), el planificador de movimientos de los
    /// entrenadores y las tablas de tipos por época.
    /// </summary>
    public class PokedexAndMovesetTests
    {
        // ---------------------------------------------------------------- utilidades
        private static MoveEffect Fx(MoveEffectKind k, EffectTarget t = EffectTarget.Opponent, string status = null,
            float amount = 0, string stat = null, int stages = 0)
            => new MoveEffect(new Percentage(100), k, t, status == null ? default : new StatusId(status),
                new Percentage(amount), stat == null ? default : new StatId(stat), stages);

        private static Move M(string id, string type, MoveCategory cat, int power, float? acc = 100, MoveTarget target = MoveTarget.SingleEnemy,
            IReadOnlyList<Condition> requirements = null, params MoveEffect[] fx)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), cat, power,
                acc.HasValue ? new Percentage(acc.Value) : (Percentage?)null, 10, 0, target, fx, requirements: requirements);

        private static readonly Condition TargetAsleep = new Condition(ConditionKind.HasStatus, ConditionSubject.Other, text: "sleep");

        // ---------------------------------------------------------------- Pokédex
        [Test]
        public void Pokedex_entry_formats_number_and_clamps_values()
        {
            var e = new PokedexEntry(1, "Semilla", 0.7f, 6.9f, "verde", "Una rara semilla...", 12.5f);
            Assert.AreEqual("Nº 001", e.NumberText);
            Assert.IsFalse(e.IsGenderless);
            StringAssert.Contains("Pokémon Semilla", e.Summary());

            var genderless = new PokedexEntry(137, "Virtual", 0.8f, 36.5f, femalePercent: -5f);
            Assert.IsTrue(genderless.IsGenderless);
            Assert.AreEqual(-1f, genderless.FemalePercent);
            Assert.AreEqual(100f, new PokedexEntry(113, "Huevo", 1.1f, 34.6f, femalePercent: 180f).FemalePercent);
            Assert.AreEqual("Nº ---", PokedexEntry.Empty.NumberText);
        }

        // ---------------------------------------------------------------- Comesueños (requisitos)
        private static readonly Move[] SleepMoves =
        {
            M("hypnosis", "psychic", MoveCategory.Status, 0, fx: Fx(MoveEffectKind.InflictStatus, status: "sleep")),
            M("dream_eater", "psychic", MoveCategory.Special, 100, requirements: new[] { TargetAsleep },
                fx: Fx(MoveEffectKind.Drain, EffectTarget.Self, amount: 50)),
            M("wait", "normal", MoveCategory.Status, 0, target: MoveTarget.Self),
        };

        private static BattleParticipant Mon(string id, int speed, params string[] moves) =>
            new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, 300).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                300, new List<Id<ElementType>> { new Id<ElementType>("normal") }, moves.Select(m => new Id<Move>(m)).ToList());

        private static TurnResolver SleepResolver() => new TurnResolver(
            new InMemoryCatalog<Move>(SleepMoves, m => m.Id.Value), new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(3),
            new InMemoryCatalog<StatusConditionDefinition>(new[]
            {
                new StatusConditionDefinition(new StatusId("sleep"), "Dormido", new Percentage(0), new Percentage(100), durationTurns: 3),
            }, s => s.Id.Value));

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));

        [Test]
        public void Dream_eater_fails_if_the_target_is_awake()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, "dream_eater"), Mon("b", 1, "wait"));
            var ev = SleepResolver().ResolveTurn(battle, Use("dream_eater"), Use("wait")).ToList();
            Assert.IsTrue(ev.OfType<MoveFailedEvent>().Any(e => e.Combatant.Value == "a"), "falla con el rival despierto");
            Assert.IsFalse(ev.OfType<DamageDealtEvent>().Any(e => e.Target.Value == "b"), "no hace daño");
        }

        [Test]
        public void Dream_eater_works_when_the_target_sleeps()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, "hypnosis", "dream_eater"), Mon("b", 1, "wait"));
            var resolver = SleepResolver();
            resolver.ResolveTurn(battle, Use("hypnosis"), Use("wait")).ToList();
            Assert.AreEqual("sleep", battle.Enemy.Status.Value.Value);
            var ev = resolver.ResolveTurn(battle, Use("dream_eater"), Use("wait")).ToList();
            Assert.IsFalse(ev.OfType<MoveFailedEvent>().Any(e => e.Combatant.Value == "a"));
            Assert.IsTrue(ev.OfType<DamageDealtEvent>().Any(e => e.Target.Value == "b" && e.Amount > 0), "hace daño al dormido");
        }

        [Test]
        public void Sleep_does_not_wear_off_in_the_same_turn_it_was_inflicted()
        {
            var moves = new InMemoryCatalog<Move>(SleepMoves, m => m.Id.Value);
            var resolver = new TurnResolver(moves, new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(3),
                new InMemoryCatalog<StatusConditionDefinition>(new[]
                {
                    // Duración 1: la versión más corta posible.
                    new StatusConditionDefinition(new StatusId("sleep"), "Dormido", new Percentage(0), new Percentage(100), durationTurns: 1),
                }, s => s.Id.Value));
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 99, "hypnosis"), Mon("b", 1, "wait"));
            var t1 = resolver.ResolveTurn(battle, Use("hypnosis"), Use("wait")).ToList();
            Assert.IsTrue(battle.Enemy.Status.HasValue, "sigue dormido al acabar el turno en que lo durmieron");
            Assert.IsFalse(t1.OfType<StatusFadedEvent>().Any());
            var t2 = resolver.ResolveTurn(battle, Use("hypnosis"), Use("wait")).ToList();
            Assert.IsTrue(t2.OfType<ActionPreventedEvent>().Any(e => e.Combatant.Value == "b"), "el turno siguiente tampoco actúa");
            Assert.IsFalse(battle.Enemy.Status.HasValue, "y al acabar ese turno se despierta");
        }

        // ---------------------------------------------------------------- Planificador de movimientos
        private static readonly Move Scratch = M("scratch", "normal", MoveCategory.Physical, 40);
        private static readonly Move Ember = M("ember", "fire", MoveCategory.Special, 40);
        private static readonly Move Flamethrower = M("flamethrower", "fire", MoveCategory.Special, 90);
        private static readonly Move Slash = M("slash", "normal", MoveCategory.Physical, 70);
        private static readonly Move WingAttack = M("wing_attack", "flying", MoveCategory.Physical, 60);
        private static readonly Move Growl = M("growl", "normal", MoveCategory.Status, 0,
            fx: Fx(MoveEffectKind.ChangeStatStage, stat: "attack", stages: -1));
        private static readonly Move Leer = M("leer", "normal", MoveCategory.Status, 0,
            fx: Fx(MoveEffectKind.ChangeStatStage, stat: "defense", stages: -1));
        private static readonly Move Splash = M("splash", "normal", MoveCategory.Status, 0, target: MoveTarget.Self);
        private static readonly Move Hypnosis = M("hypnosis", "psychic", MoveCategory.Status, 0, acc: 60,
            fx: Fx(MoveEffectKind.InflictStatus, status: "sleep"));

        private static readonly (Move, int)[] CharLearnset =
        {
            (Scratch, 1), (Growl, 1), (Ember, 7), (Leer, 13), (Slash, 30), (Flamethrower, 38), (WingAttack, 36), (Splash, 40),
        };
        private static readonly Id<ElementType>[] FireFlying = { new Id<ElementType>("fire"), new Id<ElementType>("flying") };

        [Test]
        public void Planner_resolves_the_style_from_the_ai()
        {
            Assert.AreEqual(MovesetStyle.Classic, MovesetPlanner.Resolve(MovesetStyle.ByAi, TrainerAi.Random));
            Assert.AreEqual(MovesetStyle.Balanced, MovesetPlanner.Resolve(MovesetStyle.ByAi, TrainerAi.Smart));
            Assert.AreEqual(MovesetStyle.Strong, MovesetPlanner.Resolve(MovesetStyle.ByAi, TrainerAi.Expert));
            Assert.AreEqual(MovesetStyle.Balanced, MovesetPlanner.Resolve(MovesetStyle.Balanced, TrainerAi.Expert), "lo elegido manda");
            Assert.IsNull(MovesetPlanner.Plan(CharLearnset, FireFlying, 84, 109, 50, MovesetStyle.Classic), "clásico lo resuelve la fábrica");
        }

        [Test]
        public void Strong_moveset_picks_the_best_stab_attack_and_coverage()
        {
            var plan = MovesetPlanner.Plan(CharLearnset, FireFlying, 84, 109, 50, MovesetStyle.Strong).Select(m => m.Value).ToList();
            Assert.AreEqual(4, plan.Count);
            Assert.AreEqual(plan.Count, plan.Distinct().Count(), "sin repetir");
            Assert.AreEqual("flamethrower", plan[0], "primero su mejor ataque con STAB");
            Assert.IsTrue(plan.Contains("wing_attack"), "cobertura de otro tipo con STAB");
            Assert.IsFalse(plan.Contains("splash"), "Salpicadura nunca");
            Assert.IsFalse(plan.Contains("ember") && plan.Contains("growl"), "no se queda con lo flojo si hay algo mejor");
        }

        [Test]
        public void Planner_only_uses_moves_learnt_up_to_the_level()
        {
            var plan = MovesetPlanner.Plan(CharLearnset, FireFlying, 84, 109, 20, MovesetStyle.Balanced).Select(m => m.Value).ToList();
            CollectionAssert.DoesNotContain(plan, "flamethrower");
            CollectionAssert.DoesNotContain(plan, "slash");
            Assert.LessOrEqual(plan.Count, 4);
            Assert.IsTrue(plan.Contains("ember"));
        }

        [Test]
        public void Balanced_moveset_keeps_a_good_support_move()
        {
            var learn = CharLearnset.Concat(new[] { (Hypnosis, 20) }).ToArray();
            var plan = MovesetPlanner.Plan(learn, FireFlying, 84, 109, 50, MovesetStyle.Balanced).Select(m => m.Value).ToList();
            Assert.IsTrue(plan.Contains("hypnosis"), "dormir es un apoyo que merece la pena");
            Assert.AreEqual("flamethrower", plan[0]);
        }

        [Test]
        public void Planner_scores_offense_by_the_right_stat_and_penalises_requirements()
        {
            double special = MovesetPlanner.Offense(Flamethrower, FireFlying, 0.5, 1.0, 50);
            double physicalWeak = MovesetPlanner.Offense(M("fire_punch", "fire", MoveCategory.Physical, 90), FireFlying, 0.5, 1.0, 50);
            Assert.Greater(special, physicalWeak, "con poco Ataque, el físico rinde menos");
            double dreamEater = MovesetPlanner.Offense(SleepMoves[1], new[] { new Id<ElementType>("psychic") }, 1, 1, 50);
            double psychic = MovesetPlanner.Offense(M("psychic", "psychic", MoveCategory.Special, 90), new[] { new Id<ElementType>("psychic") }, 1, 1, 50);
            Assert.Less(dreamEater, psychic, "Comesueños solo sirve con el rival dormido");
            Assert.AreEqual(0, MovesetPlanner.Support(Splash, true));
            Assert.Greater(MovesetPlanner.Support(Hypnosis, false), MovesetPlanner.Support(Growl, false));
        }

        // ---------------------------------------------------------------- Tablas de tipos por época
        [Test]
        public void Type_chart_eras_have_their_classic_differences()
        {
            var (modern, m) = TypeChartTools.Era(TypeChartTools.ChartEra.Modern);
            var (gen2, g2) = TypeChartTools.Era(TypeChartTools.ChartEra.Gen2To5);
            var (gen1, g1) = TypeChartTools.Era(TypeChartTools.ChartEra.Gen1);
            Assert.AreEqual(18, modern.Count);
            Assert.AreEqual(17, gen2.Count);
            Assert.AreEqual(15, gen1.Count);

            float V(Dictionary<(string, string), float> map, string a, string d) => map.TryGetValue((a, d), out var v) ? v : 1f;
            // Actual: Fantasma y Siniestro neutros contra Acero; Hada existe.
            Assert.AreEqual(1f, V(m, "ghost", "steel"));
            Assert.AreEqual(2f, V(m, "fairy", "dragon"));
            Assert.AreEqual(0.5f, V(m, "fire", "water"));
            Assert.AreEqual(0f, V(m, "normal", "ghost"));
            // 2ª-5ª: Acero resiste Fantasma y Siniestro; sin Hada.
            Assert.AreEqual(0.5f, V(g2, "ghost", "steel"));
            Assert.AreEqual(0.5f, V(g2, "dark", "steel"));
            Assert.IsFalse(g2.Keys.Any(k => k.Item1 == "fairy" || k.Item2 == "fairy"));
            // 1ª: Bicho y Veneno ×2 entre sí, Fantasma no afecta a Psíquico, Hielo neutro contra Fuego.
            Assert.AreEqual(2f, V(g1, "bug", "poison"));
            Assert.AreEqual(2f, V(g1, "poison", "bug"));
            Assert.AreEqual(0f, V(g1, "ghost", "psychic"));
            Assert.AreEqual(1f, V(g1, "ice", "fire"));
            Assert.IsFalse(g1.Keys.Any(k => k.Item1 == "steel" || k.Item2 == "dark"));
        }
    }
}
