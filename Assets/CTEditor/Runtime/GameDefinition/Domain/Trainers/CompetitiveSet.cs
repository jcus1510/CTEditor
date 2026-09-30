using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Abilities;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>
    /// Un SET DE COMPETICIÓN (de los análisis de Smogon o tuyo): especie, formato (ou, uu, ubers...), objeto, habilidad y
    /// naturaleza con ALTERNATIVAS, EVs, IVs y 4 huecos de movimientos, cada uno con sus alternativas. La puntuación (0-100)
    /// dice cuánto se usa de verdad: la IA elige al azar dando más peso a los más usados.
    /// </summary>
    public sealed class CompetitiveSet
    {
        public string Id { get; }
        public Id<SpeciesDef> Species { get; }
        public string Format { get; }
        public string Name { get; }
        public int Score { get; }
        public IReadOnlyList<string> Items { get; }
        public IReadOnlyList<string> Abilities { get; }
        public IReadOnlyList<string> Natures { get; }
        public StatSpread Evs { get; }
        public StatSpread Ivs { get; }
        /// <summary>Huecos de movimientos; en cada uno, las alternativas (la primera es la recomendada).</summary>
        public IReadOnlyList<IReadOnlyList<string>> MoveSlots { get; }

        public CompetitiveSet(string id, Id<SpeciesDef> species, string format, string name, int score,
            IEnumerable<string> items = null, IEnumerable<string> abilities = null, IEnumerable<string> natures = null,
            StatSpread evs = null, StatSpread ivs = null, IEnumerable<IEnumerable<string>> moveSlots = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Un set necesita id.", nameof(id));
            Id = id.Trim();
            Species = species;
            Format = (format ?? "").Trim().ToLowerInvariant();
            Name = string.IsNullOrWhiteSpace(name) ? Id : name.Trim();
            Score = Math.Max(0, Math.Min(100, score));
            Items = Clean(items);
            Abilities = Clean(abilities);
            Natures = Clean(natures);
            Evs = evs ?? StatSpread.Empty;
            Ivs = ivs ?? StatSpread.Empty;
            MoveSlots = (moveSlots ?? Enumerable.Empty<IEnumerable<string>>()).Select(Clean).Where(s => s.Count > 0).ToList();
        }

        private static IReadOnlyList<string> Clean(IEnumerable<string> list)
            => (list ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();

        // ---------------- Texto (Excel e inspector) ----------------

        /// <summary>«a, b, c» → [a, b, c] (alternativas).</summary>
        public static List<string> ParseOptions(string text)
            => (text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>«swords_dance / earthquake, stone_edge / ...» → huecos con sus alternativas.</summary>
        public static List<List<string>> ParseSlots(string text)
            => (text ?? "").Split('/').Select(ParseOptions).Where(s => s.Count > 0).ToList();

        public static string FormatOptions(IEnumerable<string> options) => string.Join(", ", options ?? Enumerable.Empty<string>());
        public static string FormatSlots(IEnumerable<IEnumerable<string>> slots) => string.Join(" / ", (slots ?? Enumerable.Empty<IEnumerable<string>>()).Select(FormatOptions));

        /// <summary>
        /// El miembro con este set aplicado: lo que el autor dejó vacío (movimientos, objeto, habilidad, naturaleza, EVs, IVs)
        /// se rellena con el set; lo escrito se respeta. 'rng' elige entre alternativas (null = siempre la primera).
        /// Solo usa lo que existe en el juego ('exists' dice si un id existe; null = todo vale).
        /// </summary>
        public TeamMemberSpec ApplyTo(TeamMemberSpec spec, IRng rng = null, Func<string, string, bool> exists = null)
        {
            bool Ok(string kind, string id) => exists == null || exists(kind, id);
            string Pick(IReadOnlyList<string> options, string kind)
            {
                var valid = options.Where(o => Ok(kind, o)).ToList();
                if (valid.Count == 0) return null;
                return rng == null ? valid[0] : valid[rng.Next(0, valid.Count)];
            }

            var moves = spec.Moves.ToList();
            if (moves.Count == 0)
                foreach (var slot in MoveSlots)
                {
                    // Una alternativa que no esté repetida (Hidden Power en dos huecos, por ejemplo).
                    var free = slot.Where(m => Ok("move", m) && !moves.Contains(new Id<Move>(m))).ToList();
                    if (free.Count == 0) continue;
                    moves.Add(new Id<Move>(rng == null ? free[0] : free[rng.Next(0, free.Count)]));
                }
            string item = !string.IsNullOrWhiteSpace(spec.HeldItem) ? spec.HeldItem : Pick(Items, "item") ?? "";
            AbilityId? ability = spec.Ability;
            if (!ability.HasValue) { var a = Pick(Abilities, "ability"); if (a != null) ability = new AbilityId(a); }
            Id<Nature>? nature = spec.Nature;
            if (!nature.HasValue) { var n = Pick(Natures, "nature"); if (n != null) nature = new Id<Nature>(n); }
            return new TeamMemberSpec(spec.Species, spec.Level, moves, item, nature, spec.FixedIv ?? 31, spec.Nickname, spec.Gender,
                spec.Ivs.IsEmpty ? Ivs : spec.Ivs, spec.Evs.IsEmpty ? Evs : spec.Evs, ability);
        }

        /// <summary>
        /// Elige un set de la especie entre 'sets' (solo de los formatos pedidos; vacío = cualquiera), al azar dando más peso a
        /// los de más puntuación. Null si no hay ninguno.
        /// </summary>
        public static CompetitiveSet Choose(IEnumerable<CompetitiveSet> sets, Id<SpeciesDef> species, IReadOnlyCollection<string> formats, IRng rng)
        {
            var candidates = (sets ?? Enumerable.Empty<CompetitiveSet>()).Where(s => s.Species == species
                && (formats == null || formats.Count == 0 || formats.Any(f => string.Equals(f, s.Format, StringComparison.OrdinalIgnoreCase)))).ToList();
            if (candidates.Count == 0) return null;
            if (rng == null) return candidates.OrderByDescending(s => s.Score).First();
            int total = candidates.Sum(Weight);
            int roll = rng.Next(0, total);
            foreach (var s in candidates)
            {
                roll -= Weight(s);
                if (roll < 0) return s;
            }
            return candidates[candidates.Count - 1];
        }

        // Peso: su puntuación más 5 (un set sin estadísticas también puede salir, pero poco).
        private static int Weight(CompetitiveSet s) => s.Score + 5;
    }
}
