using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;

namespace CTEditor.Bootstrap.Platform
{
    /// <summary>
    /// IA "desafiante": elige el movimiento que MÁS daño espera hacer, mirando
    /// poder × efectividad de tipo × STAB contra el rival. Empates, al azar.
    ///
    /// Es otra implementación del mismo seam IBattleAI (M.5): añadir niveles de dificultad nuevos =
    /// crear clases como esta, sin tocar el combate. Necesita el catálogo de movimientos y la tabla
    /// de tipos para evaluar, así que se los inyectamos por constructor.
    /// </summary>
    public sealed class AggressiveBattleAI : IBattleAI
    {
        private readonly IRng _rng;
        private readonly ICatalog<Move> _moves;
        private readonly TypeChart _typeChart;

        public AggressiveBattleAI(IRng rng, ICatalog<Move> moves, TypeChart typeChart)
        {
            _rng = rng;
            _moves = moves;
            _typeChart = typeChart;
        }

        public BattleAction ChooseAction(Combatant self, Combatant opponent)
        {
            if (self.Moves.Count == 0)
                return new Flee();

            float bestScore = -1f;
            var bestIndices = new List<int>();

            for (int i = 0; i < self.Moves.Count; i++)
            {
                var move = _moves.Get(self.Moves[i]);
                float score = ScoreOf(move, self, opponent);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndices.Clear();
                    bestIndices.Add(i);
                }
                else if (score == bestScore)
                {
                    bestIndices.Add(i);
                }
            }

            int chosen = bestIndices[_rng.Next(0, bestIndices.Count)]; // desempate al azar
            return new UseMove(self.Moves[chosen]);
        }

        // Puntúa un movimiento por daño esperado. Los de estado (sin daño directo) valen poco por
        // ahora: la IA prefiere pegar. Cuando lleguen los efectos secundarios, esto se enriquece.
        private float ScoreOf(Move move, Combatant self, Combatant opponent)
        {
            if (!move.DealsDirectDamage)
                return 0f;

            float effectiveness = _typeChart.Effectiveness(move.Type, opponent.Types).Multiplier;
            float stab = ContainsType(self.Types, move.Type) ? 1.5f : 1f;
            return move.Power * effectiveness * stab;
        }

        private static bool ContainsType(IReadOnlyList<Id<ElementType>> types, Id<ElementType> type)
        {
            for (int i = 0; i < types.Count; i++)
                if (types[i] == type)
                    return true;
            return false;
        }
    }
}
