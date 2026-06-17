using System;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// Una entrada del "learnset": QUÉ movimiento aprende una especie y A QUÉ NIVEL.
    /// Es un Value Object (Parte B): no tiene identidad propia, solo vale por sus datos.
    /// Dos LearnableMove con el mismo movimiento y nivel son, a todos los efectos, el mismo;
    /// por eso es un 'readonly struct' inmutable que se compara por valor (IEquatable).
    ///
    /// Fíjate: NO guarda un objeto Move, sino un Id&lt;Move&gt; (referencia por id, M.4). Te explico
    /// abajo por qué esto es deliberado y no una pereza.
    /// </summary>
    public readonly struct LearnableMove : IEquatable<LearnableMove>
    {
        public Id<Move> Move { get; }
        public int Level { get; }

        public LearnableMove(Id<Move> move, int level)
        {
            if (level < 1)
                throw new ArgumentOutOfRangeException(nameof(level), "El nivel de aprendizaje empieza en 1.");
            Move = move;
            Level = level;
        }

        // --- Igualdad por valor ---
        public bool Equals(LearnableMove other) => Move.Equals(other.Move) && Level == other.Level;
        public override bool Equals(object obj) => obj is LearnableMove other && Equals(other);

        // GetHashCode debe COMBINAR los dos campos en un solo número. El patrón clásico
        // "(a * 397) ^ b" mezcla los bits de ambos para que objetos distintos casi nunca
        // choquen en la misma "casilla" del diccionario. 'unchecked' = "no me lances error
        // si el número se desborda al multiplicar"; en un hash el desborde es inofensivo.
        public override int GetHashCode()
        {
            unchecked
            {
                return (Move.GetHashCode() * 397) ^ Level;
            }
        }

        public override string ToString() => $"{Move} @ Nv.{Level}";
    }
}
