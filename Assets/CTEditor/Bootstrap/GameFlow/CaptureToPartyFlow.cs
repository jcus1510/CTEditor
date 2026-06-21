using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.Battle.Domain;
using CTEditor.Party.Domain;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.Bootstrap.GameFlow
{
    /// <summary>
    /// Cuando se captura al rival (BattleOutcome.Caught), este flujo CREA un MonsterInstance a partir
    /// de la especie/nivel del salvaje y lo añade al equipo, conservando los PS y el estado con que
    /// quedó en combate. Vive en el composition root, que es quien conoce Battle, Party y GameDefinition.
    /// (Battle nunca tocó Party: solo entregó el BattleResult; aquí se traduce a un individuo real.)
    /// </summary>
    public sealed class CaptureToPartyFlow
    {
        private readonly Ruleset _ruleset;
        private readonly IStatGrowthFormula _growth;

        public CaptureToPartyFlow(Ruleset ruleset, IStatGrowthFormula growth)
        {
            _ruleset = ruleset;
            _growth = growth;
        }

        /// <summary>
        /// Añade el monstruo capturado al equipo. 'enemyParticipantId' es el id de combate del rival;
        /// con él se buscan sus PS y estado finales en el resultado. Devuelve el MonsterInstance
        /// creado, o null si no hubo captura o el equipo está lleno (futuro: enviarlo a la caja/PC).
        /// </summary>
        public MonsterInstance AddCaptured(
            BattleResult result,
            Id<BattleParticipant> enemyParticipantId,
            Species enemySpecies,
            int level,
            IReadOnlyList<Id<Move>> moves,
            Id<MonsterInstance> newId,
            CTEditor.Party.Domain.Party party)
        {
            if (result == null || result.Outcome != BattleOutcome.Caught) return null;
            if (enemySpecies == null || party == null || party.IsFull) return null;

            var mon = MonsterFactory.Create(newId, enemySpecies, level, _ruleset, _growth, moves);

            // Conserva el estado del combate: PS finales y estado alterado del capturado.
            var enemyResult = FindResult(result, enemyParticipantId);
            if (enemyResult != null)
            {
                int dmg = mon.MaxHp - enemyResult.FinalHp;
                if (dmg > 0) mon.TakeDamage(dmg);
                if (enemyResult.FinalStatus.HasValue) mon.SetStatus(enemyResult.FinalStatus.Value);
            }

            var added = party.Add(mon);
            return added.IsSuccess ? mon : null;
        }

        private static ParticipantResult FindResult(BattleResult result, Id<BattleParticipant> id)
        {
            foreach (var pr in result.Participants)
                if (pr.ParticipantId == id) return pr;
            return null;
        }
    }
}
