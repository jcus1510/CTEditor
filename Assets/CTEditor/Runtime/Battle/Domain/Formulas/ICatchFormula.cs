using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>
    /// La fórmula de captura como STRATEGY (igual que el daño): interfaz en el dominio, implementación
    /// inyectable. Un creador puede ofrecer otra regla de captura sin tocar el motor.
    /// </summary>
    public interface ICatchFormula
    {
        /// <summary>
        /// ¿Se captura al objetivo? catchBonus &gt; 1 representa mejores bolas. Usa el azar inyectado
        /// (determinista bajo semilla).
        /// </summary>
        bool TryCatch(Combatant target, float catchBonus, IRng rng);
    }
}
