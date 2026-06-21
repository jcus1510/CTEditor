using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// Un bando del combate: su combatiente ACTIVO y los de reserva (la "banca"). El activo es
    /// siempre uno de los miembros. Cambiar de monstruo = mover quién es el activo (no se crea ni se
    /// destruye nada). El bando está "barrido" (IsWipedOut) cuando TODOS sus miembros están
    /// debilitados: ahí pierde. Es parte del agregado Battle; sus transiciones son internal.
    /// </summary>
    public sealed class BattleTeam
    {
        private readonly List<Combatant> _members;

        public Combatant Active { get; private set; }
        public IReadOnlyList<Combatant> Members => _members;

        public BattleTeam(IReadOnlyList<BattleParticipant> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
                throw new ArgumentException("Un equipo necesita al menos un participante.", nameof(snapshots));

            _members = new List<Combatant>(snapshots.Count);
            foreach (var s in snapshots)
                _members.Add(new Combatant(s)); // ctor internal: mismo assembly

            Active = _members[0];
        }

        /// <summary>¿Hay algún miembro NO debilitado distinto del activo? (candidato a relevo).</summary>
        public bool HasUnfaintedReserves
        {
            get
            {
                foreach (var m in _members)
                    if (!ReferenceEquals(m, Active) && !m.IsFainted) return true;
                return false;
            }
        }

        /// <summary>El bando perdió: todos sus miembros están debilitados.</summary>
        public bool IsWipedOut
        {
            get
            {
                foreach (var m in _members)
                    if (!m.IsFainted) return false;
                return true;
            }
        }

        /// <summary>Busca un miembro por su id de combate (null si no está).</summary>
        public Combatant Find(Id<BattleParticipant> id)
        {
            foreach (var m in _members)
                if (m.Id.Equals(id)) return m;
            return null;
        }

        /// <summary>
        /// Cambia el activo a otro miembro (por id). Devuelve true si fue válido: el miembro existe,
        /// no es ya el activo y no está debilitado. El TurnResolver y Battle son quienes lo invocan.
        /// </summary>
        internal bool SwitchTo(Id<BattleParticipant> id)
        {
            var next = Find(id);
            if (next == null || ReferenceEquals(next, Active) || next.IsFainted) return false;
            Active = next;
            return true;
        }
    }
}
