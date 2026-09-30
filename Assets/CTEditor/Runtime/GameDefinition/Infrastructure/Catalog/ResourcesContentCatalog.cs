using System;
using System.Collections.Generic;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;

namespace CTEditor.GameDefinition.Infrastructure.Catalog
{
    /// <summary>
    /// Catálogo respaldado por una CARPETA de Resources (la implementación "de disco" que ICatalog
    /// anticipaba, Parte O). Carga PEREZOSA por categoría: no toca disco hasta que se le pide la
    /// primera ficha; entonces carga esa carpeta una sola vez, mapea SO -> dominio y cachea.
    ///
    /// Así el autor NO arrastra arrays de "todos los moves" a cada componente: deja los assets en
    /// Assets/GameContent/Resources/&lt;carpeta&gt; y el catálogo los encuentra por id. Y si una categoría
    /// no se usa en una escena, ni se carga.
    ///
    /// Indexa por el campo Id del asset (no por el nombre de archivo), así el autor puede nombrar los
    /// archivos como quiera. Para verdadera carga por-asset (un solo Move sin leer la carpeta entera)
    /// el camino futuro es Addressables; aquí, perezoso-por-categoría es suficiente y simple.
    /// </summary>
    public sealed class ResourcesContentCatalog<TData, T> : ICatalog<T>
        where TData : ScriptableObject
        where T : class
    {
        private readonly string _resourcesFolder;
        private readonly Func<TData, string> _getId;
        private readonly Func<TData, T> _toDomain;
        private Dictionary<string, T> _byId; // null hasta la primera carga

        public ResourcesContentCatalog(string resourcesFolder, Func<TData, string> getId, Func<TData, T> toDomain)
        {
            _resourcesFolder = resourcesFolder ?? "";
            _getId = getId ?? throw new ArgumentNullException(nameof(getId));
            _toDomain = toDomain ?? throw new ArgumentNullException(nameof(toDomain));
        }

        private void EnsureLoaded()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, T>();

            var assets = Resources.LoadAll<TData>(_resourcesFolder);
            foreach (var a in assets)
            {
                if (a == null) continue;
                var id = _getId(a);
                if (string.IsNullOrWhiteSpace(id) || _byId.ContainsKey(id)) continue;
                _byId[id] = _toDomain(a);
            }
        }

        public bool TryGet(Id<T> id, out T item)
        {
            EnsureLoaded();
            return _byId.TryGetValue(id.Value, out item);
        }

        public T Get(Id<T> id)
        {
            EnsureLoaded();
            if (_byId.TryGetValue(id.Value, out var item)) return item;
            throw new KeyNotFoundException($"No existe {typeof(T).Name} con id '{id.Value}' en Resources/{_resourcesFolder}.");
        }

        public bool Contains(Id<T> id)
        {
            EnsureLoaded();
            return _byId.ContainsKey(id.Value);
        }

        public IReadOnlyCollection<T> All
        {
            get { EnsureLoaded(); return _byId.Values; }
        }
    }
}
