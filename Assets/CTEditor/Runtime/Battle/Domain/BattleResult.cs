using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Battle.Domain
{
    /// <summary>Cómo terminó el combate.</summary>
    public enum BattleOutcome
    {
        InProgress,
        PlayerWon,
        PlayerLost,
        Fled,
        Caught
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

        /// <summary>Estado alterado con el que terminó (null = sano). Party decide si persiste.</summary>
        public StatusId? FinalStatus { get; }

        /// <summary>PP que le quedaron a cada movimiento (mismo orden). Null = no cambiaron.</summary>
        public IReadOnlyList<int> FinalPp { get; }

        /// <summary>Objeto equipado al terminar (null = ninguno; p. ej. si se comió la baya).</summary>
        public string FinalHeldItem { get; }

        public ParticipantResult(Id<BattleParticipant> participantId, int finalHp, bool fainted, StatusId? finalStatus = null,
            IReadOnlyList<int> finalPp = null, string finalHeldItem = null)
        {
            FinalHeldItem = finalHeldItem;
            ParticipantId = participantId;
            FinalHp = finalHp;
            Fainted = fainted;
            FinalStatus = finalStatus;
            FinalPp = finalPp;
        }
    }

    /// <summary>XP ganada por un participante del jugador durante el combate.</summary>
    public readonly struct XpAward
    {
        public Id<BattleParticipant> ParticipantId { get; }
        public int Amount { get; }

        public XpAward(Id<BattleParticipant> participantId, int amount)
        {
            ParticipantId = participantId;
            Amount = amount;
        }
    }

    /// <summary>EVs ganados por un participante del jugador en UNA stat (Party los aplica con sus topes).</summary>
    public readonly struct EvAward
    {
        public Id<BattleParticipant> ParticipantId { get; }
        public StatId Stat { get; }
        public int Amount { get; }

        public EvAward(Id<BattleParticipant> participantId, StatId stat, int amount)
        {
            ParticipantId = participantId;
            Stat = stat;
            Amount = amount;
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

        /// <summary>XP ganada por cada participante del jugador (Party la aplicará con su curva).</summary>
        public IReadOnlyList<XpAward> XpAwards { get; }

        /// <summary>EVs ganados por cada participante del jugador, por stat. Party respeta los topes.</summary>
        public IReadOnlyList<EvAward> EvAwards { get; }

        public BattleResult(BattleOutcome outcome, IReadOnlyList<ParticipantResult> participants, IReadOnlyList<XpAward> xpAwards = null,
            IReadOnlyList<EvAward> evAwards = null)
        {
            Outcome = outcome;
            Participants = participants == null
                ? Array.Empty<ParticipantResult>()
                : new List<ParticipantResult>(participants);
            XpAwards = xpAwards == null
                ? Array.Empty<XpAward>()
                : new List<XpAward>(xpAwards);
            EvAwards = evAwards == null
                ? Array.Empty<EvAward>()
                : new List<EvAward>(evAwards);
        }
    }
}
