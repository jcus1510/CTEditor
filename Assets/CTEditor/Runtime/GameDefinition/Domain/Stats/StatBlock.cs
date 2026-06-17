using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Stats
{
    /// <summary>
    /// Un conjunto de valores de estadísticas indexado POR CLAVE (no por campos fijos): eso es lo
    /// que permite que el autor invente stats. INMUTABLE: nunca se muta la base (E.3); para derivar
    /// un bloque nuevo usa With(...). Los 6 clásicos tienen accesores cómodos para que el bloque
    /// "se sienta" fijo en código sin renunciar a la apertura.
    ///
    /// Un único camino para todos los stats (clásicos e inventados): sin casos especiales.
    /// </summary>
    public sealed class StatBlock
    {
        private readonly Dictionary<StatId, int> _values;

        public StatBlock(IReadOnlyDictionary<StatId, int> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            _values = new Dictionary<StatId, int>(values.Count);
            foreach (var kv in values) _values[kv.Key] = kv.Value;
        }

        /// <summary>Valor de un stat. Si la clave no está, devuelve 0 (ausencia = cero).</summary>
        public int Of(StatId id) => _values.TryGetValue(id, out var v) ? v : 0;

        public bool Has(StatId id) => _values.ContainsKey(id);
        public IEnumerable<StatId> Stats => _values.Keys;
        public int Count => _values.Count;

        // --- Accesores clásicos (ergonomía) ---
        public int Hp        => Of(StatId.Hp);
        public int Attack    => Of(StatId.Attack);
        public int Defense   => Of(StatId.Defense);
        public int SpAttack  => Of(StatId.SpAttack);
        public int SpDefense => Of(StatId.SpDefense);
        public int Speed     => Of(StatId.Speed);

        /// <summary>Devuelve un bloque NUEVO con una clave cambiada. No muta este (E.3).</summary>
        public StatBlock With(StatId id, int value)
        {
            var copy = new Dictionary<StatId, int>(_values.Count + 1);
            foreach (var kv in _values) copy[kv.Key] = kv.Value;
            copy[id] = value;
            return new StatBlock(copy);
        }

        /// <summary>Constructor cómodo para armar un bloque paso a paso (lo usará la ACL/factory).</summary>
        public sealed class Builder
        {
            private readonly Dictionary<StatId, int> _v = new Dictionary<StatId, int>();

            public Builder Set(StatId id, int value)
            {
                _v[id] = value;
                return this;
            }

            public StatBlock Build() => new StatBlock(_v);
        }
    }
}
