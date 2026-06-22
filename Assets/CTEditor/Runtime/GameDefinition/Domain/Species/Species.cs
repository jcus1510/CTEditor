using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Abilities;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// La DEFINICIÓN de una criatura: su "ficha de enciclopedia". Es inmutable y COMPARTIDA:
    /// existe UNA sola "Salamandra de Fuego" en el juego, y de ella nacen muchos individuos
    /// concretos en partida. Esa es la frontera maestra del motor (C.1):
    ///
    ///   Species (esto)      = el plano: tipos, stats BASE, qué aprende, en qué evoluciona.
    ///   MonsterInstance     = un individuo concreto: "Chispa", nivel 34, con SUS PS y SUS movs.
    ///                         (lo construiremos en Party.Domain)
    ///
    /// Una Species es de SOLO LECTURA y se reutiliza para mil instancias; el estado mutable de
    /// la partida vive en las instancias, NUNCA aquí. (Esto es lo que evita el anti-patrón más
    /// peligroso, M.6: mutar una definición en runtime corrompería a todos los individuos.)
    ///
    /// 'sealed' (no se hereda) y POCO puro (cero Unity).
    /// </summary>
    public sealed class Species
    {
        public Id<Species> Id { get; }
        public string DisplayName { get; }

        /// <summary>
        /// Los tipos de la especie, por id. Es una LISTA, no uno o dos campos fijos: así soportamos
        /// monotipo, doble tipo... o más, si el autor lo quiere (la cantidad máxima es una decisión
        /// de reglas/validación, no una limitación cableada aquí). Otra forma de "crear cada cosa".
        ///
        /// IReadOnlyList = quien la reciba puede recorrerla e indexarla, pero NO añadir/quitar:
        /// protege la inmutabilidad de la ficha.
        /// </summary>
        public IReadOnlyList<Id<ElementType>> Types { get; }

        /// <summary>Estadísticas BASE (no las del individuo: estas son el punto de partida del plano).</summary>
        public StatBlock BaseStats { get; }

        /// <summary>Qué movimientos aprende y a qué nivel.</summary>
        public IReadOnlyList<LearnableMove> Learnset { get; }

        /// <summary>Sus posibles evoluciones (0, 1 o varias).</summary>
        public IReadOnlyList<Evolution> Evolutions { get; }

        /// <summary>La habilidad de la especie (null = ninguna). Rasgo pasivo que se aplica en combate.</summary>
        public AbilityId? Ability { get; }

        public Species(
            Id<Species> id,
            string displayName,
            IReadOnlyList<Id<ElementType>> types,
            StatBlock baseStats,
            IReadOnlyList<LearnableMove> learnset,
            IReadOnlyList<Evolution> evolutions,
            AbilityId? ability = null)
        {
            // Una criatura sin ningún tipo no tiene sentido en el modelo de combate.
            if (types is null || types.Count == 0)
                throw new ArgumentException("Una especie necesita al menos un tipo.", nameof(types));
            if (baseStats is null)
                throw new ArgumentNullException(nameof(baseStats));
            // Nota: que los stats base incluyan los 6 clásicos NO se valida aquí. Eso es una
            // comprobación AMIGABLE de Content Authoring (L.8), con mensajes para el autor; el
            // dominio solo blinda lo imposible, no educa al usuario.

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;

            // COPIAS DEFENSIVAS: copiamos las listas que nos pasan a listas propias. Si no lo
            // hiciéramos, quien construyó la Species podría seguir modificando "su" lista por fuera
            // y alterar la ficha a nuestras espaldas. Al copiar y exponer como IReadOnlyList,
            // la ficha queda sellada de verdad.
            Types = CopyOrEmpty(types);
            BaseStats = baseStats;          // StatBlock ya es inmutable, no hace falta copiar
            Learnset = CopyOrEmpty(learnset);
            Evolutions = CopyOrEmpty(evolutions);
            Ability = ability;
        }

        /// <summary>¿Tiene más de un tipo?</summary>
        public bool IsDualType => Types.Count >= 2;

        /// <summary>¿Puede evolucionar en algo?</summary>
        public bool CanEvolve => Evolutions.Count > 0;

        // Helper genérico de copia. 'Array.Empty&lt;T&gt;()' devuelve un arreglo vacío YA cacheado
        // (no asigna memoria nueva cada vez) y, como los arreglos también son IReadOnlyList, sirve
        // perfecto cuando la lista venía en null.
        private static IReadOnlyList<T> CopyOrEmpty<T>(IReadOnlyList<T> source)
            => source is null ? Array.Empty<T>() : new List<T>(source);
    }
}
