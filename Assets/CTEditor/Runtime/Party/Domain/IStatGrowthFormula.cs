using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// La fórmula de crecimiento de stats, como STRATEGY (otro seam de "inventar", Parte D Nivel 2):
    /// dado el valor BASE de una estadística (el de la Species) y el nivel, calcula el valor
    /// EFECTIVO del individuo. La implementación clásica de Pokémon es una de tantas; un creador
    /// puede escribir otra y elegirla desde el Ruleset.
    ///
    /// Aquí está SOLO la interfaz (el contrato), en el dominio puro.
    ///
    /// Nota de diseño: la diferencia "HP usa una fórmula distinta al resto" se resuelve DENTRO de la
    /// implementación (mira si stat == StatId.Hp), no aquí. Así "HP es especial" es lógica de la
    /// fórmula, no un caso cableado en el modelo.
    ///
    /// Lote 3: añadimos la versión COMPLETA con la genética del individuo (IV, EV, naturaleza). La
    /// versión simple se mantiene para no romper a quien ya la usa, y debe equivaler a la completa
    /// con IV = 0, EV = 0 y naturaleza neutra (100%).
    /// </summary>
    public interface IStatGrowthFormula
    {
        /// <summary>Valor efectivo de un stat dado su base y el nivel (sin genética).</summary>
        int Compute(StatId stat, int baseValue, int level);

        /// <summary>
        /// Valor efectivo COMPLETO. 'naturePercent' es un entero: 100 = neutra, 110 = +10%, 90 = -10%
        /// (entero para calcular como los juegos, sin errores de coma flotante).
        /// </summary>
        int Compute(StatId stat, int baseValue, int level, int iv, int ev, int naturePercent);
    }
}
