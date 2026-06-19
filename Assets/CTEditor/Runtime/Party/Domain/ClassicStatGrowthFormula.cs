using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// La fórmula de crecimiento clásica (versión sin IV/EV todavía, que son toggles diferidos del
    /// Ruleset). Implementa el seam IStatGrowthFormula.
    ///
    /// Aquí se ve materializado lo que dijimos al definir la interfaz: la diferencia "HP usa otra
    /// fórmula" vive DENTRO de la implementación (mira si stat == StatId.Hp), no cableada en el
    /// modelo. La división entera hace de "floor" automáticamente.
    ///
    /// Igual que ClassicDamageFormula, la dejo en .Domain (no Infrastructure) por ser matemática
    /// pura: así el test la usa sin arrastrar Unity. Anótalo para tu control de versiones.
    /// </summary>
    public sealed class ClassicStatGrowthFormula : IStatGrowthFormula
    {
        public int Compute(StatId stat, int baseValue, int level)
        {
            int common = (2 * baseValue * level) / 100;

            if (stat == StatId.Hp)
                return common + level + 10; // el HP suma nivel y una base extra
            return common + 5;              // el resto suma una base fija
        }
    }
}
