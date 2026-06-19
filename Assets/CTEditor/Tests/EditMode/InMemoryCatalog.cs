using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Un catálogo en memoria, puro, para tests y escenarios headless. Misma idea que el
    /// ScriptableObjectCatalog, pero sin Unity: se le pasan objetos ya construidos en código y una
    /// función para sacarles el id. Demuestra que el dominio no sabe (ni le importa) si el contenido
    /// viene de assets o de código: solo pide por la interfaz ICatalog&lt;T&gt;.
    /// </summary>
    public sealed class InMemoryCatalog<T> : ICatalog<T> where T : class
    {
        private readonly Dictionary<string, T> _byId = new Dictionary<string, T>();

        public InMemoryCatalog(IEnumerable<T> items, Func<T, string> getId)
        {
            foreach (var item in items)
                _byId[getId(item)] = item;
        }

        public bool TryGet(Id<T> id, out T item) => _byId.TryGetValue(id.Value, out item);

        public T Get(Id<T> id)
        {
            if (_byId.TryGetValue(id.Value, out var item))
                return item;
            throw new KeyNotFoundException($"No existe {typeof(T).Name} con id '{id.Value}'.");
        }

        public bool Contains(Id<T> id) => _byId.ContainsKey(id.Value);

        public IReadOnlyCollection<T> All => _byId.Values;
    }
}
