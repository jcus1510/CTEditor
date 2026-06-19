using System;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// El agregado raíz del combate (formato 1v1 para empezar). Sostiene el ESTADO: los dos
    /// combatientes y el desenlace. La LÓGICA del turno no vive aquí, sino en el TurnResolver (un
    /// domain service): el agregado guarda el estado y expone transiciones simples; el servicio, que
    /// es una operación sin estado sobre varias entidades, ejecuta el pipeline. Esa división
    /// (agregado = estado, servicio = operación) es justo lo que recomienda DDD.
    ///
    /// Se arma desde dos SNAPSHOTS (J.5): construye sus Combatant internos y a partir de ahí ya no
    /// necesita saber de Party para nada.
    /// </summary>
    public sealed class Battle
    {
        public Combatant Player { get; }
        public Combatant Enemy { get; }
        public BattleOutcome Outcome { get; private set; }

        public bool IsOver => Outcome != BattleOutcome.InProgress;

        public Battle(BattleParticipant player, BattleParticipant enemy)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));

            // 'new Combatant(...)' es internal, pero Battle está en el mismo assembly: puede crearlos.
            Player = new Combatant(player);
            Enemy = new Combatant(enemy);
            Outcome = BattleOutcome.InProgress;
        }

        // 'internal': solo el TurnResolver (mismo assembly) fija el desenlace; nadie de fuera.
        internal void SetOutcome(BattleOutcome outcome) => Outcome = outcome;

        /// <summary>
        /// Empaqueta el estado final como BattleResult (los "resultados-out" de J.5) para que el
        /// orquestador lo aplique de vuelta a los MonsterInstance. Battle nunca tocó Party.
        /// </summary>
        public BattleResult ToResult()
        {
            var results = new[]
            {
                new ParticipantResult(Player.Id, Player.CurrentHp, Player.IsFainted),
                new ParticipantResult(Enemy.Id, Enemy.CurrentHp, Enemy.IsFainted)
            };
            return new BattleResult(Outcome, results);
        }
    }
}
