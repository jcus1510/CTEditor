using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.Battle.Domain.AI
{
    /// <summary>
    /// LO QUE LA IA CREE que sabe del rival (el jugador). Los cálculos de daño usan el motor real, pero pasan por
    /// aquí para no hacer trampa: la IA solo «ve» los movimientos que conoce y usa sus ESTIMACIONES de tus
    /// estadísticas (IVs/EVs) en vez de las reales. El nivel Injusto usa un modelo que lo sabe todo (factor 1).
    /// </summary>
    public interface IOpponentModel
    {
        /// <summary>¿Es un Pokémon del jugador (lo que la IA no controla)?</summary>
        bool IsOpponent(Combatant c);

        /// <summary>¿Sabe que ese Pokémon del jugador existe? (lo ha visto en combate, lo recuerda, o lo sabe todo).</summary>
        bool HasSeen(Combatant opponent);

        /// <summary>Movimientos que la IA cree que tiene ese Pokémon del jugador (vistos o supuestos).</summary>
        IReadOnlyList<Move> KnownMoves(Combatant opponent);

        /// <summary>
        /// Factor por el que multiplica el daño REAL para obtener el que ella CREE (1 = lo sabe exacto). Depende de lo
        /// que estima de las estadísticas del jugador: su Ataque si ataca él, su Defensa si recibe.
        /// </summary>
        float Belief(Combatant attacker, Combatant target, Move move);
    }
}
