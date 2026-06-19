using System;

namespace CTEditor.GameDefinition.Domain.Types
{
    /// <summary>
    /// El RESULTADO de cruzar un tipo atacante contra uno (o varios) defensores: un multiplicador
    /// de daño. x2 = "muy eficaz", x0.5 = "poco eficaz", x0 = "inmune", x1 = "neutral".
    ///
    /// Es un 'readonly struct': un valor pequeño e inmutable. Implementa IEquatable&lt;TypeEffectiveness&gt;
    /// para que dos resultados con el mismo multiplicador se consideren iguales (comparación por valor).
    /// </summary>
    public readonly struct TypeEffectiveness : IEquatable<TypeEffectiveness>
    {
        public float Multiplier { get; }

        public TypeEffectiveness(float multiplier)
        {
            // Un multiplicador negativo no tiene sentido (no existe "daño antieficaz").
            if (multiplier < 0f)
                throw new ArgumentOutOfRangeException(nameof(multiplier), "El multiplicador no puede ser negativo.");
            Multiplier = multiplier;
        }

        // 'static' aquí significa "pertenece al TIPO, no a una instancia": se usa como
        // TypeEffectiveness.Neutral, sin tener un objeto concreto. Son atajos de lectura.
        public static TypeEffectiveness Neutral => new TypeEffectiveness(1f);
        public static TypeEffectiveness Immune  => new TypeEffectiveness(0f);

        // Clasificadores semánticos: leer 'IsSuperEffective' es más claro que comparar floats a mano.
        public bool IsImmune          => Multiplier <= 0f;
        public bool IsSuperEffective  => Multiplier > 1f;
        public bool IsNotVeryEffective => Multiplier > 0f && Multiplier < 1f;

        /// <summary>
        /// Combina dos efectividades MULTIPLICÁNDOLAS. Esto resuelve los tipos dobles:
        /// si Fuego es x2 contra Planta y x0.5 contra Bicho, contra un Planta/Bicho da x1.
        /// Devuelve un valor NUEVO; no muta este (los structs inmutables nunca se modifican).
        /// </summary>
        public TypeEffectiveness Combine(TypeEffectiveness other)
            => new TypeEffectiveness(Multiplier * other.Multiplier);

        // --- Igualdad por valor (gracias a IEquatable) ---
        public bool Equals(TypeEffectiveness other) => Multiplier.Equals(other.Multiplier);
        public override bool Equals(object obj) => obj is TypeEffectiveness other && Equals(other);
        public override int GetHashCode() => Multiplier.GetHashCode();
        public override string ToString() => $"x{Multiplier}";

        // Operadores: permiten escribir 'a == b' usando NUESTRA definición de igualdad.
        public static bool operator ==(TypeEffectiveness a, TypeEffectiveness b) => a.Equals(b);
        public static bool operator !=(TypeEffectiveness a, TypeEffectiveness b) => !a.Equals(b);
    }
}
