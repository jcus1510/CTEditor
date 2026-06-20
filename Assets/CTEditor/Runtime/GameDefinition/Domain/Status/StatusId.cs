using System;

namespace CTEditor.GameDefinition.Domain.Status
{
    /// <summary>
    /// Identificador de un estado alterado (quemado, paralizado...). Igual que StatId: una CLAVE de
    /// texto, no un enum cerrado — así el autor puede INVENTAR estados nuevos (Parte D, Nivel 3) sin
    /// tocar código. Los clásicos están como constantes por comodidad, pero no son un límite.
    /// </summary>
    public readonly struct StatusId : IEquatable<StatusId>
    {
        public string Value { get; }

        public StatusId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Un StatusId no puede estar vacío.", nameof(value));
            Value = value;
        }

        // Constantes clásicas (conveniencia, no obligación).
        public static readonly StatusId Burn      = new StatusId("burn");
        public static readonly StatusId Poison    = new StatusId("poison");
        public static readonly StatusId Paralysis = new StatusId("paralysis");
        public static readonly StatusId Sleep     = new StatusId("sleep");
        public static readonly StatusId Freeze    = new StatusId("freeze");

        public bool Equals(StatusId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StatusId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode();
        public override string ToString() => Value;

        public static bool operator ==(StatusId a, StatusId b) => a.Equals(b);
        public static bool operator !=(StatusId a, StatusId b) => !a.Equals(b);
    }
}
