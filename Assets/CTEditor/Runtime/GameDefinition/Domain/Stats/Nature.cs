using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Stats
{
    /// <summary>
    /// Una NATURALEZA: la "personalidad" de un individuo que inclina sus stats. En los clásicos hay
    /// 25 (cada una sube una stat un 10% y baja otra un 10%; 5 son neutras). Aquí es CONTENIDO: el
    /// autor define las suyas, elige qué stat sube y cuál baja, y hasta el porcentaje (no está
    /// clavado al 10%). Puede usar stats inventadas, porque se nombran por StatId.
    ///
    /// Es una DEFINICIÓN inmutable (como Species o GrowthCurve), identificada por id. El individuo
    /// guarda la suya: la naturaleza es POR INDIVIDUO, no por especie (dos Pikachu pueden diferir).
    /// </summary>
    public sealed class Nature
    {
        public Id<Nature> Id { get; }
        public string DisplayName { get; }

        /// <summary>Stat favorecida (null = ninguna).</summary>
        public StatId? BoostedStat { get; }

        /// <summary>Stat perjudicada (null = ninguna).</summary>
        public StatId? HinderedStat { get; }

        /// <summary>Porcentaje de inclinación (clásico: 10 → ×1.1 y ×0.9).</summary>
        public int BoostPercent { get; }

        public Nature(Id<Nature> id, string displayName, StatId? boostedStat, StatId? hinderedStat, int boostPercent = 10)
        {
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
            BoostedStat = boostedStat;
            HinderedStat = hinderedStat;
            BoostPercent = boostPercent < 0 ? 0 : boostPercent;
        }

        /// <summary>¿Es neutra? (no sube ni baja nada, o sube y baja la misma stat).</summary>
        public bool IsNeutral =>
            (!BoostedStat.HasValue && !HinderedStat.HasValue) ||
            (BoostedStat.HasValue && HinderedStat.HasValue && BoostedStat.Value == HinderedStat.Value);

        /// <summary>
        /// Multiplicador que esta naturaleza aplica a una stat: ×(1+p) si la favorece, ×(1-p) si la
        /// perjudica, ×1 si no. Si favorece y perjudica la MISMA stat, se anulan (así funcionan las 5
        /// naturalezas neutras clásicas, como Serena: +Def/-Def).
        /// </summary>
        public double Multiplier(StatId stat) => PercentFor(stat) / 100.0;

        /// <summary>
        /// El mismo multiplicador como PORCENTAJE ENTERO (100 = neutra, 110 = +10%, 90 = -10%). La
        /// fórmula usa este y no el double: multiplicar en coma flotante puede dar 6.9999 en vez de 7
        /// y truncar mal. Los juegos calculan en enteros (stat × 110 / 100); nosotros también.
        /// </summary>
        public int PercentFor(StatId stat)
        {
            bool boosted = BoostedStat.HasValue && BoostedStat.Value == stat;
            bool hindered = HinderedStat.HasValue && HinderedStat.Value == stat;
            if (boosted == hindered) return 100; // ninguna de las dos, o ambas (se anulan)

            int pct = boosted ? 100 + BoostPercent : 100 - BoostPercent;
            return pct < 0 ? 0 : pct;
        }
    }
}
