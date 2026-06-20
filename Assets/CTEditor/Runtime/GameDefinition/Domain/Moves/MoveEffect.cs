using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Status;

namespace CTEditor.GameDefinition.Domain.Moves
{
    /// <summary>
    /// Un EFECTO SECUNDARIO de un movimiento: "con cierta probabilidad, inflige un estado al objetivo"
    /// (p.ej. Ascuas: 10% de quemar). Es contenido autorable: el autor compone movimientos con estos.
    ///
    /// Nota de diseño: para los efectos secundarios DE COMBATE usamos un modelo TIPADO y acotado (esto)
    /// en vez del motor de efectos totalmente abierto (Parte E). Es lo pragmático y clásico: cubre la
    /// gran mayoría de casos y es fácil de editar. El scripting arbitrario (Nivel 4) es para después.
    /// Esta primera versión cubre "infligir estado"; los cambios de etapa de stats (bajar defensa,
    /// subir ataque) se suman cuando construyamos la capa de etapas.
    /// </summary>
    public sealed class MoveEffect
    {
        /// <summary>Probabilidad de que el efecto ocurra al impactar.</summary>
        public Percentage Chance { get; }

        /// <summary>Estado que se intenta infligir al objetivo.</summary>
        public StatusId InflictsStatus { get; }

        public MoveEffect(Percentage chance, StatusId inflictsStatus)
        {
            Chance = chance;
            InflictsStatus = inflictsStatus;
        }
    }
}
