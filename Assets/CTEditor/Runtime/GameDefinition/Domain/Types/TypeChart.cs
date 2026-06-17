using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Types
{
    /// <summary>
    /// La TABLA DE TIPOS: dice qué multiplicador aplica cuando un tipo ataca a otro.
    /// La rellena el autor (Agua x2 a Fuego, Fuego x0.5 a Agua, etc.). Es contenido puro.
    ///
    /// 'sealed' (no se hereda) e INMUTABLE: una vez construida con el Builder, no se toca.
    /// Por eso el constructor es 'private': nadie crea una tabla a medias; se arma con Builder.Build().
    /// </summary>
    public sealed class TypeChart
    {
        // El diccionario guarda SOLO los cruces que no son neutrales. La CLAVE es una TUPLA
        // (atacante, defensor): agrupa dos ids como una sola clave. Funciona como clave porque
        // Id<T> sabe compararse e identificarse (implementa IEquatable y GetHashCode).
        // Lo que no esté en el diccionario se considera neutral (x1): el autor solo define lo "raro".
        private readonly Dictionary<(Id<ElementType> Attacking, Id<ElementType> Defending), float> _entries;

        private TypeChart(Dictionary<(Id<ElementType> Attacking, Id<ElementType> Defending), float> entries)
        {
            _entries = entries;
        }

        /// <summary>Efectividad de un tipo atacante contra UN tipo defensor.</summary>
        public TypeEffectiveness Effectiveness(Id<ElementType> attacking, Id<ElementType> defending)
        {
            // TryGetValue: busca sin lanzar excepción. Si encuentra, 'm' trae el multiplicador.
            return _entries.TryGetValue((attacking, defending), out var m)
                ? new TypeEffectiveness(m)
                : TypeEffectiveness.Neutral;   // no definido -> neutral
        }

        /// <summary>
        /// Efectividad contra un defensor con VARIOS tipos (mono o doble tipo): se multiplican
        /// los cruces. Recibe IEnumerable&lt;Id&gt; (= "algo que se puede recorrer"), así da igual
        /// si le pasan un array, una lista o lo que sea.
        /// </summary>
        public TypeEffectiveness Effectiveness(Id<ElementType> attacking, IEnumerable<Id<ElementType>> defendingTypes)
        {
            if (defendingTypes is null)
                throw new ArgumentNullException(nameof(defendingTypes));

            var result = TypeEffectiveness.Neutral; // arrancamos en x1
            foreach (var def in defendingTypes)
                result = result.Combine(Effectiveness(attacking, def)); // x1 * cada cruce
            return result;
        }

        /// <summary>
        /// Construye una TypeChart paso a paso y de forma legible. Patrón "Builder":
        /// vas encadenando .Set(...).Set(...) y al final .Build() entrega la tabla ya cerrada.
        /// </summary>
        public sealed class Builder
        {
            private readonly Dictionary<(Id<ElementType> Attacking, Id<ElementType> Defending), float> _e
                = new Dictionary<(Id<ElementType> Attacking, Id<ElementType> Defending), float>();

            public Builder Set(Id<ElementType> attacking, Id<ElementType> defending, float multiplier)
            {
                if (multiplier < 0f)
                    throw new ArgumentOutOfRangeException(nameof(multiplier), "El multiplicador no puede ser negativo.");
                _e[(attacking, defending)] = multiplier; // sobrescribe si ya existía ese cruce
                return this; // devolvemos 'this' para poder encadenar: .Set(...).Set(...)
            }

            // Copia defensiva: la tabla construida no comparte el diccionario con el Builder,
            // así nadie puede modificarla por detrás después de Build().
            public TypeChart Build()
                => new TypeChart(new Dictionary<(Id<ElementType> Attacking, Id<ElementType> Defending), float>(_e));
        }
    }
}
