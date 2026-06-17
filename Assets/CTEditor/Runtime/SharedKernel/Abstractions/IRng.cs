namespace CTEditor.SharedKernel.Abstractions
{
    /// <summary>
    /// Azar INYECTABLE. El dominio depende de esta interfaz, NUNCA de UnityEngine.Random (J.2).
    /// Regalo: en tests inyectas una semilla fija y las reglas dejan de ser "imposibles de probar";
    /// además habilita combates deterministas y replays (M.2).
    /// </summary>
    public interface IRng
    {
        /// <summary>Entero en el rango [minInclusive, maxExclusive).</summary>
        int Next(int minInclusive, int maxExclusive);

        /// <summary>Flotante en el rango [0, 1).</summary>
        float NextFloat();
    }
}
