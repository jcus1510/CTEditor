using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// La fórmula de crecimiento CLÁSICA (Gen III en adelante), ahora con IVs, EVs y naturaleza.
    /// Implementa el seam IStatGrowthFormula.
    ///
    ///   común = (2 × base + IV + EV/4) × nivel / 100          (divisiones enteras = "floor")
    ///   PS    = común + nivel + 10                             (los PS no usan naturaleza)
    ///   resto = (común + 5) × naturaleza% / 100                (entero, como los juegos)
    ///
    /// Comprobación con el ejemplo de Bulbapedia (Garchomp Nv.78, naturaleza Firme +Atq):
    ///   Ataque: base 130, IV 12, EV 190 → (260+12+47)×78/100 = 248 → (248+5)×110/100 = 278 ✔
    ///   PS:     base 108, IV 24, EV 74  → (216+24+18)×78/100 = 201 → 201+78+10     = 289 ✔
    ///
    /// La dejo en .Domain (no Infrastructure) por ser matemática pura: así los tests la usan sin Unity.
    /// </summary>
    public sealed class ClassicStatGrowthFormula : IStatGrowthFormula
    {
        // La versión simple = la completa sin genética. Con IV/EV 0 y naturaleza neutra da EXACTAMENTE
        // lo mismo que la fórmula anterior al Lote 3, así los combates viejos no cambian.
        public int Compute(StatId stat, int baseValue, int level)
            => Compute(stat, baseValue, level, 0, 0, 100);

        public int Compute(StatId stat, int baseValue, int level, int iv, int ev, int naturePercent)
        {
            if (iv < 0) iv = 0;
            if (ev < 0) ev = 0;
            if (naturePercent < 0) naturePercent = 0;

            int common = (2 * baseValue + iv + ev / 4) * level / 100;

            if (stat == StatId.Hp)
                return common + level + 10;

            return (common + 5) * naturePercent / 100;
        }
    }
}
