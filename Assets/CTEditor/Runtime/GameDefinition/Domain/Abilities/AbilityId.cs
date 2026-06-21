using System;

namespace CTEditor.GameDefinition.Domain.Abilities
{
    /// <summary>
    /// Identificador de una habilidad (Intimidación, Levitación...). Igual que StatusId/StatId: una
    /// CLAVE de texto abierta, para que el autor INVENTE habilidades sin tocar código.
    /// </summary>
    public readonly struct AbilityId : IEquatable<AbilityId>
    {
        public string Value { get; }

        public AbilityId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Un AbilityId no puede estar vacío.", nameof(value));
            Value = value;
        }

        public bool Equals(AbilityId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AbilityId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(AbilityId a, AbilityId b) => a.Equals(b);
        public static bool operator !=(AbilityId a, AbilityId b) => !a.Equals(b);
    }
}
