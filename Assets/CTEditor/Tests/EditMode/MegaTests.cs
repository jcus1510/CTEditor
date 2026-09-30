using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// MEGAEVOLUCIÓN: solo con la mecánica activa en las reglas, con su megapiedra (o su movimiento), un máximo por
    /// combate y lado, se queda o vuelve al retirarse según la ficha; el jugador necesita el objeto clave y la IA
    /// megaevoluciona según su nivel y el permiso de su entrenador.
    /// </summary>
    public class MegaTests
    {
        private static Id<ElementType> T(string t) => new Id<ElementType>(t);
        private static StatBlock Stats(int hp, int atk, int def, int spa, int spd, int spe) => new StatBlock.Builder()
            .Set(StatId.Hp, hp).Set(StatId.Attack, atk).Set(StatId.Defense, def).Set(StatId.SpAttack, spa).Set(StatId.SpDefense, spd).Set(StatId.Speed, spe).Build();

        private static readonly Move[] Moves =
        {
            new Move(new Id<Move>("tackle"), "tackle", T("normal"), MoveCategory.Physical, 40, null, 35, 0, MoveTarget.SingleEnemy),
            new Move(new Id<Move>("wait"), "wait", T("normal"), MoveCategory.Status, 0, null, 35, 0, MoveTarget.Self),
            new Move(new Id<Move>("dragon_ascent"), "dragon_ascent", T("flying"), MoveCategory.Physical, 120, null, 5, 0, MoveTarget.SingleEnemy),
        };

        private static BattleRules Rules(MegaEvolutionSettings mega) => new BattleRules(
            mechanics: mega == null ? null : new[] { new MechanicDefinition("mega_evolution", "Mega", MechanicKind.MegaEvolution, mega) });

        private static TurnResolver Resolver(BattleRules rules) => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value), new TypeChart.Builder().Build(), new ClassicDamageFormula(), new SeededRng(4),
            new InMemoryCatalog<StatusConditionDefinition>(new StatusConditionDefinition[0], s => s.Id.Value), rules: rules);

        private static readonly BattleForm MegaForm = new BattleForm("mega", "Mega-Prueba", Stats(200, 180, 120, 100, 100, 120), new[] { T("fire"), T("dragon") });
        private static readonly FormChange[] MegaRule = { new FormChange("", "mega", FormTrigger.MegaEvolution, item: "testite") };

        private static BattleParticipant Mon(string id, string item = "testite", int speed = 50, string[] moves = null, FormChange[] rules = null)
            => new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50, Stats(200, 100, 100, 100, 100, speed), 200,
                new List<Id<ElementType>> { T("fire") }, (moves ?? new[] { "tackle", "wait" }).Select(m => new Id<Move>(m)).ToList(),
                heldItem: item, forms: new[] { MegaForm }, formChanges: rules ?? MegaRule);

        private static UseMove Use(string m, bool mega = false) => new UseMove(new Id<Move>(m), mega);

        [Test]
        public void Without_the_mechanic_there_is_no_mega_evolution()
        {
            var r = Resolver(Rules(null));
            var b = new CTEditor.Battle.Domain.Battle(Mon("a"), Mon("b", item: null));
            Assert.IsFalse(r.CanMegaEvolve(b, true));
            r.ResolveTurn(b, Use("wait", true), Use("wait")).ToList();
            Assert.AreEqual("", b.Player.FormId);
        }

        [Test]
        public void Mega_evolves_before_moving_and_only_once_per_side_by_default()
        {
            var r = Resolver(Rules(MegaEvolutionSettings.Official));
            var b = new CTEditor.Battle.Domain.Battle(new[] { Mon("a"), Mon("a2") }, new[] { Mon("b", item: null) });
            Assert.IsTrue(r.CanMegaEvolve(b, true));
            Assert.IsFalse(r.CanMegaEvolve(b, false), "el rival no lleva megapiedra");
            var ev = r.ResolveTurn(b, Use("wait", true), Use("wait")).ToList();
            Assert.AreEqual("mega", b.Player.FormId);
            Assert.AreEqual(180, b.Player.Stats.Of(StatId.Attack));
            var me = ev.OfType<MegaEvolvedEvent>().Single();
            Assert.AreEqual("Mega-Prueba", me.FormName);
            Assert.AreEqual("testite", me.StoneId);
            Assert.Less(ev.IndexOf(me), ev.FindIndex(e => e is MoveUsedEvent), "antes de moverse");

            // Se queda megaevolucionado al retirarse (oficial) y el compañero ya no puede (1 por combate).
            var first = b.Player;
            r.ResolveTurn(b, new SwitchMonster(new Id<BattleParticipant>("a2")), Use("wait")).ToList();
            Assert.AreEqual("mega", first.FormId);
            Assert.IsFalse(r.CanMegaEvolve(b, true), "ya se gastó la megaevolución de este lado");
        }

        [Test]
        public void The_mechanic_sheet_can_allow_many_and_revert_on_switch()
        {
            var r = Resolver(Rules(new MegaEvolutionSettings(maxPerBattle: 0, requiredKeyItem: "", revertOnSwitch: true)));
            var b = new CTEditor.Battle.Domain.Battle(new[] { Mon("a"), Mon("a2") }, new[] { Mon("b", item: null) });
            r.ResolveTurn(b, Use("wait", true), Use("wait")).ToList();
            var first = b.Player;
            r.ResolveTurn(b, new SwitchMonster(new Id<BattleParticipant>("a2")), Use("wait")).ToList();
            Assert.AreEqual("", first.FormId, "vuelve a su forma al retirarse");
            Assert.IsTrue(r.CanMegaEvolve(b, true), "sin límite: el compañero también puede");
            r.ResolveTurn(b, Use("wait", true), Use("wait")).ToList();
            Assert.AreEqual("mega", b.Player.FormId);
        }

        [Test]
        public void Some_megas_need_a_move_instead_of_a_stone()
        {
            var rules = new[] { new FormChange("", "mega", FormTrigger.MegaEvolution, move: "dragon_ascent") };
            var r = Resolver(Rules(MegaEvolutionSettings.Official));
            var without = new CTEditor.Battle.Domain.Battle(Mon("ray", item: null, rules: rules), Mon("b", item: null));
            Assert.IsFalse(r.CanMegaEvolve(without, true));
            var with = new CTEditor.Battle.Domain.Battle(Mon("ray", item: null, rules: rules, moves: new[] { "dragon_ascent", "wait" }), Mon("b", item: null));
            Assert.IsTrue(r.CanMegaEvolve(with, true));
        }

        // ---------------- Sesión: objeto clave del jugador e IA del rival ----------------

        private static readonly Species[] AllSpecies =
        {
            new Species(new Id<Species>("zard"), "Zard", new[] { T("fire") }, Stats(80, 90, 80, 110, 85, 100),
                new[] { new LearnableMove(new Id<Move>("tackle"), 1), new LearnableMove(new Id<Move>("wait"), 1) }, new Evolution[0],
                forms: new[] { new SpeciesForm("mega", "Mega-Zard", new[] { T("fire"), T("dragon") }, Stats(80, 130, 111, 130, 85, 100)) },
                formChanges: new[] { new FormChange("", "mega", FormTrigger.MegaEvolution, item: "zardite") }),
        };

        private static GameData Data() => new GameData(
            new MemoryCatalog<Species>(AllSpecies, s => s.Id.Value), new MemoryCatalog<Move>(Moves, m => m.Id.Value),
            new TypeChart.Builder().Build(),
            Ruleset.Classic.With(mechanics: new[] { MechanicDefinition.OfficialMega }),
            items: new MemoryCatalog<ItemDefinition>(new[]
            {
                new ItemDefinition("zardite", "Zardita", ItemCategory.Held, consumable: false),
                new ItemDefinition("mega_ring", "Megapulsera", ItemCategory.Key, consumable: false),
            }, i => i.Id));

        private sealed class FixedRng : IRng
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => 0.99f;
        }

        private static TeamMemberSpec Z(string item = "zardite")
            => new TeamMemberSpec(new Id<Species>("zard"), 50, new[] { new Id<Move>("tackle"), new Id<Move>("wait") }, heldItem: item, fixedIv: 31);

        private static PlayerSave Save(GameData data, bool ring)
            => TeamBuilder.NewGame(new TeamPreset("p", "Prueba", new[] { Z() }, items: ring ? new[] { ("mega_ring", 1) } : new (string, int)[0]),
                data, new FixedRng());

        private static TrainerDefinition Rival(int aiLevel, bool canMega = true)
            => new TrainerDefinition("rival", "Rival", new[] { Z() }, aiLevel: aiLevel, canMegaEvolve: canMega);

        [Test]
        public void The_player_needs_the_key_item()
        {
            var data = Data();
            var without = BattleSession.Against(data, Save(data, false), Rival(1), new FixedRng());
            without.Begin();
            Assert.IsFalse(without.CanPlayerMegaEvolve);
            StringAssert.Contains("Megapulsera", without.WhyNoMega);
            Assert.IsFalse(without.Submit(PlayerChoice.Fight(1, true)).Accepted);

            var with = BattleSession.Against(data, Save(data, true), Rival(1), new FixedRng());
            with.Begin();
            Assert.IsTrue(with.CanPlayerMegaEvolve, with.WhyNoMega);
            Assert.IsTrue(with.Submit(PlayerChoice.Fight(1, true)).Accepted);
            Assert.AreEqual("mega", with.Battle.Player.FormId);
            Assert.IsFalse(with.CanPlayerMegaEvolve, "solo una vez");
        }

        [Test]
        public void The_rival_mega_evolves_by_ai_level_and_permission()
        {
            var data = Data();
            string EnemyForm(TrainerDefinition t)
            {
                var s = BattleSession.Against(data, Save(data, false), t, new FixedRng());
                s.Begin();
                s.Submit(PlayerChoice.Fight(1));
                return s.Battle.Enemy.FormId;
            }
            Assert.AreEqual("", EnemyForm(Rival(1)), "Novato: nunca");
            Assert.AreEqual("mega", EnemyForm(Rival(3)), "Veterano: en cuanto puede");
            Assert.AreEqual("mega", EnemyForm(Rival(5)), "Campeón: con cabeza (aquí la mega sale mejor)");
            Assert.AreEqual("", EnemyForm(Rival(5, canMega: false)), "sin permiso de su entrenador");
            Assert.AreEqual(MegaTiming.Never, AiProfile.Classic(2).MegaTiming);
            Assert.AreEqual(MegaTiming.Smart, AiProfile.Classic(7).MegaTiming);
        }
    }
}
