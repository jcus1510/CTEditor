using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// EL TEST QUE VALIDA TODA LA ARQUITECTURA (I.3). Construye contenido en código (lo que el autor
    /// haría con assets), arma un combate, lo resuelve turno a turno e imprime la línea de tiempo.
    /// Corre sin abrir el juego, en milisegundos, sin sprites: la recompensa del dominio puro (A.3).
    ///
    /// Lee también este test como DOCUMENTACIÓN: muestra, de punta a punta, cómo encajan las piezas
    /// (definición -> instancia -> snapshot -> combate -> resultados).
    /// </summary>
    public sealed class ConsoleBattleTests
    {
        // Nombres legibles para imprimir la línea de tiempo (presentación mínima).
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _moveNames = new Dictionary<string, string>();

        [Test]
        public void Console_battle_runs_to_a_winner()
        {
            // --- CONTENIDO (normalmente assets del autor; aquí, código) ---
            var ember = BuildMove("ember", "fire", MoveCategory.Special, 40);
            var vine = BuildMove("vine", "grass", MoveCategory.Physical, 40);
            var moveCatalog = new InMemoryCatalog<Move>(new[] { ember, vine }, m => m.Id.Value);

            var typeChart = new TypeChart.Builder()
                .Set(new Id<ElementType>("fire"), new Id<ElementType>("grass"), 2f)   // fuego es muy eficaz vs planta
                .Set(new Id<ElementType>("grass"), new Id<ElementType>("fire"), 0.5f) // planta es poco eficaz vs fuego
                .Build();

            var charmander = BuildSpecies("charmander", "fire", hp: 39, atk: 52, def: 43, spA: 60, spD: 50, spe: 65, moveId: "ember");
            var bulbasaur = BuildSpecies("bulbasaur", "grass", hp: 45, atk: 49, def: 49, spA: 65, spD: 65, spe: 45, moveId: "vine");

            // --- REGLAS + FÓRMULAS (todo inyectado) ---
            var ruleset = Ruleset.Classic;
            var growth = new ClassicStatGrowthFormula();
            var rng = new SeededRng(12345); // semilla fija -> combate reproducible

            // --- DEFINICIÓN -> INSTANCIA ---
            var playerMon = MonsterFactory.Create(new Id<MonsterInstance>("mon-player"), charmander, 10, ruleset, growth);
            var enemyMon = MonsterFactory.Create(new Id<MonsterInstance>("mon-enemy"), bulbasaur, 10, ruleset, growth);

            // --- SNAPSHOT-IN (el orquestador arma la entrada de Battle; aquí lo hace el test) ---
            var playerSnap = Snapshot("p1", playerMon, charmander);
            var enemySnap = Snapshot("p2", enemyMon, bulbasaur);

            _names["p1"] = "Charmander";
            _names["p2"] = "Bulbasaur";
            _moveNames["ember"] = "Ascuas";
            _moveNames["vine"] = "Latigo Cepa";

            var battle = new CTEditor.Battle.Domain.Battle(playerSnap, enemySnap);
            var resolver = new TurnResolver(moveCatalog, typeChart, new ClassicDamageFormula(), rng);

            Debug.Log("=== Combate de consola: Charmander vs Bulbasaur ===");

            // --- BUCLE DE TURNOS (cada lado usa su primer movimiento) ---
            int safety = 0;
            while (!battle.IsOver && safety++ < 100)
            {
                var playerAction = new UseMove(battle.Player.Moves[0]);
                var enemyAction = new UseMove(battle.Enemy.Moves[0]);

                var events = resolver.ResolveTurn(battle, playerAction, enemyAction);
                LogEvents(events);
            }

            // --- RESULTADOS-OUT ---
            var result = battle.ToResult();

            Assert.IsTrue(battle.IsOver, "El combate deberia haber terminado.");
            Assert.AreEqual(BattleOutcome.PlayerWon, result.Outcome,
                "Charmander (fuego) deberia ganarle a Bulbasaur (planta) por ventaja de tipo.");
        }

        [Test]
        public void Party_respects_max_size_from_ruleset()
        {
            // Ruleset ALTERADO: aforo de 3. El invariante es relativo a las reglas (A.8).
            var ruleset = Ruleset.Classic.With(maxPartySize: 3);
            var growth = new ClassicStatGrowthFormula();
            var species = BuildSpecies("charmander", "fire", hp: 39, atk: 52, def: 43, spA: 60, spD: 50, spe: 65, moveId: "ember");

            var party = new CTEditor.Party.Domain.Party(ruleset);

            for (int i = 0; i < 3; i++)
            {
                var mon = MonsterFactory.Create(new Id<MonsterInstance>("m" + i), species, 5, ruleset, growth);
                Assert.IsTrue(party.Add(mon).IsSuccess, "El miembro " + i + " deberia entrar.");
            }

            Assert.AreEqual(3, party.Count);
            Assert.IsTrue(party.IsFull);

            // El 4º debe ser rechazado por el portero.
            var overflow = MonsterFactory.Create(new Id<MonsterInstance>("m3"), species, 5, ruleset, growth);
            var rejected = party.Add(overflow);

            Assert.IsTrue(rejected.IsFailure, "El 4º deberia ser rechazado por el aforo (3).");
            Assert.AreEqual(3, party.Count, "El equipo no deberia haber crecido tras el rechazo.");
        }

        // ----------------- Helpers de construcción (atajos de autoría en código) -----------------

        private static Move BuildMove(string id, string type, MoveCategory category, int power)
            => new Move(
                new Id<Move>(id),
                id,
                new Id<ElementType>(type),
                category,
                power,
                null,                 // precisión null = nunca falla (combate determinista para el test)
                25,                   // PP
                0,                    // prioridad
                MoveTarget.SingleEnemy);

        private static Species BuildSpecies(string id, string type, int hp, int atk, int def, int spA, int spD, int spe, string moveId)
        {
            var stats = new StatBlock.Builder()
                .Set(StatId.Hp, hp)
                .Set(StatId.Attack, atk)
                .Set(StatId.Defense, def)
                .Set(StatId.SpAttack, spA)
                .Set(StatId.SpDefense, spD)
                .Set(StatId.Speed, spe)
                .Build();

            var types = new List<Id<ElementType>> { new Id<ElementType>(type) };
            var learnset = new List<LearnableMove> { new LearnableMove(new Id<Move>(moveId), 1) };
            var evolutions = new List<Evolution>();

            return new Species(new Id<Species>(id), id, types, stats, learnset, evolutions);
        }

        private static BattleParticipant Snapshot(string battleId, MonsterInstance inst, Species species)
            => new BattleParticipant(
                new Id<BattleParticipant>(battleId),
                inst.SpeciesId,
                inst.Level.Value,
                inst.Stats,
                inst.CurrentHp,
                species.Types,
                inst.Moves);

        // ----------------- Presentación mínima de la línea de tiempo -----------------

        private void LogEvents(IReadOnlyList<IDomainEvent> events)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case MoveUsedEvent mu:
                        Debug.Log($"{Name(mu.Attacker)} uso {MoveName(mu.Move)}.");
                        break;
                    case MoveMissedEvent mm:
                        Debug.Log($"{Name(mm.Attacker)} fallo su movimiento.");
                        break;
                    case DamageDealtEvent dd:
                        Debug.Log($"  -> {Name(dd.Target)} recibio {dd.Amount} de dano.{EffNote(dd.Effectiveness)}");
                        break;
                    case MonsterFaintedEvent mf:
                        Debug.Log($"{Name(mf.Combatant)} se debilito.");
                        break;
                    case BattleEndedEvent be:
                        Debug.Log($"=== Fin del combate: {be.Outcome} ===");
                        break;
                }
            }
        }

        private string Name(Id<BattleParticipant> id) => _names.TryGetValue(id.Value, out var n) ? n : id.Value;
        private string MoveName(Id<Move> id) => _moveNames.TryGetValue(id.Value, out var n) ? n : id.Value;

        private static string EffNote(float eff)
        {
            if (eff <= 0f) return " No tuvo efecto.";
            if (eff > 1f) return " Es muy eficaz!";
            if (eff < 1f) return " No es muy eficaz...";
            return "";
        }
    }
}
