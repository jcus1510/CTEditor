using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.World.Domain
{
    /// <summary>Lo que salió en una simulación: por especie, cuántas veces y entre qué niveles.</summary>
    public sealed class SimulationResult
    {
        public int Steps;
        public int Encounters;
        public int Doubles;
        public readonly Dictionary<string, (int count, int minLevel, int maxLevel)> Species = new Dictionary<string, (int, int, int)>();

        /// <summary>Pasos de media entre encuentros (-1 si no salió nada).</summary>
        public double StepsPerEncounter => Encounters == 0 ? -1 : (double)Steps / Encounters;

        /// <summary>Por especie, de la que más sale a la que menos.</summary>
        public IEnumerable<(string species, int count, double percent, int minLevel, int maxLevel)> Rows()
        {
            int total = Species.Values.Sum(v => v.count);
            return Species.OrderByDescending(kv => kv.Value.count).ThenBy(kv => kv.Key)
                .Select(kv => (kv.Key, kv.Value.count, total == 0 ? 0 : kv.Value.count * 100.0 / total, kv.Value.minLevel, kv.Value.maxLevel));
        }
    }

    /// <summary>
    /// SIMULADOR DE ENCUENTROS: «anda» N pasos con una tabla (su probabilidad por paso, sus pesos, la hora, los
    /// interruptores) y cuenta lo que sale. Sirve para comprobar si una ruta está equilibrada sin jugarla. Con semilla:
    /// el mismo resultado cada vez.
    /// </summary>
    public static class EncounterSimulator
    {
        /// <param name="time">Hora; null = todas (sin mirar la hora).</param>
        public static SimulationResult Run(EncounterTable table, int ratePercent, TimeOfDay? time, Func<string, bool> flags, int steps, int seed = 1)
        {
            var r = new Random(seed);
            var result = new SimulationResult { Steps = Math.Max(0, steps) };
            var pool = table.Slots.Where(s => s.Weight > 0 && (time == null ? (string.IsNullOrEmpty(s.RequiredFlag) || (flags?.Invoke(s.RequiredFlag) ?? false))
                                                                         : s.AvailableAt(time.Value, flags))).ToList();
            int total = pool.Sum(s => s.Weight);
            if (total <= 0 || ratePercent <= 0) return result;
            EncounterSlot Pick()
            {
                int roll = r.Next(total);
                foreach (var s in pool) { if (roll < s.Weight) return s; roll -= s.Weight; }
                return pool[pool.Count - 1];
            }
            void Count(EncounterSlot s)
            {
                int level = r.Next(s.MinLevel, s.MaxLevel + 1);
                result.Species.TryGetValue(s.SpeciesId, out var v);
                result.Species[s.SpeciesId] = v.count == 0 ? (1, level, level) : (v.count + 1, Math.Min(v.minLevel, level), Math.Max(v.maxLevel, level));
            }
            for (int i = 0; i < result.Steps; i++)
            {
                if (r.Next(100) >= ratePercent) continue;
                result.Encounters++;
                Count(Pick());
                if (table.DoublePercent > 0 && r.Next(100) < table.DoublePercent) { result.Doubles++; Count(Pick()); }
            }
            return result;
        }
    }

    /// <summary>Una plantilla de zona: métodos con especies típicas y pesos clásicos, para empezar rápido.</summary>
    public sealed class EncounterTemplate
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<(string method, EncounterSlot[] slots)> Tables { get; }

        public EncounterTemplate(string id, string name, string description, params (string method, EncounterSlot[] slots)[] tables)
        {
            Id = id; Name = name; Description = description; Tables = tables;
        }

        private static EncounterSlot S(string id, int min, int max, int weight) => new EncounterSlot(id, min, max, weight);

        /// <summary>Las de fábrica (especies de la 1.ª gen.: se quitan solas las que no estén en los datos del proyecto).</summary>
        public static readonly IReadOnlyList<EncounterTemplate> BuiltIn = new[]
        {
            new EncounterTemplate("vacia", "Vacía", "Sin métodos: la rellenas tú."),
            new EncounterTemplate("ruta_temprana", "Ruta temprana", "Hierba con lo típico del principio (niveles 2-5).",
                ("hierba", new[] { S("pidgey", 2, 4, 20), S("rattata", 2, 4, 20), S("caterpie", 3, 4, 10), S("weedle", 3, 4, 10), S("spearow", 3, 5, 10),
                    S("nidoran_f", 3, 5, 10), S("nidoran_m", 3, 5, 5), S("pikachu", 4, 5, 5) })),
            new EncounterTemplate("bosque", "Bosque", "Hierba con bichos y algo de planta (niveles 3-7).",
                ("hierba", new[] { S("caterpie", 3, 5, 20), S("weedle", 3, 5, 20), S("metapod", 4, 6, 10), S("kakuna", 4, 6, 10), S("oddish", 4, 6, 10),
                    S("bellsprout", 4, 6, 10), S("pikachu", 5, 7, 5), S("paras", 5, 7, 5) })),
            new EncounterTemplate("cueva", "Cueva", "Cueva (cualquier paso) y golpe roca (niveles 8-14).",
                ("cueva", new[] { S("zubat", 8, 12, 40), S("geodude", 8, 12, 30), S("paras", 9, 12, 15), S("clefairy", 10, 12, 10), S("onix", 12, 14, 5) }),
                ("golpe_roca", new[] { S("geodude", 10, 14, 90), S("onix", 12, 14, 10) })),
            new EncounterTemplate("agua", "Agua", "Surf y cañas vieja y buena (niveles 5-25).",
                ("surf", new[] { S("tentacool", 15, 25, 60), S("goldeen", 15, 25, 30), S("psyduck", 20, 25, 5), S("slowpoke", 20, 25, 4), S("seel", 20, 25, 1) }),
                ("cana_vieja", new[] { S("magikarp", 5, 10, 70), S("goldeen", 5, 10, 30) }),
                ("cana_buena", new[] { S("poliwag", 10, 15, 70), S("krabby", 10, 15, 30) })),
        };

        /// <summary>
        /// Pone sus tablas en una zona (las que ya tenga ese método se sustituyen). Solo los métodos que existen y las
        /// especies que <paramref name="speciesExists"/> acepta (null = todas).
        /// </summary>
        public void ApplyTo(EncounterArea area, EncounterMethodCatalog methods, Func<string, bool> speciesExists)
        {
            foreach (var (method, slots) in Tables)
            {
                if (methods?.Find(method) == null) continue;
                area.Tables.RemoveAll(t => t.MethodId == method);
                var table = area.GetOrAddTable(method);
                foreach (var s in slots)
                    if (speciesExists == null || speciesExists(s.SpeciesId)) table.Slots.Add(s.Clone());
            }
        }
    }

    /// <summary>Comprobaciones de las zonas pintadas (opcionales: el autor decide si las quiere).</summary>
    public static class EncounterChecks
    {
        /// <summary>
        /// Casillas pintadas de una zona donde NINGÚN método «al pisar terreno» de la zona funcionaría (p. ej. hierba
        /// pintada sobre un camino sin etiqueta de hierba). Si la zona no tiene métodos de terreno, no hay ninguna.
        /// </summary>
        public static IReadOnlyList<(int x, int y)> CellsOffTerrain(MapDefinition map, MapTilesets sets, EncounterArea area, EncounterMethodCatalog methods)
        {
            if (area.WholeMap) return new (int, int)[0];
            var terrainMethods = area.Tables.Select(t => methods?.Find(t.MethodId)).Where(m => m != null && m.Trigger == EncounterTrigger.StepOnTerrain && m.TerrainTags.Count > 0).ToList();
            if (terrainMethods.Count == 0) return new (int, int)[0];
            return area.Cells.Where(c => map.Contains(c.x, c.y))
                .Where(c => { int tag = Passability.TerrainAt(map, sets, c.x, c.y); return !terrainMethods.Any(m => m.TerrainTags.Contains(tag)); })
                .OrderBy(c => c.y).ThenBy(c => c.x).ToList();
        }
    }
}
