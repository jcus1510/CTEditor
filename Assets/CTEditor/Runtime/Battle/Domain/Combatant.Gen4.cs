using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// Estado de combate de la 3.ª y 4.ª generación: peso, habilidad cambiante, turnos en el campo,
    /// movimientos usados, Reserva, objetos perdidos, Ausente, Absorbe Fuego...
    /// </summary>
    public sealed partial class Combatant
    {
        /// <summary>Peso en kilos (de su Pokédex).</summary>
        public float WeightKg { get; }

        // --- Habilidad que cambia en combate (Imitación, Intercambio, Abatidoras, Rastro) ---
        private bool _abilityChanged;
        private AbilityId? _abilityOverride;
        /// <summary>Cambia la habilidad hasta que se retire (null = sin habilidad).</summary>
        internal void SetAbility(AbilityId? ability) { _abilityChanged = true; _abilityOverride = ability; }

        // --- Turnos en el campo (Sorpresa, Inicio Lento) ---
        /// <summary>Turnos completos que lleva en el campo (0 = acaba de entrar).</summary>
        public int TurnsOnField { get; private set; }
        internal void TickTurnOnField() => TurnsOnField++;

        // --- Movimientos usados desde que entró (Última Baza) y seguidos (Rodar, Corte Furia) ---
        private readonly HashSet<Id<Move>> _usedSinceEntry = new HashSet<Id<Move>>();
        /// <summary>Veces SEGUIDAS que ha usado con éxito el mismo movimiento (antes de este uso).</summary>
        public int ConsecutiveUses { get; private set; }
        private Id<Move>? _lastSuccessful;

        internal void NoteMoveSuccess(Id<Move> move, bool success)
        {
            _usedSinceEntry.Add(move);
            if (success && _lastSuccessful.HasValue && _lastSuccessful.Value == move) ConsecutiveUses++;
            else ConsecutiveUses = 0;
            _lastSuccessful = success ? move : (Id<Move>?)null;
        }

        /// <summary>¿Ya usó todos sus movimientos salvo 'except'? (Última Baza)</summary>
        public bool HasUsedAllOtherMoves(Id<Move> except)
        {
            bool any = false;
            foreach (var m in Moves)
            {
                if (m == except) continue;
                any = true;
                if (!_usedSinceEntry.Contains(m)) return false;
            }
            return any;
        }

        // --- Reserva ---
        public int Stockpile { get; private set; }
        internal bool AddStockpile() { if (Stockpile >= 3) return false; Stockpile++; return true; }
        internal int ClearStockpile() { int n = Stockpile; Stockpile = 0; return n; }

        // --- Objetos ---
        /// <summary>El último objeto que gastó (para Reciclaje y Cosecha).</summary>
        public string ConsumedItem { get; private set; }
        /// <summary>¿Perdió o gastó su objeto en este combate? (Liviano)</summary>
        public bool LostItem { get; private set; }
        internal void SetHeldItem(string itemId) => HeldItem = string.IsNullOrWhiteSpace(itemId) ? null : itemId;
        /// <summary>Le quitan el objeto (Desarme, Ladrón). Devuelve cuál era.</summary>
        internal string TakeHeldItem()
        {
            var was = HeldItem;
            if (was == null) return null;
            HeldItem = null;
            LostItem = true;
            return was;
        }
        internal bool RestoreConsumedItem()
        {
            if (HeldItem != null || string.IsNullOrEmpty(ConsumedItem)) return false;
            HeldItem = ConsumedItem;
            ConsumedItem = null;
            return true;
        }

        // --- Ausente ---
        /// <summary>Con Ausente: este turno le toca holgazanear.</summary>
        public bool Loafing { get; private set; }
        internal void ToggleLoafing() => Loafing = !Loafing;

        // --- Absorbe Fuego ---
        /// <summary>Tipo potenciado tras absorber un golpe (Absorbe Fuego). Null = nada.</summary>
        public Id<ElementType>? AbsorbedTypeBoost { get; private set; }
        internal void SetAbsorbedTypeBoost(Id<ElementType> type) => AbsorbedTypeBoost = type;

        // --- Daño ---
        /// <summary>El último daño recibido este turno (Represión Metal).</summary>
        public int LastDamageTaken { get; private set; }
        /// <summary>¿Recibió algún daño de un ataque este turno?</summary>
        public bool DamagedThisTurn { get; internal set; }

        // --- Deseo Cura: el próximo que entre en su lado se cura ---
        // (lo guarda el equipo; aquí nada)

        // Lo que se olvida al retirarse (además de lo que ya limpia OnSwitchedOut).
        /// <summary>Movimiento al que le ata un objeto Elección (se suelta al retirarse).</summary>
        public Id<Move>? ChoiceLockedMove { get; private set; }
        internal void LockChoice(Id<Move> move) { if (!ChoiceLockedMove.HasValue) ChoiceLockedMove = move; }
        internal void ClearChoiceLock() => ChoiceLockedMove = null;

        private void ResetFieldState()
        {
            ChoiceLockedMove = null;
            _abilityChanged = false;
            _abilityOverride = null;
            TurnsOnField = 0;
            _usedSinceEntry.Clear();
            ConsecutiveUses = 0;
            _lastSuccessful = null;
            Stockpile = 0;
            Loafing = false;
            AbsorbedTypeBoost = null;
            LastDamageTaken = 0;
        }

        /// <summary>Cambia UNA estadística base por otra (Truco Fuerza): mientras siga en el campo.</summary>
        internal void SwapBaseStats(CTEditor.GameDefinition.Domain.Stats.StatId a, CTEditor.GameDefinition.Domain.Stats.StatId b)
        {
            var baseStats = Stats;
            var builder = new CTEditor.GameDefinition.Domain.Stats.StatBlock.Builder();
            foreach (var id in baseStats.Stats)
            {
                int v = id == a ? baseStats.Of(b) : id == b ? baseStats.Of(a) : baseStats.Of(id);
                builder.Set(id, v);
            }
            _statsOverride = builder.Build();
        }
    }
}
