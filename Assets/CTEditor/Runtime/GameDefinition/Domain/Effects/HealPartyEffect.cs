namespace CTEditor.GameDefinition.Domain.Effects
{
    /// <summary>
    /// "Cura al equipo". Fíjate que es DATO PURO: solo dice cuánto curar, no sabe nada de cómo se
    /// cura ni quién es el equipo. Esa ignorancia es deliberada (E.2): el efecto describe la
    /// intención; el dispatcher la convierte en un comando que el dueño (Party) valida y aplica.
    /// </summary>
    public sealed class HealPartyEffect : IEffect
    {
        /// <summary>Cuántos PS restaurar a cada miembro.</summary>
        public int Amount { get; }

        public HealPartyEffect(int amount) => Amount = amount;
    }
}
