using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.Battle.Domain.Events
{
    // Eventos de las mecánicas de la 3.ª y 4.ª generación.

    /// <summary>Una HABILIDAD se activa (para narrarla): «¡Llovizna de Politoed hizo llover!». Detail = qué pasó (clave corta).</summary>
    public sealed class AbilityTriggeredEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public string AbilityId { get; }
        public string Detail { get; }
        public AbilityTriggeredEvent(Id<BattleParticipant> combatant, string abilityId, string detail = "")
        { Combatant = combatant; AbilityId = abilityId ?? ""; Detail = detail ?? ""; }
    }

    /// <summary>Cambia la habilidad de alguien (Rastro, Imitación, Intercambio, Abatidoras).</summary>
    public sealed class AbilityChangedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public string AbilityId { get; }
        public AbilityChangedEvent(Id<BattleParticipant> combatant, string abilityId) { Combatant = combatant; AbilityId = abilityId ?? ""; }
    }

    /// <summary>Un objeto cambia de manos o se pierde (Ladrón, Truco, Desarme, Picoteo). To vacío = se perdió.</summary>
    public sealed class ItemTransferredEvent : IDomainEvent
    {
        public Id<BattleParticipant> From { get; }
        public Id<BattleParticipant>? To { get; }
        public string ItemId { get; }
        public ItemTransferredEvent(Id<BattleParticipant> from, Id<BattleParticipant>? to, string itemId) { From = from; To = to; ItemId = itemId ?? ""; }
    }

    /// <summary>Recupera un objeto gastado (Reciclaje, Cosecha).</summary>
    public sealed class ItemRestoredEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public string ItemId { get; }
        public ItemRestoredEvent(Id<BattleParticipant> combatant, string itemId) { Combatant = combatant; ItemId = itemId ?? ""; }
    }

    /// <summary>No puede usar ese movimiento por un estado (Mofa, Tormento, Cerca, Anticura, Humedad). Reason = id del estado o habilidad.</summary>
    public sealed class MoveRestrictedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public string Reason { get; }
        public MoveRestrictedEvent(Id<BattleParticipant> combatant, Id<Move> move, string reason) { Combatant = combatant; Move = move; Reason = reason ?? ""; }
    }

    /// <summary>Un movimiento de estado rebota (Capa Mágica, Espejo Mágico) o se lo roba otro (Robo).</summary>
    public sealed class MoveReflectedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }  // quien lo devuelve / roba
        public Id<Move> Move { get; }
        public bool Stolen { get; }
        public MoveReflectedEvent(Id<BattleParticipant> combatant, Id<Move> move, bool stolen) { Combatant = combatant; Move = move; Stolen = stolen; }
    }

    /// <summary>Cambian los PS sin ser daño de ataque (Divide Dolor). Delta positivo cura; negativo daña.</summary>
    public sealed class HpChangedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int Delta { get; }
        public HpChangedEvent(Id<BattleParticipant> combatant, int delta) { Combatant = combatant; Delta = delta; }
    }

    /// <summary>Se prepara un efecto retardado (Deseo, Premonición).</summary>
    public sealed class DelayedEffectSetEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public bool Heals { get; }
        public DelayedEffectSetEvent(Id<BattleParticipant> combatant, Id<Move> move, bool heals) { Combatant = combatant; Move = move; Heals = heals; }
    }

    /// <summary>Llega un efecto retardado (el daño/curación va en HpRestored / DamageDealt).</summary>
    public sealed class DelayedEffectTriggeredEvent : IDomainEvent
    {
        public Id<BattleParticipant> Target { get; }
        public Id<Move> Move { get; }
        public bool Heals { get; }
        public DelayedEffectTriggeredEvent(Id<BattleParticipant> target, Id<Move> move, bool heals) { Target = target; Move = move; Heals = heals; }
    }

    /// <summary>Reserva: cuántas lleva ahora.</summary>
    public sealed class StockpileEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int Count { get; }
        public StockpileEvent(Id<BattleParticipant> combatant, int count) { Combatant = combatant; Count = count; }
    }

    /// <summary>Holgazanea (Ausente): pierde el turno.</summary>
    public sealed class LoafingEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public LoafingEvent(Id<BattleParticipant> combatant) { Combatant = combatant; }
    }

    /// <summary>Se intercambian o copian etapas/estadísticas (Cambia Fuerza, Más Psique, Truco Fuerza).</summary>
    public sealed class StatsSwappedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<BattleParticipant>? Other { get; }
        public bool Copied { get; }
        public StatsSwappedEvent(Id<BattleParticipant> combatant, Id<BattleParticipant>? other, bool copied) { Combatant = combatant; Other = other; Copied = copied; }
    }

    /// <summary>Mismo Destino / Rabia se cumplen.</summary>
    public sealed class DestinyBondEvent : IDomainEvent
    {
        public Id<BattleParticipant> Holder { get; }
        public Id<BattleParticipant> Attacker { get; }
        public bool Grudge { get; }
        public DestinyBondEvent(Id<BattleParticipant> holder, Id<BattleParticipant> attacker, bool grudge) { Holder = holder; Attacker = attacker; Grudge = grudge; }
    }

    /// <summary>Canto Mortal: cuenta atrás (Count turnos que quedan).</summary>
    public sealed class PerishCountEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int Count { get; }
        public PerishCountEvent(Id<BattleParticipant> combatant, int count) { Combatant = combatant; Count = count; }
    }

    /// <summary>Un combatiente CAMBIA DE FORMA: «¡Darmanitan activó el Modo Daruma!». To = "" → vuelve a la forma normal.</summary>
    public sealed class FormChangedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public string From { get; }
        public string To { get; }
        /// <summary>Nombre de la forma nueva ("" = la normal).</summary>
        public string FormName { get; }
        public FormChangedEvent(Id<BattleParticipant> combatant, string from, string to, string formName)
        { Combatant = combatant; From = from ?? ""; To = to ?? ""; FormName = formName ?? ""; }
    }

    /// <summary>MEGAEVOLUCIÓN: «¡La Charizardita X de Charizard reacciona con la Megapulsera! ¡Charizard megaevolucionó en Mega-Charizard X!».</summary>
    public sealed class MegaEvolvedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public string FormId { get; }
        public string FormName { get; }
        /// <summary>La megapiedra (vacío si no hace falta: Rayquaza).</summary>
        public string StoneId { get; }
        public MegaEvolvedEvent(Id<BattleParticipant> combatant, string formId, string formName, string stoneId)
        { Combatant = combatant; FormId = formId ?? ""; FormName = formName ?? ""; StoneId = stoneId ?? ""; }
    }

    /// <summary>MOVIMIENTO Z: «¡Pikachu libera todo su poder Z! ¡Gigarrayo Fulminante!».</summary>
    public sealed class ZMoveUsedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        /// <summary>The move it was based on.</summary>
        public Id<Move> BaseMove { get; }
        /// <summary>The Z move used (the base move itself for status moves).</summary>
        public Id<Move> ZMove { get; }
        /// <summary>The Z crystal that made it possible.</summary>
        public string CrystalId { get; }
        public ZMoveUsedEvent(Id<BattleParticipant> combatant, Id<Move> baseMove, Id<Move> zMove, string crystalId)
        { Combatant = combatant; BaseMove = baseMove; ZMove = zMove; CrystalId = crystalId ?? ""; }
    }
}
