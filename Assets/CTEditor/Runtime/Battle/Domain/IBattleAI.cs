using CTEditor.Battle.Domain.Actions;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// La IA de combate (M.5): decide la acción del bando que no controla el jugador. Es un STRATEGY
    /// más — su interfaz vive en el dominio, sus implementaciones (Random, Agresiva, Defensiva...) se
    /// inyectan. Igual que las fórmulas, abre el seam para que un creador añada IAs nuevas.
    /// </summary>
    public interface IBattleAI
    {
        /// <summary>Elige qué hace 'self' este turno, viendo a su rival.</summary>
        BattleAction ChooseAction(Combatant self, Combatant opponent);
    }
}
