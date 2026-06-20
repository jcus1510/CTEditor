namespace CTEditor.Bootstrap
{
    /// <summary>
    /// Nivel de la IA del rival, elegible desde el Inspector. Cada nivel mapea a una implementación
    /// de IBattleAI. Para añadir más retos (p.ej. una IA que cambie de monstruo o use objetos), basta
    /// agregar un valor aquí y su clase correspondiente.
    /// </summary>
    public enum AiLevel
    {
        Easy, // elige movimientos al azar (SimpleBattleAI)
        Hard  // busca el mayor daño por tipo/STAB (AggressiveBattleAI)
    }
}
