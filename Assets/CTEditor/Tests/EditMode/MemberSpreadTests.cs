using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>EVs e IVs por estadística y habilidad elegida en los miembros de un equipo.</summary>
    public class MemberSpreadTests
    {
        [Test]
        public void Spreads_read_spanish_and_showdown_and_write_spanish()
        {
            var s = StatSpread.Parse("252 Atk / 4 HP / 252 Spe");
            Assert.AreEqual(252, s.Of(StatId.Attack));
            Assert.AreEqual(4, s.Of(StatId.Hp));
            Assert.AreEqual(508, s.Total);
            Assert.AreEqual("4 PS/252 Atq/252 Vel", s.Format());
            Assert.AreEqual("4 HP / 252 Atk / 252 Spe", s.FormatShowdown());
            Assert.AreEqual(31, StatSpread.Parse("AtqE 31, DefE: 30").Of(StatId.SpAttack));
            Assert.AreEqual(30, StatSpread.Parse("30 suerte").Of(new StatId("suerte")), "estadísticas inventadas");
            Assert.IsTrue(StatSpread.Parse("").IsEmpty);
            Assert.IsFalse(StatSpread.TryParse("252 Atq 4", out _, out _));
            Assert.IsFalse(StatSpread.TryParse("252 Atq / 4 Atk", out _, out var err));
            StringAssert.Contains("repetida", err);
        }

        private static StatBlock Stats(int v) => new StatBlock.Builder().Set(StatId.Hp, v).Set(StatId.Attack, v).Set(StatId.Defense, v)
            .Set(StatId.SpAttack, v).Set(StatId.SpDefense, v).Set(StatId.Speed, v).Build();

        private static readonly Species Mon = new Species(new Id<Species>("mon"), "Mon", new[] { new Id<ElementType>("normal") }, Stats(100),
            new[] { new LearnableMove(new Id<Move>("tackle"), 1) }, new Evolution[0],
            ability: new AbilityId("first"), secondAbility: new AbilityId("second"), hiddenAbility: new AbilityId("hidden"));

        private static GameData Data(Ruleset rules = null) => new GameData(
            new MemoryCatalog<Species>(new[] { Mon }, s => s.Id.Value),
            new MemoryCatalog<Move>(new[] { new Move(new Id<Move>("tackle"), "tackle", new Id<ElementType>("normal"), MoveCategory.Physical, 40, null, 35, 0, MoveTarget.SingleEnemy) }, m => m.Id.Value),
            new TypeChart.Builder().Build(), rules ?? Ruleset.Classic);

        private sealed class FixedRng : IRng
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => 0f;
        }

        private static MonsterInstance Build(TeamMemberSpec spec, GameData data = null, AiProfile profile = null, System.Collections.Generic.List<string> problems = null)
            => TeamBuilder.Build(spec, new Id<MonsterInstance>("m"), data ?? Data(), new FixedRng(), problems, profile: profile);

        [Test]
        public void Written_evs_ivs_and_ability_are_used()
        {
            var spec = new TeamMemberSpec(new Id<Species>("mon"), 50, fixedIv: 31, ivs: StatSpread.Parse("0 Vel"),
                evs: StatSpread.Parse("252 Atq / 252 Vel / 4 PS"), ability: new AbilityId("hidden"));
            var mon = Build(spec);
            Assert.AreEqual(31, mon.IvOf(StatId.Attack));
            Assert.AreEqual(0, mon.IvOf(StatId.Speed), "el IV escrito manda sobre «IVs fijos»");
            Assert.AreEqual(252, mon.EvOf(StatId.Attack));
            Assert.AreEqual(4, mon.EvOf(StatId.Hp));
            var plain = Build(new TeamMemberSpec(new Id<Species>("mon"), 50, fixedIv: 31));
            Assert.Greater(mon.Stats.Of(StatId.Attack), plain.Stats.Of(StatId.Attack), "los EVs suben la estadística");
            Assert.AreEqual(mon.MaxHp, mon.CurrentHp, "sale con la vida llena");
            Assert.AreEqual(2, mon.AbilitySlot, "habilidad oculta");
            Assert.AreEqual("hidden", Data().Snapshot(mon).Ability.Value.Value);
        }

        [Test]
        public void Evs_are_clamped_by_the_rules_and_foreign_abilities_are_ignored()
        {
            var problems = new System.Collections.Generic.List<string>();
            var spec = new TeamMemberSpec(new Id<Species>("mon"), 50, evs: StatSpread.Parse("255 Atq / 255 Def / 255 Vel"),
                ability: new AbilityId("levitate"));
            var mon = Build(spec, problems: problems);
            Assert.AreEqual(252, mon.EvOf(StatId.Attack));
            Assert.LessOrEqual(mon.EvTotal, 510);
            Assert.IsTrue(problems.Any(p => p.Contains("levitate")));
            Assert.IsTrue(problems.Any(p => p.Contains("tope")));

            var noEvs = Build(spec, Data(Ruleset.Classic.With(maxEvPerStat: 0, maxEvTotal: 0)));
            Assert.AreEqual(0, noEvs.EvTotal, "juego sin EVs");
        }

        [Test]
        public void Written_evs_replace_the_competitive_training_and_suggest_proposes_it()
        {
            var master = AiProfile.Classic(6);
            var auto = Build(new TeamMemberSpec(new Id<Species>("mon"), 50), profile: master);
            Assert.AreEqual(508, auto.EvTotal, "entrenamiento de competición");
            var written = Build(new TeamMemberSpec(new Id<Species>("mon"), 50, evs: StatSpread.Parse("252 DefE")), profile: master);
            Assert.AreEqual(252, written.EvTotal);
            Assert.AreEqual(252, written.EvOf(StatId.SpDefense));

            var sug = TeamBuilder.Suggest(new TeamMemberSpec(new Id<Species>("mon"), 50), Data(), master, MovesetStyle.ByAi);
            Assert.AreEqual(508, sug.Evs.Total);
            Assert.IsTrue(TeamBuilder.Suggest(new TeamMemberSpec(new Id<Species>("mon"), 50), Data(), AiProfile.Classic(2), MovesetStyle.ByAi).Evs.IsEmpty,
                "sin entrenamiento, no sugiere EVs");
        }
    }
}
