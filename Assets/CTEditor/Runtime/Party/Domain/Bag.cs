using System;
using System.Collections.Generic;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// La MOCHILA del jugador: cuántas unidades tiene de cada objeto (por id). No sabe qué hace cada
    /// objeto (eso está en su ficha): solo cuenta. Tope por objeto configurable (clásico: 999).
    /// </summary>
    public sealed class Bag
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>(); // orden de llegada (para mostrarla)

        public int MaxPerItem { get; }

        public Bag(int maxPerItem = 999) { MaxPerItem = maxPerItem < 1 ? 1 : maxPerItem; }

        /// <summary>Cuántos hay de ese objeto (0 si ninguno).</summary>
        public int Count(string itemId) => itemId != null && _counts.TryGetValue(itemId, out var n) ? n : 0;

        public bool Has(string itemId, int quantity = 1) => Count(itemId) >= quantity;

        /// <summary>Añade unidades (respetando el tope). Devuelve cuántas entraron de verdad.</summary>
        public int Add(string itemId, int quantity = 1)
        {
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0) return 0;
            int current = Count(itemId);
            int added = Math.Min(quantity, MaxPerItem - current);
            if (added <= 0) return 0;
            if (current == 0) _order.Add(itemId);
            _counts[itemId] = current + added;
            return added;
        }

        /// <summary>Quita unidades. Devuelve false (y no quita nada) si no hay suficientes.</summary>
        public bool Remove(string itemId, int quantity = 1)
        {
            if (quantity <= 0) return true;
            int current = Count(itemId);
            if (current < quantity) return false;
            if (current == quantity) { _counts.Remove(itemId); _order.Remove(itemId); }
            else _counts[itemId] = current - quantity;
            return true;
        }

        /// <summary>Contenido en orden de llegada: (id, cantidad).</summary>
        public IEnumerable<(string itemId, int count)> Contents()
        {
            foreach (var id in _order) yield return (id, _counts[id]);
        }
    }
}
