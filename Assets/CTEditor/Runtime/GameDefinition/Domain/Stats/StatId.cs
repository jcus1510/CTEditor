using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Stats
{
    /// <summary>
    /// Clave ESTABLE y tipada de una estadística. Los 6 clásicos están como constantes
    /// "bien conocidas": dan ergonomía (no escribes el string a mano) y el motor garantiza
    /// que existan. Un autor puede crear StatIds nuevos para inventar stats (decisión I.1 #1:
    /// "fijos los 6, abiertos a más").
    /// </summary>
    public readonly struct StatId : IEquatable<StatId>
    {
        public string Value { get; }

        public StatId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Un StatId no puede ser nulo ni vacío.", nameof(value));
            Value = value;
        }

        // --- Los 6 clásicos (claves bien conocidas) ---
        public static readonly StatId Hp        = new StatId("hp");
        public static readonly StatId Attack    = new StatId("attack");
        public static readonly StatId Defense   = new StatId("defense");
        public static readonly StatId SpAttack  = new StatId("sp_attack");
        public static readonly StatId SpDefense = new StatId("sp_defense");
        public static readonly StatId Speed     = new StatId("speed");

        /// <summary>
        /// Las 6 estadísticas clásicas en orden canónico. La validación de contenido (L.8)
        /// exige que todas existan: el invariante "los 6 siempre están" vive aquí como garantía.
        /// </summary>
        public static readonly IReadOnlyList<StatId> Classic =
            new[] { Hp, Attack, Defense, SpAttack, SpDefense, Speed };

        public bool IsClassic
        {
            get
            {
                for (int i = 0; i < Classic.Count; i++)
                    if (Equals(Classic[i])) return true;
                return false;
            }
        }

        public bool Equals(StatId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StatId other && Equals(other);
        public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "(stat vacío)";

        public static bool operator ==(StatId a, StatId b) => a.Equals(b);
        public static bool operator !=(StatId a, StatId b) => !a.Equals(b);
    }
}
