using System.Collections.Generic;
using NUnit.Framework;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Valida el Lote 2 de XP: al caer un rival, el combate emite ExperienceAwardedEvent con la
    /// fórmula plana clásica (b × L / 7, ×1.5 vs entrenador) y el BattleResult trae los totales.
    /// </summary>
    public class XpAwardTests
    {
        private const string Fire = "fire";

        private static Move StrongMove() => new Move(
            new Id<Move>("blast"), "Blast", new Id<ElementType>(Fire), MoveCategory.Physical,
            250,                  // potencia altísima: garantiza el KO en un turno
            null,                 // precisión null = nunca falla (test determinista)
            25,                   // PP
            0,                    // prioridad
            MoveTarget.SingleEnemy);

        private static BattleParticipant Fighter(string id, int level, int speed, int baseYield) =>
            new BattleParticipant(
                new Id<BattleParticipant>(id), new Id<CTEditor.GameDefinition.Domain.Species.Species>(id),
                level,
                new StatBlock.Builder()
                    .Set(StatId.Hp, 30).Set(StatId.Attack, 100).Set(StatId.Defense, 10)
                    .Set(StatId.SpAttack, 10).Set(StatId.SpDefense, 10).Set(StatId.Speed, speed)
                    .Build(),
                30,
                new List<Id<ElementType>> { new Id<ElementType>(Fire) },
                new List<Id<Move>> { new Id<Move>("blast") },
                baseExpYield: baseYield);

        private static TurnResolver Resolver(ICatalog<Move> moves)
        {
            var chart = new TypeChart.Builder().Build(); // sin ventajas: 1x
            return new TurnResolver(moves, chart, new CTEditor.Battle.Domain.Formulas.ClassicDamageFormula(),
                new SeededRng(7), xpFormula: new ClassicXpFormula());
        }

        [Test]
        public void Defeating_wild_enemy_awards_flat_xp()
        {
            var moves = new InMemoryCatalog<Move>(new[] { StrongMove() }, m => m.Id.Value);
            // Jugador rápido nivel 10; rival salvaje nivel 5 con rendimiento base 64.
            var battle = new CTEditor.Battle.Domain.Battle(Fighter("p1", 10, 99, 64), Fighter("e1", 5, 1, 64), isTrainerBattle: false);
            var resolver = Resolver(moves);

            var events = resolver.ResolveTurn(battle,
                new UseMove(new Id<Move>("blast")), new UseMove(new Id<Move>("blast")));

            // b*L/7 = 64*5/7 = 45 (división entera), 1 participante, salvaje (sin x1.5).
            ExperienceAwardedEvent award = null;
            foreach (var e in events) if (e is ExperienceAwardedEvent xp) award = xp;
            Assert.IsNotNull(award, "Debe emitirse ExperienceAwardedEvent al caer el rival.");
            Assert.AreEqual("p1", award.Recipient.Value);
            Assert.AreEqual(45, award.Amount);

            var result = battle.ToResult();
            Assert.AreEqual(1, result.XpAwards.Count);
            Assert.AreEqual(45, result.XpAwards[0].Amount);
        }

        [Test]
        public void Trainer_battle_multiplies_xp()
        {
            var moves = new InMemoryCatalog<Move>(new[] { StrongMove() }, m => m.Id.Value);
            var battle = new CTEditor.Battle.Domain.Battle(Fighter("p1", 10, 99, 64), Fighter("e1", 5, 1, 64), isTrainerBattle: true);
            var resolver = Resolver(moves);

            var events = resolver.ResolveTurn(battle,
                new UseMove(new Id<Move>("blast")), new UseMove(new Id<Move>("blast")));

            ExperienceAwardedEvent award = null;
            foreach (var e in events) if (e is ExperienceAwardedEvent xp) award = xp;
            Assert.IsNotNull(award);
            Assert.AreEqual(67, award.Amount); // 45 * 3 / 2 = 67 (entero, como los clásicos)
        }
    }
}
