using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Abilities;

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

        /// <summary>La habilidad del combatiente (de su especie). Null = ninguna. Solo lectura.</summary>
        public AbilityId? Ability { get; }

        /// <summary>Cuántos turnos lleva activo el estado actual (para tóxico progresivo y duraciones).</summary>
        public int StatusTurns { get; private set; }

        /// <summary>
        /// Etapas de stats en combate (-6..+6 por stat). NO modifica el StatBlock base (inmutable):
        /// es un modificador temporal que vive solo durante la batalla. El TurnResolver lo combina
        /// con la stat base y los modificadores de estado en EffectiveStat.
        /// </summary>
        private readonly Dictionary<StatId, int> _stages = new Dictionary<StatId, int>();

        /// <summary>Límite clásico de etapas por stat.</summary>
        public const int MinStage = -6;
        public const int MaxStage = 6;

        /// <summary>
        /// Retroceso (flinch): marca transitoria de UN turno. Si está activa cuando le toca actuar,
        /// el combatiente pierde el turno. Se limpia al final de cada turno. Solo "pega" si quien
        /// provocó el flinch actuó ANTES (si ya actuó, la marca no le afecta este turno).
        /// </summary>
        public bool Flinched { get; private set; }

        /// <summary>Si tiene valor, el combatiente está CARGANDO ese movimiento y lo lanzará el próximo turno.</summary>
        public Id<Move>? ChargingMove { get; private set; }

        /// <summary>Si true, este turno debe RECARGAR (tras un movimiento de recarga) y pierde la acción.</summary>
        public bool MustRecharge { get; private set; }

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
            Ability = snapshot.Ability;
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

        // Revive: cura a un combatiente DEBILITADO (HealHp se niega a tocar a un caído). Solo aplica
        // si está debilitado; deja al menos 1 PS y nunca supera el máximo.
        internal void Revive(int hp)
        {
            if (!IsFainted) return;
            CurrentHp = Math.Min(MaxHp, Math.Max(1, hp));
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

        // Retroceso (flinch): lo marca un movimiento durante la resolución; se limpia al fin de turno.
        internal void SetFlinched() => Flinched = true;
        internal void ClearFlinch() => Flinched = false;

        // Dos turnos: cargar un movimiento y, al turno siguiente, lanzarlo.
        internal void SetChargingMove(Id<Move> move) => ChargingMove = move;
        internal void ClearChargingMove() => ChargingMove = null;

        // Recarga obligatoria tras un movimiento de recarga.
        internal void SetMustRecharge() => MustRecharge = true;
        internal void ClearRecharge() => MustRecharge = false;

        /// <summary>Etapa actual de una stat (0 si nunca se modificó).</summary>
        public int GetStage(StatId stat)
            => _stages.TryGetValue(stat, out var s) ? s : 0;

        /// <summary>
        /// Cambia la etapa de una stat aplicando el delta y recortando a [-6, +6].
        /// Devuelve el delta REAL aplicado (0 si ya estaba en el tope): así el resolvedor sabe
        /// si emitir el evento o no.
        /// </summary>
        internal int ChangeStage(StatId stat, int delta)
        {
            int current = GetStage(stat);
            int next = Math.Max(MinStage, Math.Min(MaxStage, current + delta));
            _stages[stat] = next;
            return next - current;
        }
    }
}
