using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Types;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>Lo que se pide para PREPARAR UN RETO (un entrenador generado).</summary>
    public sealed class ChallengeRequest
    {
        public string Id = "reto";
        public string Name = "";
        public string TrainerClass = "";
        /// <summary>Nivel de IA 1-7.</summary>
        public int AiLevel = 2;
        public int TeamSize = 3;
        public int MinLevel = 10, MaxLevel = 14;
        /// <summary>Solo especies de este tipo (null = cualquiera). Útil para líderes temáticos.</summary>
        public Id<ElementType>? Type;
        /// <summary>Solo estas especies (null = todas). Por ejemplo, las de una zona salvaje.</summary>
        public ICollection<Id<SpeciesDef>> Pool;
        public bool AllowLegendary;
        public int BaseMoney = 0;   // 0 = según su nivel de IA
    }

    /// <summary>
    /// PREPARA UN RETO: genera un entrenador con un equipo coherente para su nivel. Sirve en el editor
    /// («🎲 Preparar un reto») y en el juego (entrenadores al azar por el mapa).
    ///
    /// Qué cuida:
    ///   • Sin repetir especie (si hay de dónde elegir) y sin legendarios salvo que se pidan.
    ///   • ETAPA según el nivel: un Bulbasaur de nivel 20 sale como Ivysaur; un Venusaur de nivel 10 sale como Bulbasaur.
    ///   • El último miembro es el de más nivel (el «as»), como en los juegos.
    ///   • Sin movimientos escritos: los elige el planificador según su nivel de IA (MT, tutor, huevo, sinergias).
    /// </summary>
    public static class ChallengeBuilder
    {
        public static TrainerDefinition Build(IEnumerable<SpeciesDef> allSpecies, ChallengeRequest req, IRng rng)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var all = (allSpecies ?? Enumerable.Empty<SpeciesDef>()).Where(s => s != null).ToList();
            var byId = all.ToDictionary(s => s.Id, s => s);
            var parent = new Dictionary<Id<SpeciesDef>, (SpeciesDef from, Evolution evo)>();
            foreach (var s in all)
                foreach (var e in s.Evolutions)
                    if (!parent.ContainsKey(e.Target)) parent[e.Target] = (s, e);

            int size = Math.Max(1, Math.Min(6, req.TeamSize));
            int lo = Math.Max(1, Math.Min(req.MinLevel, req.MaxLevel)), hi = Math.Max(lo, Math.Max(req.MinLevel, req.MaxLevel));

            var candidates = all.Where(s => (req.AllowLegendary || !s.Dex.Legendary)
                                            && (!req.Type.HasValue || s.Types.Contains(req.Type.Value))
                                            && (req.Pool == null || req.Pool.Contains(s.Id))).ToList();
            if (candidates.Count == 0) candidates = all.Where(s => req.AllowLegendary || !s.Dex.Legendary).ToList();
            if (candidates.Count == 0) candidates = all;

            var levels = new List<int>();
            for (int i = 0; i < size; i++) levels.Add(rng.Next(lo, hi + 1));
            levels.Sort();
            levels[levels.Count - 1] = hi;   // el as, al máximo

            var team = new List<TeamMemberSpec>();
            var used = new HashSet<Id<SpeciesDef>>();
            for (int i = 0; i < size && candidates.Count > 0; i++)
            {
                SpeciesDef pick = null;
                for (int tries = 0; tries < 12; tries++)
                {
                    var c = candidates[rng.Next(0, candidates.Count)];
                    var staged = StageFor(c, levels[i], byId, parent);
                    if (!used.Contains(staged.Id) || tries == 11) { pick = staged; break; }
                }
                if (pick == null) continue;
                used.Add(pick.Id);
                team.Add(new TeamMemberSpec(pick.Id, levels[i]));
            }

            string cls = string.IsNullOrWhiteSpace(req.TrainerClass) ? ClassFor(req.AiLevel) : req.TrainerClass;
            string name = string.IsNullOrWhiteSpace(req.Name) ? "Reto" : req.Name;
            int money = req.BaseMoney > 0 ? req.BaseMoney : new[] { 16, 24, 36, 60, 100, 120, 150 }[Math.Max(1, Math.Min(AiProfile.MaxLevel, req.AiLevel)) - 1];
            return new TrainerDefinition(req.Id, name, team, cls, AiProfile.Classic(req.AiLevel).LegacyAi, money,
                "¡Te reto a un combate!", "¡Me has ganado!", "¡Esta vez gano yo!", new TrainerAiSettings(), req.AiLevel);
        }

        /// <summary>La ETAPA adecuada de una especie a un nivel (sube por evoluciones de nivel, o baja si aún no tocaba).</summary>
        public static SpeciesDef StageFor(SpeciesDef s, int level, IDictionary<Id<SpeciesDef>, SpeciesDef> byId,
            IDictionary<Id<SpeciesDef>, (SpeciesDef from, Evolution evo)> parent)
        {
            // Bajar: si se obtiene por NIVEL más alto que el pedido, se usa la etapa anterior.
            for (int guard = 0; guard < 4 && parent.TryGetValue(s.Id, out var p); guard++)
            {
                bool tooEarly = p.evo.Method == EvolutionMethod.Level && p.evo.RequiredLevel > level;
                bool nonLevelTooEarly = p.evo.Method != EvolutionMethod.Level && level < 20;   // piedras, intercambio...: no antes del 20
                if (!tooEarly && !nonLevelTooEarly) break;
                s = p.from;
            }
            // Subir: si ya tendría que haber evolucionado por nivel, se usa la evolución.
            for (int guard = 0; guard < 4; guard++)
            {
                var next = s.Evolutions.FirstOrDefault(e => e.Method == EvolutionMethod.Level && e.RequiredLevel <= level && byId.ContainsKey(e.Target));
                if (next == null) break;
                s = byId[next.Target];
            }
            return s;
        }

        /// <summary>Clase por defecto según el nivel de IA.</summary>
        public static string ClassFor(int aiLevel)
        {
            switch (Math.Max(1, Math.Min(AiProfile.MaxLevel, aiLevel)))
            {
                case 1: return "Joven";
                case 2: return "Entrenador";
                case 3: return "Entrenador guay";
                case 4: return "Experto";
                case 5: return "As del combate";
                case 6: return "Maestro";
                default: return "Leyenda";
            }
        }
    }
}
