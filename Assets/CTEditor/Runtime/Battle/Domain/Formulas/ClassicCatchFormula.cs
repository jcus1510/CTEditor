using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>
    /// Fórmula de captura clásica (simplificada y abierta). La probabilidad sube cuando el objetivo
    /// tiene pocos PS y cuando está bajo un estado alterado; las mejores bolas (catchBonus alto) la
    /// suben aún más. Un catchBonus enorme (estilo "Master Ball") garantiza la captura.
    /// </summary>
    public sealed class ClassicCatchFormula : ICatchFormula
    {
        public bool TryCatch(Combatant target, float catchBonus, IRng rng)
        {
            if (target == null) return false;
            int max = target.MaxHp <= 0 ? 1 : target.MaxHp;

            // hpFactor: 0 con PS llenos, cerca de 1 con PS casi a cero.
            float hpFactor = 1f - (float)target.CurrentHp / max;
            float statusBonus = target.Status.HasValue ? 0.2f : 0f;

            // Base 0.10 (difícil a PS llenos) + hasta 0.70 por PS bajos + 0.20 por estado, escalado por la bola.
            float chance = (0.10f + hpFactor * 0.70f + statusBonus) * catchBonus;
            if (chance < 0f) chance = 0f;
            if (chance > 1f) chance = 1f;

            return rng.NextFloat() < chance;
        }
    }
}
