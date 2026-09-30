using System.Collections.Generic;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.Battle.Domain;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Adventure.Domain
{
    // Eventos de la SESIÓN de combate: lo que pasa alrededor de los turnos (aparece un salvaje, el
    // entrenador saca otro, sube de nivel, aprende un movimiento, gana dinero, evoluciona...). Se
    // mezclan en orden con los eventos del motor, así la interfaz los narra todos igual.

    /// <summary>Tipo de combate.</summary>
    public enum BattleKind { Wild, Trainer }

    /// <summary>Empieza el combate: "¡Un Pidgey salvaje apareció!" o "¡El Cazabichos Pepe te desafía!".</summary>
    public sealed class BattleIntroEvent : IDomainEvent
    {
        public BattleKind Kind { get; }
        public string TrainerName { get; }
        public string IntroLine { get; }
        public Id<BattleParticipant> FirstEnemy { get; }
        public BattleIntroEvent(BattleKind kind, Id<BattleParticipant> firstEnemy, string trainerName = "", string introLine = "")
        { Kind = kind; FirstEnemy = firstEnemy; TrainerName = trainerName ?? ""; IntroLine = introLine ?? ""; }
    }

    /// <summary>Un bando saca a un monstruo al campo (al empezar, o el entrenador tras un debilitado).</summary>
    public sealed class SentOutEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public bool IsPlayer { get; }
        public string TrainerName { get; }
        public SentOutEvent(Id<BattleParticipant> combatant, bool isPlayer, string trainerName = "")
        { Combatant = combatant; IsPlayer = isPlayer; TrainerName = trainerName ?? ""; }
    }

    /// <summary>Subió de nivel (en mitad del combate). Incluye cuánto subió cada estadística.</summary>
    public sealed class LevelUpEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public int NewLevel { get; }
        public IReadOnlyDictionary<StatId, int> StatGains { get; }
        public LevelUpEvent(Id<BattleParticipant> combatant, int newLevel, IReadOnlyDictionary<StatId, int> statGains)
        { Combatant = combatant; NewLevel = newLevel; StatGains = statGains; }
    }

    /// <summary>Aprendió un movimiento (en un hueco libre, u olvidando otro).</summary>
    public sealed class MoveLearnedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        /// <summary>El que olvidó para aprenderlo (null = había hueco).</summary>
        public Id<Move>? Forgotten { get; }
        public MoveLearnedEvent(Id<BattleParticipant> combatant, Id<Move> move, Id<Move>? forgotten = null)
        { Combatant = combatant; Move = move; Forgotten = forgotten; }
    }

    /// <summary>Quiere aprender un movimiento pero ya tiene todos los huecos llenos: hay que decidir.</summary>
    public sealed class MoveLearnPromptEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public MoveLearnPromptEvent(Id<BattleParticipant> combatant, Id<Move> move) { Combatant = combatant; Move = move; }
    }

    /// <summary>Decidió NO aprender el movimiento.</summary>
    public sealed class MoveNotLearnedEvent : IDomainEvent
    {
        public Id<BattleParticipant> Combatant { get; }
        public Id<Move> Move { get; }
        public MoveNotLearnedEvent(Id<BattleParticipant> combatant, Id<Move> move) { Combatant = combatant; Move = move; }
    }

    /// <summary>
    /// Alguien usó un objeto de su mochila: "Usaste Poción en Pikachu" / "¡El Líder Brock usó Hiperpoción!".
    /// Va justo antes del ItemUsedInBattleEvent del motor (que trae el efecto).
    /// </summary>
    public sealed class BagItemUsedEvent : IDomainEvent
    {
        public bool ByPlayer { get; }
        public string UserName { get; }
        public string ItemId { get; }
        public Id<BattleParticipant> Target { get; }
        public BagItemUsedEvent(bool byPlayer, string userName, string itemId, Id<BattleParticipant> target)
        { ByPlayer = byPlayer; UserName = userName ?? ""; ItemId = itemId ?? ""; Target = target; }
    }

    /// <summary>El entrenador rival habla (al perder o al ganar).</summary>
    public sealed class TrainerSaysEvent : IDomainEvent
    {
        public string TrainerName { get; }
        public string Line { get; }
        public TrainerSaysEvent(string trainerName, string line) { TrainerName = trainerName ?? ""; Line = line ?? ""; }
    }

    /// <summary>Ganó dinero (premio de un entrenador).</summary>
    public sealed class MoneyWonEvent : IDomainEvent
    {
        public int Amount { get; }
        public MoneyWonEvent(int amount) { Amount = amount; }
    }

    /// <summary>Perdió el combate: se queda sin monstruos en pie, pierde dinero y vuelve al Centro.</summary>
    public sealed class BlackoutEvent : IDomainEvent
    {
        public int MoneyLost { get; }
        public bool Healed { get; }
        public BlackoutEvent(int moneyLost, bool healed) { MoneyLost = moneyLost; Healed = healed; }
    }

    /// <summary>El capturado se guardó (en el equipo, en el PC o se liberó por falta de sitio).</summary>
    public sealed class CaptureStoredEvent : IDomainEvent
    {
        public Id<CTEditor.Party.Domain.MonsterInstance> Monster { get; }
        public string Name { get; }
        public StoredIn Where { get; }
        public CaptureStoredEvent(Id<CTEditor.Party.Domain.MonsterInstance> monster, string name, StoredIn where)
        { Monster = monster; Name = name ?? ""; Where = where; }
    }

    /// <summary>"¿Qué? ¡X está evolucionando!": hay que confirmar (o cancelar) la evolución.</summary>
    public sealed class EvolutionPromptEvent : IDomainEvent
    {
        public Id<CTEditor.Party.Domain.MonsterInstance> Monster { get; }
        public Id<SpeciesDef> From { get; }
        public Id<SpeciesDef> To { get; }
        public EvolutionPromptEvent(Id<CTEditor.Party.Domain.MonsterInstance> monster, Id<SpeciesDef> from, Id<SpeciesDef> to)
        { Monster = monster; From = from; To = to; }
    }

    /// <summary>Evolucionó (o se canceló la evolución, si Evolved = false).</summary>
    public sealed class EvolutionResultEvent : IDomainEvent
    {
        public Id<CTEditor.Party.Domain.MonsterInstance> Monster { get; }
        public Id<SpeciesDef> From { get; }
        public Id<SpeciesDef> To { get; }
        public bool Evolved { get; }
        public EvolutionResultEvent(Id<CTEditor.Party.Domain.MonsterInstance> monster, Id<SpeciesDef> from, Id<SpeciesDef> to, bool evolved)
        { Monster = monster; From = from; To = to; Evolved = evolved; }
    }
}
