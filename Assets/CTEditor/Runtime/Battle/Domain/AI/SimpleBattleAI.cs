using CTEditor.SharedKernel.Abstractions;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;

namespace CTEditor.Battle.Domain.AI
{
    /// <summary>
    /// La IA más simple: elige un movimiento al azar de los que tiene. Usa el IRng inyectado, así que
    /// es determinista bajo semilla. Implementación concreta del seam IBattleAI; el día que quieras
    /// una IA que apunte al máximo daño, implementas otra y la inyectas, sin tocar el combate.
    /// </summary>
    public sealed class SimpleBattleAI : IBattleAI
    {
        private readonly IRng _rng;

        /// <summary>
        /// Filtro opcional «¿se puede elegir este movimiento?» (Mofa, Tormento, Anticura, Cerca...). Lo pone
        /// quien crea la IA con TurnResolver.CanChooseMove; sin él solo se miran los PP.
        /// </summary>
        public System.Func<Combatant, int, bool> CanUse { get; set; }

        public SimpleBattleAI(IRng rng) => _rng = rng;

        public BattleAction ChooseAction(Combatant self, Combatant opponent)
        {
            if (self.Moves.Count == 0)
                return new Flee();

            // Al azar entre los que aún tienen PP (-1 = PP sin cargar todavía: se consideran usables).
            var usable = new System.Collections.Generic.List<int>();
            for (int i = 0; i < self.Moves.Count; i++)
                if (CanUse != null ? CanUse(self, i) : self.CanChoose(i)) usable.Add(i);
            if (usable.Count == 0) return new UseMove(self.Moves[0]); // el motor lo convierte en Forcejeo

            int index = usable[_rng.Next(0, usable.Count)];
            return new UseMove(self.Moves[index]);
        }
    }
}
