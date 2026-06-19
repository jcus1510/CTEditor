using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Un IRng determinista para tests (y, el día de mañana, para replays). Usa System.Random con
    /// una SEMILLA fija: misma semilla -> misma secuencia de "azar" -> mismo combate. Es puro
    /// (System.Random no es de Unity). En producción, el Bootstrap inyectará otro IRng concreto.
    /// </summary>
    public sealed class SeededRng : IRng
    {
        private readonly System.Random _random;

        public SeededRng(int seed) => _random = new System.Random(seed);

        public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

        public float NextFloat() => (float)_random.NextDouble();
    }
}
