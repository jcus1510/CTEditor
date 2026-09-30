using System.Collections.Generic;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;

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

    /// <summary>El movimiento no afecta al objetivo por su tipo (Onda Trueno contra un Tierra).</summary>
    public sealed class MoveHadNoEffectEvent : IDomainEvent
    {
        public Id<BattleParticipant> Attacker { get; }
        public Id<BattleParticipant> Target { get; }
        public Id<Move> Move { get; }
        public MoveHadNoEffectEvent(Id<BattleParticipant> attacker, Id<BattleParticipant> target, Id<Move> move)
        {
            Attacker = attacker;
            Target = target;
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

    /// <summary>Se intentó usar un movimiento sin PP: el turno se pierde.</summary>
    public sealed class OutOfPpEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public OutOfPpEvent(Id<BattleParticipant> combatant, Id<Move> move) { Combatant = combatant; Move = move; }
    }

    /// <summary>Al combatiente no le quedan PP en ningún movimiento: usa Forcejeo.</summary>
    public sealed class StruggleEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public StruggleEvent(Id<BattleParticipant> combatant) => Combatant = combatant;
    }

    /// <summary>Un participante del jugador ganó experiencia (al caer o ser capturado un rival).</summary>
    public sealed class ExperienceAwardedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Recipient { get; }
        public int Amount { get; }

        public ExperienceAwardedEvent(Id<BattleParticipant> recipient, int amount)
        {
            Recipient = recipient;
            Amount = amount;
        }
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

    /// <summary>Un monstruo se retiró del campo (lo cambiaron por otro).</summary>
    public sealed class MonsterWithdrawnEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public MonsterWithdrawnEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Un monstruo entró al campo (relevo voluntario o forzado).</summary>
    public sealed class MonsterSentEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public MonsterSentEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Un bando debe enviar un relevo: su activo cayó pero aún tiene reservas. PlayerSide
    /// indica de qué lado (true = jugador). El combate se PAUSA hasta que llegue el reemplazo.</summary>
    public sealed class ReplacementRequiredEvent : IDomainEvent
    {
        public bool PlayerSide { get; }
        public ReplacementRequiredEvent(bool playerSide) { PlayerSide = playerSide; }
    }

    /// <summary>Se usó un objeto sobre un combatiente (sus efectos concretos se narran aparte).</summary>
    public sealed class ItemUsedInBattleEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        public ItemUsedInBattleEvent(Id<BattleParticipant> target) { Target = target; }
    }

    /// <summary>Se capturó al rival. El orquestador lo añadirá al equipo (integración con Party).</summary>
    public sealed class MonsterCapturedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        /// <summary>Sacudidas antes de cerrarse (siempre 3 si se capturó con la fórmula clásica).</summary>
        public int Shakes { get; }
        public MonsterCapturedEvent(Id<BattleParticipant> target, int shakes = 3) { Target = target; Shakes = shakes; }
    }

    /// <summary>El intento de captura falló.</summary>
    public sealed class CaptureFailedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        /// <summary>Cuántas veces se sacudió la bola antes de que se escapara (0-3).</summary>
        public int Shakes { get; }
        public CaptureFailedEvent(Id<BattleParticipant> target, int shakes = 0) { Target = target; Shakes = shakes; }
    }

    /// <summary>No se puede capturar: el monstruo es de un ENTRENADOR (según las reglas del juego).</summary>
    public sealed class CaptureBlockedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        public CaptureBlockedEvent(Id<BattleParticipant> target) { Target = target; }
    }

    /// <summary>Intentó huir y no pudo (el rival es más rápido y hubo mala suerte).</summary>
    public sealed class FleeFailedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public FleeFailedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>No se puede huir de este combate (contra un entrenador, según las reglas).</summary>
    public sealed class FleeBlockedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public FleeBlockedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }
}

namespace CTEditor.Battle.Domain.Events
{
    // ---------------- Lote A/B: protección, aguante, trampas, estados y clima ----------------

    /// <summary>El objetivo se protegió: el movimiento no le hace nada (Protección).</summary>
    public sealed class MoveBlockedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Target { get; }
        public MoveBlockedEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> target) { Target = target; }
    }

    /// <summary>Aguantó el golpe con 1 PS (Aguante).</summary>
    public sealed class EnduredEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public EnduredEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>No pudo cambiarse ni huir porque está atrapado.</summary>
    public sealed class SwitchPreventedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public CTEditor.GameDefinition.Domain.Status.StatusId Status { get; }
        public SwitchPreventedEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant, CTEditor.GameDefinition.Domain.Status.StatusId status)
        { Combatant = combatant; Status = status; }
    }

    /// <summary>El estado no llegó a aplicarse ("¡pero falló!"), p. ej. Protección usada dos turnos seguidos.</summary>
    public sealed class StatusFailedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Target { get; }
        public CTEditor.GameDefinition.Domain.Status.StatusId Status { get; }
        public StatusFailedEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> target, CTEditor.GameDefinition.Domain.Status.StatusId status)
        { Target = target; Status = status; }
    }

    /// <summary>Se activa el objeto equipado (Restos, una baya...). Consumed = se gastó.</summary>
    public sealed class HeldItemActivatedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public string ItemId { get; }
        public bool Consumed { get; }
        public HeldItemActivatedEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant, string itemId, bool consumed)
        { Combatant = combatant; ItemId = itemId; Consumed = consumed; }
    }

    /// <summary>Empieza un clima.</summary>
    public sealed class WeatherStartedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public string WeatherId { get; }
        public int Turns { get; }
        public WeatherStartedEvent(string weatherId, int turns) { WeatherId = weatherId; Turns = turns; }
    }

    /// <summary>Termina el clima.</summary>
    public sealed class WeatherEndedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public string WeatherId { get; }
        public WeatherEndedEvent(string weatherId) { WeatherId = weatherId; }
    }

    /// <summary>El clima daña a un combatiente al final del turno (arena, granizo).</summary>
    public sealed class WeatherDamageEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public string WeatherId { get; }
        public int Amount { get; }
        public WeatherDamageEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant, string weatherId, int amount)
        { Combatant = combatant; WeatherId = weatherId; Amount = amount; }
    }
}

namespace CTEditor.Battle.Domain.Events
{
    // ---------------- Trampas de campo y cambios forzados ----------------

    /// <summary>Se colocó una capa de trampa en un lado (PlayerSide = el lado del jugador).</summary>
    public sealed class HazardSetEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public bool PlayerSide { get; }
        public string HazardId { get; }
        public int Layers { get; }
        public HazardSetEvent(bool playerSide, string hazardId, int layers) { PlayerSide = playerSide; HazardId = hazardId; Layers = layers; }
    }

    /// <summary>Se quitaron trampas de un lado (Giro Rápido, Despejar).</summary>
    public sealed class HazardsClearedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public bool PlayerSide { get; }
        public System.Collections.Generic.IReadOnlyList<string> HazardIds { get; }
        public HazardsClearedEvent(bool playerSide, System.Collections.Generic.IReadOnlyList<string> ids) { PlayerSide = playerSide; HazardIds = ids; }
    }

    /// <summary>Una trampa hizo daño al que entraba.</summary>
    public sealed class HazardDamageEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public string HazardId { get; }
        public int Amount { get; }
        public HazardDamageEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant, string hazardId, int amount) { Combatant = combatant; HazardId = hazardId; Amount = amount; }
    }

    /// <summary>El que entraba RETIRÓ la trampa (un tipo Veneno absorbe las Púas Tóxicas).</summary>
    public sealed class HazardAbsorbedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public string HazardId { get; }
        public HazardAbsorbedEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant, string hazardId) { Combatant = combatant; HazardId = hazardId; }
    }

    /// <summary>El movimiento no tuvo ningún efecto ("¡Pero falló!"): trampa ya al máximo, nadie a quien cambiar...</summary>
    public sealed class MoveFailedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public MoveFailedEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>
    /// Un Rugido/Remolino echó al objetivo del campo. Si BattleEnded = true, era un combate salvaje y se
    /// terminó; si no, a continuación viene la entrada del que lo sustituye.
    /// </summary>
    public sealed class ForcedOutEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> Combatant { get; }
        public bool BattleEnded { get; }
        public ForcedOutEvent(CTEditor.SharedKernel.ValueObjects.Id<BattleParticipant> combatant, bool battleEnded) { Combatant = combatant; BattleEnded = battleEnded; }
    }

    // =====================================================================
    // CIERRE DEL COMBATE: efectos de lado, sustituto, Niebla, Foco Energía, Anulación, Otra Vez,
    // Saña, Furia, Venganza, Metrónomo/Espejo/Mimético, Transformación, Conversión y cambios propios.
    // =====================================================================

    /// <summary>Se puso un efecto de lado ("¡Reflejo protege a tu equipo!").</summary>
    public sealed class SideConditionStartedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public bool PlayerSide { get; }
        public string ConditionId { get; }
        public SideConditionStartedEvent(bool playerSide, string conditionId) { PlayerSide = playerSide; ConditionId = conditionId; }
    }

    /// <summary>Se acabó un efecto de lado.</summary>
    public sealed class SideConditionEndedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public bool PlayerSide { get; }
        public string ConditionId { get; }
        public SideConditionEndedEvent(bool playerSide, string conditionId) { PlayerSide = playerSide; ConditionId = conditionId; }
    }

    /// <summary>Un efecto de lado protegió a alguien (Neblina frenó una bajada, Velo Sagrado un estado).</summary>
    public sealed class ProtectedBySideEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public string ConditionId { get; }
        public ProtectedBySideEvent(Id<BattleParticipant> combatant, string conditionId) { Combatant = combatant; ConditionId = conditionId; }
    }

    /// <summary>Creó un sustituto (pagando PS).</summary>
    public sealed class SubstituteCreatedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int Cost { get; }
        public SubstituteCreatedEvent(Id<BattleParticipant> combatant, int cost) { Combatant = combatant; Cost = cost; }
    }

    /// <summary>El sustituto recibió el golpe en su lugar.</summary>
    public sealed class SubstituteDamagedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int Amount { get; }
        public SubstituteDamagedEvent(Id<BattleParticipant> combatant, int amount) { Combatant = combatant; Amount = amount; }
    }

    /// <summary>El sustituto se rompió.</summary>
    public sealed class SubstituteBrokeEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public SubstituteBrokeEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>El sustituto bloqueó un efecto (estado, bajada de etapa...).</summary>
    public sealed class SubstituteBlockedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public SubstituteBlockedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Sus etapas volvieron a 0 (Niebla).</summary>
    public sealed class StagesResetEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public StagesResetEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Se concentró: más probabilidad de crítico (Foco Energía).</summary>
    public sealed class CritBoostedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public CritBoostedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Un movimiento del objetivo quedó ANULADO.</summary>
    public sealed class MoveDisabledEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public MoveDisabledEvent(Id<BattleParticipant> combatant, Id<Move> move) { Combatant = combatant; Move = move; }
    }

    /// <summary>Intentó usar un movimiento anulado y no pudo.</summary>
    public sealed class DisabledMoveTriedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public DisabledMoveTriedEvent(Id<BattleParticipant> combatant, Id<Move> move) { Combatant = combatant; Move = move; }
    }

    /// <summary>Terminó la anulación.</summary>
    public sealed class DisableEndedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public DisableEndedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Otra Vez: tendrá que repetir 'Move'.</summary>
    public sealed class EncoreStartedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public EncoreStartedEvent(Id<BattleParticipant> combatant, Id<Move> move) { Combatant = combatant; Move = move; }
    }

    /// <summary>Terminó Otra Vez.</summary>
    public sealed class EncoreEndedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public EncoreEndedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Terminó de encadenar (Saña): a continuación puede venir su estado (confusión).</summary>
    public sealed class RampageEndedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public RampageEndedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>La furia aumenta (Furia): a continuación viene la subida de etapa.</summary>
    public sealed class RageBuildingEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public RageBuildingEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Venganza: está aguantando (Started = true el primer turno).</summary>
    public sealed class BideStoringEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public bool Started { get; }
        public BideStoringEvent(Id<BattleParticipant> combatant, bool started) { Combatant = combatant; Started = started; }
    }

    /// <summary>Venganza: ¡desata la energía!</summary>
    public sealed class BideUnleashedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public BideUnleashedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Un movimiento llamó a otro (Metrónomo → Lanzallamas; Espejo → el del rival).</summary>
    public sealed class MoveCalledEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Via { get; }
        public Id<Move> Called { get; }
        public MoveCalledEvent(Id<BattleParticipant> combatant, Id<Move> via, Id<Move> called) { Combatant = combatant; Via = via; Called = called; }
    }

    /// <summary>Copió un movimiento (Mimético).</summary>
    public sealed class MoveCopiedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public MoveCopiedEvent(Id<BattleParticipant> combatant, Id<Move> move) { Combatant = combatant; Move = move; }
    }

    /// <summary>Se transformó en el objetivo.</summary>
    public sealed class TransformedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<BattleParticipant> Into { get; }
        public TransformedEvent(Id<BattleParticipant> combatant, Id<BattleParticipant> into) { Combatant = combatant; Into = into; }
    }

    /// <summary>Cambió de tipo (Conversión).</summary>
    public sealed class TypeChangedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public IReadOnlyList<Id<ElementType>> Types { get; }
        public TypeChangedEvent(Id<BattleParticipant> combatant, IReadOnlyList<Id<ElementType>> types) { Combatant = combatant; Types = types; }
    }

    /// <summary>
    /// El JUGADOR tiene que elegir quién entra tras Ida y Vuelta / Relevo. El turno queda EN PAUSA hasta
    /// que se llame a TurnResolver.ResumeTurn con el elegido.
    /// </summary>
    public sealed class SelfSwitchRequiredEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public bool PassesBoosts { get; }
        public SelfSwitchRequiredEvent(Id<BattleParticipant> combatant, bool passesBoosts) { Combatant = combatant; PassesBoosts = passesBoosts; }
    }

    /// <summary>Se retira por su propio movimiento (Ida y Vuelta / Relevo); a continuación entra otro.</summary>
    public sealed class SelfSwitchedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public bool PassesBoosts { get; }
        public SelfSwitchedEvent(Id<BattleParticipant> combatant, bool passesBoosts) { Combatant = combatant; PassesBoosts = passesBoosts; }
    }

    /// <summary>Escapó con Teletransporte (combate salvaje terminado).</summary>
    public sealed class TeleportedEvent : CTEditor.SharedKernel.Events.IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public TeleportedEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }
}
