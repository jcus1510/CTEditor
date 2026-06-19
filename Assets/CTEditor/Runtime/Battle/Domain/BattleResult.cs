using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.Battle.Domain
{
    /// <summary>Cómo terminó el combate.</summary>
    public enum BattleOutcome
    {
        InProgress,
        PlayerWon,
        PlayerLost,
        Fled
    }

    /// <summary>
    /// El resultado de UN participante al terminar: su estado final, que el orquestador aplicará de
    /// vuelta al MonsterInstance correspondiente (mapeando por ParticipantId). Aquí irán creciendo
    /// los deltas: por ahora PS finales y "se debilitó"; más adelante XP ganada, cambios de estado...
    /// </summary>
    public sealed class ParticipantResult
    {
        public Id<BattleParticipant> ParticipantId { get; }
        public int FinalHp { get; }
        public bool Fainted { get; }

        public ParticipantResult(Id<BattleParticipant> participantId, int finalHp, bool fainted)
        {
            ParticipantId = participantId;
            FinalHp = finalHp;
            Fainted = fainted;
        }
    }

    /// <summary>
    /// El "resultados-out" de J.5: lo que Battle ENTREGA al terminar, sin haber tocado jamás a Party.
    /// El orquestador lo consume y aplica cada ParticipantResult a su MonsterInstance, respetando los
    /// invariantes de Party. Así "no tocar el original hasta el final" se cumple de verdad.
    /// </summary>
    public sealed class BattleResult
    {
        public BattleOutcome Outcome { get; }
        public IReadOnlyList<ParticipantResult> Participants { get; }

        public BattleResult(BattleOutcome outcome, IReadOnlyList<ParticipantResult> participants)
        {
            Outcome = outcome;
            Participants = participants == null
                ? Array.Empty<ParticipantResult>()
                : new List<ParticipantResult>(participants);
        }
    }
}
