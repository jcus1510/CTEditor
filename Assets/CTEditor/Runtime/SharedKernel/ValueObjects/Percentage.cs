using System;

namespace CTEditor.SharedKernel.ValueObjects
{
    /// <summary>
    /// Un porcentaje en [0, 100]. Value Object inmutable, definido por su valor (Parte B).
    /// </summary>
    public readonly struct Percentage : IEquatable<Percentage>
    {
        public float Value { get; }

        public Percentage(float value)
        {
            if (value < 0f || value > 100f)
                throw new ArgumentOutOfRangeException(nameof(value), "Un porcentaje va de 0 a 100.");
            Value = value;
        }

        /// <summary>El valor como fracción [0, 1]. P.ej. 50% -> 0.5f.</summary>
        public float AsFraction => Value / 100f;

        public static Percentage FromFraction(float fraction) => new Percentage(fraction * 100f);

        public bool Equals(Percentage other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is Percentage other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"{Value}%";
    }
}
