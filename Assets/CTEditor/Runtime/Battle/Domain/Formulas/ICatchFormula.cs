using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>Todo lo que una fórmula de captura necesita saber del intento.</summary>
    public readonly struct CatchContext
    {
        public int MaxHp { get; }
        public int CurrentHp { get; }
        /// <summary>Ratio de captura de la especie (1-255; más alto = más fácil).</summary>
        public int CatchRate { get; }
        /// <summary>Multiplicador de la bola (Poké Ball 1, Super Ball 1,5, Ultra Ball 2, Master Ball 255).</summary>
        public float BallMultiplier { get; }
        /// <summary>Multiplicador por el estado del objetivo (dormido 2,5; paralizado 1,5; sano 1).</summary>
        public float StatusMultiplier { get; }
        /// <summary>Multiplicador global de las reglas del juego (1 = clásico).</summary>
        public float GlobalMultiplier { get; }

        public CatchContext(int maxHp, int currentHp, int catchRate, float ballMultiplier, float statusMultiplier = 1f, float globalMultiplier = 1f)
        {
            MaxHp = maxHp < 1 ? 1 : maxHp;
            CurrentHp = currentHp < 0 ? 0 : currentHp;
            CatchRate = catchRate < 1 ? 1 : catchRate > 255 ? 255 : catchRate;
            BallMultiplier = ballMultiplier <= 0f ? 1f : ballMultiplier;
            StatusMultiplier = statusMultiplier <= 0f ? 1f : statusMultiplier;
            GlobalMultiplier = globalMultiplier <= 0f ? 1f : globalMultiplier;
        }
    }

    /// <summary>Resultado de lanzar una bola: ¿se capturó? y cuántas veces se sacudió (0-3) antes.</summary>
    public readonly struct CatchAttempt
    {
        public bool Caught { get; }
        /// <summary>Sacudidas de la bola (0-3). Con 3 y Caught = ¡capturado!; con menos, se escapó.</summary>
        public int Shakes { get; }
        public CatchAttempt(bool caught, int shakes) { Caught = caught; Shakes = shakes < 0 ? 0 : shakes > 3 ? 3 : shakes; }
    }

    /// <summary>
    /// La fórmula de captura como STRATEGY (igual que el daño): interfaz en el dominio, implementación
    /// inyectable. Un creador puede ofrecer otra regla de captura sin tocar el motor.
    /// </summary>
    public interface ICatchFormula
    {
        /// <summary>Intenta capturar. Usa el azar inyectado (determinista bajo semilla).</summary>
        CatchAttempt Attempt(CatchContext context, IRng rng);
    }
}
