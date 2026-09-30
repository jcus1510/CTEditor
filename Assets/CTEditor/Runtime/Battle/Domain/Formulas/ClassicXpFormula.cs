namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>
    /// La fórmula PLANA clásica (Gen I–IV y VI):
    ///
    ///     XP = (b × L / 7) ÷ participantes      (divisiones enteras, como los juegos)
    ///     ×1.5 si el rival pertenecía a un entrenador
    ///
    /// donde b = rendimiento base de la especie caída y L = su nivel. Desde Gen VI los juegos también
    /// dan XP al CAPTURAR (como si se hubiera debilitado); aquí es configurable. Siempre da al menos
    /// 1 punto (derrotar algo nunca "no cuenta").
    /// </summary>
    public sealed class ClassicXpFormula : IXpFormula
    {
        private readonly bool _awardOnCapture;

        public ClassicXpFormula(bool awardOnCapture = true)
        {
            _awardOnCapture = awardOnCapture;
        }

        public int Compute(XpContext ctx)
        {
            if (ctx.WasCaptured && !_awardOnCapture) return 0;

            // División entera en cascada, igual que los clásicos: b*L/7, y luego el reparto.
            int amount = ctx.BaseExpYield * ctx.DefeatedLevel / 7 / ctx.ParticipantCount;

            if (ctx.IsTrainerBattle)
                amount = amount * 3 / 2; // ×1.5 sin pasar por float

            return amount < 1 ? 1 : amount;
        }
    }
}
