using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Conditions
{
    /// <summary>
    /// QUÉ se pregunta una condición. Se evalúa durante el combate con "propio" (el dueño de la ficha:
    /// quien usa el movimiento, o quien tiene la habilidad) y "rival" (el otro).
    /// El orden importa: Unity guarda el número, así que solo se añaden valores AL FINAL.
    /// </summary>
    public enum ConditionKind
    {
        HasAnyStatus,     // tiene algún estado (principal o volátil)
        HasStatus,        // tiene el estado Text (p. ej. "poison")
        HpPercent,        // % de PS actuales  (comparado con Number)
        IsType,           // es del tipo Text
        Weather,          // el clima actual es Text ("rain")
        Friendship,       // amistad 0-255     (comparado con Number)
        Level,            // nivel             (comparado con Number)
        LevelDifference,  // nivel propio - nivel del otro (comparado con Number)
        StatStage,        // etapa de la stat Text (comparada con Number)
        AlreadyActed,     // ya actuó en este turno
        MoveType,         // el movimiento es del tipo Text
        MoveCategory,     // el movimiento es de la categoría Text (Physical / Special / Status)
        MovePower,        // potencia base del movimiento (comparada con Number)
        MoveMakesContact, // el movimiento hace contacto
        MoveHasTag,       // el movimiento tiene la etiqueta Text ("puño", "sonido"...)
        RandomChance,     // probabilidad Number % (una tirada propia)
        // --- 3.ª y 4.ª generación (solo AL FINAL) ---
        DamagedThisTurn,      // recibió daño este turno (Venganza, Alud, Buena Baza; Puño Certero falla si sí)
        TurnsOnField,         // turnos que lleva en el campo (comparado con Number): Sorpresa solo el primero, Inicio Lento
        TargetChoseAttack,    // el otro eligió este turno un movimiento que hace daño (Golpe Bajo)
        UsedAllOtherMoves,    // ya usó todos sus otros movimientos desde que entró (Última Baza)
        StockpileCount,       // reservas de Reserva (comparado con Number)
        MoveEffectiveness,    // eficacia del movimiento contra quien diga el sujeto (comparada con Number: 2 = muy eficaz)
        MoveHasSecondary,     // el movimiento tiene algún efecto secundario (Potencia Bruta)
        HasItem,              // lleva un objeto equipado
        LostItem,             // perdió (o gastó) su objeto en este combate (Liviano)
        WeightKg,             // peso en kilos (comparado con Number)
        // --- Lote F (género) ---
        SameGender,           // los dos tienen el MISMO género (Rivalidad ×1,25)
        OppositeGender,       // tienen géneros OPUESTOS (Seducción, Rivalidad ×0,75); los sin género nunca
        // --- Lote F (5.ª-6.ª gen.) ---
        FieldCondition,       // hay activo el efecto de campo Text en cualquier lado ("grassy_terrain", "trick_room")
        CanEvolve             // aún puede evolucionar (Mineral Evolutivo)
    }

    /// <summary>De quién se pregunta: el dueño de la ficha o el otro.</summary>
    public enum ConditionSubject { Self, Other }

    public enum Comparison { Less, LessOrEqual, Equal, GreaterOrEqual, Greater, NotEqual }

    /// <summary>
    /// Una CONDICIÓN: "si el rival tiene menos del 50% de vida", "si llueve", "si su amistad es 200 o más".
    /// Se usan en los efectos de los movimientos (el efecto solo ocurre si se cumplen TODAS) y en los
    /// modificadores de potencia (de movimientos y de habilidades). Sin condiciones = siempre.
    /// </summary>
    public sealed class Condition
    {
        public ConditionKind Kind { get; }
        public ConditionSubject Subject { get; }
        public Comparison Comparison { get; }
        public float Number { get; }
        public string Text { get; }
        /// <summary>Invierte el resultado ("NO tiene estado", "NO llueve").</summary>
        public bool Negate { get; }

        public Condition(ConditionKind kind, ConditionSubject subject = ConditionSubject.Self,
            Comparison comparison = Comparison.GreaterOrEqual, float number = 0f, string text = null, bool negate = false)
        {
            Kind = kind;
            Subject = subject;
            Comparison = comparison;
            Number = number;
            Text = text ?? "";
            Negate = negate;
        }

        /// <summary>Compara un valor según la comparación de esta condición.</summary>
        public bool Compare(float value)
        {
            const float eps = 1e-4f;
            switch (Comparison)
            {
                case Comparison.Less: return value < Number - eps;
                case Comparison.LessOrEqual: return value <= Number + eps;
                case Comparison.Equal: return Math.Abs(value - Number) <= eps;
                case Comparison.GreaterOrEqual: return value >= Number - eps;
                case Comparison.Greater: return value > Number + eps;
                case Comparison.NotEqual: return Math.Abs(value - Number) > eps;
                default: return false;
            }
        }

        /// <summary>¿Esta condición mira el movimiento (y no a los combatientes)?</summary>
        public bool IsAboutMove => Kind == ConditionKind.MoveType || Kind == ConditionKind.MoveCategory
            || Kind == ConditionKind.MovePower || Kind == ConditionKind.MoveMakesContact || Kind == ConditionKind.MoveHasTag;

        /// <summary>¿Usa el número (y la comparación)?</summary>
        public static bool UsesNumber(ConditionKind k) => k == ConditionKind.HpPercent || k == ConditionKind.Friendship
            || k == ConditionKind.Level || k == ConditionKind.LevelDifference || k == ConditionKind.StatStage
            || k == ConditionKind.MovePower || k == ConditionKind.RandomChance;

        /// <summary>¿Usa el texto (un id)?</summary>
        public static bool UsesText(ConditionKind k) => k == ConditionKind.HasStatus || k == ConditionKind.IsType
            || k == ConditionKind.Weather || k == ConditionKind.StatStage || k == ConditionKind.MoveType
            || k == ConditionKind.MoveCategory || k == ConditionKind.MoveHasTag || k == ConditionKind.FieldCondition;

        /// <summary>¿Tiene sentido "propio/rival"? (el clima o el movimiento no son de nadie).</summary>
        public static bool UsesSubject(ConditionKind k) => !(k == ConditionKind.Weather || k == ConditionKind.RandomChance
            || k == ConditionKind.MoveType || k == ConditionKind.MoveCategory || k == ConditionKind.MovePower
            || k == ConditionKind.MoveMakesContact || k == ConditionKind.MoveHasTag || k == ConditionKind.FieldCondition
            || k == ConditionKind.SameGender || k == ConditionKind.OppositeGender);
    }

    /// <summary>
    /// Un MODIFICADOR DE POTENCIA: multiplica la potencia si se cumplen sus condiciones.
    /// Ej.: Fachada = ×2 si propio tiene estado. Técnico (habilidad) = ×1,5 si la potencia es 60 o menos.
    /// </summary>
    public sealed class PowerModifier
    {
        public float Multiplier { get; }
        public IReadOnlyList<Condition> Conditions { get; }

        public PowerModifier(float multiplier, IReadOnlyList<Condition> conditions = null)
        {
            Multiplier = multiplier < 0f ? 0f : multiplier;
            Conditions = conditions == null ? Array.Empty<Condition>() : new List<Condition>(conditions);
        }
    }
}
