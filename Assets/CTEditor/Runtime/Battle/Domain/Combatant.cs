using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Status;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// El COMBATIENTE: la copia de trabajo INTERNA de Battle. Se crea desde el snapshot
    /// (BattleParticipant) al empezar el combate, y es lo que Battle MUTA durante la pelea (sus PS
    /// bajan, etc.). El snapshot original y, más atrás, el MonsterInstance, no se tocan: por eso el
    /// daño cae aquí y no allá. Al final, la diferencia entre el estado de este Combatant y el
    /// snapshot se convierte en los "resultados-out".
    ///
    /// Conserva el MISMO id que su participante, para poder mapear resultados al final.
    /// El constructor es 'internal': solo Battle (este assembly) crea combatientes, desde un snapshot.
    /// </summary>
    public sealed class Combatant
    {
        public Id<BattleParticipant> Id { get; }
        public int Level { get; }
        public StatBlock Stats { get; }
        public IReadOnlyList<Id<ElementType>> Types { get; }
        public IReadOnlyList<Id<Move>> Moves { get; }

        /// <summary>PS actuales DENTRO del combate. Es lo único mutable aquí (de momento).</summary>
        public int CurrentHp { get; private set; }

        /// <summary>Estado alterado actual (quemado, etc.). Null = sano. Mutable durante el combate.</summary>
        public StatusId? Status { get; private set; }

        /// <summary>Cuántos turnos lleva activo el estado actual (para tóxico progresivo y duraciones).</summary>
        public int StatusTurns { get; private set; }

        internal Combatant(BattleParticipant snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            Id = snapshot.Id;
            Level = snapshot.Level;
            Stats = snapshot.Stats;       // StatBlock ya es inmutable
            Types = snapshot.Types;       // ya vienen como listas de solo lectura copiadas
            Moves = snapshot.Moves;
            CurrentHp = snapshot.CurrentHp;
            Status = snapshot.InitialStatus;
        }

        public int MaxHp => Stats.Of(StatId.Hp);
        public bool IsFainted => CurrentHp <= 0;

        // Mutaciones internas del combate. 'internal' porque solo la lógica de Battle las dispara,
        // nunca código de fuera del contexto.
        internal void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            CurrentHp = Math.Max(0, CurrentHp - amount);
        }

        internal void HealHp(int amount)
        {
            if (amount <= 0 || IsFainted) return;
            CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
        }

        // Estado alterado: lo fija/limpia solo la lógica de Battle (TurnResolver).
        internal void SetStatus(StatusId status)
        {
            Status = status;
            StatusTurns = 0; // arranca el contador para este estado
        }

        internal void ClearStatus()
        {
            Status = null;
            StatusTurns = 0;
        }

        // Avanza el contador de turnos del estado (lo llama el fin de turno).
        internal void AdvanceStatusTurn() => StatusTurns++;
    }
}
