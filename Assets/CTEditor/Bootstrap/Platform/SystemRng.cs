using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.Bootstrap.Platform
{
    /// <summary>
    /// La implementación concreta de IRng para producción, basada en System.Random. Vive en Platform
    /// (el borde más externo): es un detalle técnico que el Bootstrap inyecta. El dominio solo conoce
    /// IRng. Aceptar una semilla la hace determinista (útil para replays, M.2).
    ///
    /// La uso en vez de UnityEngine.Random porque es seedeable de forma explícita y no depende del
    /// estado global de Unity; si prefirieras la de Unity, se cambia solo aquí, sin tocar el dominio.
    /// </summary>
    public sealed class SystemRng : IRng
    {
        private readonly System.Random _random;

        public SystemRng() => _random = new System.Random();
        public SystemRng(int seed) => _random = new System.Random(seed);

        public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
        public float NextFloat() => (float)_random.NextDouble();
    }
}
