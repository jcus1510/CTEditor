namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// Resultado de TurnResolver.PreviewDamage: el daño de UN impacto de un movimiento en sus extremos
    /// (tirada mínima 85% / máxima 100%, con y sin crítico), más lo necesario para interpretarlo.
    /// Es solo lectura: calcularlo no cambia el combate.
    /// </summary>
    public readonly struct DamagePreview
    {
        public bool DealsDamage { get; }
        public float Effectiveness { get; }
        public bool Stab { get; }
        public bool ImmuneByAbility { get; }
        public int Min { get; }
        public int Max { get; }
        public int CritMin { get; }
        public int CritMax { get; }
        public int MinHits { get; }
        public int MaxHits { get; }
        /// <summary>Precisión en % (null = nunca falla).</summary>
        public float? AccuracyPercent { get; }
        public int TargetHp { get; }
        public int TargetMaxHp { get; }

        public DamagePreview(bool dealsDamage, float effectiveness, bool stab, bool immuneByAbility,
            int min, int max, int critMin, int critMax, int minHits, int maxHits,
            float? accuracyPercent, int targetHp, int targetMaxHp)
        {
            DealsDamage = dealsDamage;
            Effectiveness = effectiveness;
            Stab = stab;
            ImmuneByAbility = immuneByAbility;
            Min = min; Max = max; CritMin = critMin; CritMax = critMax;
            MinHits = minHits < 1 ? 1 : minHits;
            MaxHits = maxHits < MinHits ? MinHits : maxHits;
            AccuracyPercent = accuracyPercent;
            TargetHp = targetHp;
            TargetMaxHp = targetMaxHp < 1 ? 1 : targetMaxHp;
        }

        /// <summary>Porcentaje de los PS MÁXIMOS del objetivo que representa un daño.</summary>
        public float PercentOfMaxHp(int damage) => damage * 100f / TargetMaxHp;

        /// <summary>
        /// Cuántos USOS del movimiento hacen falta para debilitar al objetivo desde sus PS actuales:
        /// en el mejor caso (tirada máxima, todos los golpes) y en el peor (tirada mínima, menos golpes).
        /// Sin críticos. 0 = no le hace daño.
        /// </summary>
        public (int best, int worst) UsesToKo()
        {
            if (!DealsDamage || Max <= 0) return (0, 0);
            int bestPerUse = Max * MaxHits;
            int worstPerUse = (Min < 1 ? 1 : Min) * MinHits;
            return (Ceil(TargetHp, bestPerUse), Ceil(TargetHp, worstPerUse));
        }

        private static int Ceil(int a, int b) => (a + b - 1) / b;
    }
}
