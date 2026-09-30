using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.GameDefinition.Domain.Encounters
{
    /// <summary>Una especie que puede aparecer en una zona: entre qué niveles y con qué frecuencia (peso).</summary>
    public readonly struct EncounterEntry
    {
        public Id<SpeciesDef> Species { get; }
        public int MinLevel { get; }
        public int MaxLevel { get; }
        /// <summary>Peso relativo: 30 frente a 10 = aparece 3 veces más.</summary>
        public int Weight { get; }

        public EncounterEntry(Id<SpeciesDef> species, int minLevel, int maxLevel, int weight = 10)
        {
            Species = species;
            MinLevel = Math.Max(1, Math.Min(minLevel, maxLevel));
            MaxLevel = Math.Max(MinLevel, Math.Max(minLevel, maxLevel));
            Weight = Math.Max(1, weight);
        }
    }

    /// <summary>
    /// Una ZONA de encuentros salvajes (hierba alta de la Ruta 1, una cueva...): la lista de lo que
    /// puede aparecer. El sorteo lo hace la capa de partida (con el azar inyectado).
    /// </summary>
    public sealed class EncounterZone
    {
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<EncounterEntry> Entries { get; }

        public EncounterZone(string id, string displayName, IReadOnlyList<EncounterEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("La zona necesita un id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Entries = entries == null ? Array.Empty<EncounterEntry>() : new List<EncounterEntry>(entries);
        }

        public int TotalWeight
        {
            get { int t = 0; foreach (var e in Entries) t += e.Weight; return t; }
        }

        /// <summary>Elige una entrada según un número 0..TotalWeight-1 (el azar lo pone quien llama).</summary>
        public EncounterEntry? Pick(int roll)
        {
            if (Entries.Count == 0) return null;
            int acc = 0;
            foreach (var e in Entries)
            {
                acc += e.Weight;
                if (roll < acc) return e;
            }
            return Entries[Entries.Count - 1];
        }
    }
}
