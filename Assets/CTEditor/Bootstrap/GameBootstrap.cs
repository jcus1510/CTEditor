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
    /// EL COMPOSITION ROOT (J.4): el único lugar que conoce a todos los contextos y los ENSAMBLA.
    /// Es un MonoBehaviour que corre al arrancar: toma los assets que arrastró el autor, los traduce
    /// con la ACL, construye los catálogos y las fórmulas, crea las instancias y corre un combate
    /// imprimiéndolo en la Console. Así el motor pasa de "aprueba tests" a "arranca en una escena".
    ///
    /// Nadie depende de esta clase; ella depende de todo. Por eso está permitido que lo conozca todo:
    /// es el punto de ensamblaje, en el borde más externo. Aquí (y solo aquí) el acoplamiento es legítimo.
    ///
    /// Versión mínima: corre UN combate 1v1 de demostración. Más adelante crecerá con el wiring del
    /// EffectDispatcher, los flujos entre contextos y la presentación real.
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
        [SerializeField] private int level = 10;
        [SerializeField] private int seed = 12345;

        private void Start()
        {
            // Comprobaciones amables: si falta algo por asignar, lo decimos claro y salimos.
            if (moves == null || moves.Length == 0) { Debug.LogError("[CTEditor] Asigna al menos un MoveData en 'moves'."); return; }
            if (playerSpecies == null || enemySpecies == null) { Debug.LogError("[CTEditor] Asigna playerSpecies y enemySpecies."); return; }
            if (typeChart == null) { Debug.LogError("[CTEditor] Asigna un TypeChartData."); return; }
            if (ruleset == null) { Debug.LogError("[CTEditor] Asigna un RulesetData."); return; }

            // --- COMPOSICIÓN: assets -> dominio (vía ACL y catálogos) ---
            ICatalog<Move> moveCatalog = new ScriptableObjectCatalog<MoveData, Move>(
                moves, d => d.Id, MoveMapper.ToDomain);

            var chart = TypeChartMapper.ToDomain(typeChart);
            var rules = RulesetMapper.ToDomain(ruleset);
            IRng rng = new SystemRng(seed);
            var growth = new ClassicStatGrowthFormula();

            var playerDef = SpeciesMapper.ToDomain(playerSpecies);
            var enemyDef = SpeciesMapper.ToDomain(enemySpecies);

            // --- DEFINICIÓN -> INSTANCIA -> SNAPSHOT ---
            var playerMon = MonsterFactory.Create(new Id<MonsterInstance>("player"), playerDef, level, rules, growth);
            var enemyMon = MonsterFactory.Create(new Id<MonsterInstance>("enemy"), enemyDef, level, rules, growth);

            var playerSnap = Snapshot("p1", playerMon, playerDef);
            var enemySnap = Snapshot("p2", enemyMon, enemyDef);

            // 'Battle' calificado completo por la política de namespaces que elegimos (opción 1).
            var battle = new CTEditor.Battle.Domain.Battle(playerSnap, enemySnap);
            var resolver = new TurnResolver(moveCatalog, chart, new ClassicDamageFormula(), rng);

            // --- BUCLE: el dominio resuelve, nosotros imprimimos la línea de tiempo ---
            Debug.Log($"=== Combate: {playerDef.DisplayName} vs {enemyDef.DisplayName} ===");
            int safety = 0;
            while (!battle.IsOver && safety++ < 100)
            {
                var playerAction = new UseMove(battle.Player.Moves[0]);
                var enemyAction = new UseMove(battle.Enemy.Moves[0]);
                foreach (var ev in resolver.ResolveTurn(battle, playerAction, enemyAction))
                    Debug.Log(Describe(ev));
            }
            Debug.Log($"=== Resultado: {battle.Outcome} ===");
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

        // Traduce un evento de dominio a texto. Esto es presentación MÍNIMA: en el juego real, un
        // MonoBehaviour suscrito haría animaciones en vez de Debug.Log.
        private static string Describe(IDomainEvent e)
        {
            switch (e)
            {
                case MoveUsedEvent mu: return $"{mu.Attacker} uso {mu.Move}.";
                case MoveMissedEvent mm: return $"{mm.Attacker} fallo {mm.Move}.";
                case DamageDealtEvent dd: return $"  -> {dd.Target} recibio {dd.Amount} de dano (x{dd.Effectiveness}).";
                case MonsterFaintedEvent mf: return $"{mf.Combatant} se debilito.";
                case BattleEndedEvent be: return $"Fin: {be.Outcome}";
                default: return e.GetType().Name;
            }
        }
    }
}
