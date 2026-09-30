using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Daño especial (Bomba Sónica, Sísmico, Superdiente, Guillotina) y etapas de precisión/evasión
    /// (Ataque Arena, Doble Equipo): lo que necesitan los movimientos de 1ª generación.
    /// </summary>
    public class SpecialDamageTests
    {
        private static Move M(string id, string type, FixedDamageKind kind, int amount = 0, float? acc = null, int power = 0,
            params MoveEffect[] effects)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), power == 0 && kind == FixedDamageKind.None ? MoveCategory.Status : MoveCategory.Physical,
                power, acc.HasValue ? new Percentage(acc.Value) : (Percentage?)null, 20, 0, MoveTarget.SingleEnemy, effects,
                fixedDamage: kind, fixedDamageAmount: amount);

        private static readonly Move[] Moves =
        {
            M("sonic_boom", "normal", FixedDamageKind.Fixed, 20),
            M("seismic_toss", "fighting", FixedDamageKind.UserLevel),
            M("super_fang", "normal", FixedDamageKind.HalfTargetHp),
            M("guillotine", "normal", FixedDamageKind.OneHitKo),
            M("tackle", "normal", FixedDamageKind.None, 0, 100f, 40),
            M("sand_attack", "ground", FixedDamageKind.None, 0, null, 0,
                new MoveEffect(new Percentage(100), MoveEffectKind.ChangeStatStage, stat: StatId.Accuracy, stages: -1)),
            M("double_team", "normal", FixedDamageKind.None, 0, null, 0,
                new MoveEffect(new Percentage(100), MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: StatId.Evasion, stages: 1)),
        };

        private static BattleParticipant Mon(string id, int level, int hp, string move, string type = "normal", int speed = 50) =>
            new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), level,
                new StatBlock.Builder().Set(StatId.Hp, hp).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>(type) },
                new List<Id<Move>> { new Id<Move>(move) });

        private static TurnResolver Resolver() => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Set(new Id<ElementType>("normal"), new Id<ElementType>("ghost"), 0f)
                .Set(new Id<ElementType>("fighting"), new Id<ElementType>("normal"), 2f).Build(),
            new ClassicDamageFormula(), new SeededRng(3));

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));

        private static int DamageTo(IEnumerable<CTEditor.SharedKernel.Events.IDomainEvent> events, string target)
            => events.OfType<DamageDealtEvent>().Where(e => e.Target.Value == target).Sum(e => e.Amount);

        [Test]
        public void Fixed_damage_ignores_stats_but_respects_immunity()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 50, 300, "sonic_boom", speed: 99), Mon("b", 50, 300, "sonic_boom"));
            var ev = Resolver().ResolveTurn(battle, Use("sonic_boom"), Use("sonic_boom"));
            Assert.AreEqual(20, DamageTo(ev, "b"));

            var ghost = new CTEditor.Battle.Domain.Battle(Mon("a", 50, 300, "sonic_boom", speed: 99), Mon("g", 50, 300, "sonic_boom", "ghost"));
            ev = Resolver().ResolveTurn(ghost, Use("sonic_boom"), Use("sonic_boom"));
            Assert.AreEqual(0, DamageTo(ev, "g"));
        }

        [Test]
        public void Level_damage_equals_user_level_even_when_super_effective()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 37, 300, "seismic_toss", speed: 99), Mon("b", 50, 300, "sonic_boom"));
            var ev = Resolver().ResolveTurn(battle, Use("seismic_toss"), Use("sonic_boom"));
            Assert.AreEqual(37, DamageTo(ev, "b"));
            Assert.AreEqual(1f, ev.OfType<DamageDealtEvent>().First(e => e.Target.Value == "b").Effectiveness);
        }

        [Test]
        public void Super_fang_halves_current_hp()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 50, 300, "super_fang", speed: 99), Mon("b", 50, 301, "sonic_boom"));
            var ev = Resolver().ResolveTurn(battle, Use("super_fang"), Use("sonic_boom"));
            Assert.AreEqual(150, DamageTo(ev, "b"));
        }

        [Test]
        public void One_hit_ko_works_on_lower_level_and_fails_on_higher_level()
        {
            var low = new CTEditor.Battle.Domain.Battle(Mon("a", 50, 300, "guillotine", speed: 99), Mon("b", 40, 999, "sonic_boom"));
            var ev = Resolver().ResolveTurn(low, Use("guillotine"), Use("sonic_boom"));
            Assert.IsTrue(ev.OfType<MonsterFaintedEvent>().Any(e => e.Combatant.Value == "b"));

            var high = new CTEditor.Battle.Domain.Battle(Mon("a", 50, 300, "guillotine", speed: 99), Mon("b", 60, 999, "sonic_boom"));
            ev = Resolver().ResolveTurn(high, Use("guillotine"), Use("sonic_boom"));
            Assert.IsTrue(ev.OfType<MoveMissedEvent>().Any(e => e.Attacker.Value == "a"));
            Assert.AreEqual(0, DamageTo(ev, "b"));
        }

        [Test]
        public void Accuracy_and_evasion_stages_change_the_hit_chance()
        {
            // b usa Ataque Arena sobre a: la precisión de a baja una etapa (×0,75).
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", 50, 300, "tackle"), Mon("b", 50, 300, "sand_attack", speed: 99));
            var r = Resolver();
            r.ResolveTurn(battle, Use("tackle"), Use("sand_attack"));
            Assert.AreEqual(75f, r.PreviewDamage(battle.Player, battle.Enemy, new Id<Move>("tackle")).AccuracyPercent.Value, 0.01);

            // Y si además b sube su evasión: -2 en total (×0,6).
            var b2 = new CTEditor.Battle.Domain.Battle(Mon("a", 50, 300, "tackle"), Mon("b", 50, 300, "double_team", speed: 99));
            var r2 = Resolver();
            r2.ResolveTurn(b2, Use("tackle"), Use("double_team"));
            r2.ResolveTurn(b2, Use("tackle"), Use("double_team"));
            Assert.AreEqual(60f, r2.PreviewDamage(b2.Player, b2.Enemy, new Id<Move>("tackle")).AccuracyPercent.Value, 0.01);
        }
    }
}
