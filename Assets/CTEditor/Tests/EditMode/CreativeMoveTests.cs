using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
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
    /// "¿Puede el autor inventar lo que quiera?": movimientos que COMBINAN muchos efectos a la vez,
    /// estados inventados con sus propias inmunidades, y las reglas de inmunidad de tipo (un golpe que
    /// no afecta tampoco aplica sus efectos; los movimientos de estado pueden respetar la tabla).
    /// </summary>
    public class CreativeMoveTests
    {
        private static MoveEffect Fx(MoveEffectKind k, EffectTarget t = EffectTarget.Opponent, string status = null,
            float amount = 0, string stat = null, int stages = 0)
            => new MoveEffect(new Percentage(100), k, t,
                status == null ? default : new StatusId(status),
                new Percentage(amount), stat == null ? default : new StatId(stat), stages);

        private static Move M(string id, string type, MoveCategory cat, int power, int minHits = 1, int maxHits = 1,
            bool respectsImmunity = false, MoveTarget target = MoveTarget.SingleEnemy, float? acc = null, params MoveEffect[] fx)
            => new Move(new Id<Move>(id), id, new Id<ElementType>(type), cat, power,
                acc.HasValue ? new Percentage(acc.Value) : (Percentage?)null, 10, 0, target, fx, minHits, maxHits,
                respectsTypeImmunity: respectsImmunity);

        // Un movimiento "de autor": 2 golpes, quema seguro, drena, sube su Ataque, baja la Defensa rival,
        // pierde un 10% de PS máx y sube una stat INVENTADA ("suerte").
        private static readonly Move Kitchen = M("kitchen_sink", "fire", MoveCategory.Physical, 40, 2, 2, fx: new[]
        {
            Fx(MoveEffectKind.InflictStatus, status: "burn"),
            Fx(MoveEffectKind.Drain, EffectTarget.Self, amount: 50),
            Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "attack", stages: 1),
            Fx(MoveEffectKind.ChangeStatStage, stat: "defense", stages: -1),
            Fx(MoveEffectKind.RecoilMaxHp, EffectTarget.Self, amount: 10),
            Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "suerte", stages: 2),
        });

        private static readonly Move[] Moves =
        {
            Kitchen,
            M("zap", "electric", MoveCategory.Special, 60, fx: new[] { Fx(MoveEffectKind.InflictStatus, status: "paralysis"),
                                                                    Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "speed", stages: 1) }),
            M("thunder_wave", "electric", MoveCategory.Status, 0, respectsImmunity: true, acc: 100, fx: new[] { Fx(MoveEffectKind.InflictStatus, status: "paralysis") }),
            M("glare", "normal", MoveCategory.Status, 0, acc: 100, fx: new[] { Fx(MoveEffectKind.InflictStatus, status: "paralysis") }),
            M("hex_curse", "ghost", MoveCategory.Status, 0, fx: new[] { Fx(MoveEffectKind.InflictStatus, status: "maldicion") }),
            M("focus", "normal", MoveCategory.Status, 0, target: MoveTarget.Self, acc: 100,
                fx: new[] { Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "attack", stages: 1) }),
            M("double_team", "normal", MoveCategory.Status, 0, target: MoveTarget.Self,
                fx: new[] { Fx(MoveEffectKind.ChangeStatStage, EffectTarget.Self, stat: "evasion", stages: 1) }),
            M("wait", "normal", MoveCategory.Status, 0, target: MoveTarget.Self),
        };

        private static readonly StatusConditionDefinition[] Statuses =
        {
            new StatusConditionDefinition(new StatusId("burn"), "Quemado", new Percentage(6.25f), new Percentage(0),
                immuneTypes: new[] { new Id<ElementType>("fire") }),
            new StatusConditionDefinition(new StatusId("paralysis"), "Parálisis", new Percentage(0), new Percentage(25),
                immuneTypes: new[] { new Id<ElementType>("electric") }),
            // Estado INVENTADO: 10% por turno, Velocidad a la mitad, y los Fantasma son inmunes.
            new StatusConditionDefinition(new StatusId("maldicion"), "Maldición", new Percentage(10), new Percentage(0),
                passiveModifiers: new[] { new StatPassiveModifier(StatId.Speed, 0.5f) },
                immuneTypes: new[] { new Id<ElementType>("ghost") }),
        };

        private static BattleParticipant Mon(string id, string type, string move, int speed = 50, int hp = 400) =>
            new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, hp).Set(StatId.Attack, 100).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, 100).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                hp, new List<Id<ElementType>> { new Id<ElementType>(type) }, new List<Id<Move>> { new Id<Move>(move) });

        private static TurnResolver Resolver() => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Set(new Id<ElementType>("electric"), new Id<ElementType>("ground"), 0f).Build(),
            new ClassicDamageFormula(), new SeededRng(11),
            new InMemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value));

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));

        private static List<IDomainEvent> Turn(string aType, string aMove, string bType, string bMove, out CTEditor.Battle.Domain.Battle battle)
        {
            battle = new CTEditor.Battle.Domain.Battle(Mon("a", aType, aMove, 99), Mon("b", bType, bMove, 1));
            return Resolver().ResolveTurn(battle, Use(aMove), Use(bMove)).ToList();
        }

        [Test]
        public void A_move_can_combine_every_kind_of_effect_at_once()
        {
            var ev = Turn("normal", "kitchen_sink", "normal", "wait", out var battle);

            Assert.AreEqual(2, ev.OfType<DamageDealtEvent>().Count(e => e.Target.Value == "b"), "dos golpes");
            Assert.IsTrue(ev.OfType<StatusInflictedEvent>().Any(e => e.Target.Value == "b"), "quema");
            Assert.IsTrue(ev.OfType<HpRestoredEvent>().Any(e => e.Combatant.Value == "a"), "drena");
            Assert.IsTrue(ev.OfType<RecoilDamageEvent>().Any(e => e.Combatant.Value == "a"), "retroceso");
            var stages = ev.OfType<StatStageChangedEvent>().ToList();
            Assert.IsTrue(stages.Any(e => e.Combatant.Value == "a" && e.Stat.Value == "attack" && e.Delta == 1));
            Assert.IsTrue(stages.Any(e => e.Combatant.Value == "b" && e.Stat.Value == "defense" && e.Delta == -1));
            Assert.IsTrue(stages.Any(e => e.Combatant.Value == "a" && e.Stat.Value == "suerte" && e.Delta == 2), "stat inventada");
        }

        [Test]
        public void Statuses_respect_their_configured_type_immunities()
        {
            var fire = Turn("normal", "kitchen_sink", "fire", "wait", out var b1);
            Assert.IsFalse(fire.OfType<StatusInflictedEvent>().Any(), "un Fuego no se quema");
            Assert.IsFalse(b1.Enemy.Status.HasValue);

            var ghost = Turn("normal", "hex_curse", "ghost", "wait", out _);
            Assert.IsFalse(ghost.OfType<StatusInflictedEvent>().Any(), "estado inventado: los Fantasma son inmunes");

            var normal = Turn("normal", "hex_curse", "normal", "wait", out var b3);
            Assert.AreEqual("maldicion", b3.Enemy.Status.Value.Value);
        }

        [Test]
        public void An_immune_target_takes_no_damage_and_no_secondary_or_self_effects()
        {
            var ev = Turn("normal", "zap", "ground", "wait", out var battle);
            Assert.IsTrue(ev.OfType<DamageDealtEvent>().All(e => e.Amount == 0 && e.Effectiveness == 0f));
            Assert.IsFalse(ev.OfType<StatusInflictedEvent>().Any(), "no paraliza");
            Assert.IsFalse(ev.OfType<StatStageChangedEvent>().Any(), "ni sube su Velocidad");
        }

        [Test]
        public void Status_moves_can_optionally_respect_type_immunity()
        {
            var wave = Turn("normal", "thunder_wave", "ground", "wait", out _);
            Assert.IsTrue(wave.OfType<MoveHadNoEffectEvent>().Any(), "Onda Trueno no afecta a Tierra");

            var glare = Turn("normal", "glare", "ground", "wait", out var b2);
            Assert.IsTrue(b2.Enemy.Status.HasValue, "Deslumbrar (sin la casilla) sí paraliza a un Tierra");
        }

        [Test]
        public void Self_targeted_moves_ignore_the_opponent_evasion()
        {
            var battle = new CTEditor.Battle.Domain.Battle(Mon("a", "normal", "focus", 1), Mon("b", "normal", "double_team", 99));
            var r = Resolver();
            r.ResolveTurn(battle, Use("focus"), Use("double_team"));
            r.ResolveTurn(battle, Use("focus"), Use("double_team"));
            Assert.AreEqual(100f, r.PreviewDamage(battle.Player, battle.Enemy, new Id<Move>("focus")).AccuracyPercent.Value, 0.01);
        }
    }
}
