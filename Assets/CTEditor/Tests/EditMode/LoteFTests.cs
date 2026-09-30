using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
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
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Adventure.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// LOTE F: niveles de IA 6-7 (Maestro, Injusto), MEMORIA del entrenador, PREDICCIÓN, GÉNERO y la 5.ª-6.ª gen.:
    /// objetos de competición, campos, Zona Mágica, Escudo Real, Alas Vendaval, Piel Feérica, Rivalidad, Baba...
    /// </summary>
    public class LoteFTests
    {
        private static readonly Percentage Always = new Percentage(100);
        private static Id<ElementType> T(string t) => new Id<ElementType>(t);

        private static MoveEffect Fx(MoveEffectKind k, EffectTarget t = EffectTarget.Opponent, string status = null, string side = null,
            StatId? stat = null, int stages = 0)
            => new MoveEffect(Always, k, t, status == null ? default : new StatusId(status), new Percentage(0),
                stat ?? default, stages, null, false, null, 0, null, 0, side);

        private static Move Hit(string id, string type, MoveCategory cat, int power, bool contact = true, int priority = 0)
            => new Move(new Id<Move>(id), id, T(type), cat, power, null, 20, priority, MoveTarget.SingleEnemy, new MoveEffect[0], makesContact: contact);

        private static Move Status(string id, MoveTarget target, int priority, params MoveEffect[] fx)
            => new Move(new Id<Move>(id), id, T("normal"), MoveCategory.Status, 0, null, 20, priority, target, fx);

        private static readonly Move[] Moves =
        {
            Hit("tackle", "normal", MoveCategory.Physical, 40),
            Hit("strike", "normal", MoveCategory.Physical, 80),
            Hit("quake", "ground", MoveCategory.Physical, 100, contact: false),
            Hit("ember", "fire", MoveCategory.Special, 40, contact: false),
            Hit("bolt", "electric", MoveCategory.Special, 90, contact: false),
            Hit("gust", "flying", MoveCategory.Physical, 40, contact: false),
            Hit("blizzard", "ice", MoveCategory.Special, 110, contact: false),
            Status("growl", MoveTarget.SingleEnemy, 0, Fx(MoveEffectKind.ChangeStatStage, stat: StatId.Attack, stages: -1)),
            Status("wait", MoveTarget.Self, 0),
            Status("spore", MoveTarget.SingleEnemy, 0, Fx(MoveEffectKind.InflictStatus, status: "sleep")),
            Status("attract", MoveTarget.SingleEnemy, 0, Fx(MoveEffectKind.InflictStatus, status: "infatuation")),
            Status("protect", MoveTarget.Self, 4, Fx(MoveEffectKind.InflictStatus, EffectTarget.Self, status: "protect")),
            Status("kings_shield", MoveTarget.Self, 4, Fx(MoveEffectKind.InflictStatus, EffectTarget.Self, status: "kings_shield")),
            Status("misty", MoveTarget.Self, 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "misty_terrain")),
            Status("grassy", MoveTarget.Self, 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "grassy_terrain")),
            Status("electric_t", MoveTarget.Self, 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "electric_terrain")),
            Status("magic_room", MoveTarget.Self, 0, Fx(MoveEffectKind.SetSideCondition, EffectTarget.Self, side: "magic_room")),
        };

        private static StatusConditionDefinition Vol(string id, int dur, bool blocks = false, StatusExtras x = null, float prevent = 0f)
            => new StatusConditionDefinition(new StatusId(id), id, new Percentage(0), new Percentage(prevent), clearedOnSwitch: true,
                durationTurns: dur, isVolatile: true, blocksIncomingMoves: blocks, harderWhenRepeated: blocks, extras: x);

        private static readonly StatusConditionDefinition[] Statuses =
        {
            new StatusConditionDefinition(new StatusId("sleep"), "Dormido", new Percentage(0), new Percentage(100), durationTurns: 3),
            Vol("protect", 1, true),
            Vol("kings_shield", 1, true, new StatusExtras { ProtectOnlyDamaging = true, ProtectContactStat = StatId.Attack, ProtectContactStages = -2 }),
            Vol("infatuation", 0, x: new StatusExtras { RequiresOppositeGender = true }, prevent: 50),
        };

        private static readonly AbilityDefinition[] Abilities =
        {
            new AbilityDefinition(new AbilityId("gale_wings"), "Alas Vendaval", extras: new AbilityExtras { PriorityType = "flying", PriorityTypeBonus = 1 }),
            new AbilityDefinition(new AbilityId("pixilate"), "Piel Feérica", extras: new AbilityExtras { ConvertNormalTo = "fairy", ConvertBoost = 1.3f }),
            new AbilityDefinition(new AbilityId("gooey"), "Baba", extras: new AbilityExtras { ContactStatDrop = StatId.Speed, ContactStatDropStages = -1 }),
            new AbilityDefinition(new AbilityId("rivalry"), "Rivalidad", offensivePowerModifiers: new[]
            {
                new PowerModifier(1.25f, new[] { new Condition(ConditionKind.SameGender) }),
                new PowerModifier(0.75f, new[] { new Condition(ConditionKind.OppositeGender) }),
            }),
        };

        private static ConditionalStat Cs(string stat, float m, params Condition[] c) => new ConditionalStat(new StatId(stat), m, c);
        private static ItemDefinition Held(string id, ItemExtras x, IReadOnlyList<PowerModifier> pm = null)
            => new ItemDefinition(id, id, ItemCategory.Held, heldPowerModifiers: pm, extras: x);

        private static readonly ItemDefinition[] Items =
        {
            Held("choice_band", new ItemExtras { StatMultipliers = new[] { Cs("attack", 1.5f) }, ChoiceLock = true }),
            Held("life_orb", new ItemExtras { AttackRecoilPercent = 10f }, new[] { new PowerModifier(1.3f) }),
            Held("focus_sash", new ItemExtras { SurviveFromFullHp = true }),
            Held("assault_vest", new ItemExtras { StatMultipliers = new[] { Cs("sp_defense", 1.5f) }, BlocksStatusMoves = true }),
            Held("weakness_policy", new ItemExtras
            {
                OnHitStats = new[]
                {
                    new OnHitStat(StatId.Attack, 2, new[] { new Condition(ConditionKind.MoveEffectiveness, ConditionSubject.Self, Comparison.Greater, 1f) }),
                    new OnHitStat(StatId.SpAttack, 2, new[] { new Condition(ConditionKind.MoveEffectiveness, ConditionSubject.Self, Comparison.Greater, 1f) }),
                },
                OnHitConsumed = true,
            }),
            Held("air_balloon", new ItemExtras { AirBalloon = true }),
            Held("occa_berry", new ItemExtras { ResistBerryType = "fire" }),
            Held("eviolite", new ItemExtras { StatMultipliers = new[] { Cs("defense", 1.5f, new Condition(ConditionKind.CanEvolve)) } }),
            Held("rocky_helmet", new ItemExtras { ContactDamagePercent = 16.67f }),
            Held("lum_berry", new ItemExtras { CuresAnyStatus = true }),
        };

        private static readonly SideConditionDefinition[] Sides =
        {
            new SideConditionDefinition("misty_terrain", "Campo de Niebla", 5, group: "campo", groundedStatusBlock: new[] { "*" }),
            new SideConditionDefinition("grassy_terrain", "Campo de Hierba", 5, group: "campo", endOfTurnHealPercent: 6.25f,
                typePowerMultipliers: new Dictionary<string, float> { ["grass"] = 1.5f }),
            new SideConditionDefinition("electric_terrain", "Campo Eléctrico", 5, group: "campo", groundedStatusBlock: new[] { "sleep" },
                typePowerMultipliers: new Dictionary<string, float> { ["electric"] = 1.5f }),
            new SideConditionDefinition("magic_room", "Zona Mágica", 5, suppressesItems: true),
        };

        private static TypeChart Chart() => new TypeChart.Builder()
            .Set(T("fire"), T("grass"), 2f).Set(T("ground"), T("electric"), 2f).Set(T("ground"), T("flying"), 0f)
            .Set(T("fairy"), T("dragon"), 2f).Set(T("dragon"), T("fairy"), 0f).Set(T("ice"), T("grass"), 2f).Build();

        private static TurnResolver Resolver(int seed = 3) => new TurnResolver(
            new InMemoryCatalog<Move>(Moves, m => m.Id.Value), Chart(), new ClassicDamageFormula(), new SeededRng(seed),
            new InMemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value),
            abilities: new InMemoryCatalog<AbilityDefinition>(Abilities, a => a.Id.Value),
            items: new InMemoryCatalog<ItemDefinition>(Items, i => i.Id),
            sideConditions: new InMemoryCatalog<SideConditionDefinition>(Sides, s => s.Id));

        private static BattleParticipant Mon(string id, string type = "normal", int speed = 50, int hp = 500, string item = null,
            string ability = null, Gender gender = Gender.Genderless, bool canEvolve = false, int atk = 100, int cur = -1, params string[] moves)
            => new BattleParticipant(new Id<BattleParticipant>(id), new Id<Species>(id), 50,
                new StatBlock.Builder().Set(StatId.Hp, hp).Set(StatId.Attack, atk).Set(StatId.Defense, 100)
                    .Set(StatId.SpAttack, atk).Set(StatId.SpDefense, 100).Set(StatId.Speed, speed).Build(),
                cur < 0 ? hp : cur, new List<Id<ElementType>> { T(type) }, (moves.Length == 0 ? new[] { "tackle" } : moves).Select(m => new Id<Move>(m)).ToList(),
                null, ability == null ? (AbilityId?)null : new AbilityId(ability), heldItem: item, gender: gender, canEvolve: canEvolve);

        private static UseMove Use(string id) => new UseMove(new Id<Move>(id));
        private static CTEditor.Battle.Domain.Battle Duel(BattleParticipant a, BattleParticipant b) => new CTEditor.Battle.Domain.Battle(a, b);
        private static int MaxDamage(TurnResolver r, CTEditor.Battle.Domain.Battle b, Combatant from, Combatant to, string move)
            => r.PreviewDamage(from, to, new Id<Move>(move), b).Max;

        // ================================================================= OBJETOS DE COMPETICIÓN

        [Test]
        public void Choice_band_boosts_attack_and_locks_the_first_move()
        {
            var r = Resolver();
            var plain = Duel(Mon("a", moves: new[] { "tackle", "strike" }), Mon("b"));
            var band = Duel(Mon("a", item: "choice_band", moves: new[] { "tackle", "strike" }), Mon("b"));
            int d0 = MaxDamage(r, plain, plain.Player, plain.Enemy, "strike"), d1 = MaxDamage(r, band, band.Player, band.Enemy, "strike");
            Assert.IsTrue((d1) > (d0 * 1.4f), "Ataque ×1, 5");

            r.ResolveTurn(band, Use("tackle"), Use("tackle")).ToList();
            Assert.AreEqual("choice_band", r.RestrictionFor(band.Player, new Id<Move>("strike")), "bloqueado en Placaje");
            Assert.IsNull(r.RestrictionFor(band.Player, new Id<Move>("tackle")));
        }

        [Test]
        public void Life_orb_hits_harder_and_costs_a_tenth_of_hp()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, item: "life_orb"), Mon("b"));
            var plain = Duel(Mon("a"), Mon("b"));
            Assert.IsTrue((MaxDamage(r, b, b.Player, b.Enemy, "strike")) > (MaxDamage(r, plain, plain.Player, plain.Enemy, "strike") * 1.25f));
            var ev = r.ResolveTurn(b, Use("tackle"), Use("tackle")).ToList();
            Assert.IsTrue(ev.OfType<RecoilDamageEvent>().Any(e => e.Combatant.Value == "a" && e.Amount == 50), "pierde 1/10 de 500");
        }

        [Test]
        public void Focus_sash_survives_a_knockout_from_full_hp_only_once()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, atk: 600, moves: "strike"), Mon("b", hp: 60, item: "focus_sash", moves: "wait"));
            var ev = r.ResolveTurn(b, Use("strike"), Use("wait")).ToList();
            Assert.AreEqual(1, b.Enemy.CurrentHp, "aguanta con 1 PS");
            Assert.IsNull(b.Enemy.HeldItem, "la banda se gasta");
            Assert.IsTrue(ev.OfType<EnduredEvent>().Any());
        }

        [Test]
        public void Assault_vest_boosts_sp_def_and_forbids_status_moves()
        {
            var r = Resolver();
            var b = Duel(Mon("a", item: "assault_vest", moves: new[] { "growl", "tackle" }), Mon("b"));
            Assert.AreEqual(150, r.CurrentStat(b.Player, StatId.SpDefense));
            Assert.AreEqual("assault_vest", r.RestrictionFor(b.Player, new Id<Move>("growl")));
            Assert.IsFalse(r.CanChooseMove(b.Player, 0));
            Assert.IsTrue(r.CanChooseMove(b.Player, 1));
        }

        [Test]
        public void Weakness_policy_sharply_raises_attack_after_a_super_effective_hit()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, moves: "ember"), Mon("b", type: "grass", hp: 900, item: "weakness_policy", moves: "wait"));
            r.ResolveTurn(b, Use("ember"), Use("wait")).ToList();
            Assert.AreEqual(2, b.Enemy.GetStage(StatId.Attack));
            Assert.AreEqual(2, b.Enemy.GetStage(StatId.SpAttack));
            Assert.IsNull(b.Enemy.HeldItem, "se gasta");
        }

        [Test]
        public void Air_balloon_gives_ground_immunity_until_it_pops()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, moves: new[] { "quake", "tackle" }), Mon("b", hp: 900, item: "air_balloon", moves: "wait"));
            Assert.AreEqual(0, MaxDamage(r, b, b.Player, b.Enemy, "quake"), "flota");
            r.ResolveTurn(b, Use("tackle"), Use("wait")).ToList();
            Assert.IsNull(b.Enemy.HeldItem, "el golpe lo revienta");
            Assert.IsTrue((MaxDamage(r, b, b.Player, b.Enemy, "quake")) > (0));
        }

        [Test]
        public void Resist_berry_halves_a_super_effective_hit_of_its_type()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, moves: "ember"), Mon("b", type: "grass", hp: 900, item: "occa_berry", moves: "wait"));
            int max = MaxDamage(r, b, b.Player, b.Enemy, "ember");
            var ev = r.ResolveTurn(b, Use("ember"), Use("wait")).ToList();
            int dealt = ev.OfType<DamageDealtEvent>().First(e => e.Target.Value == "b").Amount;
            Assert.LessOrEqual(dealt, max / 2 + 1);
            Assert.IsNull(b.Enemy.HeldItem);
        }

        [Test]
        public void Eviolite_only_helps_monsters_that_can_still_evolve()
        {
            var r = Resolver();
            var b = Duel(Mon("a", item: "eviolite", canEvolve: true), Mon("b", item: "eviolite"));
            Assert.AreEqual(150, r.CurrentStat(b.Player, StatId.Defense));
            Assert.AreEqual(100, r.CurrentStat(b.Enemy, StatId.Defense));
        }

        [Test]
        public void Rocky_helmet_hurts_contact_attackers_only()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, moves: new[] { "tackle", "ember" }), Mon("b", hp: 900, item: "rocky_helmet", moves: "wait"));
            r.ResolveTurn(b, Use("tackle"), Use("wait")).ToList();
            Assert.AreEqual(500 - 83, b.Player.CurrentHp, "1/6 de 500");
            r.ResolveTurn(b, Use("ember"), Use("wait")).ToList();
            Assert.AreEqual(500 - 83, b.Player.CurrentHp, "Ascuas no hace contacto");
        }

        [Test]
        public void Lum_berry_cures_a_status_right_away()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, moves: "spore"), Mon("b", item: "lum_berry", moves: "wait"));
            r.ResolveTurn(b, Use("spore"), Use("wait")).ToList();
            Assert.IsFalse(b.Enemy.Status.HasValue);
            Assert.IsNull(b.Enemy.HeldItem);
        }

        [Test]
        public void Magic_room_switches_off_every_held_item()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, item: "choice_band", moves: new[] { "magic_room", "strike" }), Mon("b", moves: "wait"));
            int boosted = MaxDamage(r, b, b.Player, b.Enemy, "strike");
            r.ResolveTurn(b, Use("magic_room"), Use("wait")).ToList();
            Assert.IsTrue((MaxDamage(r, b, b.Player, b.Enemy, "strike")) < (boosted), "la Cinta ya no sube el Ataque");
            Assert.IsNull(r.RestrictionFor(b.Player, new Id<Move>("strike")), "ni bloquea");
        }

        // ================================================================= CAMPOS Y PROTECCIONES

        [Test]
        public void Misty_terrain_protects_grounded_monsters_from_status()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, moves: new[] { "misty", "spore" }), Mon("b", moves: "wait"));
            r.ResolveTurn(b, Use("misty"), Use("wait")).ToList();
            var ev = r.ResolveTurn(b, Use("spore"), Use("wait")).ToList();
            Assert.IsFalse(b.Enemy.Status.HasValue);
            Assert.IsTrue(ev.OfType<StatusFailedEvent>().Any());

            var flying = Duel(Mon("a", speed: 99, moves: new[] { "misty", "spore" }), Mon("b", type: "flying", moves: "wait"));
            r.ResolveTurn(flying, Use("misty"), Use("wait")).ToList();
            r.ResolveTurn(flying, Use("spore"), Use("wait")).ToList();
            Assert.IsTrue(flying.Enemy.Status.HasValue, "un Volador no pisa el campo");
        }

        [Test]
        public void Terrains_boost_their_type_heal_and_replace_each_other()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, cur: 400, moves: new[] { "grassy", "electric_t", "bolt" }), Mon("b", moves: "wait"));
            int before = MaxDamage(r, b, b.Player, b.Enemy, "bolt");
            r.ResolveTurn(b, Use("grassy"), Use("wait")).ToList();
            Assert.AreEqual(400 + 31, b.Player.CurrentHp, "Campo de Hierba: +1/16");
            r.ResolveTurn(b, Use("electric_t"), Use("wait")).ToList();
            Assert.IsFalse(b.PlayerTeam.HasSideCondition("grassy_terrain"), "solo un campo a la vez");
            Assert.IsTrue((MaxDamage(r, b, b.Player, b.Enemy, "bolt")) > (before * 1.4f), "Eléctrico ×1, 5");
        }

        [Test]
        public void Kings_shield_blocks_attacks_punishes_contact_and_lets_status_moves_through()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 1, moves: new[] { "tackle", "growl" }), Mon("b", speed: 99, moves: "kings_shield"));
            var ev = r.ResolveTurn(b, Use("tackle"), Use("kings_shield")).ToList();
            Assert.IsTrue(ev.OfType<MoveBlockedEvent>().Any());
            Assert.AreEqual(500, b.Enemy.CurrentHp);
            Assert.AreEqual(-2, b.Player.GetStage(StatId.Attack), "Escudo Real: −2 Ataque al que lo toca");
            // Turno siguiente (el escudo repetido puede fallar: se comprueba con uno nuevo).
            var b2 = Duel(Mon("a", speed: 1, moves: "growl"), Mon("b", speed: 99, moves: "kings_shield"));
            r.ResolveTurn(b2, Use("growl"), Use("kings_shield")).ToList();
            Assert.AreEqual(-1, b2.Enemy.GetStage(StatId.Attack), "los movimientos de estado pasan");
        }

        // ================================================================= HABILIDADES 5.ª-6.ª

        [Test]
        public void Gale_wings_gives_priority_to_flying_moves()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 1, ability: "gale_wings", moves: new[] { "gust", "tackle" }), Mon("b", speed: 99, moves: "tackle"));
            var ev = r.ResolveTurn(b, Use("gust"), Use("tackle")).ToList();
            Assert.AreEqual("a", ev.OfType<MoveUsedEvent>().First().Attacker.Value, "Tornado va primero");
            ev = r.ResolveTurn(b, Use("tackle"), Use("tackle")).ToList();
            Assert.AreEqual("b", ev.OfType<MoveUsedEvent>().First().Attacker.Value, "Placaje no");
        }

        [Test]
        public void Pixilate_turns_normal_moves_into_boosted_fairy_moves()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99, ability: "pixilate"), Mon("b", type: "dragon", hp: 900, moves: "wait"));
            var ev = r.ResolveTurn(b, Use("tackle"), Use("wait")).ToList();
            Assert.AreEqual(2f, ev.OfType<DamageDealtEvent>().First(e => e.Target.Value == "b").Effectiveness, "Hada contra Dragón");
        }

        [Test]
        public void Gooey_slows_down_whoever_touches_it()
        {
            var r = Resolver();
            var b = Duel(Mon("a", speed: 99), Mon("b", hp: 900, ability: "gooey", moves: "wait"));
            r.ResolveTurn(b, Use("tackle"), Use("wait")).ToList();
            Assert.AreEqual(-1, b.Player.GetStage(StatId.Speed));
        }

        // ================================================================= GÉNERO

        [Test]
        public void Gender_comes_from_the_species_ratio()
        {
            Assert.AreEqual(Gender.Genderless, GenderText.FromRoll(-1f, 10f));
            Assert.AreEqual(Gender.Female, GenderText.FromRoll(12.5f, 5f));
            Assert.AreEqual(Gender.Male, GenderText.FromRoll(12.5f, 50f));
            Assert.AreEqual(Gender.Male, GenderText.FromRoll(0f, 0f), "0 % de hembras: siempre macho");
            Assert.AreEqual(Gender.Female, GenderText.FromRoll(100f, 99.9f));
            Assert.IsFalse(GenderText.Opposite(Gender.Male, Gender.Genderless));
        }

        [Test]
        public void Attract_needs_opposite_genders()
        {
            var r = Resolver();
            var same = Duel(Mon("a", speed: 99, gender: Gender.Male, moves: "attract"), Mon("b", gender: Gender.Male, moves: "wait"));
            var ev = r.ResolveTurn(same, Use("attract"), Use("wait")).ToList();
            Assert.IsFalse(same.Enemy.HasVolatile(new StatusId("infatuation")));
            Assert.IsTrue(ev.OfType<StatusFailedEvent>().Any());
            var opposite = Duel(Mon("a", speed: 99, gender: Gender.Male, moves: "attract"), Mon("b", gender: Gender.Female, moves: "wait"));
            r.ResolveTurn(opposite, Use("attract"), Use("wait")).ToList();
            Assert.IsTrue(opposite.Enemy.HasVolatile(new StatusId("infatuation")));
        }

        [Test]
        public void Rivalry_hits_harder_against_the_same_gender()
        {
            var r = Resolver();
            var same = Duel(Mon("a", ability: "rivalry", gender: Gender.Male, moves: "strike"), Mon("b", gender: Gender.Male));
            var opposite = Duel(Mon("a", ability: "rivalry", gender: Gender.Male, moves: "strike"), Mon("b", gender: Gender.Female));
            Assert.IsTrue((MaxDamage(r, same, same.Player, same.Enemy, "strike")) > (MaxDamage(r, opposite, opposite.Player, opposite.Enemy, "strike") * 1.5f));
        }

        [Test]
        public void Csv_reads_gender_evolutions_levels_and_new_conditions()
        {
            var evos = CsvCodecs.ParseEvolutions("gallade@objeto:dawn_stone+genero:macho|sylveon@subir+amistad:160+sabe_tipo:fairy|meowstic@25+nivel:25");
            Assert.AreEqual(1, evos[0].Conditions.Single(c => c.Kind == EvolutionConditionKind.Gender).Value, "macho = 1");
            Assert.AreEqual(160, evos[1].Conditions.Single(c => c.Kind == EvolutionConditionKind.MinFriendship).Value, "amistad:160 es un número");
            Assert.AreEqual(25, evos[2].Conditions.Single(c => c.Kind == EvolutionConditionKind.MinLevel).Value);
            Assert.AreEqual(2, CsvCodecs.ParseEvolutions("froslass@objeto:dawn_stone+genero:hembra")[0].Conditions[0].Value);
            var field = ConditionText.Parse("campo=grassy_terrain");
            Assert.AreEqual(ConditionKind.FieldCondition, field.Kind);
            Assert.AreEqual("campo=grassy_terrain", ConditionText.Format(field));
            Assert.AreEqual(ConditionKind.CanEvolve, ConditionText.Parse("propio.puede_evolucionar").Kind);
        }

        [Test]
        public void The_modern_type_chart_has_fairy()
        {
            var (types, chart) = TypeChartTools.Era(TypeChartTools.ChartEra.Modern);
            Assert.IsTrue(types.Contains("fairy"));
            Assert.AreEqual(0f, chart[("dragon", "fairy")]);
            Assert.AreEqual(2f, chart[("fairy", "dragon")]);
            Assert.IsFalse(TypeChartTools.Era(TypeChartTools.ChartEra.Gen2To5).types.Contains("fairy"));
        }

        // ================================================================= IA: MAESTRO, INJUSTO, MEMORIA Y PREDICCIÓN

        private sealed class FixedRng : IRng
        {
            private readonly float _f;
            public FixedRng(float f) { _f = f; }
            public int Next(int minInclusive, int maxExclusive) => minInclusive + (int)((maxExclusive - minInclusive) * _f * 0.999f);
            public float NextFloat() => _f;
        }

        private static Species Sp(string id, string type, int stat, int speed, params string[] learn)
            => new Species(new Id<Species>(id), id, new[] { T(type) },
                new StatBlock.Builder().Set(StatId.Hp, stat).Set(StatId.Attack, stat).Set(StatId.Defense, stat)
                    .Set(StatId.SpAttack, stat).Set(StatId.SpDefense, stat).Set(StatId.Speed, speed).Build(),
                learn.Select(m => new LearnableMove(new Id<Move>(m), 1)).ToList(), new Evolution[0]);

        private static GameData AiData() => new GameData(
            new MemoryCatalog<Species>(new[] { Sp("hero", "normal", 100, 120, "tackle", "blizzard"), Sp("leafy", "grass", 60, 30, "tackle", "protect") }, s => s.Id.Value),
            new MemoryCatalog<Move>(Moves, m => m.Id.Value), Chart(), Ruleset.Classic,
            statuses: new MemoryCatalog<StatusConditionDefinition>(Statuses, s => s.Id.Value));

        private static TeamMemberSpec Member(string species, int level, params string[] moves)
            => new TeamMemberSpec(new Id<Species>(species), level, moves.Select(m => new Id<Move>(m)).ToList(), fixedIv: 31);

        [Test]
        public void The_ai_tournament_plays_whole_mirror_battles_on_both_sides()
        {
            var data = AiData();
            var team = new TrainerDefinition("espejo", "Espejo", new[] { Member("hero", 30, "tackle"), Member("leafy", 30, "tackle", "protect") });
            var r = AiTournament.Duel(data, team, (1, ""), (7, ""), 6, 42);
            Assert.AreEqual(6, r.Games, "juega todos los combates pedidos");
            CollectionAssert.IsEmpty(r.Problems, string.Join(" | ", r.Problems));
            Assert.Greater(r.Wins + r.Losses, 0, "al menos un combate termina con ganador");
            Assert.Greater(r.AverageTurns, 0);
            var ai = AiTournament.WithAi(team, "B", 5, "ia_prueba");
            Assert.AreEqual(5, ai.AiLevel);
            Assert.AreEqual("ia_prueba", ai.AiProfileId);
            Assert.AreEqual(team.Team.Count, ai.Team.Count, "mismo equipo");
        }

        [Test]
        public void A_custom_ai_is_used_by_id_and_does_not_replace_its_level()
        {
            var custom = new AiProfile(4, "IA de Brock", "", MoveBrain.Predictor, 0, 100, HealStyle.Smart, 25, true,
                MovesetStyle.Strong, true, true, true, true, false, predictPercent: 80).WithIdentity("ia_brock", true);
            var data = new GameData(AiData().Species, AiData().Moves, AiData().TypeChart, Ruleset.Classic, aiProfiles: new[] { custom });
            var brock = new TrainerDefinition("brock", "Brock", new[] { Member("leafy", 12, "tackle") }, aiLevel: 4, aiProfileId: "ia_brock");
            var other = new TrainerDefinition("otro", "Otro", new[] { Member("leafy", 12, "tackle") }, aiLevel: 4);
            Assert.AreSame(custom, data.AiProfileFor(brock), "Brock usa su IA personalizada");
            Assert.AreNotSame(custom, data.AiProfileFor(other), "los demás de nivel 4 siguen con la del nivel");
            Assert.AreEqual(MoveBrain.Predictor, data.AiProfileFor(brock).Brain);
            var missing = new TrainerDefinition("x", "X", new[] { Member("leafy", 12, "tackle") }, aiLevel: 2, aiProfileId: "no_existe");
            Assert.AreEqual(2, data.AiProfileFor(missing).Level, "si la IA personalizada no existe, usa la de su nivel");
        }

        [Test]
        public void Levels_6_and_7_are_the_master_and_the_unfair_one()
        {
            var master = AiProfile.Classic(6);
            Assert.AreEqual(MoveBrain.Predictor, master.Brain);
            Assert.AreEqual(AiKnowledge.Memory, master.Knowledge);
            Assert.AreEqual(HeldItemStyle.Competitive, master.HeldItems);
            Assert.IsTrue(master.CompetitiveTraining);
            Assert.AreEqual(60, master.PredictPercent);
            var unfair = AiProfile.Classic(7);
            Assert.AreEqual(AiKnowledge.Omniscient, unfair.Knowledge);
            Assert.AreEqual(100, unfair.PredictPercent);
            Assert.AreEqual("Injusto", AiProfile.ClassicName(7));
            Assert.AreEqual(AiKnowledge.Battle, AiProfile.Classic(4).Knowledge);
        }

        [Test]
        public void A_champion_remembers_your_moves_and_stats_for_the_rematch()
        {
            var data = AiData();
            var save = TeamBuilder.NewGame(new TeamPreset("t", "Prueba", new[] { Member("hero", 50, "tackle", "blizzard") }), data, new FixedRng(0.5f));
            var trainer = new TrainerDefinition("rival", "Rival", new[] { Member("leafy", 50, "tackle") }, "Campeón", aiLevel: 5);
            var s = BattleSession.Against(data, save, trainer, new FixedRng(0.5f));
            s.Begin();
            s.Submit(PlayerChoice.Fight(0));   // Placaje: lo ve y estima tu Ataque
            var heroId = save.Party.Members[0].Id.Value;
            var intel = save.MemoryOf("rival").About(heroId);
            Assert.IsTrue(intel.Moves.Contains("tackle"), "recuerda Placaje");
            Assert.IsFalse(intel.Moves.Contains("blizzard"), "Ventisca aún no la ha visto");
            Assert.IsTrue(intel.HasEstimate("attack"), "estima tu Ataque por el daño");

            // Revancha: ya te conoce antes de verte luchar.
            var s2 = BattleSession.Against(data, save, trainer, new FixedRng(0.5f));
            s2.Begin();
            var brain = new TrainerBrain(data, s2.Battle, s2.Resolver, trainer, new FixedRng(0.5f), save.MemoryOf("rival"));
            Assert.IsTrue(brain.Model.HasSeen(s2.Battle.Player));
            Assert.IsTrue(brain.Model.KnownMoves(s2.Battle.Player).Any(m => m.Id.Value == "tackle"));
        }

        [Test]
        public void A_veteran_forgets_you_after_the_battle()
        {
            var data = AiData();
            var save = TeamBuilder.NewGame(new TeamPreset("t", "Prueba", new[] { Member("hero", 50, "tackle") }), data, new FixedRng(0.5f));
            var trainer = new TrainerDefinition("vet", "Veterano", new[] { Member("leafy", 50, "tackle") }, "", aiLevel: 4);
            var s = BattleSession.Against(data, save, trainer, new FixedRng(0.5f));
            s.Begin();
            s.Submit(PlayerChoice.Fight(0));
            Assert.AreEqual(0, save.MemoryOf("vet").All.Count, "Élite: aprende en el combate pero no lo guarda");
        }

        [Test]
        public void The_unfair_ai_knows_your_hidden_moves_and_protects_from_the_knockout()
        {
            var data = AiData();
            foreach (var (level, expected) in new[] { (7, "protect"), (4, "tackle") })
            {
                var save = TeamBuilder.NewGame(new TeamPreset("t", "Prueba", new[] { Member("hero", 60, "blizzard") }), data, new FixedRng(0.5f));
                var trainer = new TrainerDefinition("t" + level, "Rival", new[] { Member("leafy", 30, "tackle", "protect") }, "", aiLevel: level);
                var s = BattleSession.Against(data, save, trainer, new FixedRng(0.5f));
                s.Begin();
                var brain = new TrainerBrain(data, s.Battle, s.Resolver, trainer, new FixedRng(0.5f), save.MemoryOf(trainer.Id));
                bool knows = brain.Model.KnownMoves(s.Battle.Player).Any(m => m.Id.Value == "blizzard");
                Assert.AreEqual(level == 7, knows, $"nivel {level}: ¿conoce Ventisca sin verla?");
                var (action, _) = brain.Decide();
                Assert.AreEqual(expected, ((UseMove)action).Move.Value, $"nivel {level}");
                if (level == 7) Assert.IsTrue((brain.LastPrediction ?? "").StartsWith("proteccion:"), brain.LastPrediction);
            }
        }
    }
}
