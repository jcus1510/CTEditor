using System;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>La ACL para sets de competición: CompetitiveSetData (Unity) -> CompetitiveSet (dominio puro).</summary>
    public static class CompetitiveSetMapper
    {
        public static CompetitiveSet ToDomain(CompetitiveSetData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            if (string.IsNullOrWhiteSpace(d.SpeciesId)) throw new ArgumentException($"El set '{d.Id}' no tiene especie.");
            return new CompetitiveSet(d.Id, new Id<SpeciesDef>(d.SpeciesId.Trim()), d.Format, d.DisplayName, d.Score,
                CompetitiveSet.ParseOptions(d.Items), CompetitiveSet.ParseOptions(d.Abilities), CompetitiveSet.ParseOptions(d.Natures),
                Spread(d.Evs), Spread(d.Ivs), CompetitiveSet.ParseSlots(d.Moves));
        }

        // Un reparto mal escrito no rompe el juego: se ignora (el validador lo avisa).
        private static StatSpread Spread(string text) => StatSpread.TryParse(text, out var s, out _) ? s : StatSpread.Empty;
    }
}
