using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Moves
{
    /// <summary>
    /// La DEFINICIÓN de un movimiento: una ficha inmutable que rellena el autor (C.1, Definición
    /// vs Instancia). No es "el ataque que ocurre en combate" (eso es estado de partida); es la
    /// plantilla a partir de la cual se ejecuta.
    ///
    /// Aquí está SOLO el "esqueleto" del movimiento (tipo, números, categoría, objetivo). Los
    /// EFECTOS SECUNDARIOS (quemar, bajar defensa, golpear dos veces...) NO están todavía y es a
    /// propósito: viven en el motor de efectos (Parte E), que aún no construimos, y hay un detalle
    /// de dependencias que resolver cuando lleguen (te lo explico tras el código).
    /// </summary>
    public sealed class Move
    {
        /// <summary>Id estable propio del movimiento (M.4), no la referencia del asset de Unity.</summary>
        public Id<Move> Id { get; }

        public string DisplayName { get; }

        /// <summary>Su tipo elemental, referenciado POR ID (Fuego, Agua, o el que inventó el autor).</summary>
        public Id<ElementType> Type { get; }

        public MoveCategory Category { get; }

        /// <summary>Potencia base. 0 = sin daño directo (lo normal en movimientos de Estado).</summary>
        public int Power { get; }

        /// <summary>
        /// Precisión. Fíjate en el '?': 'Percentage?' es un VALOR NULABLE. Un Percentage normal
        /// SIEMPRE tiene un valor; al ponerle '?', además puede valer 'null' (= "nada").
        /// Aquí lo usamos con un significado preciso: null = el movimiento NUNCA FALLA (se salta
        /// el chequeo de precisión). Un valor presente = ese % de acierto.
        /// </summary>
        public Percentage? Accuracy { get; }

        /// <summary>PP máximos: cuántas veces puede usarse antes de recargar.</summary>
        public int MaxPp { get; }

        /// <summary>
        /// Prioridad de turno. 0 es lo normal; &gt;0 actúa antes que lo normal sin importar la
        /// velocidad (los "movimientos rápidos"); &lt;0, después. Lo lee el ordenador de turnos (M.1).
        /// </summary>
        public int Priority { get; }

        public MoveTarget Target { get; }

        /// <summary>Efectos secundarios al impactar (p.ej. 10% de quemar). Vacío = ninguno.</summary>
        public IReadOnlyList<MoveEffect> SecondaryEffects { get; }

        public Move(
            Id<Move> id,
            string displayName,
            Id<ElementType> type,
            MoveCategory category,
            int power,
            Percentage? accuracy,
            int maxPp,
            int priority,
            MoveTarget target,
            IReadOnlyList<MoveEffect> secondaryEffects = null)
        {
            // Estas validaciones LANZAN si los datos son absurdos. Son la última línea de defensa:
            // el autor nunca debería llegar aquí con basura, porque la validación amigable (con
            // mensajes claros, sin excepciones) ocurre antes, en Content Authoring (L.8). El
            // dominio simplemente se niega a existir en un estado imposible.
            if (power < 0)
                throw new ArgumentOutOfRangeException(nameof(power), "La potencia no puede ser negativa.");
            if (maxPp < 1)
                throw new ArgumentOutOfRangeException(nameof(maxPp), "Un movimiento necesita al menos 1 PP.");

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
            Type = type;
            Category = category;
            Power = power;
            Accuracy = accuracy;
            MaxPp = maxPp;
            Priority = priority;
            Target = target;
            // Copia defensiva: la ficha es inmutable, nadie debe alterar su lista por fuera.
            SecondaryEffects = secondaryEffects == null
                ? Array.Empty<MoveEffect>()
                : new List<MoveEffect>(secondaryEffects);
        }

        /// <summary>¿Este movimiento hace daño directo? (No es de Estado y tiene potencia.)</summary>
        public bool DealsDirectDamage => Category != MoveCategory.Status && Power > 0;

        /// <summary>¿Nunca falla? (Su precisión es null.)</summary>
        public bool NeverMisses => Accuracy is null;
    }
}
