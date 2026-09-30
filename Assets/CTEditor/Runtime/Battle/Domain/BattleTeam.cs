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

            // Sale a combatir el PRIMERO que puede luchar (como en los juegos: un debilitado en la
            // primera posición no sale). Si todos están debilitados, el primero (el combate acabará).
            Active = _members[0];
            foreach (var m in _members)
                if (!m.IsFainted) { Active = m; break; }
        }

        // ---------------- Trampas de campo en ESTE lado ----------------
        private readonly Dictionary<string, int> _hazards = new Dictionary<string, int>();

        /// <summary>Trampas colocadas en este lado: id → capas.</summary>
        public IReadOnlyDictionary<string, int> Hazards => _hazards;

        public int LayersOf(string hazardId) => hazardId != null && _hazards.TryGetValue(hazardId, out var n) ? n : 0;

        /// <summary>Añade una capa (hasta el máximo). Devuelve las capas resultantes, o 0 si ya estaba al máximo.</summary>
        internal int AddHazardLayer(string hazardId, int maxLayers)
        {
            int now = LayersOf(hazardId);
            if (now >= maxLayers) return 0;
            _hazards[hazardId] = now + 1;
            return now + 1;
        }

        /// <summary>Quita una trampa (id) o todas (id vacío). Devuelve los ids quitados.</summary>
        internal List<string> ClearHazards(string hazardId)
        {
            var removed = new List<string>();
            if (string.IsNullOrEmpty(hazardId)) { removed.AddRange(_hazards.Keys); _hazards.Clear(); }
            else if (_hazards.Remove(hazardId)) removed.Add(hazardId);
            return removed;
        }

        // ---------------- Efectos de lado (Reflejo, Pantalla de Luz, Neblina...) ----------------
        private readonly Dictionary<string, int> _sideConditions = new Dictionary<string, int>();

        /// <summary>Efectos de lado activos: id → turnos que quedan.</summary>
        public IReadOnlyDictionary<string, int> SideConditions => _sideConditions;
        public bool HasSideCondition(string id) => id != null && _sideConditions.ContainsKey(id);

        /// <summary>Pone un efecto de lado. false si ya estaba.</summary>
        internal bool AddSideCondition(string id, int turns)
        {
            if (string.IsNullOrEmpty(id) || _sideConditions.ContainsKey(id)) return false;
            _sideConditions[id] = Math.Max(1, turns);
            return true;
        }

        /// <summary>Resta un turno a cada efecto; devuelve los que se acabaron.</summary>
        internal List<string> TickSideConditions()
        {
            var ended = new List<string>();
            foreach (var id in new List<string>(_sideConditions.Keys))
            {
                if (--_sideConditions[id] > 0) continue;
                _sideConditions.Remove(id);
                ended.Add(id);
            }
            return ended;
        }

        /// <summary>Quita efectos de lado (Demolición, Despejar). Devuelve los que quitó.</summary>
        internal List<string> RemoveSideConditions(Func<string, bool> which)
        {
            var removed = new List<string>();
            foreach (var id in new List<string>(_sideConditions.Keys))
                if (which(id)) { _sideConditions.Remove(id); removed.Add(id); }
            return removed;
        }

        // ---------------- 3.ª y 4.ª generación ----------------

        /// <summary>Deseo Cura / Danza Lunar: el próximo que entre en este lado se cura del todo.</summary>
        public bool PendingHealingWish { get; internal set; }

        /// <summary>Efectos retardados sobre este lado (Deseo: cura; Premonición: daño).</summary>
        internal readonly List<DelayedEffect> Delayed = new List<DelayedEffect>();

        /// <summary>Miembros NO debilitados en la banca (candidatos a salir).</summary>
        public List<Combatant> Reserves()
        {
            var list = new List<Combatant>();
            foreach (var m in _members) if (!ReferenceEquals(m, Active) && !m.IsFainted) list.Add(m);
            return list;
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
