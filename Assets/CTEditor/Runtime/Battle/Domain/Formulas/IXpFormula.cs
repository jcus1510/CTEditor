namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>
    /// Todo lo que una fórmula de XP puede querer saber sobre "el caído" y "el ganador". Incluye el
    /// CONTEXTO que pediste: si el rival era de un ENTRENADOR (no salvaje), y si en vez de caer fue
    /// CAPTURADO (los juegos modernos también dan XP al capturar).
    /// </summary>
    public readonly struct XpContext
    {
        /// <summary>Rendimiento base de XP de la especie derrotada (b en las fórmulas clásicas).</summary>
        public int BaseExpYield { get; }
        /// <summary>Nivel del caído (L).</summary>
        public int DefeatedLevel { get; }
        /// <summary>Nivel de quien recibe la XP (para fórmulas escaladas por diferencia de nivel).</summary>
        public int WinnerLevel { get; }
        /// <summary>¿Combate contra un entrenador? (los clásicos multiplican ×1.5).</summary>
        public bool IsTrainerBattle { get; }
        /// <summary>Entre cuántos participantes se reparte (s en la fórmula clásica).</summary>
        public int ParticipantCount { get; }
        /// <summary>¿El rival fue CAPTURADO en vez de debilitado?</summary>
        public bool WasCaptured { get; }

        public XpContext(int baseExpYield, int defeatedLevel, int winnerLevel,
            bool isTrainerBattle, int participantCount, bool wasCaptured)
        {
            BaseExpYield = baseExpYield;
            DefeatedLevel = defeatedLevel;
            WinnerLevel = winnerLevel;
            IsTrainerBattle = isTrainerBattle;
            ParticipantCount = participantCount < 1 ? 1 : participantCount;
            WasCaptured = wasCaptured;
        }
    }

    /// <summary>
    /// La fórmula de GANANCIA de experiencia, como STRATEGY (mismo seam que IDamageFormula, Parte D
    /// Nivel 2): el motor entrega el contexto y la fórmula decide cuánta XP recibe cada participante.
    /// Un creador puede implementar la suya (escalada por niveles, plana, fija...) y elegirla.
    /// </summary>
    public interface IXpFormula
    {
        /// <summary>XP que recibe UN participante. Devuelve 0 para "no dar nada".</summary>
        int Compute(XpContext context);
    }
}
