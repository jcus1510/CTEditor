using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.GameDefinition.Domain.Status
{
    /// <summary>
    /// Un modificador PASIVO de estadística que aplica un estado mientras dura: multiplica una stat
    /// por un factor. Quemado = (Attack, 0.5); Parálisis = (Speed, 0.5). El combate calcula la stat
    /// EFECTIVA aplicando estos sobre la base; nunca toca la base (E.3: nunca mutes el dato permanente).
    /// </summary>
    public readonly struct StatPassiveModifier
    {
        public StatId Stat { get; }
        public float Multiplier { get; }

        public StatPassiveModifier(StatId stat, float multiplier)
        {
            Stat = stat;
            Multiplier = multiplier;
        }
    }
}
