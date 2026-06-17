using System;
using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.SharedKernel.ValueObjects
{
    /// <summary>
    /// Una probabilidad en [0, 1]. Sabe "tirar el dado", pero con un IRng INYECTADO:
    /// por eso la decisión es determinista bajo una misma semilla (M.2). El dominio nunca
    /// inventa su propio azar.
    /// </summary>
    public readonly struct Chance : IEquatable<Chance>
    {
        public float Value { get; }

        public Chance(float value)
        {
            if (value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(value), "Una probabilidad va de 0 a 1.");
            Value = value;
        }

        public static Chance Always => new Chance(1f);
        public static Chance Never => new Chance(0f);

        /// <summary>Devuelve true con probabilidad Value, usando el azar inyectado.</summary>
        public bool Roll(IRng rng)
        {
            if (rng is null) throw new ArgumentNullException(nameof(rng));
            if (Value <= 0f) return false;
            if (Value >= 1f) return true;
            return rng.NextFloat() < Value;
        }

        public bool Equals(Chance other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is Chance other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"{Value:P0}";
    }
}
