namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// Lo que el JUGADOR elige en su turno, tal como lo ve en el menú (Luchar / Mochila / Equipo /
    /// Huir). La sesión lo valida con las reglas y lo traduce a una acción del motor.
    /// </summary>
    public abstract class PlayerChoice
    {
        /// <summary>Luchar: usar el movimiento del hueco 'moveIndex' (0-3).</summary>
        public static PlayerChoice Fight(int moveIndex) => new FightChoice(moveIndex);
        /// <summary>Cambiar al miembro 'teamIndex' del equipo.</summary>
        public static PlayerChoice Switch(int teamIndex) => new SwitchChoice(teamIndex);
        /// <summary>Usar un objeto de la mochila sobre el miembro 'teamIndex' (pociones, revivir, Ataque X...).</summary>
        public static PlayerChoice UseItem(string itemId, int teamIndex) => new ItemChoice(itemId, teamIndex);
        /// <summary>Lanzar una bola de la mochila al rival.</summary>
        public static PlayerChoice ThrowBall(string itemId) => new BallChoice(itemId);
        /// <summary>Huir.</summary>
        public static PlayerChoice Run() => new RunChoice();
    }

    public sealed class FightChoice : PlayerChoice { public int MoveIndex { get; } public FightChoice(int i) { MoveIndex = i; } }
    public sealed class SwitchChoice : PlayerChoice { public int TeamIndex { get; } public SwitchChoice(int i) { TeamIndex = i; } }
    public sealed class ItemChoice : PlayerChoice
    {
        public string ItemId { get; }
        public int TeamIndex { get; }
        public ItemChoice(string itemId, int teamIndex) { ItemId = itemId; TeamIndex = teamIndex; }
    }
    public sealed class BallChoice : PlayerChoice { public string ItemId { get; } public BallChoice(string itemId) { ItemId = itemId; } }
    public sealed class RunChoice : PlayerChoice { }

    /// <summary>En qué momento está la sesión: qué espera del jugador.</summary>
    public enum SessionPhase
    {
        ChooseAction,       // elegir qué hacer este turno
        ChooseReplacement,  // su monstruo se debilitó: elegir quién sale
        LearnMove,          // quiere aprender un movimiento y no hay hueco: ¿olvidar uno?
        Evolution,          // está evolucionando: ¿dejarlo o cancelarlo?
        Finished            // terminó (y ya se aplicó todo a la partida)
    }

    /// <summary>Respuesta a una decisión del jugador: si se aceptó, por qué no, y lo que pasó.</summary>
    public sealed class SessionStep
    {
        public bool Accepted { get; }
        /// <summary>Si no se aceptó: el motivo, en palabras para el jugador.</summary>
        public string Message { get; }
        /// <summary>Lo que pasó, en orden (eventos del motor + de la sesión).</summary>
        public System.Collections.Generic.IReadOnlyList<CTEditor.SharedKernel.Events.IDomainEvent> Events { get; }

        private SessionStep(bool ok, string msg, System.Collections.Generic.IReadOnlyList<CTEditor.SharedKernel.Events.IDomainEvent> events)
        {
            Accepted = ok; Message = msg ?? "";
            Events = events ?? System.Array.Empty<CTEditor.SharedKernel.Events.IDomainEvent>();
        }

        public static SessionStep Rejected(string message) => new SessionStep(false, message, null);
        public static SessionStep Done(System.Collections.Generic.IReadOnlyList<CTEditor.SharedKernel.Events.IDomainEvent> events) => new SessionStep(true, "", events);
    }
}
