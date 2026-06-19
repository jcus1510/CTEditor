using System.Collections;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Bootstrap.Platform;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// EL COMPOSITION ROOT (J.4): el único que conoce a todos los contextos y los ensambla. Toma los
    /// assets del autor, los traduce con la ACL, construye catálogos y fórmulas, crea instancias y
    /// corre un combate — ahora REPRODUCIDO CON RITMO a través del event bus.
    ///
    /// Aquí se ve M.2 en vivo: el dominio resuelve cada TURNO AL INSTANTE (ResolveTurn devuelve toda
    /// la lista de eventos de golpe), y la PRESENTACIÓN (esta corutina) es la que "tarda": publica
    /// cada evento en el bus y espera un momento. El dominio nunca esperó; la presentación marca el
    /// ritmo. Cambia los Debug.Log de los suscriptores por animaciones y tienes el combate visual.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Contenido — arrastra aquí tus assets (Create > CTEditor > ...)")]
        [SerializeField] private MoveData[] moves;
        [SerializeField] private SpeciesData playerSpecies;
        [SerializeField] private SpeciesData enemySpecies;
        [SerializeField] private TypeChartData typeChart;
        [SerializeField] private RulesetData ruleset;

        [Header("Parámetros de la demo")]
        [SerializeField] private int playerLevel = 10;
        [SerializeField] private int enemyLevel = 10;
        [SerializeField] private int seed = 12345;
        [SerializeField] private float secondsPerBeat = 0.6f; // ritmo de la presentación

        private void Start() => StartCoroutine(RunBattle());

        private IEnumerator RunBattle()
        {
            // Comprobaciones amables.
            if (moves == null || moves.Length == 0) { Debug.LogError("[CTEditor] Asigna al menos un MoveData en 'moves'."); yield break; }
            if (playerSpecies == null || enemySpecies == null) { Debug.LogError("[CTEditor] Asigna playerSpecies y enemySpecies."); yield break; }
            if (typeChart == null) { Debug.LogError("[CTEditor] Asigna un TypeChartData."); yield break; }
            if (ruleset == null) { Debug.LogError("[CTEditor] Asigna un RulesetData."); yield break; }

            // --- COMPOSICIÓN: assets -> dominio (vía ACL y catálogos) ---
            ICatalog<Move> moveCatalog = new ScriptableObjectCatalog<MoveData, Move>(
                moves, d => d.Id, MoveMapper.ToDomain);
            var chart = TypeChartMapper.ToDomain(typeChart);
            var rules = RulesetMapper.ToDomain(ruleset);
            IRng rng = new SystemRng(seed);
            var growth = new ClassicStatGrowthFormula();

            var playerDef = SpeciesMapper.ToDomain(playerSpecies);
            var enemyDef = SpeciesMapper.ToDomain(enemySpecies);

            // --- EL TABLÓN: presentación se SUSCRIBE a los eventos (M.2) ---
            var bus = new EventBus();
            bus.Subscribe<MoveUsedEvent>(e => Debug.Log($"{Name(e.Attacker)} uso {e.Move}."));
            bus.Subscribe<MoveMissedEvent>(e => Debug.Log($"{Name(e.Attacker)} fallo {e.Move}."));
            bus.Subscribe<DamageDealtEvent>(e => Debug.Log($"  -> {Name(e.Target)} recibio {e.Amount} de dano (x{e.Effectiveness})."));
            bus.Subscribe<MonsterFaintedEvent>(e => Debug.Log($"{Name(e.Combatant)} se debilito."));
            bus.Subscribe<BattleEndedEvent>(e => Debug.Log($"=== Fin del combate: {e.Outcome} ==="));

            // --- DEFINICIÓN -> INSTANCIA -> SNAPSHOT (cada uno con SU nivel) ---
            var playerMon = MonsterFactory.Create(new Id<MonsterInstance>("player"), playerDef, playerLevel, rules, growth);
            var enemyMon = MonsterFactory.Create(new Id<MonsterInstance>("enemy"), enemyDef, enemyLevel, rules, growth);

            _names["p1"] = playerDef.DisplayName;
            _names["p2"] = enemyDef.DisplayName;

            var playerSnap = Snapshot("p1", playerMon, playerDef);
            var enemySnap = Snapshot("p2", enemyMon, enemyDef);

            var battle = new CTEditor.Battle.Domain.Battle(playerSnap, enemySnap);
            var resolver = new TurnResolver(moveCatalog, chart, new ClassicDamageFormula(), rng);

            Debug.Log($"=== Combate: {playerDef.DisplayName} (Nv.{playerLevel}) vs {enemyDef.DisplayName} (Nv.{enemyLevel}) ===");

            // --- BUCLE DE TURNOS ---
            int turn = 1;
            int safety = 0;
            while (!battle.IsOver && safety++ < 100)
            {
                Debug.Log($"--- Turno {turn++} ---");

                // El dominio resuelve el turno AL INSTANTE (toda la lista de golpe)...
                var events = resolver.ResolveTurn(
                    battle,
                    new UseMove(battle.Player.Moves[0]),
                    new UseMove(battle.Enemy.Moves[0]));

                // ...y la PRESENTACIÓN la reproduce con ritmo: publica cada evento y espera.
                foreach (var ev in events)
                {
                    bus.Publish(ev);
                    yield return new WaitForSeconds(secondsPerBeat);
                }
            }
        }

        private static BattleParticipant Snapshot(string id, MonsterInstance inst, Species species)
            => new BattleParticipant(
                new Id<BattleParticipant>(id),
                inst.SpeciesId,
                inst.Level.Value,
                inst.Stats,
                inst.CurrentHp,
                species.Types,
                inst.Moves);

        private readonly System.Collections.Generic.Dictionary<string, string> _names
            = new System.Collections.Generic.Dictionary<string, string>();

        private string Name(Id<BattleParticipant> id) => _names.TryGetValue(id.Value, out var n) ? n : id.Value;
    }
}
