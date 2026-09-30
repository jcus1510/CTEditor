using System;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// La experiencia TOTAL acumulada de un individuo. Value Object inmutable (nunca negativa).
    /// Guardamos el TOTAL (no "lo que falta para el siguiente nivel"): así, dado el total y la curva,
    /// el nivel se deduce sin ambigüedad y subir de nivel es solo "¿el total cruzó el umbral?".
    /// </summary>
    public readonly struct Experience : IEquatable<Experience>
    {
        public int Value { get; }

        public Experience(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "La experiencia no puede ser negativa.");
            Value = value;
        }

        public static readonly Experience Zero = new Experience(0);

        /// <summary>Devuelve una nueva XP con 'amount' sumado (ignora cantidades <= 0).</summary>
        public Experience Plus(int amount) => amount <= 0 ? this : new Experience(Value + amount);

        public bool Equals(Experience other) => Value == other.Value;
        public override bool Equals(object obj) => obj is Experience e && Equals(e);
        public override int GetHashCode() => Value;
        public override string ToString() => $"{Value} XP";
    }
}
