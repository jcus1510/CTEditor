using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Catalog
{
    /// <summary>
    /// Catálogo en MEMORIA: una lista de fichas ya construidas y una función para sacarles el id.
    /// Lo usan la capa de partida (catálogos vacíos por defecto, contenido generado) y las pruebas.
    /// </summary>
    public sealed class MemoryCatalog<T> : ICatalog<T> where T : class
    {
        private readonly Dictionary<string, T> _byId = new Dictionary<string, T>();

        public MemoryCatalog(IEnumerable<T> items, Func<T, string> getId)
        {
            if (items == null || getId == null) return;
            foreach (var item in items)
                if (item != null) _byId[getId(item)] = item;
        }

        /// <summary>Un catálogo vacío (para lo que el juego no use).</summary>
        public static MemoryCatalog<T> Empty() => new MemoryCatalog<T>(null, null);

        public bool TryGet(Id<T> id, out T item)
        {
            item = null;
            return id.Value != null && _byId.TryGetValue(id.Value, out item);
        }

        public T Get(Id<T> id)
        {
            if (TryGet(id, out var item)) return item;
            throw new KeyNotFoundException($"No existe {typeof(T).Name} con id '{id.Value}'.");
        }

        public bool Contains(Id<T> id) => id.Value != null && _byId.ContainsKey(id.Value);

        public IReadOnlyCollection<T> All => _byId.Values;
    }
}
