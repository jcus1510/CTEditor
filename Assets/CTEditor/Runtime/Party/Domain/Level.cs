using System;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// El nivel de un monstruo. Value Object: vale por su número, es inmutable, sin identidad.
    /// Lo hacemos un tipo propio (no un int suelto) para que el modelo sea claro y para colgarle
    /// utilidades como el recorte al tope del Ruleset.
    /// </summary>
    public readonly struct Level : IEquatable<Level>
    {
        public int Value { get; }

        public Level(int value)
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "El nivel mínimo es 1.");
            Value = value;
        }

        /// <summary>
        /// Crea un nivel "recortado" al rango válido [1, cap]. Si te pasan 250 y el cap es 100,
        /// devuelve 100; si te pasan 0, devuelve 1. Así el factory nunca crea un monstruo por
        /// encima del LevelCap del Ruleset (otro invariante relativo a las reglas).
        /// </summary>
        public static Level Clamped(int value, int cap)
        {
            if (cap < 1) cap = 1;
            int clamped = value < 1 ? 1 : (value > cap ? cap : value);
            return new Level(clamped);
        }

        public bool Equals(Level other) => Value == other.Value;
        public override bool Equals(object obj) => obj is Level other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Nv.{Value}";
    }
}
