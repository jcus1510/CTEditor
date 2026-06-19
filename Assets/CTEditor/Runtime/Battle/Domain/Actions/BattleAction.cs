using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.Battle.Domain.Actions
{
    /// <summary>
    /// Una ACCIÓN que un combatiente elige en su turno. Es una clase base abstracta: no se instancia
    /// sola, solo sus hijas concretas (UseMove, Flee...). Esto permite que el resolvedor reciba "una
    /// acción" y decida qué hacer según su tipo real. En combate completo habrá también CambiarMonstruo
    /// y UsarObjeto; arrancamos con las dos que el combate de consola necesita.
    /// </summary>
    public abstract class BattleAction
    {
    }

    /// <summary>Usar un movimiento (por su id).</summary>
    public sealed class UseMove : BattleAction
    {
        public Id<Move> Move { get; }
        public UseMove(Id<Move> move) => Move = move;
    }

    /// <summary>Intentar huir del combate.</summary>
    public sealed class Flee : BattleAction
    {
    }
}
