using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Status
{
    /// <summary>
    /// Comportamientos extra de un ESTADO (sobre todo volátiles de la 3.ª y 4.ª generación). Todo es
    /// opcional: por defecto no hacen nada. Se combinan libremente para inventar estados nuevos.
    /// Se rellena al construir la ficha (mapper) y después no se toca.
    /// </summary>
    public sealed class StatusExtras
    {
        public static readonly StatusExtras None = new StatusExtras();

        /// <summary>No puede usar movimientos de ESTADO (Mofa).</summary>
        public bool BlocksStatusMoves { get; set; }
        /// <summary>No puede usar el mismo movimiento dos turnos seguidos (Tormento).</summary>
        public bool BlocksRepeatedMove { get; set; }
        /// <summary>No puede curarse (Anticura): ni movimientos de curar ni drenar.</summary>
        public bool BlocksHealing { get; set; }
        /// <summary>No puede usar ni aprovechar objetos (Embargo).</summary>
        public bool BlocksItems { get; set; }
        /// <summary>Su habilidad no hace nada (Bilis).</summary>
        public bool SuppressesAbility { get; set; }
        /// <summary>El daño residual solo ocurre si tiene ESTE estado; si no, el volátil se va (Pesadilla: dormido).</summary>
        public StatusId? ResidualRequiresStatus { get; set; }
        /// <summary>Al acabarse su duración, el portador se debilita (Canto Mortal).</summary>
        public bool FaintsWhenEnds { get; set; }
        /// <summary>Si lo debilita un ataque mientras dura, el atacante también cae (Mismo Destino).</summary>
        public bool DestinyBond { get; set; }
        /// <summary>Si lo debilita un ataque, ese movimiento se queda sin PP (Rabia).</summary>
        public bool Grudge { get; set; }
        /// <summary>Devuelve los movimientos de estado que le lanzan (Capa Mágica).</summary>
        public bool ReflectsStatusMoves { get; set; }
        /// <summary>Se queda las mejoras propias que el rival use este turno (Robo).</summary>
        public bool StealsBoostMoves { get; set; }
        /// <summary>Su próximo movimiento de este tipo hace más daño (Carga: Eléctrico ×2).</summary>
        public Id<ElementType>? BoostedType { get; set; }
        public float BoostMultiplier { get; set; } = 1f;
        /// <summary>El refuerzo de tipo se gasta al usarlo.</summary>
        public bool BoostConsumed { get; set; }
        /// <summary>Tipos a los que es inmune mientras dura (Levitón: Tierra).</summary>
        public IReadOnlyList<Id<ElementType>> TypeImmunities { get; set; } = Array.Empty<Id<ElementType>>();
        /// <summary>Pierde este tipo mientras dura (Respiro: Volador).</summary>
        public Id<ElementType>? SuppressedType { get; set; }
        /// <summary>Identificado: se ignora su evasión y lo alcanzan los tipos de HittableByTypes aunque fuera inmune (Profecía, Gran Ojo).</summary>
        public bool Identified { get; set; }
        public IReadOnlyList<Id<ElementType>> HittableByTypes { get; set; } = Array.Empty<Id<ElementType>>();
        /// <summary>Su próximo movimiento no falla (Fijar Blanco, Telépata). Se pone en el USUARIO.</summary>
        public bool SureHit { get; set; }
        /// <summary>El rival no puede usar los movimientos que el portador conoce (Cerca).</summary>
        public bool Imprisons { get; set; }
        /// <summary>Queda en el suelo: le afectan los movimientos de Tierra aunque sea Volador o levite (Arraigo).</summary>
        public bool Grounded { get; set; }
    }
}
