using CTEditor.SharedKernel.Abstractions;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;

namespace CTEditor.Bootstrap.Platform
{
    /// <summary>
    /// La IA más simple: elige un movimiento al azar de los que tiene. Usa el IRng inyectado, así que
    /// es determinista bajo semilla. Implementación concreta del seam IBattleAI; el día que quieras
    /// una IA que apunte al máximo daño, implementas otra y la inyectas, sin tocar el combate.
    /// </summary>
    public sealed class SimpleBattleAI : IBattleAI
    {
        private readonly IRng _rng;

        public SimpleBattleAI(IRng rng) => _rng = rng;

        public BattleAction ChooseAction(Combatant self, Combatant opponent)
        {
            if (self.Moves.Count == 0)
                return new Flee();

            int index = _rng.Next(0, self.Moves.Count);
            return new UseMove(self.Moves[index]);
        }
    }
}
