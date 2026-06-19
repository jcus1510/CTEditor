using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// La fórmula de crecimiento de stats, como STRATEGY (otro seam de "inventar", Parte D Nivel 2):
    /// dado el valor BASE de una estadística (el de la Species) y el nivel, calcula el valor
    /// EFECTIVO del individuo. La implementación clásica de Pokémon es una de tantas; un creador
    /// puede escribir otra y elegirla desde el Ruleset.
    ///
    /// Aquí está SOLO la interfaz (el contrato), en el dominio puro. La implementación concreta
    /// (ClassicStatGrowthFormula) vivirá en la capa de Infrastructure de Party, y se inyectará.
    ///
    /// Nota de diseño: la diferencia "HP usa una fórmula distinta al resto" se resuelve DENTRO de la
    /// implementación (mira si stat == StatId.Hp), no aquí. Así "HP es especial" es lógica de la
    /// fórmula, no un caso cableado en el modelo; coherente con que los clásicos se diferencien por
    /// la fórmula, no por el almacenamiento.
    /// </summary>
    public interface IStatGrowthFormula
    {
        /// <summary>Valor efectivo de un stat dado su base y el nivel.</summary>
        int Compute(StatId stat, int baseValue, int level);
    }
}
