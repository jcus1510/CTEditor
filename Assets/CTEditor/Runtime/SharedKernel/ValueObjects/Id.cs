using System;

namespace CTEditor.SharedKernel.ValueObjects
{
    /// <summary>
    /// Identificador ESTABLE y TIPADO. El parámetro de tipo da seguridad en compilación:
    /// un Id&lt;Species&gt; jamás se confunde con un Id&lt;Move&gt;. Es el id propio del contenido
    /// (un string que asigna el autor), independiente del GUID de archivo de Unity (M.4):
    /// así un guardado sigue siendo válido aunque muevas o renombres el asset.
    /// </summary>
    public readonly struct Id<T> : IEquatable<Id<T>>
    {
        public string Value { get; }

        public Id(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Un Id no puede ser nulo ni vacío.", nameof(value));
            Value = value;
        }

        public bool Equals(Id<T> other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is Id<T> other && Equals(other);
        public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "(id vacío)";

        public static bool operator ==(Id<T> a, Id<T> b) => a.Equals(b);
        public static bool operator !=(Id<T> a, Id<T> b) => !a.Equals(b);
    }
}
