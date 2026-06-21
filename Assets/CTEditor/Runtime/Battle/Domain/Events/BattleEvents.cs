using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;

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

    /// <summary>Se infligió un estado alterado a un combatiente (p.ej. quedó quemado).</summary>
    public sealed class StatusInflictedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        public StatusId Status { get; }
        public StatusInflictedEvent(Id<BattleParticipant> target, StatusId status)
        {
            Target = target;
            Status = status;
        }
    }

    /// <summary>Daño residual por un estado al final del turno (veneno, quemadura).</summary>
    public sealed class StatusDamageEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        public StatusId Status { get; }
        public int Amount { get; }
        public StatusDamageEvent(Id<BattleParticipant> target, StatusId status, int amount)
        {
            Target = target;
            Status = status;
            Amount = amount;
        }
    }

    /// <summary>Un estado impidió actuar al combatiente este turno (paralizado/dormido/congelado).</summary>
    public sealed class ActionPreventedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public StatusId Status { get; }
        public ActionPreventedEvent(Id<BattleParticipant> combatant, StatusId status)
        {
            Combatant = combatant;
            Status = status;
        }
    }

    /// <summary>Un estado se disipó solo al cumplirse su duración (p.ej. el monstruo despertó).</summary>
    public sealed class StatusFadedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public StatusId Status { get; }
        public StatusFadedEvent(Id<BattleParticipant> combatant, StatusId status)
        {
            Combatant = combatant;
            Status = status;
        }
    }

    /// <summary>Un combatiente recuperó PS (drenaje, autocuración, regeneración por estado).</summary>
    public sealed class HpRestoredEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int Amount { get; }
        public HpRestoredEvent(Id<BattleParticipant> combatant, int amount)
        {
            Combatant = combatant;
            Amount = amount;
        }
    }

    /// <summary>Un combatiente recibió daño de retroceso (recoil) por su propio movimiento.</summary>
    public sealed class RecoilDamageEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int Amount { get; }
        public RecoilDamageEvent(Id<BattleParticipant> combatant, int amount)
        {
            Combatant = combatant;
            Amount = amount;
        }
    }

    /// <summary>Un combatiente retrocedió (flinch) y perdió su turno.</summary>
    public sealed class FlinchedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public FlinchedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Una stat cambió de etapa en combate (Delta real aplicado; +sube, -baja).</summary>
    public sealed class StatStageChangedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public StatId Stat { get; }
        public int Delta { get; }
        public StatStageChangedEvent(Id<BattleParticipant> combatant, StatId stat, int delta)
        {
            Combatant = combatant;
            Stat = stat;
            Delta = delta;
        }
    }

    /// <summary>Un golpe fue crítico (se aplicó el multiplicador de crítico).</summary>
    public sealed class CriticalHitEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        public CriticalHitEvent(Id<BattleParticipant> target) { Target = target; }
    }

    /// <summary>Un combatiente empezó a cargar un movimiento de dos turnos (golpeará al siguiente).</summary>
    public sealed class ChargingStartedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public ChargingStartedEvent(Id<BattleParticipant> combatant, Id<Move> move)
        {
            Combatant = combatant;
            Move = move;
        }
    }

    /// <summary>Un combatiente debe recargar este turno (tras un movimiento de recarga) y pierde el turno.</summary>
    public sealed class RechargingEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public RechargingEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }
}
