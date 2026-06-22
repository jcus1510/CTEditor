using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Status;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// El agregado raíz del combate. Sostiene el ESTADO: los dos EQUIPOS (cada uno con su activo y su
    /// banca) y el desenlace. La LÓGICA del turno vive en el TurnResolver (un domain service): el
    /// agregado guarda el estado y expone transiciones simples; el servicio ejecuta el pipeline.
    ///
    /// Se arma desde SNAPSHOTS (J.5): construye sus Combatant internos y a partir de ahí ya no
    /// necesita saber de Party para nada. 'Player'/'Enemy' son atajos al ACTIVO de cada equipo, así
    /// el grueso del resolvedor (que pelea 1 contra 1) no cambia al introducir equipos.
    /// </summary>
    public sealed class Battle
    {
        public BattleTeam PlayerTeam { get; }
        public BattleTeam EnemyTeam { get; }

        /// <summary>El combatiente activo del jugador (atajo a PlayerTeam.Active).</summary>
        public Combatant Player => PlayerTeam.Active;
        /// <summary>El combatiente activo del rival (atajo a EnemyTeam.Active).</summary>
        public Combatant Enemy => EnemyTeam.Active;

        public BattleOutcome Outcome { get; private set; }
        public bool IsOver => Outcome != BattleOutcome.InProgress;

        /// <summary>Constructor 1v1 (compatibilidad): un participante por bando.</summary>
        public Battle(BattleParticipant player, BattleParticipant enemy)
            : this(new[] { player ?? throw new ArgumentNullException(nameof(player)) },
                   new[] { enemy ?? throw new ArgumentNullException(nameof(enemy)) })
        {
        }

        /// <summary>Constructor de EQUIPOS: una lista de participantes por bando (el primero entra al campo).</summary>
        public Battle(IReadOnlyList<BattleParticipant> playerTeam, IReadOnlyList<BattleParticipant> enemyTeam)
        {
            if (playerTeam == null) throw new ArgumentNullException(nameof(playerTeam));
            if (enemyTeam == null) throw new ArgumentNullException(nameof(enemyTeam));

            PlayerTeam = new BattleTeam(playerTeam);
            EnemyTeam = new BattleTeam(enemyTeam);
            Outcome = BattleOutcome.InProgress;
        }

        // 'internal': solo el TurnResolver (mismo assembly) fija el desenlace; nadie de fuera.
        internal void SetOutcome(BattleOutcome outcome) => Outcome = outcome;

        // 'internal': cambio durante el turno (lo invoca el resolvedor al procesar una acción de cambio).
        internal bool SwitchActive(bool playerSide, Id<BattleParticipant> target)
            => (playerSide ? PlayerTeam : EnemyTeam).SwitchTo(target);

        /// <summary>
        /// RELEVO FORZADO entre turnos: cuando el activo de un bando cayó y quedan reservas, el
        /// orquestador (fuera de este assembly) envía el reemplazo con esta transición pública. Solo
        /// procede si el activo de ese bando está debilitado.
        /// </summary>
        public bool SendReplacement(bool playerSide, Id<BattleParticipant> target)
        {
            var team = playerSide ? PlayerTeam : EnemyTeam;
            if (!team.Active.IsFainted) return false; // solo se releva a un activo caído
            return team.SwitchTo(target);
        }

        /// <summary>¿Ese bando necesita enviar un relevo? (su activo cayó pero aún tiene reservas).</summary>
        public bool NeedsReplacement(bool playerSide)
        {
            var team = playerSide ? PlayerTeam : EnemyTeam;
            return team.Active.IsFainted && team.HasUnfaintedReserves;
        }

        /// <summary>
        /// Empaqueta el estado final como BattleResult (los "resultados-out" de J.5) para que el
        /// orquestador lo aplique de vuelta a los MonsterInstance. Incluye a TODOS los miembros de
        /// ambos equipos (activos y banca), con sus PS y estado finales. Battle nunca tocó Party.
        /// </summary>
        public BattleResult ToResult()
        {
            var results = new List<ParticipantResult>();
            foreach (var m in PlayerTeam.Members) results.Add(ToParticipantResult(m));
            foreach (var m in EnemyTeam.Members) results.Add(ToParticipantResult(m));
            return new BattleResult(Outcome, results);
        }

        private static ParticipantResult ToParticipantResult(Combatant c)
            => new ParticipantResult(c.Id, c.CurrentHp, c.IsFainted, c.Status);
    }
}
