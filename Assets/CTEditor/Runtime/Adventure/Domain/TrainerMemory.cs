using System;
using System.Collections.Generic;

namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// Lo que un entrenador sabe de UNO de tus Pokémon: qué movimientos le ha visto usar y cuánto se desvían sus
    /// estadísticas de lo «normal» (IVs/EVs ESTIMADOS a partir del daño que hizo y recibió).
    ///
    /// Las estimaciones se guardan como un FACTOR sobre la estadística que supone por defecto (IVs 20, EVs 85,
    /// naturaleza neutra): 1 = «normal», 1,2 = «está más entrenado de lo que pensaba». Así sirven aunque tu Pokémon
    /// suba de nivel antes de la revancha.
    /// </summary>
    public sealed class MonsterIntel
    {
        public string SpeciesId { get; set; } = "";
        public HashSet<string> Moves { get; } = new HashSet<string>();
        private readonly Dictionary<string, (double sum, int count)> _factors = new Dictionary<string, (double, int)>();

        /// <summary>Factor estimado de una estadística (1 = lo normal). Sin datos, 1.</summary>
        public double FactorOf(string statId) => _factors.TryGetValue(statId, out var f) && f.count > 0 ? f.sum / f.count : 1.0;

        /// <summary>¿Tiene ya alguna estimación de esa estadística?</summary>
        public bool HasEstimate(string statId) => _factors.TryGetValue(statId, out var f) && f.count > 0;

        /// <summary>Añade una observación (se promedian: cuantas más, mejor estimación). Se ignoran las absurdas.</summary>
        public void Observe(string statId, double factor)
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0.4 || factor > 2.5) return;
            var f = _factors.TryGetValue(statId, out var v) ? v : (0.0, 0);
            // Solo las últimas 8 cuentan igual (si entrena entre combates, la estimación se adapta).
            if (f.Item2 >= 8) f = (f.Item1 / f.Item2 * 7, 7);
            _factors[statId] = (f.Item1 + factor, f.Item2 + 1);
        }

        /// <summary>Estadísticas con estimación (para enseñarlas en el editor o depurar).</summary>
        public IEnumerable<(string stat, double factor)> Estimates()
        {
            foreach (var kv in _factors) if (kv.Value.count > 0) yield return (kv.Key, kv.Value.sum / kv.Value.count);
        }
    }

    /// <summary>
    /// MEMORIA de un entrenador sobre el equipo del jugador (nivel de IA con conocimiento «Memoria»): se guarda en la
    /// PARTIDA, así en la revancha recuerda tus movimientos y tus estadísticas estimadas. Clave = id de tu Pokémon.
    /// </summary>
    public sealed class TrainerMemory
    {
        private readonly Dictionary<string, MonsterIntel> _intel = new Dictionary<string, MonsterIntel>();

        public int BattlesFought { get; set; }

        /// <summary>Lo que sabe de ese Pokémon (lo crea vacío si aún no lo conocía).</summary>
        public MonsterIntel About(string monsterId, string speciesId = null)
        {
            if (string.IsNullOrEmpty(monsterId)) throw new ArgumentException("Falta el id del Pokémon.", nameof(monsterId));
            if (!_intel.TryGetValue(monsterId, out var i)) _intel[monsterId] = i = new MonsterIntel();
            if (!string.IsNullOrEmpty(speciesId)) i.SpeciesId = speciesId;   // si evolucionó, se actualiza
            return i;
        }

        public bool Knows(string monsterId) => _intel.ContainsKey(monsterId);

        public IReadOnlyDictionary<string, MonsterIntel> All => _intel;
    }
}
