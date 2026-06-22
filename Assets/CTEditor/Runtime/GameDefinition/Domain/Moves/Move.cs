using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Moves
{
    /// <summary>Movimientos que ocupan dos turnos.</summary>
    public enum TwoTurnKind
    {
        None,     // normal (un turno)
        Charge,   // turno 1 carga, turno 2 golpea (Rayo Solar)
        Recharge  // turno 1 golpea, turno 2 debe recargar y pierde el turno (Hiperrayo)
    }

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

        /// <summary>
        /// Golpe múltiple: cuántas veces impacta el movimiento en un turno. 1/1 = normal.
        /// 2/5 = clásico multi-golpe (entre 2 y 5 impactos, al azar). Cada impacto calcula su daño.
        /// </summary>
        public int MinHits { get; }
        public int MaxHits { get; }

        /// <summary>Nivel de crítico (0 = normal; mayor sube la probabilidad de crítico).</summary>
        public int CritStage { get; }

        /// <summary>Modo de dos turnos (cargar / recargar). None = normal.</summary>
        public TwoTurnKind TwoTurn { get; }

        /// <summary>¿El movimiento hace CONTACTO físico? Relevante para habilidades de reacción
        /// (Estática, Cuerpo Llama, Piel Tosca...). El autor lo marca; típico en físicos.</summary>
        public bool MakesContact { get; }

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
            IReadOnlyList<MoveEffect> secondaryEffects = null,
            int minHits = 1,
            int maxHits = 1,
            int critStage = 0,
            TwoTurnKind twoTurn = TwoTurnKind.None,
            bool makesContact = false)
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

            // Normaliza el rango de golpes: al menos 1, y MaxHits nunca menor que MinHits.
            MinHits = minHits < 1 ? 1 : minHits;
            MaxHits = maxHits < MinHits ? MinHits : maxHits;

            CritStage = critStage < 0 ? 0 : critStage;
            TwoTurn = twoTurn;
            MakesContact = makesContact;
        }

        /// <summary>¿Este movimiento hace daño directo? (No es de Estado y tiene potencia.)</summary>
        public bool DealsDirectDamage => Category != MoveCategory.Status && Power > 0;

        /// <summary>¿Nunca falla? (Su precisión es null.)</summary>
        public bool NeverMisses => Accuracy is null;
    }
}
