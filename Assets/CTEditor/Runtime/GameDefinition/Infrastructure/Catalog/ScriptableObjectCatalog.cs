using System;
using System.Collections.Generic;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;

namespace CTEditor.GameDefinition.Infrastructure.Catalog
{
    /// <summary>
    /// La implementación REAL del catálogo: lee una colección de ScriptableObjects, los mapea a
    /// dominio puro UNA vez, los guarda indexados por su id, y resuelve "dame X por id" al instante.
    ///
    /// Es genérica en DOS tipos a la vez:
    ///   TData = el tipo del ScriptableObject (p.ej. SpeciesData)
    ///   T     = el tipo del dominio resultante (p.ej. Species)
    /// y cumple ICatalog&lt;T&gt;, así que el dominio solo ve "un catálogo de Species" sin saber que por
    /// dentro hay assets de Unity.
    ///
    /// Conceptos nuevos aquí:
    /// - DOS parámetros de tipo y sus restricciones ('where ...').
    /// - 'Func&lt;A, B&gt;' = una FUNCIÓN pasada como argumento (recibe un A, devuelve un B). Le inyectamos
    ///   "cómo sacar el id de un asset" y "cómo mapearlo a dominio" desde fuera, en vez de cablearlo
    ///   aquí. Eso mantiene esta clase ignorante de los mappers concretos: se los dan ya hechos.
    /// - "Mapear una vez y cachear": la ÚNICA optimización temprana sensata (Parte O). Traducimos
    ///   SO -> dominio al cargar, no en cada acceso.
    /// </summary>
    public sealed class ScriptableObjectCatalog<TData, T> : ICatalog<T>
        where TData : ScriptableObject
        where T : class
    {
        // Indexado por el id de TEXTO (Id<T>.Value). Se llena una sola vez en el constructor.
        private readonly Dictionary<string, T> _byId;

        public ScriptableObjectCatalog(
            IEnumerable<TData> assets,
            Func<TData, string> getId,
            Func<TData, T> toDomain)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (getId == null) throw new ArgumentNullException(nameof(getId));
            if (toDomain == null) throw new ArgumentNullException(nameof(toDomain));

            _byId = new Dictionary<string, T>();

            foreach (var asset in assets)
            {
                if (asset == null) continue; // hueco vacío en la lista de assets

                var id = getId(asset);
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException(
                        $"Un asset de tipo {typeof(TData).Name} no tiene id asignado.");

                if (_byId.ContainsKey(id))
                    throw new InvalidOperationException(
                        $"Id duplicado '{id}' en el catálogo de {typeof(T).Name}.");

                // Mapeo SO -> dominio AQUÍ, una vez. A partir de ahora el catálogo entrega objetos
                // de dominio ya listos; nadie vuelve a tocar el ScriptableObject.
                _byId[id] = toDomain(asset);
            }
        }

        public bool TryGet(Id<T> id, out T item) => _byId.TryGetValue(id.Value, out item);

        public T Get(Id<T> id)
        {
            if (_byId.TryGetValue(id.Value, out var item))
                return item;
            throw new KeyNotFoundException($"No existe {typeof(T).Name} con id '{id.Value}'.");
        }

        public bool Contains(Id<T> id) => _byId.ContainsKey(id.Value);

        // Dictionary.Values ya es una colección de solo lectura; encaja con el contrato de ICatalog.
        public IReadOnlyCollection<T> All => _byId.Values;
    }
}
