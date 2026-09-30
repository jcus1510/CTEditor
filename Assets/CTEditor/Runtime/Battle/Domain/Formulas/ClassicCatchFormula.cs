using System;
using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>
    /// Fórmula de captura CLÁSICA (3ª/4ª generación), con sus sacudidas:
    ///
    ///   a = ((3·PSmáx − 2·PSactuales) · ratio · bola) / (3·PSmáx) · estado
    ///   si a ≥ 255 → capturado sin más (Master Ball, o un monstruo muy fácil y muy herido)
    ///   b = 1048560 / √(√(16711680 / a))
    ///   se hacen 4 comprobaciones: cada una pasa si un número al azar 0-65535 &lt; b.
    ///   Las 3 primeras son las SACUDIDAS que se ven; si pasan las 4 → ¡capturado!
    ///
    /// Así se cumple lo que todo jugador sabe: bajar los PS y dormir/paralizar al objetivo ayuda, y
    /// las mejores bolas también. Nada de esto está escrito a mano: sale del ratio de captura de la
    /// especie, de la bola y del multiplicador del estado (todo editable).
    /// </summary>
    public sealed class ClassicCatchFormula : ICatchFormula
    {
        public CatchAttempt Attempt(CatchContext c, IRng rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            double a = (3.0 * c.MaxHp - 2.0 * c.CurrentHp) * c.CatchRate * c.BallMultiplier / (3.0 * c.MaxHp);
            a *= c.StatusMultiplier * c.GlobalMultiplier;
            if (a < 1) a = 1;
            if (a >= 255) return new CatchAttempt(true, 3);

            double b = 1048560.0 / Math.Sqrt(Math.Sqrt(16711680.0 / a));
            int passed = 0;
            for (int i = 0; i < 4; i++)
            {
                if (rng.Next(0, 65536) >= b) break;
                passed++;
            }
            return passed >= 4 ? new CatchAttempt(true, 3) : new CatchAttempt(false, passed);
        }

        /// <summary>Probabilidad (0-1) de capturar en UN intento (para mostrarla en editores/UI).</summary>
        public static double Probability(CatchContext c)
        {
            double a = (3.0 * c.MaxHp - 2.0 * c.CurrentHp) * c.CatchRate * c.BallMultiplier / (3.0 * c.MaxHp);
            a *= c.StatusMultiplier * c.GlobalMultiplier;
            if (a < 1) a = 1;
            if (a >= 255) return 1.0;
            double b = 1048560.0 / Math.Sqrt(Math.Sqrt(16711680.0 / a));
            return Math.Pow(Math.Min(1.0, b / 65536.0), 4);
        }
    }
}
