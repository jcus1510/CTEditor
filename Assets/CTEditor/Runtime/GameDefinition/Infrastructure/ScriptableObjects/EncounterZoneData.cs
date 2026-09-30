using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Una ZONA de encuentros salvajes (la hierba alta de una ruta, una cueva...): qué especies salen,
    /// a qué niveles y con qué frecuencia.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Zona salvaje", fileName = "NuevaZona")]
    public sealed class EncounterZoneData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private EncounterEntryData[] entries;

        public string Id => id;
        public string DisplayName => displayName;
        public EncounterEntryData[] Entries => entries;

        // Guarda el id de cada especie junto a su referencia (ver TeamMemberData).
        private void OnValidate()
        {
            if (entries == null) return;
            foreach (var e in entries)
                if (e != null && e.species != null && !string.IsNullOrWhiteSpace(e.species.Id)) e.speciesId = e.species.Id;
        }
    }

    /// <summary>Una especie de la zona: niveles mínimo y máximo, y frecuencia (peso).</summary>
    [System.Serializable]
    public sealed class EncounterEntryData
    {
        public SpeciesData species;
        [Min(1)] public int minLevel = 2;
        [Min(1)] public int maxLevel = 4;
        [Tooltip("Frecuencia relativa: 30 frente a 10 = sale 3 veces más.")]
        [Min(1)] public int weight = 10;
        [HideInInspector] public string speciesId = "";

        /// <summary>Id de la especie: el de la referencia si existe; si se rompió, el guardado.</summary>
        public string SpeciesKey => species != null && !string.IsNullOrWhiteSpace(species.Id) ? species.Id : (speciesId ?? "").Trim();
    }
}
