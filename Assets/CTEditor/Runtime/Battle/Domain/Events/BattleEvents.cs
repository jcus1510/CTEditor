using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.Battle.Domain.Events
{
    // Estos son EVENTOS DE DOMINIO internos de Battle (implementan IDomainEvent del SharedKernel).
    // El resolvedor de turno los emite en orden formando una "línea de tiempo" (M.2): el dominio
    // resuelve el turno al instante y produce esta secuencia; luego la presentación la reproduce a
    // lo largo del tiempo (animaciones, texto). En el combate de consola, simplemente los imprimimos.
    //
    // OJO: NO son eventos de integración (los que cruzan contextos, como ExperienciaOtorgada). Esos
    // vivirán en GameContracts. Estos son para uso interno de Battle y su presentación.

    public sealed class MoveUsedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Attacker { get; }
        public Id<Move> Move { get; }
        public MoveUsedEvent(Id<BattleParticipant> attacker, Id<Move> move)
        {
            Attacker = attacker;
            Move = move;
        }
    }

    public sealed class MoveMissedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Attacker { get; }
        public Id<Move> Move { get; }
        public MoveMissedEvent(Id<BattleParticipant> attacker, Id<Move> move)
        {
            Attacker = attacker;
            Move = move;
        }
    }

    public sealed class DamageDealtEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        public int Amount { get; }
        public float Effectiveness { get; }
        public DamageDealtEvent(Id<BattleParticipant> target, int amount, float effectiveness)
        {
            Target = target;
            Amount = amount;
            Effectiveness = effectiveness;
        }
    }

    public sealed class MonsterFaintedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public MonsterFaintedEvent(Id<BattleParticipant> combatant) => Combatant = combatant;
    }

    public sealed class BattleEndedEvent : IDomainEvent
    {
        public BattleOutcome Outcome { get; }
        public BattleEndedEvent(BattleOutcome outcome) => Outcome = outcome;
    }
}
