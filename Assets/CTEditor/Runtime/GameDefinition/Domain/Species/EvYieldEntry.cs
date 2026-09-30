using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// Una entrada del RENDIMIENTO DE EVs de una especie: derrotar a un ejemplar otorga 'Amount'
    /// puntos de esfuerzo en la stat 'Stat'. Clásico: 1 a 3 puntos repartidos en 1 o 2 stats
    /// (p. ej. Geodude da 1 EV de Defensa; Machamp da 3 de Ataque). Value Object inmutable.
    /// </summary>
    public readonly struct EvYieldEntry
    {
        public StatId Stat { get; }
        public int Amount { get; }

        public EvYieldEntry(StatId stat, int amount)
        {
            Stat = stat;
            Amount = amount < 0 ? 0 : amount;
        }
    }
}
