using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>
    /// La fórmula de daño clásica de Pokémon, traducida. Es la implementación concreta del seam
    /// IDamageFormula, seleccionada por el FormulaId "classic" del Ruleset.
    ///
    /// NOTA DE UBICACIÓN (te la marco por tu control de versiones): el documento la ponía en
    /// Infrastructure. La dejo en Battle.Domain porque es MATEMÁTICA PURA — no toca Unity ni IO —,
    /// y así el test de combate (que solo referencia Battle.Domain) puede usarla sin arrastrar el
    /// assembly de Unity. Lo que SÍ es Infrastructure es el REGISTRO que mapea "classic" -> esta
    /// clase; la clase en sí es dominio puro. Pequeña desviación justificada (O).
    /// </summary>
    public sealed class ClassicDamageFormula : IDamageFormula
    {
        /// <summary>Tabla de críticos moderna (7ª gen.+), la que se usa si el Ruleset no dice otra.</summary>
        public static readonly System.Collections.Generic.IReadOnlyList<int> DefaultCritTable = new[] { 24, 8, 2, 1 };

        public DamageResult Compute(DamageContext c)
        {
            // Sin potencia (movimientos de estado) no hay daño directo.
            if (c.MovePower <= 0) return new DamageResult(0, false);
            // Inmune: la efectividad fue 0.
            if (c.TypeEffectiveness <= 0f) return new DamageResult(0, false);

            // Evitamos dividir por cero si por algún dato raro la defensa fuera 0.
            int defense = c.DefenseStat <= 0 ? 1 : c.DefenseStat;

            // --- NÚCLEO ---
            float core = (2f * c.AttackerLevel / 5f + 2f) * c.MovePower * c.AttackStat / defense;
            core = core / 50f + 2f;

            // --- MODIFICADORES ---
            float stab = c.StabMultiplier > 0f ? c.StabMultiplier : (c.Stab ? 1.5f : 1f);

            // Crítico: la probabilidad sube con el "crit stage" del movimiento. La TABLA la decide el
            // Ruleset (configurable). Moderna: etapa 0 -> 1/24, 1 -> 1/8, 2 -> 1/2, 3+ -> siempre.
            // Un 0 en la tabla = nunca crítico. Usa el azar INYECTADO.
            var denominators = c.CritDenominators ?? DefaultCritTable;
            bool isCrit = false;
            if (denominators.Count > 0)
            {
                int idx = c.CritStage < 0 ? 0 : (c.CritStage >= denominators.Count ? denominators.Count - 1 : c.CritStage);
                int n = denominators[idx];
                isCrit = n > 0 && c.Rng.Next(0, n) == 0;
            }
            float crit = isCrit ? c.CritMultiplier : 1f;

            // Variación aleatoria: entre 0.85 y 1.00, para que no todo golpe sea idéntico.
            float random = 0.85f + c.Rng.NextFloat() * 0.15f;

            float total = core * stab * c.TypeEffectiveness * crit * random;

            // Convertimos a entero. Un golpe que acierta hace al menos 1 (salvo inmunidad, ya filtrada).
            int damage = (int)total;
            if (damage < 1) damage = 1;
            return new DamageResult(damage, isCrit);
        }
    }
}
